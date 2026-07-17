using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.Server.Orders;

/// <summary>Coordinates the durable, replay-safe cross-service order creation saga.</summary>
public sealed class OrderCreationWorkflow(
    ILogger<OrderCreationWorkflow> logger,
    IOrderCreationStateStore stateStore)
{
    private const string NotificationWarning = "Order created, but the confirmation notification failed.";

    /// <summary>Computes a stable identity guard over every browser-owned field and uploaded byte.</summary>
    public static async Task<string> CreateFingerprintAsync(
        OrderCreateRequest input,
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(input, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var buffer = new byte[64 * 1024];
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"\n{file.FileName}\n{file.ContentType}\n{file.Length}\n"));
            await using var stream = file.OpenReadStream();
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Creates an order and required children exactly once per durable workflow identity.</summary>
    public Task<OrderCreatedResult> CreateAsync(
        string workflowKey,
        string fingerprint,
        OrderCreateRequest input,
        string? customerEmail,
        IReadOnlyList<IFormFile> files,
        Func<OrderCreateRequest, string, CancellationToken, Task<int>> CreateOrder,
        Func<int, IReadOnlyList<IFormFile>, string, CancellationToken, Task<IReadOnlyList<StoredOrderFile>>> UploadFiles,
        Func<int, StoredOrderFile, CancellationToken, Task<int>> CreateOrderFile,
        Func<int, string, CancellationToken, Task> CreateInitialStatus,
        Func<string, int, CancellationToken, Task> SendNotification,
        Func<int, CancellationToken, Task> DeleteOrderFile,
        Func<StoredOrderFile, CancellationToken, Task> DeleteStoredFile,
        Func<int, CancellationToken, Task> DeleteOrder,
        CancellationToken cancellationToken) =>
        stateStore.ExecuteLockedAsync(
            workflowKey,
            token => CreateLockedAsync(
                workflowKey,
                fingerprint,
                input,
                customerEmail,
                files,
                CreateOrder,
                UploadFiles,
                CreateOrderFile,
                CreateInitialStatus,
                SendNotification,
                DeleteOrderFile,
                DeleteStoredFile,
                DeleteOrder,
                token),
            cancellationToken);

    private async Task<OrderCreatedResult> CreateLockedAsync(
        string workflowKey,
        string fingerprint,
        OrderCreateRequest input,
        string? customerEmail,
        IReadOnlyList<IFormFile> files,
        Func<OrderCreateRequest, string, CancellationToken, Task<int>> createOrder,
        Func<int, IReadOnlyList<IFormFile>, string, CancellationToken, Task<IReadOnlyList<StoredOrderFile>>> uploadFiles,
        Func<int, StoredOrderFile, CancellationToken, Task<int>> createOrderFile,
        Func<int, string, CancellationToken, Task> createInitialStatus,
        Func<string, int, CancellationToken, Task> sendNotification,
        Func<int, CancellationToken, Task> deleteOrderFile,
        Func<StoredOrderFile, CancellationToken, Task> deleteStoredFile,
        Func<int, CancellationToken, Task> deleteOrder,
        CancellationToken cancellationToken)
    {
        var state = await stateStore.GetAsync(workflowKey, cancellationToken);
        if (state is not null && !string.Equals(state.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new OrderCreationConflictException();
        }

        if (state?.Phase == OrderCreationPhase.Completed && state.Result is not null)
        {
            return state.Result;
        }

        if (state?.Phase == OrderCreationPhase.Compensating)
        {
            if (!await CompensateAsync(state, deleteOrderFile, deleteStoredFile, deleteOrder))
            {
                throw new OrderCreationOutcomeUnknownException("The prior order creation attempt is still compensating.");
            }

            await stateStore.RemoveAsync(workflowKey, CancellationToken.None);
            state = null;
        }

        if (state is null)
        {
            state = new(
                fingerprint,
                Guid.NewGuid().ToString("D"),
                OrderCreationPhase.Active,
                null,
                [],
                [],
                0,
                null);
            await stateStore.SetAsync(workflowKey, state, cancellationToken);
        }
        try
        {
            if (state.OrderId is null)
            {
                var orderId = await createOrder(input, state.DownstreamAttemptId, cancellationToken);
                state = state with { OrderId = orderId };
                await stateStore.SetAsync(workflowKey, state, cancellationToken);
            }

            if (files.Count > 0 && state.StoredFiles.Count == 0)
            {
                state = state with { Phase = OrderCreationPhase.Uploading };
                await stateStore.SetAsync(workflowKey, state, cancellationToken);
                var stored = await uploadFiles(input.CustomerId, files, state.DownstreamAttemptId, cancellationToken);
                state = state with { Phase = OrderCreationPhase.Active, StoredFiles = stored };
                await stateStore.SetAsync(workflowKey, state, cancellationToken);
            }

            while (state.LinkedFileCount < state.StoredFiles.Count)
            {
                var stored = state.StoredFiles[state.LinkedFileCount];
                var linkedId = await createOrderFile(state.OrderId.Value, stored, cancellationToken);
                state = state with
                {
                    LinkedFileIds = state.LinkedFileIds.Append(linkedId).ToArray(),
                    LinkedFileCount = state.LinkedFileCount + 1,
                };
                await stateStore.SetAsync(workflowKey, state, cancellationToken);
            }

            if (state.Phase is OrderCreationPhase.Active or OrderCreationPhase.StatusUncertain)
            {
                try
                {
                    await createInitialStatus(state.OrderId.Value, state.DownstreamAttemptId, cancellationToken);
                }
                catch (OrderCreationOutcomeUnknownException)
                {
                    state = state with { Phase = OrderCreationPhase.StatusUncertain };
                    await stateStore.SetAsync(workflowKey, state, CancellationToken.None);
                    throw;
                }

                state = state with { Phase = OrderCreationPhase.RequiredCommitted };
                await stateStore.SetAsync(workflowKey, state, cancellationToken);
            }
        }
        catch (OrderCreationOutcomeUnknownException)
        {
            throw;
        }
        catch (OrderCreationBusyException)
        {
            throw;
        }
        catch (Exception exception) when (IsCancellationOrTimeout(exception))
        {
            throw new OrderCreationOutcomeUnknownException(
                "The request ended after the durable workflow started; its checkpoint was retained for same-key reconciliation.",
                exception);
        }
        catch
        {
            state = state with { Phase = OrderCreationPhase.Compensating };
            await stateStore.SetAsync(workflowKey, state, CancellationToken.None);
            if (await CompensateAsync(state, deleteOrderFile, deleteStoredFile, deleteOrder))
            {
                await stateStore.RemoveAsync(workflowKey, CancellationToken.None);
            }
            throw;
        }

        if (!input.SendConfirmationEmail)
        {
            return await CompleteAsync(workflowKey, state, new(state.OrderId.Value, null), cancellationToken);
        }

        if (state.Phase == OrderCreationPhase.NotificationStarted)
        {
            return await CompleteAsync(workflowKey, state, new(state.OrderId.Value, NotificationWarning), cancellationToken);
        }

        state = state with { Phase = OrderCreationPhase.NotificationStarted };
        await stateStore.SetAsync(workflowKey, state, cancellationToken);
        try
        {
            await sendNotification(customerEmail!, state.OrderId.Value, cancellationToken);
            return await CompleteAsync(workflowKey, state, new(state.OrderId.Value, null), cancellationToken);
        }
        catch (Exception exception) when (IsNonfatal(exception))
        {
            logger.LogWarning(exception, "Order {OrderId} was created but its optional confirmation notification failed.", state.OrderId);
            return await CompleteAsync(workflowKey, state, new(state.OrderId.Value, NotificationWarning), CancellationToken.None);
        }
    }

    private async Task<OrderCreatedResult> CompleteAsync(
        string workflowKey,
        OrderCreationCheckpoint state,
        OrderCreatedResult result,
        CancellationToken cancellationToken)
    {
        await stateStore.SetAsync(
            workflowKey,
            state with { Phase = OrderCreationPhase.Completed, Result = result },
            cancellationToken);
        return result;
    }

    private async Task<bool> CompensateAsync(
        OrderCreationCheckpoint state,
        Func<int, CancellationToken, Task> deleteOrderFile,
        Func<StoredOrderFile, CancellationToken, Task> deleteStoredFile,
        Func<int, CancellationToken, Task> deleteOrder)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var succeeded = true;
        foreach (var fileId in state.LinkedFileIds.Reverse())
        {
            succeeded &= await TryCompensateAsync("delete order-file metadata", state.OrderId, () => deleteOrderFile(fileId, cleanup.Token));
        }
        foreach (var stored in state.StoredFiles.Reverse())
        {
            succeeded &= await TryCompensateAsync("delete stored order file", state.OrderId, () => deleteStoredFile(stored, cleanup.Token));
        }
        if (state.OrderId is not null)
        {
            succeeded &= await TryCompensateAsync("delete order", state.OrderId, () => deleteOrder(state.OrderId.Value, cleanup.Token));
        }
        return succeeded;
    }

    private async Task<bool> TryCompensateAsync(string operation, int? orderId, Func<Task> compensate)
    {
        try
        {
            await compensate();
            return true;
        }
        catch (Exception exception) when (IsNonfatal(exception))
        {
            logger.LogError(exception, "Failed to {CompensationOperation} while rolling back order {OrderId}.", operation, orderId);
            return false;
        }
    }

    private static bool IsNonfatal(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    private static bool IsCancellationOrTimeout(Exception exception) =>
        exception is OperationCanceledException ||
        exception is HttpRequestException { StatusCode: not null } request && (int)request.StatusCode.Value >= 500 ||
        string.Equals(exception.GetType().FullName, "Polly.Timeout.TimeoutRejectedException", StringComparison.Ordinal);
}
