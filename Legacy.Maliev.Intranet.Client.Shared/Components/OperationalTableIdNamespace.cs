namespace Legacy.Maliev.Intranet.Client.Shared.Components;

internal static class OperationalTableIdNamespace
{
    private static long nextComponentInstanceId;

    internal static string Allocate() => $"operational-table-{Interlocked.Increment(ref nextComponentInstanceId)}-quick-view";
}
