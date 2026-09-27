using System.Text.Json;
using FilesMate.Platform.Windows.Shell;

namespace FilesMate.App.Navigation;

/// <summary>Versioned navigation data, never file operations or executable commands.</summary>
internal sealed record TabTransferPayload(int Version, int SourceProcessId, Guid Id, ClosedTabState State)
{
    public const string Format = "FilesMate.NavigatorTab.v1";
    [System.Text.Json.Serialization.JsonIgnore]
    public string ReceiptName => $"Local\\FilesMate.TabTransfer.{SourceProcessId}.{Id:N}";
    internal const int MaximumLength = 1024 * 1024;
    public string Serialize() => JsonSerializer.Serialize(this);

    public static TabTransferPayload? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaximumLength) return null;
        try
        {
            var value = JsonSerializer.Deserialize<TabTransferPayload>(json);
            return value is { Version: 1, SourceProcessId: > 0 } && value.Id != Guid.Empty
                && value.State is { } state && ValidPane(state.Left)
                && (state.Right is null || ValidPane(state.Right)) ? value : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool ValidPane(ClosedPaneState? pane) => pane is not null
        && ValidLocation(pane.Path) && pane.View is not null
        && double.IsFinite(pane.ScrollOffset) && pane.ScrollOffset >= 0
        && pane.FilterQuery is { Length: <= 8192 }
        && (pane.SelectedNames is null || pane.SelectedNames.Length <= 10000 && pane.SelectedNames.All(name =>
            name is { Length: > 0 and <= 32768 } && name.IndexOfAny(['\\', '/', '\0']) < 0))
        && (pane.History is null || ValidHistory(pane.History.Back) && ValidHistory(pane.History.Forward));

    private static bool ValidHistory(string[]? items) => items is { Length: <= 4096 } && items.All(ValidLocation);
    private static bool ValidLocation(string? path) => path is { Length: > 0 and <= 32768 }
        && !path.Contains('\0') && (HomeLocation.IsHome(path) || TagLocation.TryParse(path, out _)
            || PortableDeviceLocation.TryParse(path, out _) || Path.IsPathFullyQualified(path));
}
