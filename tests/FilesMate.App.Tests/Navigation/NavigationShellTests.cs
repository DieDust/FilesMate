using System.Diagnostics;

using FilesMate.App.Navigation;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.Navigation;

public sealed class NavigationShellTests
{
    private const string Root = @"D:\filesmate-nav\root";
    private const string Sub = @"D:\filesmate-nav\root\sub";
    private const string Other = @"D:\filesmate-nav\other";

    [Fact]
    public async Task File_operation_failure_keeps_the_loaded_directory_visible()
    {
        await using var vm = CreateViewModel(CreateEnumerator());
        vm.Navigate(Root);
        await WaitIdle(vm);
        var store = vm.Store;
        var count = vm.ItemCount;
        vm.ReportUserError("A folder with this name already exists");
        Assert.Null(vm.ErrorText);
        Assert.Equal(Root, vm.AddressText);
        Assert.Same(store, vm.Store);
        Assert.Equal(count, vm.ItemCount);
        Assert.Contains("alpha.txt", vm.PlaceholderNames);
        Assert.Equal("A folder with this name already exists", vm.StatusText);
    }

    [Fact]
    public async Task Opening_a_path_updates_the_address_before_enumeration_finishes()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerator = CreateEnumerator();
        enumerator.BeforeFirstBatch = _ => hold.Task;
        await using var vm = CreateViewModel(enumerator);

        vm.Navigate(Root);

