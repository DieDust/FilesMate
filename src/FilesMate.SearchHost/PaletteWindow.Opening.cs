using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FilesMate.App.Models;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private ItemOpeningMode _fileOpeningMode, _folderOpeningMode, _pressedOpeningMode;
    private bool _pressedName, _pressedModified, _openingMoved;
    private readonly ItemClickTracker _openingClicks = new();
    public static readonly DependencyProperty SelectionCheckboxesProperty = DependencyProperty.Register(
        nameof(SelectionCheckboxes), typeof(bool), typeof(PaletteWindow), new PropertyMetadata(false));
    public bool SelectionCheckboxes { get => (bool)GetValue(SelectionCheckboxesProperty); set => SetValue(SelectionCheckboxesProperty, value); }
    private ItemOpeningMode OpeningMode(SearchRow row) => row.Hit.IsDirectory ? _folderOpeningMode : _fileOpeningMode;
    private void LoadOpeningPreferences()
    {
        _fileOpeningMode = _folderOpeningMode = ItemOpeningMode.DoubleClick;
        _openingClicks.CancelPendingClick();
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_host.Profile, "explorer.json")));
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected preference object.");
            _fileOpeningMode = Read("fileOpeningMode");
            _folderOpeningMode = Read("folderOpeningMode");
            ItemOpeningMode Read(string key) => json.RootElement.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String && !int.TryParse(value.GetString(), out _)
                && Enum.TryParse<ItemOpeningMode>(value.GetString(), true, out var mode) && Enum.IsDefined(mode) ? mode : ItemOpeningMode.DoubleClick;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        SelectionCheckboxes = _fileOpeningMode == ItemOpeningMode.SingleClick || _folderOpeningMode == ItemOpeningMode.SingleClick;
    }
    private static bool OnResultName(object source) => Ancestor<TextBlock>(source as DependencyObject)?.Name == "ResultName";
    private void ResultName_Enter(object sender, MouseEventArgs e)
    {
        if (sender is TextBlock { DataContext: SearchRow row } text && !row.IsApplication && OpeningMode(row) == ItemOpeningMode.NameClick)
        { text.Cursor = Cursors.Hand; text.TextDecorations = TextDecorations.Underline; }
    }
    private void ResultName_Leave(object sender, MouseEventArgs e)
    { if (sender is TextBlock text) { text.ClearValue(CursorProperty); text.TextDecorations = null; } }
    private bool ShouldOpenResult(SearchRow row, MouseButtonEventArgs e)
    {
        if (_pressedModified || _openingMoved || Keyboard.Modifiers != ModifierKeys.None || _pressedOpeningMode != OpeningMode(row))
        { _openingClicks.CancelPendingClick(); return false; }
        var point = e.GetPosition(this);
        var dpi = VisualTreeHelper.GetDpi(this);
        var size = System.Windows.Forms.SystemInformation.DoubleClickSize;
        return _openingClicks.ShouldOpen(row.Path, OpeningMode(row), _pressedName && OnResultName(e.OriginalSource),
            Environment.TickCount64, point.X, point.Y, System.Windows.Forms.SystemInformation.DoubleClickTime,
            size.Width / (2 * dpi.DpiScaleX), size.Height / (2 * dpi.DpiScaleY));
    }
}
