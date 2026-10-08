namespace Legacy.Maliev.Intranet.Client.Features.Orders.Components;

/// <summary>Page-owned pending result survives child disposal; it is never an authority credential.</summary>
public sealed class ReplacementPendingState
{
    public ReplacementPendingOperation? Operation { get; set; }
    public bool OriginalWriteInProgress { get; set; }
}
public sealed record ReplacementPendingOperation(Guid OperationId, string Path, string Payload, int OrderId, int CaseId, string? EmployeeSubject, bool Dispatched = false);
