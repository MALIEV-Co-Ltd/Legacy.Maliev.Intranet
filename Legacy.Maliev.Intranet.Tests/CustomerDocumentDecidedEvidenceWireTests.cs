using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Exercises actual BFF projection of immutable evidence; downstream authority remains synthetic.</summary>
public sealed class CustomerDocumentDecidedEvidenceWireTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var route in new[] { "receipt", "history", "verification" })
        foreach (var status in new[] { "Verified", "Rejected" })
        {
            foreach (var fault in new[] { "missing-actor", "blank-actor", "missing-time", "default-time", "non-utc-time" })
                yield return [route, status, fault, HttpStatusCode.ServiceUnavailable];
            yield return [route, status, "complete", HttpStatusCode.OK];
        }
        foreach (var route in new[] { "receipt", "history" })
            yield return [route, "PendingVerification", "complete", HttpStatusCode.OK];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ActualMappedEvidenceRefusesMalformedDecisionsAndPreservesValidStates(
        string route, string status, string fault, HttpStatusCode expected)
    {
        var document = Guid.NewGuid();
        var version = Guid.NewGuid();
        var digest = new string('a', 64);
        var at = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        string? actor = status == "PendingVerification" ? null : "synthetic-employee";
        DateTimeOffset? verifiedAt = status == "PendingVerification" ? null : at;
        switch (fault)
        {
            case "missing-actor": actor = null; break;
            case "blank-actor": actor = " \t "; break;
            case "missing-time": verifiedAt = null; break;
            case "default-time": verifiedAt = default(DateTimeOffset); break;
            case "non-utc-time": verifiedAt = at.ToOffset(TimeSpan.FromHours(7)); break;
        }
        var receipt = new CustomerDocumentReceipt(document, version, 42, "Evidence", digest, null, [],
            status, actor, verifiedAt, 8);
        var payload = route == "history"
            ? JsonSerializer.Serialize(new[] { new CustomerDocumentVersionSummary(document, version, 1,
                "Evidence", digest, at, status, actor, verifiedAt, 8) })
            : JsonSerializer.Serialize(receipt);
        var handler = new CustomerDocumentBffTests.RecordingHandler(payload);
        await using var app = await CustomerDocumentBffTests.HostAsync(handler);
        using var http = app.GetTestClient();
        http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        var path = route == "history"
            ? $"/bff/customers/42/documents/{document:D}/versions"
            : $"/bff/customers/42/documents/{document:D}/versions/{version:D}/{route}";
        if (route == "verification")
        {
            using var csrf = await http.GetAsync("/fixture/csrf");
            Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
            http.DefaultRequestHeaders.Add("Cookie", csrf.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
            http.DefaultRequestHeaders.Add("RequestVerificationToken", await csrf.Content.ReadFromJsonAsync<string>());
        }
        using var response = route == "verification"
            ? await http.PostAsJsonAsync(path, new CustomerDocumentVerificationRequest(7, status, "Synthetic exact evidence review"))
            : await http.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(path.Replace("/bff", "", StringComparison.Ordinal), handler.Path);
        Assert.Equal("Bearer synthetic-acting-token", handler.Authorization);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("synthetic-acting-token", body);
        if (expected == HttpStatusCode.ServiceUnavailable)
        {
            Assert.DoesNotContain(digest, body);
            Assert.DoesNotContain(document.ToString("D"), body);
            Assert.DoesNotContain(version.ToString("D"), body);
        }
        else
        {
            using var returned = JsonDocument.Parse(body);
            var evidence = route == "history" ? returned.RootElement[0] : returned.RootElement;
            Assert.Equal(status, evidence.GetProperty("verificationStatus").GetString());
            Assert.Equal(8, evidence.GetProperty("revision").GetInt64());
        }
    }
}
