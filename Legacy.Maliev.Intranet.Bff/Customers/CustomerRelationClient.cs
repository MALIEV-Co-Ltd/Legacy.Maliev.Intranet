using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.Intranet.Bff.Employees;

namespace Legacy.Maliev.Intranet.Bff.Customers;

/// <summary>Single-send relation transport using only the acting employee credential.</summary>
public sealed class CustomerRelationClient(HttpClient client, EmployeeAdministrationCredentialSender credentials)
{
    private static readonly JsonSerializerOptions LegacyJson = new(JsonSerializerDefaults.General) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Reads the exact bound relation, or captures the missing binding before creation.</summary>
    public Task<HttpResponseMessage> ReadAsync(int id, string kind, string relation, HttpContext context, CancellationToken token) =>
        credentials.SendAsync(client, new(HttpMethod.Get, Path(id, kind, relation, relation == "new" ? "" : "/edit")),
            Permission(kind, relation, false), context, token, "legacy-customer.customers.update");

    /// <summary>Forwards both captured validators without rereading or replaying a write.</summary>
    public Task<HttpResponseMessage> SaveAsync<T>(int id, string kind, string relation, T input,
        string version, string customerVersion, HttpContext context, CancellationToken token)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, Path(id, kind, relation, "/versioned")) { Content = JsonContent.Create(input, options: LegacyJson) };
        request.Headers.TryAddWithoutValidation("If-Match", version);
        request.Headers.TryAddWithoutValidation("X-Customer-If-Match", customerVersion);
        return credentials.SendAsync(client, request, Permission(kind, relation, true), context, token, "legacy-customer.customers.update");
    }

    /// <summary>Rejects missing current read/write permissions before reading or sending a mutation.</summary>
    public Task<HttpResponseMessage> CheckWritePermissionsAsync(string kind, string relation, HttpContext context, CancellationToken token) =>
        credentials.CheckPermissionsAsync([Permission(kind, relation, false), Permission(kind, relation, true), "legacy-customer.customers.update"], context, token);

    private static string Permission(string kind, string relation, bool write) =>
        $"legacy-customer.{(kind == "company" ? "companies" : "addresses")}.{(relation == "new" ? "create" : write ? "update" : "read")}";
    private static string Path(int id, string kind, string relation, string suffix) =>
        $"/customers/{id}/relations/{kind}/{(kind == "company" ? "" : "address/")}{relation}{suffix}";
}