        Assert.Equal(Root, vm.AddressText);
        Assert.True(vm.IsLoading);
        Assert.True(vm.CanRefresh);
        hold.TrySetResult();
        await WaitIdle(vm);
    }

    [Fact]
    public async Task Back_forward_and_up_history_is_deterministic()
    {
        var enumerator = CreateEnumerator();
        await using var vm = CreateViewModel(enumerator);

        vm.Navigate(Sub);
        await WaitIdle(vm);
        vm.Up();
        await WaitIdle(vm);
        Assert.Equal(Root, vm.AddressText);
        Assert.True(vm.CanGoBack);
        Assert.True(vm.CanGoUp);

        vm.Up();
        await WaitIdle(vm);
        Assert.Equal(@"D:\filesmate-nav", vm.AddressText);

        vm.Back();
        await WaitIdle(vm);
        Assert.Equal(Root, vm.AddressText);
        vm.Back();
        await WaitIdle(vm);
        Assert.Equal(Sub, vm.AddressText);

        vm.Forward();
        await WaitIdle(vm);
        Assert.Equal(Root, vm.AddressText);
    }

    [Fact]
    public async Task Invalid_path_shows_an_inline_error_and_can_go_back()
    {
        var enumerator = CreateEnumerator();
        await using var vm = CreateViewModel(enumerator);

        vm.Navigate(Root);
        await WaitIdle(vm);
        vm.Navigate("relative-path");

        Assert.Equal("relative-path", vm.AddressText);
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));
        Assert.False(vm.IsLoading);
        Assert.True(vm.CanGoBack);

        vm.Back();
        await WaitIdle(vm);
        Assert.Equal(Root, vm.AddressText);
        Assert.Contains("alpha.txt", vm.PlaceholderNames);
    }

    [Fact]
    public async Task Opening_a_directory_entry_navigates_into_it()
    {
        var enumerator = CreateEnumerator();
        await using var vm = CreateViewModel(enumerator);
        vm.Navigate(Root);
        await WaitIdle(vm);
        vm.Open(FakeDirectoryEnumerator.Entry(2, "sub", EntryKind.Directory));
        await WaitIdle(vm);
        Assert.Equal(Sub, vm.AddressText);
        Assert.Contains("child.txt", vm.PlaceholderNames);
    }

    [Fact]
    public async Task Captured_folder_path_stays_stable_if_open_is_delivered_twice()
    {
        var enumerator = CreateEnumerator();
        await using var vm = CreateViewModel(enumerator);
        vm.Navigate(Root);
        await WaitIdle(vm);
        var entry = FakeDirectoryEnumerator.Entry(2, "sub", EntryKind.Directory);
        var captured = vm.FullPath(entry);

        vm.Navigate(captured);
        await WaitIdle(vm);
        vm.Navigate(captured);
        await WaitIdle(vm);

        Assert.Equal(Sub, vm.AddressText);
        Assert.Contains("child.txt", vm.PlaceholderNames);
    }

    [Fact]
    public async Task Filter_narrows_the_current_view()
    {
        var enumerator = CreateEnumerator();
        await using var vm = CreateViewModel(enumerator);
        vm.Navigate(Root);
        await WaitIdle(vm);
        vm.SetFilterQuery("alpha");
        Assert.Equal(1, vm.ItemCount);
        Assert.Equal(["alpha.txt"], vm.PlaceholderNames);
    }

    [Fact]
    public async Task History_navigation_resets_folder_specific_name_and_tag_filters()
    {
        await using var vm = CreateViewModel(CreateEnumerator());
        vm.Navigate(Root);
        await WaitIdle(vm);
        vm.Navigate(Sub);
        await WaitIdle(vm);
        vm.SetFilterQuery("child");
        vm.SetTagFilter("Current folder only", []);

        vm.Back();
        await WaitIdle(vm);
        Assert.Equal(string.Empty, vm.FilterQuery);
        Assert.Null(vm.TagFilterLabel);
        Assert.Contains("alpha.txt", vm.PlaceholderNames);

        vm.SetFilterQuery("alpha");
        vm.Forward();
        await WaitIdle(vm);
        Assert.Equal(string.Empty, vm.FilterQuery);
        Assert.Contains("child.txt", vm.PlaceholderNames);

        vm.SetFilterQuery("child");
        vm.Up();
        await WaitIdle(vm);
        Assert.Equal(string.Empty, vm.FilterQuery);
        Assert.Contains("alpha.txt", vm.PlaceholderNames);
    }

    [Fact]
    public async Task Starting_a_second_navigation_cancels_the_first()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerator = CreateEnumerator();
        enumerator.BeforeFirstBatch = request =>
            request.Path.Equals(Root, StringComparison.OrdinalIgnoreCase) ? hold.Task : Task.CompletedTask;
        await using var vm = CreateViewModel(enumerator);

        vm.Navigate(Root);
        await WaitUntil(() => enumerator.Started >= 1);
        Assert.True(vm.IsLoading);
        vm.Navigate(Other);
        await WaitUntil(() => enumerator.Canceled > 0);
        hold.TrySetResult();
        await WaitIdle(vm);

        Assert.Equal(Other, vm.AddressText);
        Assert.Contains("beta.txt", vm.PlaceholderNames);
        Assert.DoesNotContain("alpha.txt", vm.PlaceholderNames);
        Assert.True(enumerator.Canceled > 0);
    }

    [Fact]
    public async Task Loading_does_not_block_back_or_refresh()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerator = CreateEnumerator();
        enumerator.BeforeFirstBatch = request =>
            request.Path.Equals(Other, StringComparison.OrdinalIgnoreCase) ? hold.Task : Task.CompletedTask;
        await using var vm = CreateViewModel(enumerator);

        vm.Navigate(Root);
        await WaitIdle(vm);
        vm.Navigate(Other);

        Assert.True(vm.IsLoading);
        Assert.True(vm.CanGoBack);
        Assert.True(vm.CanRefresh);
        vm.Back();
        hold.TrySetResult();
        await WaitIdle(vm);

        Assert.Equal(Root, vm.AddressText);
        Assert.Contains("alpha.txt", vm.PlaceholderNames);
    }

    [Fact]
    public async Task Missing_folder_shows_an_error_and_keeps_the_address()
    {
        var enumerator = CreateEnumerator();
        enumerator.Errors[Root] = new DirectoryReadError(
            DirectoryReadErrorKind.NotFound,
            2,
            "Folder is missing.",
            isTerminal: true);
        await using var vm = CreateViewModel(enumerator);

        vm.Navigate(Root);
        await WaitIdle(vm);

        Assert.Equal(Root, vm.AddressText);
        Assert.Equal("Folder is missing.", vm.ErrorText);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Stale_generation_batches_are_ignored()
    {
        var enumerator = CreateEnumerator();
        enumerator.EmitStaleGeneration = true;
        await using var vm = CreateViewModel(enumerator);

        vm.Navigate(Root);
        await WaitIdle(vm);

        Assert.Equal(Root, vm.AddressText);
        Assert.DoesNotContain("stale.txt", vm.PlaceholderNames);
        Assert.Contains("alpha.txt", vm.PlaceholderNames);
    }

    [Fact]
    public async Task Independent_panes_keep_their_own_path_and_filter()
    {
        var enumerator = CreateEnumerator();
        await using var first = CreateViewModel(enumerator);
        await using var second = CreateViewModel(enumerator);

        first.Navigate(Root);
        second.Navigate(Other);
        await WaitIdle(first);
        await WaitIdle(second);

        first.SetFilterQuery("alpha");
        Assert.Equal(Root, first.AddressText);
        Assert.Equal(["alpha.txt"], first.PlaceholderNames);
        Assert.Equal(Other, second.AddressText);
        Assert.Contains("beta.txt", second.PlaceholderNames);
        Assert.DoesNotContain("alpha.txt", second.PlaceholderNames);
    }

    [Fact]
    public async Task Navigating_to_a_file_opens_it_and_lists_the_parent_folder()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-open-file-").FullName;
        var file = Path.Combine(root, "pkg.exe");
        File.WriteAllText(file, "ok");
        try
        {
            var enumerator = new FakeDirectoryEnumerator();
            enumerator.Folders[root] = [FakeDirectoryEnumerator.Entry(1, "pkg.exe")];
            await using var vm = CreateViewModel(enumerator);
            string? opened = null;
            vm.OpenFileRequested += (_, path) => opened = path;

            vm.Navigate("\"" + file + "\"");
            await WaitIdle(vm);

            Assert.Equal(file, opened);
            Assert.Equal(root, vm.AddressText, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("pkg.exe", vm.PlaceholderNames);
            Assert.Equal(1, enumerator.Started);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PaneViewModel CreateViewModel(FakeDirectoryEnumerator enumerator) =>
        new(new ImmediateUiDispatcher(), new WindowsPathService(), enumerator, NaturalStringComparer.Instance);

    private static FakeDirectoryEnumerator CreateEnumerator()
    {
        var enumerator = new FakeDirectoryEnumerator();
        enumerator.Folders[Root] =
        [
            FakeDirectoryEnumerator.Entry(1, "alpha.txt"),
            FakeDirectoryEnumerator.Entry(2, "sub", EntryKind.Directory),
        ];
        enumerator.Folders[Sub] = [FakeDirectoryEnumerator.Entry(3, "child.txt")];
        enumerator.Folders[Other] = [FakeDirectoryEnumerator.Entry(4, "beta.txt")];
        enumerator.Folders[@"D:\filesmate-nav"] = [FakeDirectoryEnumerator.Entry(5, "root", EntryKind.Directory)];
        return enumerator;
    }

    private static async Task WaitIdle(PaneViewModel vm)
    {
        await vm.WhenCurrentSessionCompletes;
        await WaitUntil(() => !vm.IsLoading);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var sw = Stopwatch.StartNew();
        while (!predicate())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException("Timed out waiting for navigation state.");
            }

            await Task.Delay(10);
        }
    }
}
