using System.Net;
using System.Text;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Customers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerAccountCreationServiceTests
{
    private static readonly Guid TestOperationId = Guid.Parse("1904e7f5-7223-45c9-8ea3-91c0aa498cf0");

    [Fact]
    public async Task CreateAsync_ValidProfileAndIdentity_ReturnsCreatedWithoutCompensation()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Created, "{\"databaseID\":42}"));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Created, result.Status);
        Assert.Equal(42, result.CustomerId);
        Assert.Equal("setup-token", result.OnboardingToken);
        Assert.Null(typeof(CustomerAccountCreationResult).GetProperty("TemporaryPassword"));
        Assert.Empty(profiles.DeletedIds);
        Assert.Equal(42, identities.CustomerIds.Single());
        Assert.Single(identities.BootstrapPasswords);
        Assert.DoesNotContain("setup-token", identities.BootstrapPasswords);
        Assert.Equal([42], identities.SetupCustomerIds);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, CustomerAccountCreationStatus.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized, CustomerAccountCreationStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, CustomerAccountCreationStatus.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, CustomerAccountCreationStatus.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests, CustomerAccountCreationStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, CustomerAccountCreationStatus.Unavailable)]
    public async Task CreateAsync_ProfileFailure_StopsBeforeIdentity(
        HttpStatusCode downstreamStatus,
        CustomerAccountCreationStatus expectedStatus)
    {
        var profiles = new ProfileClientStub(Response(downstreamStatus, "{}", retryAfterSeconds: 3));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Created, "{\"databaseID\":42}"));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Empty(identities.CustomerIds);
        Assert.Empty(profiles.DeletedIds);
        if (downstreamStatus == HttpStatusCode.TooManyRequests)
        {
            Assert.Equal(TimeSpan.FromSeconds(3), result.RetryAfter);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, CustomerAccountCreationStatus.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized, CustomerAccountCreationStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, CustomerAccountCreationStatus.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, CustomerAccountCreationStatus.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests, CustomerAccountCreationStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, CustomerAccountCreationStatus.Unavailable)]
    public async Task CreateAsync_IdentityFailure_RetainsProfileForReconciliation(
        HttpStatusCode downstreamStatus,
        CustomerAccountCreationStatus expectedStatus)
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(downstreamStatus, "{}", retryAfterSeconds: 5));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(42, result.CustomerId);
        Assert.Empty(profiles.DeletedIds);
        if (downstreamStatus == HttpStatusCode.TooManyRequests)
        {
            Assert.Equal(TimeSpan.FromSeconds(5), result.RetryAfter);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidIdentityPayload_RetainsProfileAndReturnsBadGateway()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Created, "not-json"));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.BadGateway, result.Status);
        Assert.Equal(42, result.CustomerId);
        Assert.Empty(profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_IdentityTransportFailure_RetainsProfileAndReturnsUnavailable()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(new HttpRequestException("auth unavailable"));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Unavailable, result.Status);
        Assert.Equal(42, result.CustomerId);
        Assert.Empty(profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_IdentityConflict_DoesNotDeletePossiblyReplayedProfile()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Conflict, "{}"));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Conflict, result.Status);
        Assert.Empty(profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_ReplayedProfile_IdentityConflictRetainsOriginalProfile()
    {
        var profiles = new ProfileClientStub(
            Response(HttpStatusCode.Created, "{\"id\":41}"),
            Response(HttpStatusCode.Created, "{\"id\":41}"));
        var identities = new IdentityClientStub(
            Response(HttpStatusCode.Created, "{\"databaseID\":41}"),
            Response(HttpStatusCode.Conflict, "{}"));
        var service = CreateService(profiles, identities);

        var first = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);
        var duplicate = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Created, first.Status);
        Assert.Equal(41, first.CustomerId);
        Assert.Equal(CustomerAccountCreationStatus.Conflict, duplicate.Status);
        Assert.Equal(41, duplicate.CustomerId);
        Assert.Equal([TestOperationId, TestOperationId], profiles.OperationIds);
        Assert.Empty(profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_ProfileResponseLost_RetryUsesOriginalOperationKey()
    {
        var profiles = new ProfileClientStub(
            new HttpRequestException("response lost after commit"),
            Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Created, "{\"databaseID\":42}"));
        var service = CreateService(profiles, identities);

        var uncertain = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);
        var replay = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Unavailable, uncertain.Status);
        Assert.Equal(CustomerAccountCreationStatus.Created, replay.Status);
        Assert.Equal(42, replay.CustomerId);
        Assert.Equal([TestOperationId, TestOperationId], profiles.OperationIds);
        Assert.Empty(profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_ReconcileCreate_ReplaysLostIdentityResponseWithIdenticalSecret()
    {
        var profiles = new ProfileClientStub(
            Response(HttpStatusCode.Created, "{\"id\":42}"),
            Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(
            new HttpRequestException("response lost after commit"),
            Response(HttpStatusCode.OK, "{\"databaseId\":42,\"status\":\"replayed\"}"));
        var service = CreateService(profiles, identities, EnabledOptions());

        var uncertain = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);
        var replay = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Unavailable, uncertain.Status);
        Assert.Equal(CustomerAccountCreationStatus.Created, replay.Status);
        Assert.Equal(42, replay.CustomerId);
        Assert.Equal([TestOperationId, TestOperationId], identities.ReconcileOperationIds);
        Assert.Equal(identities.BootstrapPasswords[0], identities.BootstrapPasswords[1]);
        Assert.Empty(identities.CreateCustomerIds);
        Assert.Empty(profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_ReconcileCreate_AcceptsCreatedReceipt()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Created, "{\"databaseId\":42,\"status\":\"created\"}"));

        var result = await CreateService(profiles, identities, EnabledOptions())
            .CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Created, result.Status);
        Assert.Equal(42, result.CustomerId);
        Assert.Equal([TestOperationId], identities.ReconcileOperationIds);
        Assert.Empty(identities.CreateCustomerIds);
        Assert.Equal([42], identities.SetupCustomerIds);
    }

    [Fact]
    public async Task CreateAsync_ReconcileCreate_ChangedProfileWithSameKeyConflictsBeforeIdentity()
    {
        var profiles = new ProfileClientStub(
            Response(HttpStatusCode.Created, "{\"id\":42}"),
            Response(HttpStatusCode.Conflict, "{}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Created, "{\"databaseId\":42,\"status\":\"created\"}"));
        var service = CreateService(profiles, identities, EnabledOptions());
        var changed = ValidRequest();
        changed.FirstName = "Grace";

        var created = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);
        var conflict = await service.CreateAsync(changed, TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Created, created.Status);
        Assert.Equal(CustomerAccountCreationStatus.Conflict, conflict.Status);
        Assert.Equal([TestOperationId, TestOperationId], profiles.OperationIds);
        Assert.Equal([TestOperationId], identities.ReconcileOperationIds);
        Assert.Empty(profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_ReconcileCreate_RejectsMismatchedReceiptWithoutOnboarding()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.OK, "{\"databaseId\":41,\"status\":\"replayed\"}"));
        var result = await CreateService(profiles, identities, EnabledOptions())
            .CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.BadGateway, result.Status);
        Assert.Equal(42, result.CustomerId);
        Assert.Empty(identities.SetupCustomerIds);
        Assert.Empty(profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_ReconcileCreate_RejectsStatusCodeAndBodyMismatch()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Created, "{\"databaseId\":42,\"status\":\"replayed\"}"));
        var result = await CreateService(profiles, identities, EnabledOptions())
            .CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.BadGateway, result.Status);
        Assert.Empty(identities.SetupCustomerIds);
    }

    [Fact]
    public async Task CreateAsync_ReconcileCreate_RejectsUnexpectedSuccessfulStatusAndRetainsProfile()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Accepted, "{\"databaseId\":42}"));
        var result = await CreateService(profiles, identities, EnabledOptions())
            .CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.BadGateway, result.Status);
        Assert.Equal(42, result.CustomerId);
        Assert.Empty(identities.SetupCustomerIds);
        Assert.Empty(profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_ReconcileCreate_MissingProtectedKeyStopsBeforeProfileWrite()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Created, "{\"databaseId\":42,\"status\":\"created\"}"));
        var service = CreateService(profiles, identities, new() { Enabled = true });

        var result = await service.CreateAsync(ValidRequest(), TestOperationId, CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Unavailable, result.Status);
        Assert.Empty(profiles.OperationIds);
        Assert.Empty(identities.CustomerIds);
    }

    [Fact]
    public void ReconcileBootstrapSecret_IsStableForOperationAndDistinctForChangedOperation()
    {
        var options = EnabledOptions();
        var first = options.DeriveBootstrapSecret(TestOperationId, 42);
        Assert.Equal(first, options.DeriveBootstrapSecret(TestOperationId, 42));
        Assert.NotEqual(first, options.DeriveBootstrapSecret(Guid.NewGuid(), 42));
        Assert.NotEqual(first, options.DeriveBootstrapSecret(TestOperationId, 43));
        Assert.StartsWith("Aa1!", first, StringComparison.Ordinal);
        Assert.True(first.Length >= 40);
    }

    [Fact]
    public async Task CreateAsync_CallerCancelsAfterProfileCreation_RetainsProfile()
    {
        using var cancellation = new CancellationTokenSource();
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new CancelingIdentityClient(cancellation);
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), TestOperationId, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(CustomerAccountCreationStatus.Unavailable, result.Status);
        Assert.Empty(profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_CallerCancelsBeforeProfileCreation_PropagatesWithoutCompensation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var profiles = new ProfileClientStub(new OperationCanceledException(cancellation.Token));
        var service = CreateService(
            profiles,
            new IdentityClientStub(Response(HttpStatusCode.Created, "{\"databaseID\":42}")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateAsync(ValidRequest(), TestOperationId, cancellation.Token));

        Assert.Empty(profiles.DeletedIds);
    }

    private static CustomerAccountCreationService CreateService(
        ICustomerProfileCreationClient profiles,
        ICustomerIdentityCreationClient identities,
        CustomerIdentityReconciliationOptions? options = null) =>
        new(profiles, identities, Options.Create(options ?? new()), NullLogger<CustomerAccountCreationService>.Instance);

    private static CustomerIdentityReconciliationOptions EnabledOptions() => new()
    {
        Enabled = true,
        BootstrapKeyBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)index).ToArray()),
    };

    private static CreateCustomerAccountRequest ValidRequest() => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@example.com",
        Telephone = "+66 2 123 4567",
        Mobile = "+66 81 234 5678",
        Fax = "+66 2 765 4321",
        DateOfBirth = new DateTime(1815, 12, 10),
    };

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string body, int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (retryAfterSeconds is not null)
        {
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(retryAfterSeconds.Value));
        }

        return response;
    }

    private sealed class ProfileClientStub : ICustomerProfileCreationClient
    {
        private readonly Queue<object> _createResults;
        private readonly Exception? _deleteException;

        public ProfileClientStub(params object[] createResults)
            : this(createResults, null)
        {
        }

        public ProfileClientStub(object createResult, Exception deleteException)
            : this([createResult], deleteException)
        {
        }

        private ProfileClientStub(IEnumerable<object> createResults, Exception? deleteException)
        {
            _createResults = new Queue<object>(createResults);
            _deleteException = deleteException;
        }

        public List<int> DeletedIds { get; } = [];

        public List<Guid> OperationIds { get; } = [];

        public Task<HttpResponseMessage> CreateAsync(CreateCustomerAccountRequest request, Guid operationId, CancellationToken cancellationToken)
        {
            OperationIds.Add(operationId);
            return NextAsync(_createResults);
        }

        public Task<HttpResponseMessage> DeleteAsync(int customerId, CancellationToken cancellationToken)
        {
            DeletedIds.Add(customerId);
            return _deleteException is null
                ? Task.FromResult(Response(HttpStatusCode.NoContent, string.Empty))
                : Task.FromException<HttpResponseMessage>(_deleteException);
        }
    }

    private sealed class IdentityClientStub(params object[] createResults) : ICustomerIdentityCreationClient
    {
        private readonly Queue<object> _createResults = new(createResults);

        public List<int> CustomerIds { get; } = [];

        public List<int> CreateCustomerIds { get; } = [];

        public List<Guid> ReconcileOperationIds { get; } = [];

        public List<string> BootstrapPasswords { get; } = [];

        public List<int> SetupCustomerIds { get; } = [];

        public Task<HttpResponseMessage> CreateAsync(
            int customerId,
            CreateCustomerAccountRequest request,
            string bootstrapSecret,
            CancellationToken cancellationToken)
        {
            CustomerIds.Add(customerId);
            CreateCustomerIds.Add(customerId);
            BootstrapPasswords.Add(bootstrapSecret);
            return NextAsync(_createResults);
        }

        public Task<HttpResponseMessage> ReconcileCreateAsync(
            int customerId,
            CreateCustomerAccountRequest request,
            string bootstrapSecret,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            CustomerIds.Add(customerId);
            ReconcileOperationIds.Add(operationId);
            BootstrapPasswords.Add(bootstrapSecret);
            return NextAsync(_createResults);
        }

        public Task<HttpResponseMessage> CreatePasswordSetupChallengeAsync(
            int customerId,
            CancellationToken cancellationToken)
        {
            SetupCustomerIds.Add(customerId);
            return Task.FromResult(Response(HttpStatusCode.OK, "{\"accepted\":true,\"token\":\"setup-token\"}"));
        }
    }

    private sealed class CancelingIdentityClient(CancellationTokenSource cancellation) : ICustomerIdentityCreationClient
    {
        public Task<HttpResponseMessage> CreateAsync(
            int customerId,
            CreateCustomerAccountRequest request,
            string bootstrapSecret,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromException<HttpResponseMessage>(new OperationCanceledException(cancellation.Token));
        }

        public Task<HttpResponseMessage> CreatePasswordSetupChallengeAsync(
            int customerId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Setup must not run after identity creation fails.");

        public Task<HttpResponseMessage> ReconcileCreateAsync(
            int customerId,
            CreateCustomerAccountRequest request,
            string bootstrapSecret,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Reconciliation was not enabled for this test.");
    }

    private static Task<HttpResponseMessage> NextAsync(Queue<object> results)
    {
        var result = results.Dequeue();
        return result is Exception exception
            ? Task.FromException<HttpResponseMessage>(exception)
            : Task.FromResult((HttpResponseMessage)result);
    }
}
