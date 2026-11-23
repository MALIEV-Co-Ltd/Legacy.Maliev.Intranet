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
    public async Task CreateAsync_AddressFailureDoesNotDeleteAcknowledgedProfile()
    {
        var profiles = new ProfileClient(HttpStatusCode.Created, "{\"id\":42}");
        var addresses = new AddressClient(HttpStatusCode.Conflict, "{}");
        var service = new SupplierCreationService(profiles, addresses, NullLogger<SupplierCreationService>.Instance);

        var result = await service.CreateAsync(ValidRequest, CancellationToken.None);

        Assert.Equal(SupplierCreationStatus.Unavailable, result.Status);
        Assert.Equal(0, profiles.DeleteCount);
    }

    [Fact]
    public async Task CreateAsync_LostAddressResponseDoesNotDeleteAcknowledgedProfile()
    {
        var profiles = new ProfileClient(HttpStatusCode.Created, "{\"id\":42}");
        var addresses = new AddressClient(HttpStatusCode.BadRequest, "{}") { ThrowOnWrite = true };
        var service = new SupplierCreationService(profiles, addresses, NullLogger<SupplierCreationService>.Instance);

        var result = await service.CreateAsync(ValidRequest, CancellationToken.None);

        Assert.Equal(SupplierCreationStatus.Unavailable, result.Status);
        Assert.Equal(0, profiles.DeleteCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_WithoutAnyAddressCreatesOnlyProfile(string address)
    {
        var profiles = new ProfileClient(HttpStatusCode.Created, "{\"id\":42}");
        var addresses = new AddressClient(HttpStatusCode.Created, "{}");
        var service = new SupplierCreationService(profiles, addresses, NullLogger<SupplierCreationService>.Instance);
        var result = await service.CreateAsync(new SupplierCreateRequest { Name = "Acme", Address1 = address }, CancellationToken.None);
        Assert.Equal(SupplierCreationStatus.Created, result.Status);
        Assert.Equal(42, result.SupplierId);
        Assert.Null(addresses.SupplierId);
        Assert.Equal(0, profiles.DeleteCount);
    }

    [Theory]
    [InlineData("1 Main Road", 0, null)]
    [InlineData("", 66, null)]
    [InlineData("   ", 0, "Bangkok")]
    public async Task CreateAsync_PartialAddressIsRejectedBeforeProfileWrite(string address, int countryId, string? city)
    {
        var profiles = new ProfileClient(HttpStatusCode.Created, "{\"id\":42}");
        var addresses = new AddressClient(HttpStatusCode.Created, "{}");
        var service = new SupplierCreationService(profiles, addresses, NullLogger<SupplierCreationService>.Instance);
        var result = await service.CreateAsync(new SupplierCreateRequest { Name = "Acme", Address1 = address, CountryId = countryId, City = city }, CancellationToken.None);
        Assert.Equal(SupplierCreationStatus.BadRequest, result.Status);
        Assert.Equal(0, profiles.CreateCount);
        Assert.Null(addresses.SupplierId);
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
        public int CreateCount { get; private set; }
        public int DeleteCount { get; private set; }
        public int? DeletedId { get; private set; }

        public Task<HttpResponseMessage> CreateAsync(SupplierCreateRequest request, CancellationToken cancellationToken)
        {
            CreateCount++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(System.Text.Json.JsonDocument.Parse(json).RootElement) });
        }

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
        public bool ThrowOnWrite { get; init; }

        public Task<HttpResponseMessage> CreateAsync(int supplierId, SupplierCreateRequest request, CancellationToken cancellationToken)
        {
            SupplierId = supplierId;
            if (ThrowOnWrite) throw new HttpRequestException("synthetic lost response");
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(System.Text.Json.JsonDocument.Parse(json).RootElement) });
        }
    }
}
