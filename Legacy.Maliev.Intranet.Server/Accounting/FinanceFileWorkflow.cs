using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.Server.Accounting;

/// <summary>Coordinates scanned Finance file storage and Accounting metadata outside transport code.</summary>
public sealed class FinanceFileWorkflow(ILogger<FinanceFileWorkflow> logger)
{
    /// <summary>Links every scanned upload and compensates both boundaries when linking fails.</summary>
    public async Task<IReadOnlyList<FinanceFileItem>> UploadAsync(
        int paymentId,
        IReadOnlyList<IFormFile> files,
        Func<IReadOnlyList<IFormFile>, CancellationToken, Task<IReadOnlyList<FinanceStoredFile>>> upload,
        Func<int, FinanceStoredFile, CancellationToken, Task<FinanceFileItem>> link,
        Func<FinanceFileItem, CancellationToken, Task> unlink,
        Func<FinanceStoredFile, CancellationToken, Task> delete,
        CancellationToken cancellationToken)
    {
        var stored = await upload(files, cancellationToken);
        var linked = new List<FinanceFileItem>();
        try
        {
            foreach (var item in stored)
            {
                linked.Add(await link(paymentId, item, cancellationToken));
            }

            return linked;
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (var item in linked)
            {
                await TryCompensateAsync("unlink payment-file metadata", item.Id, () => unlink(item, cleanup.Token));
            }

            foreach (var item in stored)
            {
                await TryCompensateAsync("delete stored payment file", 0, () => delete(item, cleanup.Token));
            }

            throw;
        }
    }

    /// <summary>Removes only metadata resolved as owned by the requested payment.</summary>
    public async Task<bool> RemoveAsync(
        int fileId,
        IReadOnlyList<FinanceFileItem> ownedFiles,
        Func<FinanceFileItem, CancellationToken, Task> delete,
        Func<int, CancellationToken, Task> unlink,
        CancellationToken cancellationToken)
    {
        var owned = ownedFiles.SingleOrDefault(file => file.Id == fileId);
        if (owned is null)
        {
            return false;
        }

        // Storage first is deliberate: a retry can tolerate a missing object and then remove metadata.
        await delete(owned, cancellationToken);
        await unlink(owned.Id, cancellationToken);
        return true;
    }

    private async Task TryCompensateAsync(string operation, int fileId, Func<Task> compensate)
    {
        try
        {
            await compensate();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            logger.LogError(exception, "Failed to {CompensationOperation} for Finance file {FileId}.", operation, fileId);
        }
    }
}

/// <summary>Server-owned FileService identity for one clean Finance upload.</summary>
public sealed record FinanceStoredFile(string Bucket, string ObjectName);
