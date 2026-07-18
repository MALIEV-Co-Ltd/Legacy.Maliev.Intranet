using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Quotations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationCreationWorkflowTests
{
    [Fact]
    public async Task CreateAsync_SameWorkflowAndFingerprint_ReplaysCommittedResult()
    {
        var gateway = new RecordingGateway();
        var workflow = new QuotationCreationWorkflow(
            new InMemoryQuotationCreationStateStore(),
            NullLogger<QuotationCreationWorkflow>.Instance);
        var input = Request();
        var priced = QuotationPricing.Calculate(input, DateTimeOffset.Parse("2026-07-18T00:00:00Z"));

        var first = await workflow.CreateAsync("employee:abc:quotation:create", "fingerprint", input, priced, gateway, CancellationToken.None);
        var replay = await workflow.CreateAsync("employee:abc:quotation:create", "fingerprint", input, priced, gateway, CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal(77, first.Id);
        Assert.Null(first.Warning);
        Assert.Equal(1, gateway.QuotationCreates);
        Assert.Equal(2, gateway.LineCreates);
        Assert.Equal(1, gateway.LinkCreates);
        Assert.Equal(1, gateway.StatusTransitions);
        Assert.Equal(1, gateway.Finalizations);
        Assert.All(gateway.IdempotencyKeys, key => Assert.StartsWith(gateway.AttemptId, key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_SameWorkflowDifferentPayload_FailsClosedBeforeCallingGateway()
    {
        var gateway = new RecordingGateway();
        var workflow = new QuotationCreationWorkflow(
            new InMemoryQuotationCreationStateStore(),
            NullLogger<QuotationCreationWorkflow>.Instance);
        var input = Request();
        var priced = QuotationPricing.Calculate(input, DateTimeOffset.Parse("2026-07-18T00:00:00Z"));
        await workflow.CreateAsync("employee:abc:quotation:create", "first", input, priced, gateway, CancellationToken.None);

        await Assert.ThrowsAsync<QuotationCreationConflictException>(() => workflow.CreateAsync(
            "employee:abc:quotation:create", "different", input, priced, gateway, CancellationToken.None));

        Assert.Equal(1, gateway.QuotationCreates);
    }

    [Fact]
    public async Task CreateAsync_FinalizationFailure_CommitsWithExplicitWarningAndDoesNotRepeatDelivery()
    {
        var gateway = new RecordingGateway { FinalizationFailure = new HttpRequestException("notification unavailable") };
        var workflow = new QuotationCreationWorkflow(
            new InMemoryQuotationCreationStateStore(),
            NullLogger<QuotationCreationWorkflow>.Instance);
        var input = Request();
        var priced = QuotationPricing.Calculate(input, DateTimeOffset.Parse("2026-07-18T00:00:00Z"));

        var first = await workflow.CreateAsync("employee:abc:quotation:create", "fingerprint", input, priced, gateway, CancellationToken.None);
        var replay = await workflow.CreateAsync("employee:abc:quotation:create", "fingerprint", input, priced, gateway, CancellationToken.None);

        Assert.Equal("Quotation created, but its document delivery requires attention.", first.Warning);
        Assert.Equal(first, replay);
        Assert.Equal(1, gateway.Finalizations);
    }

    private static QuotationCreateRequest Request() => new(
        3,
        2,
        1,
        30,
        "Courier",
        "Bangkok",
        "Net 30",
        "fixture",
        false,
        [new(42, "Order", 2, 50m, 0m), new(null, "Manual", 1, 20m, 0m)]);

    private sealed class RecordingGateway : IQuotationCreationGateway
    {
        public int QuotationCreates { get; private set; }
        public int LineCreates { get; private set; }
        public int LinkCreates { get; private set; }
        public int StatusTransitions { get; private set; }
        public int Finalizations { get; private set; }
        public string AttemptId { get; private set; } = string.Empty;
        public List<string> IdempotencyKeys { get; } = [];
        public Exception? FinalizationFailure { get; init; }

        public Task<int> CreateQuotationAsync(QuotationCreateRequest input, PricedQuotation priced, string idempotencyKey, CancellationToken cancellationToken)
        {
            QuotationCreates++;
            Capture(idempotencyKey);
            return Task.FromResult(77);
        }

        public Task<int> CreateLineAsync(int quotationId, PricedQuotationLine line, string idempotencyKey, CancellationToken cancellationToken)
        {
            LineCreates++;
            Capture(idempotencyKey);
            return Task.FromResult(100 + LineCreates);
        }

        public Task<int> CreateOrderLinkAsync(int quotationId, int orderId, string idempotencyKey, CancellationToken cancellationToken)
        {
            LinkCreates++;
            Capture(idempotencyKey);
            return Task.FromResult(200 + LinkCreates);
        }

        public Task MarkOrderQuotedAsync(int orderId, string idempotencyKey, CancellationToken cancellationToken)
        {
            StatusTransitions++;
            Capture(idempotencyKey);
            return Task.CompletedTask;
        }

        public Task FinalizeDocumentDeliveryAsync(int quotationId, QuotationCreateRequest input, PricedQuotation priced, CancellationToken cancellationToken)
        {
            Finalizations++;
            return FinalizationFailure is null ? Task.CompletedTask : Task.FromException(FinalizationFailure);
        }

        private void Capture(string key)
        {
            AttemptId = string.IsNullOrEmpty(AttemptId) ? key[..key.IndexOf(':')] : AttemptId;
            IdempotencyKeys.Add(key);
        }
    }
}
