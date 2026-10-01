#if FILESMATE_UI_TEST
using System.Collections.Concurrent;
using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Core.Entries;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    internal async Task<object> RunFolderMutationSmokeAsync(string fixture, Func<Task> createFromChrome)
    {
        var measured = new ConcurrentQueue<string>();
        var frames = new List<string[]>();
        var sizes = new[] { 48_000L, 24_000L, 12_000L };
        string[] names = ["A-large", "B-medium", "C-small"];
        for (var i = 0; i < names.Length; i++)
        {
            var directory = Directory.CreateDirectory(Path.Combine(fixture, names[i])).FullName;
            File.WriteAllBytes(Path.Combine(directory, "data.txt"), new byte[sizes[i]]);
        }
        var hidden = Path.Combine(fixture, "hidden.dat");
        File.WriteAllBytes(hidden, new byte[1000]); File.SetAttributes(hidden, FileAttributes.Hidden);
        _leftVm.Navigate(fixture);
        await Until(() => !_leftVm.IsLoading && _leftVm.ItemCount == 3);
        FileSurface.SetLayout(FileLayoutKind.Details);
        _leftVm.RestoreSort(EntrySort.Size with { Ascending = false });
        await Until(() => names.All(name => FolderSizeCache.TryGet(Path.Combine(fixture, name), out _))
            && PaneChrome.ZoomText == StringTable.Format("Status_FolderSize", DriveCapacity.FormatBytes(85_000)));
        await Task.Delay(200);
        var generation = _leftVm.Navigation.CurrentGeneration;
        var store = _leftVm.Store;
        var survivingIds = store!.Snapshot().Where(e => e.Name != names[2]).ToDictionary(e => e.Name, e => e.Id);
        void Cached(string path) => measured.Enqueue(path);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(PaneViewModel.Store) || _leftVm.ViewIndex is null) return;
            frames.Add(_leftVm.ViewIndex.Select(i => _leftVm.Store![i].Name).ToArray());
        }
        FolderSizeCache.SizeCached += Cached;
        _leftVm.PropertyChanged += Changed;
        RecycleItemResult[] recycled = [];
        try
        {
            Require(FileSurface.TrySelectByName(names[2]), "Cannot select deletion fixture");
            await _fileActions.RunAsync(AppCommandId.Recycle);
            recycled = App.FileUndo.Latest?.RecycledItems.ToArray() ?? [];
            Require(recycled.Length == 1 && recycled[0].Receipt is not null, "Deletion did not use an exact Recycle Bin receipt");
            await Until(() => _leftVm.ItemCount == 2 && PaneChrome.ZoomText ==
                StringTable.Format("Status_FolderSize", DriveCapacity.FormatBytes(73_000)));
            await createFromChrome();
            await Until(() => _leftVm.ItemCount == 3 && FileSurface.IsRenaming);
            var created = App.FileUndo.Latest!.Paths.Single();
            FileSurface.CancelRenameForMutationSmoke();
            await Task.Delay(350);
            Require(ReferenceEquals(store, _leftVm.Store) && generation == _leftVm.Navigation.CurrentGeneration,
                "A file operation replaced the folder generation/store");
            Require(survivingIds.All(pair => _leftVm.Store!.Snapshot().Single(e => e.Name == pair.Key).Id == pair.Value), "Surviving entry IDs changed");
            Require(frames.All(frame => Array.IndexOf(frame, names[0]) < Array.IndexOf(frame, names[1])), "Existing size order flickered");
            Require(!measured.Any(path => names.Take(2).Any(name => path.Equals(Path.Combine(fixture, name), StringComparison.OrdinalIgnoreCase))),
                "Unchanged siblings were measured again after deletion/new folder");
            Require(FileSurface.Selection.Count == 1 && FileSurface.SelectedPaths().Single() == created, "Created folder selection moved to another file");
            var before = measured.ToArray();
            var child = Path.Combine(fixture, names[0], "new.txt");
            File.WriteAllBytes(child, new byte[10_000]);
            // A watch event for the changed directory must update just that directory.
            await Until(() => FolderSizeCache.TryGet(Path.Combine(fixture, names[0]), out var size) && size == 58_000);
            await Until(() => PaneChrome.ZoomText == StringTable.Format("Status_FolderSize", DriveCapacity.FormatBytes(83_000)));
            Require(!measured.Skip(before.Length).Any(path => path.Equals(Path.Combine(fixture, names[1]), StringComparison.OrdinalIgnoreCase)),
                "Changing one child's content restarted its sibling walk");
            return new { DeletionUsesRecycleBin = true, HiddenFilesIncludedInTotal = true, GenerationPreserved = true,
                SurvivingIdsPreserved = true, ExistingSortStable = true, NewFolderSelectedAndRenaming = true,
                UnchangedSiblingRecounts = 0, ChangedFolderRecounted = true, Frames = frames.ToArray(), CreatedPath = created,
                FinalTotal = PaneChrome.ZoomText };
        }
        finally
        {
            FolderSizeCache.SizeCached -= Cached;
            _leftVm.PropertyChanged -= Changed;
            FileSurface.CancelRenameForMutationSmoke();
            if (recycled.Length > 0)
                await ShellOperationWorker.RunAsync(() => new WindowsLocalFileOperations().RestoreRecycledItems(recycled));
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        async Task Until(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(30);
            if (!ready()) throw new TimeoutException($"Folder mutation state did not settle; path={_leftVm.AddressText}; loading={_leftVm.IsLoading}; count={_leftVm.ItemCount}; total={PaneChrome.ZoomText}; sizes={string.Join(';', names.Select(name => name + ':' + (FolderSizeCache.TryGet(Path.Combine(fixture, name), out var n) ? n.ToString() : "missing")))}; status={_leftVm.StatusText}; error={_leftVm.ErrorText}; preference={App.ExplorerPreferences.ShowFolderSizes}");
        }
    }
}
#endif
