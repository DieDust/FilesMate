using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.Navigation;

public sealed class TabTransferPayloadTests
{
    private static TabTransferPayload Fixture() => new(1, 1234, Guid.NewGuid(), new(
        new(@"D:\work", new FolderViewSettings(true, 2, EntrySort.Name), 234,
            ["report.txt"], "report", new([HomeLocation.Uri, @"D:\"], [@"D:\next"])),
        new(@"E:\archive", new FolderViewSettings(false, 4, EntrySort.Name), 85), true, true));

    [Fact]
    public void Transfer_preserves_both_panes_selection_filter_view_and_navigation_history()
    {
        var original = Fixture();
        var restored = Assert.IsType<TabTransferPayload>(TabTransferPayload.Parse(original.Serialize()));
        Assert.Equal(original.Serialize(), restored.Serialize());
        Assert.Equal(original.ReceiptName, restored.ReceiptName);
    }

    [Fact]
    public void Unsupported_or_incomplete_external_data_is_rejected()
    {
        var payload = Fixture();
        foreach (var json in new[] { "null", "{}", "broken", new string('x', TabTransferPayload.MaximumLength + 1),
            (payload with { Version = 99 }).Serialize(), (payload with { Id = Guid.Empty }).Serialize(),
            (payload with { State = null! }).Serialize(),
            (payload with { State = payload.State with { Left = null! } }).Serialize(),
            (payload with { State = payload.State with { Left = payload.State.Left with { Path = "https://example.org" } } }).Serialize(),
            (payload with { State = payload.State with { Left = payload.State.Left with { History = new(null!, []) } } }).Serialize() })
            Assert.Null(TabTransferPayload.Parse(json));
    }

    [Fact]
    public void Navigation_transfer_does_not_accept_path_injections_as_selection_names()
    {
        var payload = Fixture();
        foreach (var name in new[] { @"..\other.txt", @"C:\other.txt", "../other.txt", "bad\0name" })
            Assert.Null(TabTransferPayload.Parse((payload with
            { State = payload.State with { Left = payload.State.Left with { SelectedNames = [name] } } }).Serialize()));
    }
}
