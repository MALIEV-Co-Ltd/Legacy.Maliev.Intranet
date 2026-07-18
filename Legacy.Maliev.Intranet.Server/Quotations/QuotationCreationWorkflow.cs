using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.Server.Quotations;

/// <summary>Server-owned, durable orchestration for quotation creation.</summary>
public sealed class QuotationCreationWorkflow(
    IQuotationCreationStateStore stateStore,
    ILogger<QuotationCreationWorkflow> logger)
{
    private const string DeliveryWarning = "Quotation created, but its document delivery requires attention.";

    /// <summary>Creates a stable guard over every browser-owned input field.</summary>
    public static string CreateFingerprint(QuotationCreateRequest input) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            input,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    /// <summary>Creates all required quotation children exactly once and finalizes delivery at most once.</summary>
    public Task<QuotationCreatedResult> CreateAsync(
        string workflowKey,
        string fingerprint,
        QuotationCreateRequest input,
        PricedQuotation priced,
        IQuotationCreationGateway gateway,
        CancellationToken cancellationToken) =>
        stateStore.ExecuteLockedAsync(
            workflowKey,
            token => CreateLockedAsync(workflowKey, fingerprint, input, priced, gateway, token),
            cancellationToken);

    private async Task<QuotationCreatedResult> CreateLockedAsync(
        string workflowKey,
        string fingerprint,
        QuotationCreateRequest input,
        PricedQuotation priced,
        IQuotationCreationGateway gateway,
        CancellationToken cancellationToken)
    {
        var state = await stateStore.GetAsync(workflowKey, cancellationToken);
        if (state is not null && !string.Equals(state.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new QuotationCreationConflictException();
        }

        if (state?.Phase == QuotationCreationPhase.Completed && state.Result is not null) return state.Result;
        if (state is null)
        {
            state = new(
                fingerprint,
                Guid.NewGuid().ToString("D"),
                QuotationCreationPhase.Active,
                null,
                0,
                [],
                0,
                null);
            await stateStore.SetAsync(workflowKey, state, cancellationToken);
        }

        if (state.QuotationId is null)
        {
            var quotationId = await gateway.CreateQuotationAsync(
                input,
                priced,
                Key(state, "quotation"),
                cancellationToken);
            state = state with { QuotationId = quotationId };
            await stateStore.SetAsync(workflowKey, state, cancellationToken);
        }

        while (state.CreatedLineCount < priced.Lines.Count)
        {
            var index = state.CreatedLineCount;
            await gateway.CreateLineAsync(
                state.QuotationId.Value,
                priced.Lines[index],
                Key(state, $"line:{index}"),
                cancellationToken);
            state = state with { CreatedLineCount = index + 1 };
            await stateStore.SetAsync(workflowKey, state, cancellationToken);
        }

        var orderIds = priced.Lines.Where(line => line.OrderId is > 0).Select(line => line.OrderId!.Value).Distinct().ToArray();
        foreach (var orderId in orderIds.Where(orderId => !state.LinkedOrderIds.Contains(orderId)))
        {
            await gateway.CreateOrderLinkAsync(
                state.QuotationId.Value,
                orderId,
                Key(state, $"order-link:{orderId}"),
                cancellationToken);
            state = state with { LinkedOrderIds = state.LinkedOrderIds.Append(orderId).ToArray() };
            await stateStore.SetAsync(workflowKey, state, cancellationToken);
        }

        while (state.QuotedOrderCount < orderIds.Length)
        {
            var index = state.QuotedOrderCount;
            await gateway.MarkOrderQuotedAsync(orderIds[index], Key(state, $"order-status:{orderIds[index]}"), cancellationToken);
            state = state with { QuotedOrderCount = index + 1 };
            await stateStore.SetAsync(workflowKey, state, cancellationToken);
        }

        if (state.Phase == QuotationCreationPhase.Active)
        {
            state = state with { Phase = QuotationCreationPhase.RequiredCommitted };
            await stateStore.SetAsync(workflowKey, state, cancellationToken);
        }

        if (state.Phase == QuotationCreationPhase.FinalizationStarted)
        {
            return await CompleteAsync(workflowKey, state, DeliveryWarning, CancellationToken.None);
        }

        state = state with { Phase = QuotationCreationPhase.FinalizationStarted };
        await stateStore.SetAsync(workflowKey, state, cancellationToken);
        try
        {
            await gateway.FinalizeDocumentDeliveryAsync(state.QuotationId.Value, input, priced, cancellationToken);
            return await CompleteAsync(workflowKey, state, null, cancellationToken);
        }
        catch (Exception exception) when (IsNonfatal(exception))
        {
            logger.LogWarning(exception, "Quotation {QuotationId} was committed but document delivery did not complete.", state.QuotationId);
            return await CompleteAsync(workflowKey, state, DeliveryWarning, CancellationToken.None);
        }
    }

    private async Task<QuotationCreatedResult> CompleteAsync(
        string workflowKey,
        QuotationCreationCheckpoint state,
        string? warning,
        CancellationToken cancellationToken)
    {
        var result = new QuotationCreatedResult(state.QuotationId!.Value, warning);
        await stateStore.SetAsync(
            workflowKey,
            state with { Phase = QuotationCreationPhase.Completed, Result = result },
            cancellationToken);
        return result;
    }

    private static string Key(QuotationCreationCheckpoint state, string operation) =>
        $"{state.DownstreamAttemptId}:{operation}";

    private static bool IsNonfatal(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;
}

/// <summary>Server-only service gateway used by the durable workflow.</summary>
public interface IQuotationCreationGateway
{
    /// <summary>Creates the quotation root with authoritative totals.</summary>
    Task<int> CreateQuotationAsync(QuotationCreateRequest input, PricedQuotation priced, string idempotencyKey, CancellationToken cancellationToken);
    /// <summary>Creates one server-priced quotation line.</summary>
    Task<int> CreateLineAsync(int quotationId, PricedQuotationLine line, string idempotencyKey, CancellationToken cancellationToken);
    /// <summary>Links one existing order to the quotation.</summary>
    Task<int> CreateOrderLinkAsync(int quotationId, int orderId, string idempotencyKey, CancellationToken cancellationToken);
    /// <summary>Transitions one linked order to its quoted state.</summary>
    Task MarkOrderQuotedAsync(int orderId, string idempotencyKey, CancellationToken cancellationToken);
    /// <summary>Performs at-most-once PDF storage and customer delivery.</summary>
    Task FinalizeDocumentDeliveryAsync(int quotationId, QuotationCreateRequest input, PricedQuotation priced, CancellationToken cancellationToken);
}
