extern alias Bff;

using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Time.Testing;
using DiagnosticEventStore = Bff::Legacy.Maliev.Intranet.Bff.Diagnostics.DiagnosticEventStore;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class DiagnosticEventStoreTests
{
    [Fact]
    public void Record_RedactsIdentifiersQueryDataAndUnsafeTokens()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero));
        var store = new DiagnosticEventStore(clock);

        store.RecordResponseFailure(
            500,
            "/orders/123/customers/5/secret@example.com?token=secret@example.com",
            "trace value");

        var item = Assert.Single(store.Query(DiagnosticEventSort.LogTimestamp_Descending, null, 1, 50).Items);
        Assert.Equal("Error", item.Level);
        Assert.Equal("HTTP_500", item.Code);
        Assert.Equal("Legacy.Maliev.Intranet.Bff.Request", item.Category);
        Assert.Equal("/orders/{id}/customers/{id}/{redacted}", item.Path);
        Assert.Equal("unavailable", item.CorrelationId);
        Assert.DoesNotContain("token", item.Path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret@example.com", string.Join('|', item.Level, item.Code, item.Category, item.Path, item.CorrelationId), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(clock.GetUtcNow(), item.Timestamp);
    }

    [Fact]
    public void Query_IsBoundedSearchableAndKeepsOnlyTheMostRecentEvents()
    {
        var store = new DiagnosticEventStore(new FakeTimeProvider());
        for (var index = 1; index <= 205; index++)
        {
            store.RecordResponseFailure(500 + (index % 100), $"/orders/{index}", $"trace-{index}");
        }

        var newest = store.Query(DiagnosticEventSort.LogId_Descending, null, 1, 500);
        var match = store.Query(DiagnosticEventSort.LogId_Ascending, "trace-205", 1, 25);

        Assert.Equal(200, newest.TotalRecords);
        Assert.Equal(100, newest.Items.Count);
        Assert.Equal(205, newest.Items[0].Id);
        Assert.Equal(106, newest.Items[^1].Id);
        Assert.Equal(205, Assert.Single(match.Items).Id);
    }
}
