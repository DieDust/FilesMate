using FilesMate.App.Models;
using FilesMate.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileRow
{
    private CancellationTokenSource? _propertyRead;
    private string? _propertySignature;
    private long _propertyVersion;

    private void CancelPropertyRead()
    {
        ++_propertyVersion;
        _propertyRead?.Cancel();
        _propertyRead?.Dispose();
        _propertyRead = null;
        _propertySignature = null;
    }

    private async void RefreshProperties()
    {
        if (EntryId < 0 || _entryPath is not { Length: > 0 } path) return;
        var columns = _columns?.Where(c => c.Visible && c.Id == DetailsColumnId.ShellProperty).ToArray() ?? [];
        var signature = string.Join('|', columns.Select(c => c.PropertyName));
        if (_propertySignature == signature) return;
        CancelPropertyRead();
        _propertySignature = signature;
        if (columns.Length == 0) return;
        _propertyRead = new();
        var token = _propertyRead.Token;
        var version = _propertyVersion;
        try
        {
            var values = await FilePropertyCache.GetAsync(path, Entry, columns.Select(c => c.PropertyName!).ToArray(), token);
            if (token.IsCancellationRequested || version != _propertyVersion) return;
            foreach (var column in columns)
                if (_extraCells.TryGetValue(column.Key, out var cell) && cell.Visibility == Visibility.Visible)
                {
                    cell.Text = values.GetValueOrDefault(column.PropertyName!)?.Text ?? "";
                    ToolTipService.SetToolTip(cell, cell.Text);
                }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("File metadata: {0}", error.Message); }
    }

    internal string ColumnText(DetailsColumn column) => column.Id switch
    {
        DetailsColumnId.Name => NameText.Text,
        DetailsColumnId.Modified => ModifiedText.Text,
        DetailsColumnId.Type => TypeText.Text,
        DetailsColumnId.Size => SizeText.Text,
        DetailsColumnId.Tags => string.Join(" ", _tags.Select(t => t.Name)),
        _ => _extraCells.GetValueOrDefault(column.Key)?.Text ?? ""
    };
}
