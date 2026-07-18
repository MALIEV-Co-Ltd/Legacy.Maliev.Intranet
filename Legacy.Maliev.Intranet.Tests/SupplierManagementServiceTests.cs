using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Suppliers;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class SupplierManagementServiceTests
{
    [Fact]
    public async Task GetAsync_CombinesProfileAndOwnedAddress()
    {
        var client = new Client();
        var service = new SupplierManagementService(client);
        var result = await service.GetAsync(42, CancellationToken.None);
        Assert.Equal(SupplierManagementStatus.Success, result.Status);
        Assert.Equal("Acme", result.Supplier?.Name);
        Assert.Equal("1 Main", result.Supplier?.Address1);
    }

    [Fact]
    public async Task UpdateAsync_CreatesAddressWhenMissing()
    {
        var client = new Client { AddressMissing = true };
        var service = new SupplierManagementService(client);
        var result = await service.UpdateAsync(42, Request, CancellationToken.None);
        Assert.Equal(SupplierManagementStatus.Success, result.Status);
        Assert.Equal(1, client.CreateAddressCount);
        Assert.Equal(0, client.UpdateAddressCount);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesExistingAddress()
    {
        var client = new Client();
        var service = new SupplierManagementService(client);
        var result = await service.UpdateAsync(42, Request, CancellationToken.None);
        Assert.Equal(SupplierManagementStatus.Success, result.Status);
        Assert.Equal(7, client.UpdatedAddressId);
    }

    [Fact]
    public async Task UpdateAsync_InvalidOwnedAddressIdentifier_IsRejectedBeforeWrites()
    {
        var client = new Client { AddressId = 0 };
        var service = new SupplierManagementService(client);

        var result = await service.UpdateAsync(42, Request, CancellationToken.None);

        Assert.Equal(SupplierManagementStatus.BadGateway, result.Status);
        Assert.Equal(0, client.ProfileUpdateCount);
        Assert.Equal(0, client.UpdateAddressCount);
    }

    private static readonly SupplierCreateRequest Request = new() { Name = "Acme", Address1 = "1 Main", CountryId = 66 };

    private sealed class Client : ISupplierManagementClient
    {
        public bool AddressMissing { get; init; }
        public int AddressId { get; init; } = 7;
        public int CreateAddressCount { get; private set; }
        public int UpdateAddressCount { get; private set; }
        public int ProfileUpdateCount { get; private set; }
        public int? UpdatedAddressId { get; private set; }
        public Task<HttpResponseMessage> GetProfileAsync(int id, CancellationToken ct) => Task.FromResult(Json(HttpStatusCode.OK, new { id, name = "Acme", website = (string?)null, taxNumber = (string?)null, email = "sales@acme.test", note = (string?)null, telephone = (string?)null, mobile = (string?)null, fax = (string?)null }));
        public Task<HttpResponseMessage> GetAddressAsync(int id, CancellationToken ct) => Task.FromResult(AddressMissing ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(HttpStatusCode.OK, new { id = AddressId, building = (string?)null, address1 = "1 Main", address2 = (string?)null, city = "Bangkok", state = (string?)null, postalCode = "10110", countryId = 66 }));
        public Task<HttpResponseMessage> UpdateProfileAsync(int id, SupplierCreateRequest request, CancellationToken ct)
        {
            ProfileUpdateCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
        public Task<HttpResponseMessage> CreateAddressAsync(int id, SupplierCreateRequest request, CancellationToken ct) { CreateAddressCount++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)); }
        public Task<HttpResponseMessage> UpdateAddressAsync(int id, SupplierCreateRequest request, CancellationToken ct) { UpdateAddressCount++; UpdatedAddressId = id; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
        public Task<HttpResponseMessage> DeleteProfileAsync(int id, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status) { Content = JsonContent.Create(value) };
    }
}
