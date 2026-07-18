using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Suppliers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class SupplierCreationServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesProfileThenOwnedAddress()
    {
        var profiles = new ProfileClient(HttpStatusCode.Created, "{\"id\":42}");
        var addresses = new AddressClient(HttpStatusCode.Created, "{\"id\":7,\"supplierId\":42}");
        var service = new SupplierCreationService(profiles, addresses, NullLogger<SupplierCreationService>.Instance);

        var result = await service.CreateAsync(ValidRequest, CancellationToken.None);

        Assert.Equal(SupplierCreationStatus.Created, result.Status);
        Assert.Equal(42, result.SupplierId);
        Assert.Equal(42, addresses.SupplierId);
        Assert.Equal(0, profiles.DeleteCount);
    }

    [Fact]
    public async Task CreateAsync_AddressFailureCompensatesProfileAndPreservesFailure()
    {
        var profiles = new ProfileClient(HttpStatusCode.Created, "{\"id\":42}");
        var addresses = new AddressClient(HttpStatusCode.Conflict, "{}");
        var service = new SupplierCreationService(profiles, addresses, NullLogger<SupplierCreationService>.Instance);

        var result = await service.CreateAsync(ValidRequest, CancellationToken.None);

        Assert.Equal(SupplierCreationStatus.Conflict, result.Status);
        Assert.Equal(1, profiles.DeleteCount);
        Assert.Equal(42, profiles.DeletedId);
    }

    [Fact]
    public async Task CreateAsync_CompensationFailureIsUnavailable()
    {
        var profiles = new ProfileClient(HttpStatusCode.Created, "{\"id\":42}") { DeleteStatus = HttpStatusCode.InternalServerError };
        var addresses = new AddressClient(HttpStatusCode.BadRequest, "{}");
        var service = new SupplierCreationService(profiles, addresses, NullLogger<SupplierCreationService>.Instance);

        var result = await service.CreateAsync(ValidRequest, CancellationToken.None);

        Assert.Equal(SupplierCreationStatus.Unavailable, result.Status);
    }

    private static readonly SupplierCreateRequest ValidRequest = new()
    {
        Name = "Acme",
        Address1 = "1 Main Road",
        CountryId = 66,
        Email = "sales@acme.test",
    };

    private sealed class ProfileClient(HttpStatusCode status, string json) : ISupplierProfileCreationClient
    {
        public HttpStatusCode DeleteStatus { get; init; } = HttpStatusCode.NoContent;
        public int DeleteCount { get; private set; }
        public int? DeletedId { get; private set; }

        public Task<HttpResponseMessage> CreateAsync(SupplierCreateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(System.Text.Json.JsonDocument.Parse(json).RootElement) });

        public Task<HttpResponseMessage> DeleteAsync(int supplierId, CancellationToken cancellationToken)
        {
            DeleteCount++;
            DeletedId = supplierId;
            return Task.FromResult(new HttpResponseMessage(DeleteStatus));
        }
    }

    private sealed class AddressClient(HttpStatusCode status, string json) : ISupplierAddressCreationClient
    {
        public int? SupplierId { get; private set; }

        public Task<HttpResponseMessage> CreateAsync(int supplierId, SupplierCreateRequest request, CancellationToken cancellationToken)
        {
            SupplierId = supplierId;
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(System.Text.Json.JsonDocument.Parse(json).RootElement) });
        }
    }
}
