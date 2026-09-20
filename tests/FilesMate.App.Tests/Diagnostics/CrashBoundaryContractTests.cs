using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Diagnostics;

public sealed class CrashBoundaryContractTests
{
    [Fact]
    public void Global_xaml_handler_records_but_does_not_resume_a_corrupted_visual_tree()
    {
        var app = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "App.xaml.cs"));
        var handlerStart = app.IndexOf("private static void App_UnhandledException", StringComparison.Ordinal);
        var domainStart = app.IndexOf("private static void CurrentDomain_UnhandledException", handlerStart, StringComparison.Ordinal);
        var handler = app[handlerStart..domainStart];

        Assert.Contains("AppendCrashRecord", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("args.Handled = true", handler, StringComparison.Ordinal);
        Assert.Contains("args.SetObserved()", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Drag_drop_and_tag_creation_keep_expected_com_failures_local()
    {
        var surface = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml.cs"));
        var tags = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Tags",
            "TagPickerFlyout.cs"));

        Assert.Contains("Drop data retrieval failed", surface, StringComparison.Ordinal);
        Assert.Contains("Tag creation failed", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialog", tags, StringComparison.Ordinal);
        Assert.Contains("SetRemoveVisible", tags, StringComparison.Ordinal);
        Assert.Contains("button.Opacity = visible ? 1 : 0", tags, StringComparison.Ordinal);
        Assert.Contains("QuietButtonStyle", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("GlassButtonStyle", tags, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_disposal_waits_for_schema_initialization_before_returning()
    {
        var store = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Services",
            "SqliteFileMetadataStore.cs"));
        var disposeStart = store.IndexOf("public async ValueTask DisposeAsync()", StringComparison.Ordinal);
        var ensureStart = store.IndexOf("private async Task EnsureSchemaAsync", disposeStart, StringComparison.Ordinal);
        var dispose = store[disposeStart..ensureStart];

        Assert.Contains("await _schemaGate.WaitAsync()", dispose, StringComparison.Ordinal);
        Assert.Contains("_schemaGate.Release()", dispose, StringComparison.Ordinal);
        Assert.DoesNotContain("_schemaGate.Dispose()", dispose, StringComparison.Ordinal);
    }
}
