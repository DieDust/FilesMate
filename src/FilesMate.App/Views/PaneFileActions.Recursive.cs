using FilesMate.App.Localization;
using FilesMate.App.Services;

namespace FilesMate.App.Views;

internal sealed partial class PaneFileActions
{
    public Func<Func<bool>>? CaptureRecursiveOperationGuard { get; set; }
    private readonly record struct RecursiveActionContext(bool Active, Func<bool>? IsCurrent);
    private RecursiveActionContext CaptureRecursiveContext() => RecursiveSelection?.Invoke() == true
        ? new(true, CaptureRecursiveOperationGuard?.Invoke()) : default;

    private static void CheckRecursiveContext(RecursiveActionContext context)
    {
        if (context.Active && context.IsCurrent?.Invoke() != true)
            throw new IOException(StringTable.Get("Recursive_OperationUnavailable"));
    }

    private static async Task CheckRecursiveSourcesAsync(RecursiveActionContext context, IReadOnlyList<string> paths)
    {
        CheckRecursiveContext(context);
        if (context.Active) await RecursiveTransferCompatibility.VerifySourcesAsync(paths);
        CheckRecursiveContext(context);
    }
}
