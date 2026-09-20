#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    internal async Task RunCreateRenameSmokeAsync()
    {
        var samples = new List<object>();
        var output = Path.Combine(AppContext.BaseDirectory, "create-rename-results.json");
        var fixture = Path.Combine(AppContext.BaseDirectory, "create-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(fixture);
            for (int i = 0; i < 300; i++) Directory.CreateDirectory(Path.Combine(fixture, $"Folder-{i:D3}"));
            _leftVm.Navigate(fixture);
            await Until(() => !_leftVm.IsLoading && _leftVm.ItemCount == 300);
            await Task.Delay(450);
            foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.Grid })
            {
                FileSurface.SetLayout(layout);
                _leftVm.RestoreSort(EntrySort.Name with { Ascending = false });
                await Task.Delay(300);
                FileSurface.RestoreScrollOffset(1500);
                await Task.Delay(250);
                await _fileActions.RunAsync(AppCommandId.NewFolder);
                await Until(() => FileSurface.IsRenaming);
                var editor = Editor(FileSurface);
                var createdPath = FileSurface.SelectedPaths().Single();
                Require(Directory.Exists(createdPath), "Created folder is missing");
                Require(editor.SelectedText == Path.GetFileName(createdPath), "Default name is not fully selected");
                Require(editor.FocusState != FocusState.Unfocused, "Name editor did not receive focus");
                var rect = editor.TransformToVisual(FileSurface).TransformBounds(new Rect(0, 0, editor.ActualWidth, editor.ActualHeight));
                Require(rect.Bottom > 0 && rect.Top < FileSurface.ActualHeight, "Created item is outside viewport");
                await Task.Delay(600);
                Require(FileSurface.IsRenaming, "Watcher refresh removed editor");
                editor.Text = "Renamed-" + layout;
                await (Task)Call(FileSurface, "CommitInlineRenameAsync", 0)!;
                await Until(() => Directory.Exists(Path.Combine(fixture, "Renamed-" + layout)) && !_leftVm.IsLoading);
                Require(!Directory.Exists(createdPath), "Commit did not rename folder");

                await _fileActions.RunAsync(AppCommandId.NewFolder);
                await Until(() => FileSurface.IsRenaming);
                var cancelled = FileSurface.SelectedPaths().Single();
                Call(FileSurface, "CancelInlineRename");
                Require(Directory.Exists(cancelled) && !FileSurface.IsRenaming, "Cancel removed new folder");
                await _fileActions.RunAsync(AppCommandId.NewFolder);
                await Until(() => FileSurface.IsRenaming);
                var duplicate = FileSurface.SelectedPaths().Single();
                Require(duplicate != cancelled && Directory.Exists(cancelled), "Duplicate default name overwrote folder");
                Call(FileSurface, "CancelInlineRename");
                samples.Add(new { Layout = layout.ToString(), CreatedSelectedVisible = true,
                    DefaultNameSelected = true, Focused = true, CommitRenamed = true,
                    CancelKeptFolder = true, DuplicateNameSafe = true });
            }
            _leftVm.SetFilterQuery("will-not-match");
            await Task.Delay(200);
            await _fileActions.RunAsync(AppCommandId.NewFolder);
            await Until(() => FileSurface.IsRenaming);
            Require(_leftVm.FilterQuery == "", "Filter still hides new folder");
            Call(FileSurface, "CancelInlineRename");

            var rightFolder = fixture + "-right";
            Directory.CreateDirectory(rightFolder);
            SetDualPane(true, persist: false);
            _rightVm!.Navigate(rightFolder);
            ActivateRight(true);
            await Until(() => !_rightVm.IsLoading && _rightVm.Navigation.CurrentPath == rightFolder);
            await Task.Delay(350);
            await _fileActions.RunAsync(AppCommandId.NewFolder);
            await Until(() => _rightSurface!.IsRenaming);
            Require(!_leftVm.IsLoading && !FileSurface.IsRenaming
                && Path.GetDirectoryName(_rightSurface!.SelectedPaths().Single()) == rightFolder,
                "New folder renamed in the wrong pane");
            Call(_rightSurface!, "CancelInlineRename");
            SetDualPane(false, persist: false);

            await _fileActions.RunAsync(AppCommandId.NewFolder);
            _leftVm.Navigate(Path.GetDirectoryName(fixture)!);
            await Until(() => !_leftVm.IsLoading);
            await Task.Delay(700);
            Require(!FileSurface.IsRenaming && !_pendingCreatedItemRename, "Navigation retained rename request");
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, samples,
                FilterRevealsCreatedItem = true, RightPaneIndependent = true,
                NavigationCancels = true, DesktopInputUsed = false },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = false, Error = error.ToString(), samples }));
        }
        static TextBox Editor(FileDetailsSurface surface) =>
            (TextBox)typeof(FileDetailsSurface).GetField("_renameEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(surface)!;
        static object? Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
        static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException(error);
        }
        static async Task Until(Func<bool> predicate)
        {
            for (var i = 0; i < 160; i++) { if (predicate()) return; await Task.Delay(50); }
            throw new TimeoutException("Create/rename did not settle");
        }
    }
}
#endif
