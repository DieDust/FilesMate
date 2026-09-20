using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private InfoBar? _transferResultNotice;
    private FileUndoRecord? _transferResultRecord;

    internal void ShowPermanentDeletionFeedback(int count, FileUndoRecord? recycled)
    {
        // Reuse the persistent result surface; Undo applies only to the explicitly recycled subset.
        ShowTransferFeedback(new ShelfTransferResult([], [StringTable.Format("Files_PermanentlyDeletedCount", count)], false) { Undo = recycled });
        if (_transferResultNotice is null) return;
        _transferResultNotice.Title = StringTable.Format("Files_PermanentlyDeletedCount", count);
        _transferResultNotice.Severity = InfoBarSeverity.Informational;
        _transferResultNotice.Content = recycled is null ? null : new TextBlock
        {
            Text = StringTable.Format("Files_RecycledUndoOnly", recycled.Paths.Count), TextWrapping = TextWrapping.Wrap,
        };
        if (_transferResultNotice.ActionButton is Button undo)
            undo.Content = StringTable.Get("Files_UndoRecycledOnly");
    }

    internal void ShowTransferFeedback(ShelfTransferResult result)
    {
        if (_disposed || !IsLoaded) return;
        if (!TransferFeedback.NeedsAttention(result))
        {
            if (_transferResultNotice is not null) _transferResultNotice.IsOpen = false;
            return;
        }
        if (_transferResultNotice is null)
        {
            _transferResultNotice = new InfoBar
            {
                IsClosable = true, MaxWidth = 680, Margin = new Thickness(16, 0, 16, 44),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            };
            Canvas.SetZIndex(_transferResultNotice, 31);
            ShellRoot.Children.Add(_transferResultNotice);
        }
        _transferResultRecord = result.Undo;
        _transferResultNotice.ActionButton = null;
        if (result.Undo is { } record && ReferenceEquals(App.FileUndo.Latest, record))
        {
            var undo = new Button { Content = StringTable.Get("Undo") };
            undo.Click += (_, _) =>
            {
                if (ReferenceEquals(App.FileUndo.Latest, record)) _fileActions.Undo();
            };
            _transferResultNotice.ActionButton = undo;
        }
        _transferResultNotice.Title = TransferFeedback.Summary(result);
        _transferResultNotice.Severity = result.Errors.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        _transferResultNotice.Content = result.Errors.Count == 0 ? null : new Expander
        {
            Header = StringTable.Get("Transfer_Details"), HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new ScrollViewer
            {
                MaxHeight = 220, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    Text = string.Join(Environment.NewLine, result.Errors), TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true, MaxWidth = 540,
                },
            },
        };
        _transferResultNotice.IsOpen = true;
        if (_operationNotice is not null) _operationNotice.Visibility = Visibility.Collapsed;
        _operationNoticeTimer?.Stop();
    }
}
