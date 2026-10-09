using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Independent exact Documents partition; does not alter the owner-controlled legacy/replacement census.</summary>
public sealed class CustomerDocumentRouteSurfaceTests
{
    private const string CustomerRoot = "/bff/customers/{customerId:int}/documents";
    private const string ReminderRoot = "/bff/staff/nda-reminders";
    private const string Document = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string Version = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private static readonly (string Method, string Template)[] Expected =
    [
        ("GET", CustomerRoot + "/"),
        ("GET", CustomerRoot + "/{documentId:guid}/versions"),
        ("GET", CustomerRoot + "/{documentId:guid}/nda"),
        ("GET", CustomerRoot + "/{documentId:guid}/versions/{versionId:guid}/receipt"),
        ("GET", CustomerRoot + "/{documentId:guid}/versions/{versionId:guid}/download"),
        ("GET", ReminderRoot),
        ("POST", CustomerRoot + "/"),
        ("POST", CustomerRoot + "/{documentId:guid}/versions"),
        ("POST", CustomerRoot + "/{documentId:guid}/archive"),
        ("POST", CustomerRoot + "/{documentId:guid}/nda/verification"),
        ("POST", CustomerRoot + "/{documentId:guid}/versions/{versionId:guid}/verification"),
    ];

    public static IEnumerable<object[]> AnonymousCases() => Expected.Select(x => new object[] { x.Method, x.Template });
    public static IEnumerable<object[]> UnsupportedCases() => Expected.Select(x => x.Template).Distinct(StringComparer.Ordinal).Select(x => new object[] { x });

    [Fact]
    public async Task RuntimeDocumentsPartitionIsExactlyElevenAuthorizedRoutes()
    {
        await using var app = await CustomerDocumentBffTests.HostAsync(new("[]"));
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(x => x.RoutePattern.RawText is { } path && (path.StartsWith(CustomerRoot, StringComparison.Ordinal) || path.StartsWith(ReminderRoot, StringComparison.Ordinal)))
            .ToArray();
        var actual = endpoints.SelectMany(x => x.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Select(method => method + " " + x.RoutePattern.RawText)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(Expected.Select(x => x.Method + " " + x.Template).Order(StringComparer.Ordinal), actual);
        Assert.Equal(11, actual.Length); Assert.Equal(6, actual.Count(x => x.StartsWith("GET ", StringComparison.Ordinal))); Assert.Equal(5, actual.Count(x => x.StartsWith("POST ", StringComparison.Ordinal)));
        Assert.All(endpoints, endpoint => { Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()); Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>()); });
    }

    [Theory]
    [MemberData(nameof(AnonymousCases))]
    public async Task EveryDeclaredDocumentsMethodRejectsAnonymousBeforeDownstream(string method, string template)
    {
        var handler = new CustomerDocumentBffTests.RecordingHandler("[]"); await using var app = await CustomerDocumentBffTests.HostAsync(handler);
        using var http = app.GetTestClient(); using var request = new HttpRequestMessage(new HttpMethod(method), Materialize(template));
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Null(handler.Path);
    }

    [Theory]
    [MemberData(nameof(UnsupportedCases))]
    public async Task DocumentsPathsRejectUndeclaredPutWithoutDownstream(string template)
    {
        var handler = new CustomerDocumentBffTests.RecordingHandler("[]"); await using var app = await CustomerDocumentBffTests.HostAsync(handler);
        using var http = app.GetTestClient(); using var request = new HttpRequestMessage(HttpMethod.Put, Materialize(template));
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode); Assert.Null(handler.Path);
    }

    private static string Materialize(string template)
    {
        var path = template.Replace("{customerId:int}", "42", StringComparison.Ordinal).Replace("{documentId:guid}", Document, StringComparison.Ordinal).Replace("{versionId:guid}", Version, StringComparison.Ordinal);
        Assert.DoesNotContain("{", path);
        return template.EndsWith("/{documentId:guid}/nda", StringComparison.Ordinal) ? path + "?versionId=" + Version : path;
    }
}
