using FilesMate.App.Navigation;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.Navigation;

public sealed class DeviceDisconnectTests
{
    [Fact]
    public async Task Removed_volume_releases_entries_and_ignores_other_drive_notifications()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-device-disconnect-").FullName;
        try
        {
            var enumerator = new FakeDirectoryEnumerator();
            enumerator.Folders[root] = [FakeDirectoryEnumerator.Entry(1, "keep.txt")];
            await using var vm = new PaneViewModel(new ImmediateUiDispatcher(), new WindowsPathService(),
                enumerator, NaturalStringComparer.Instance);
            vm.Navigate(root);
            await vm.WhenCurrentSessionCompletes;
            Assert.NotNull(vm.Store);
            var mask = 1u << (char.ToUpperInvariant(root[0]) - 'A');
            Assert.False(vm.DisconnectVolume(mask == 1 ? 2u : 1u));
            Assert.NotNull(vm.Store);
            Assert.True(vm.DisconnectVolume(mask));
            await vm.WhenFolderReleased;
            Assert.Equal(root, vm.AddressText);
            Assert.NotNull(vm.ErrorText);
            Assert.False(vm.IsLoading);
            Assert.Null(vm.Store);
            Assert.Null(vm.ViewIndex);
            Assert.Empty(vm.PlaceholderNames);
            vm.Refresh();
            await vm.WhenCurrentSessionCompletes;
            Assert.Null(vm.ErrorText);
            Assert.NotNull(vm.Store);
        }
        finally { Directory.Delete(root); }
    }
}
