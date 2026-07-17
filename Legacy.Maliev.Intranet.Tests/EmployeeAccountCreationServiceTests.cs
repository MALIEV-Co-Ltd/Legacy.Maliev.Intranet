using System.Net;
using System.Text;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Employees;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeAccountCreationServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidProfileAndIdentity_ReturnsCreatedWithoutCompensation()
    {
        var profiles = new ProfileStub(Response(HttpStatusCode.Created, "{\"Id\":42}"));
        var identities = new IdentityStub(Response(HttpStatusCode.Created, "{\"databaseID\":42}"));
        var service = CreateService(profiles, identities);

        var result = await service.CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(EmployeeAccountCreationStatus.Created, result.Status);
        Assert.Equal(42, result.EmployeeId);
        Assert.Empty(profiles.DeletedIds);
        Assert.Equal([42], identities.EmployeeIds);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, EmployeeAccountCreationStatus.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized, EmployeeAccountCreationStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, EmployeeAccountCreationStatus.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, EmployeeAccountCreationStatus.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests, EmployeeAccountCreationStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, EmployeeAccountCreationStatus.Unavailable)]
    public async Task CreateAsync_ProfileFailure_StopsBeforeIdentity(HttpStatusCode status, EmployeeAccountCreationStatus expected)
    {
        var profiles = new ProfileStub(Response(status, "{}", 3));
        var identities = new IdentityStub(Response(HttpStatusCode.Created, "{\"databaseID\":42}"));

        var result = await CreateService(profiles, identities).CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Empty(identities.EmployeeIds);
        Assert.Empty(profiles.DeletedIds);
        if (status == HttpStatusCode.TooManyRequests) Assert.Equal(TimeSpan.FromSeconds(3), result.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, EmployeeAccountCreationStatus.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized, EmployeeAccountCreationStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, EmployeeAccountCreationStatus.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, EmployeeAccountCreationStatus.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests, EmployeeAccountCreationStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, EmployeeAccountCreationStatus.Unavailable)]
    public async Task CreateAsync_IdentityFailure_CompensatesProfile(HttpStatusCode status, EmployeeAccountCreationStatus expected)
    {
        var profiles = new ProfileStub(Response(HttpStatusCode.Created, "{\"Id\":42}"));
        var identities = new IdentityStub(Response(status, "{}", 5));

        var result = await CreateService(profiles, identities).CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal([42], profiles.DeletedIds);
        if (status == HttpStatusCode.TooManyRequests) Assert.Equal(TimeSpan.FromSeconds(5), result.RetryAfter);
    }

    [Fact]
    public async Task CreateAsync_InvalidIdentityPayload_CompensatesAndReturnsBadGateway()
    {
        var profiles = new ProfileStub(Response(HttpStatusCode.Created, "{\"Id\":42}"));
        var identities = new IdentityStub(Response(HttpStatusCode.Created, "not-json"));

        var result = await CreateService(profiles, identities).CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(EmployeeAccountCreationStatus.BadGateway, result.Status);
        Assert.Equal([42], profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_CompensationFailure_FailsClosed()
    {
        var profiles = new ProfileStub(
            Response(HttpStatusCode.Created, "{\"Id\":42}"),
            new HttpRequestException("delete unavailable"));
        var identities = new IdentityStub(Response(HttpStatusCode.Conflict, "{}"));

        var result = await CreateService(profiles, identities).CreateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(EmployeeAccountCreationStatus.Unavailable, result.Status);
        Assert.Equal([42], profiles.DeletedIds);
    }

    [Fact]
    public async Task CreateAsync_CallerCancellationAfterProfileCreation_StillCompensates()
    {
        using var cancellation = new CancellationTokenSource();
        var profiles = new ProfileStub(Response(HttpStatusCode.Created, "{\"Id\":42}"));
        var identities = new CancelingIdentityStub(cancellation);

        var result = await CreateService(profiles, identities).CreateAsync(ValidRequest(), cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(EmployeeAccountCreationStatus.Unavailable, result.Status);
        Assert.Equal([42], profiles.DeletedIds);
    }

    private static EmployeeAccountCreationService CreateService(IEmployeeProfileCreationClient profiles, IEmployeeIdentityCreationClient identities) =>
        new(profiles, identities, NullLogger<EmployeeAccountCreationService>.Instance);

    private static CreateEmployeeAccountRequest ValidRequest() => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@example.com",
        Password = "correct horse battery staple",
        ConfirmPassword = "correct horse battery staple",
        PhoneNumber = "+66 81 234 5678",
        RoleId = 7,
        DateOfBirth = new DateTime(1815, 12, 10),
    };

    private static HttpResponseMessage Response(HttpStatusCode status, string body, int? retryAfter = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is not null) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(retryAfter.Value));
        return response;
    }

    private sealed class ProfileStub : IEmployeeProfileCreationClient
    {
        private readonly HttpResponseMessage created;
        private readonly Exception? deleteException;
        public ProfileStub(HttpResponseMessage created, Exception? deleteException = null) => (this.created, this.deleteException) = (created, deleteException);
        public List<int> DeletedIds { get; } = [];
        public Task<HttpResponseMessage> CreateAsync(CreateEmployeeAccountRequest request, CancellationToken cancellationToken) => Task.FromResult(created);
        public Task<HttpResponseMessage> DeleteAsync(int employeeId, CancellationToken cancellationToken)
        {
            DeletedIds.Add(employeeId);
            return deleteException is null ? Task.FromResult(Response(HttpStatusCode.NoContent, string.Empty)) : Task.FromException<HttpResponseMessage>(deleteException);
        }
    }

    private sealed class IdentityStub(HttpResponseMessage response) : IEmployeeIdentityCreationClient
    {
        public List<int> EmployeeIds { get; } = [];
        public Task<HttpResponseMessage> CreateAsync(int employeeId, CreateEmployeeAccountRequest request, CancellationToken cancellationToken)
        {
            EmployeeIds.Add(employeeId);
            return Task.FromResult(response);
        }
    }

    private sealed class CancelingIdentityStub(CancellationTokenSource cancellation) : IEmployeeIdentityCreationClient
    {
        public Task<HttpResponseMessage> CreateAsync(int employeeId, CreateEmployeeAccountRequest request, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromException<HttpResponseMessage>(new OperationCanceledException(cancellation.Token));
        }
    }
}
