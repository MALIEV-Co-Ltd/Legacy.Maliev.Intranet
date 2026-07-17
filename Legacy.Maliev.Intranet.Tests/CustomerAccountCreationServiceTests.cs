using System.Net;
using System.Text;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Customers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerAccountCreationServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidProfileAndIdentity_ReturnsCreatedWithoutCompensation()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Created, "{\"databaseID\":42}"));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Created, result.Status);
        Assert.Equal(42, result.CustomerId);
        Assert.Empty(profiles.DeletedIds);
        Assert.Equal(42, identities.CustomerIds.Single());
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

        var result = await service.CreateAsync(ValidRequest(), CancellationToken.None);

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
    public async Task CreateAsync_IdentityFailure_CompensatesProfile(
        HttpStatusCode downstreamStatus,
        CustomerAccountCreationStatus expectedStatus)
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(downstreamStatus, "{}", retryAfterSeconds: 5));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal([42], profiles.DeletedIds);
        if (downstreamStatus == HttpStatusCode.TooManyRequests)
        {
            Assert.Equal(TimeSpan.FromSeconds(5), result.RetryAfter);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidIdentityPayload_CompensatesAndReturnsBadGateway()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Created, "not-json"));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.BadGateway, result.Status);
        Assert.Equal([42], profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_IdentityTransportFailure_CompensatesAndReturnsUnavailable()
    {
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(new HttpRequestException("auth unavailable"));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Unavailable, result.Status);
        Assert.Equal([42], profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_CompensationFailure_FailsClosedWithoutMaskingWithAnException()
    {
        var profiles = new ProfileClientStub(
            Response(HttpStatusCode.Created, "{\"id\":42}"),
            deleteException: new HttpRequestException("delete unavailable"));
        var identities = new IdentityClientStub(Response(HttpStatusCode.Conflict, "{}"));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Unavailable, result.Status);
        Assert.Equal([42], profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_DuplicateSubmission_CompensatesOnlyTheDuplicateProfile()
    {
        var profiles = new ProfileClientStub(
            Response(HttpStatusCode.Created, "{\"id\":41}"),
            Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new IdentityClientStub(
            Response(HttpStatusCode.Created, "{\"databaseID\":41}"),
            Response(HttpStatusCode.Conflict, "{}"));
        var service = CreateService(profiles, identities);

        var first = await service.CreateAsync(ValidRequest(), CancellationToken.None);
        var duplicate = await service.CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(CustomerAccountCreationStatus.Created, first.Status);
        Assert.Equal(41, first.CustomerId);
        Assert.Equal(CustomerAccountCreationStatus.Conflict, duplicate.Status);
        Assert.Equal([42], profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_CallerCancelsAfterProfileCreation_StillCompensatesTheProfile()
    {
        using var cancellation = new CancellationTokenSource();
        var profiles = new ProfileClientStub(Response(HttpStatusCode.Created, "{\"id\":42}"));
        var identities = new CancelingIdentityClient(cancellation);
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(CustomerAccountCreationStatus.Unavailable, result.Status);
        Assert.Equal([42], profiles.DeletedIds);
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
            service.CreateAsync(ValidRequest(), cancellation.Token));

        Assert.Empty(profiles.DeletedIds);
    }

    private static CustomerAccountCreationService CreateService(
        ICustomerProfileCreationClient profiles,
        ICustomerIdentityCreationClient identities) =>
        new(profiles, identities, NullLogger<CustomerAccountCreationService>.Instance);

    private static CreateCustomerAccountRequest ValidRequest() => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@example.com",
        Password = "correct horse battery staple",
        ConfirmPassword = "correct horse battery staple",
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

        public Task<HttpResponseMessage> CreateAsync(CreateCustomerAccountRequest request, CancellationToken cancellationToken) =>
            NextAsync(_createResults);

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

        public Task<HttpResponseMessage> CreateAsync(
            int customerId,
            CreateCustomerAccountRequest request,
            CancellationToken cancellationToken)
        {
            CustomerIds.Add(customerId);
            return NextAsync(_createResults);
        }
    }

    private sealed class CancelingIdentityClient(CancellationTokenSource cancellation) : ICustomerIdentityCreationClient
    {
        public Task<HttpResponseMessage> CreateAsync(
            int customerId,
            CreateCustomerAccountRequest request,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromException<HttpResponseMessage>(new OperationCanceledException(cancellation.Token));
        }
    }

    private static Task<HttpResponseMessage> NextAsync(Queue<object> results)
    {
        var result = results.Dequeue();
        return result is Exception exception
            ? Task.FromException<HttpResponseMessage>(exception)
            : Task.FromResult((HttpResponseMessage)result);
    }
}
