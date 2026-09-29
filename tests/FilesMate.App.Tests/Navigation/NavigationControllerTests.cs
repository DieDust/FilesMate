using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class NavigationControllerTests
{
    [Fact]
    public void Navigation_notification_can_capture_the_departing_folder_before_up_back_and_forward()
    {
        var controller = new NavigationController(new WindowsPathService(), Core.Navigation.PaneId.New());
        var transitions = new List<(string? From, string To)>();
        controller.Navigating += (_, intent) =>
        {
            transitions.Add((controller.CurrentPath, intent.Path));
            Assert.Equal(controller.CurrentGeneration, intent.Generation);
        };
        controller.Open(@"D:\parent", true);
        controller.Open(@"D:\parent\child", true);
        controller.Up();
        controller.Back();
        controller.Forward();
        Assert.Equal(new (string?, string)[]
        {
            (null, @"D:\parent"), (@"D:\parent", @"D:\parent\child"),
            (@"D:\parent\child", @"D:\parent"), (@"D:\parent", @"D:\parent\child"),
            (@"D:\parent\child", @"D:\parent")
        }, transitions);
    }

    [Fact]
    public void HibernationSnapshotRestoresBackAndForwardOrder()
    {
        var original = new NavigationController(new WindowsPathService(), Core.Navigation.PaneId.New());
        foreach (var path in new[] { @"D:\a", @"D:\b", @"D:\c", @"D:\d" }) original.Open(path, true);
        original.Back(); original.Back();
        var restored = new NavigationController(new WindowsPathService(), Core.Navigation.PaneId.New());
        restored.Open(original.CurrentPath!, true);
        restored.RestoreHistory(original.CaptureHistory());
        Assert.Equal(@"D:\c", restored.Forward()?.Path);
        Assert.Equal(@"D:\d", restored.Forward()?.Path);
        Assert.Equal(@"D:\c", restored.Back()?.Path);
        Assert.Equal(@"D:\b", restored.Back()?.Path);
        Assert.Equal(@"D:\a", restored.Back()?.Path);
        Assert.Null(restored.Back());
    }
    [Fact]
    public void Back_and_forward_restore_paths_in_order()
    {
        var controller = new NavigationController(new WindowsPathService(), Core.Navigation.PaneId.New());
        controller.Open(@"D:\a\b", recordHistory: true);
        controller.Open(@"D:\a", recordHistory: true);
        controller.Open(@"D:\z", recordHistory: true);

        Assert.Equal(@"D:\a", controller.Back()?.Path);
        Assert.Equal(@"D:\a", controller.CurrentPath);
        Assert.Equal(@"D:\z", controller.Forward()?.Path);
        Assert.Equal(@"D:\z", controller.CurrentPath);
    }

    [Fact]
    public void Up_stops_at_the_drive_root()
    {
        var controller = new NavigationController(new WindowsPathService(), Core.Navigation.PaneId.New());
        controller.Open(@"D:\a\b", recordHistory: true);
        Assert.Equal(@"D:\a", controller.Up()?.Path);
        Assert.Equal(@"D:\", controller.Up()?.Path);
        Assert.Null(controller.Up());
        Assert.False(controller.CanGoUp);
    }

    [Fact]
    public void Refresh_reuses_the_path_and_advances_generation()
    {
        var controller = new NavigationController(new WindowsPathService(), Core.Navigation.PaneId.New());
        var first = controller.Open(@"D:\a", recordHistory: true);
        var refresh = controller.Refresh();
        Assert.NotNull(refresh);
        Assert.Equal(@"D:\a", refresh.Value.Path);
        Assert.True(refresh.Value.Generation > first.Generation);
        Assert.False(controller.CanGoBack);
    }
}
