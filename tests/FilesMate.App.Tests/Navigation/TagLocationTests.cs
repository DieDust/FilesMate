using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class TagLocationTests
{
    [Fact]
    public void Tag_uris_round_trip_and_reject_junk()
    {
        Assert.Equal("filesmate:tag:7", TagLocation.Uri(7));
        Assert.True(TagLocation.IsTag("filesmate:tag:7"));
        Assert.True(TagLocation.TryParse("filesmate:tag:7", out var id));
        Assert.Equal(7, id);
        Assert.False(TagLocation.IsTag(HomeLocation.Uri));
        Assert.False(TagLocation.TryParse("filesmate:tag:0", out _));
        Assert.False(TagLocation.TryParse(@"C:\Work", out _));
    }

    [Fact]
    public void Path_service_keeps_tag_uris_out_of_win32_normalization()
    {
        var paths = new WindowsPathService();
        Assert.Equal(TagLocation.Uri(3), paths.Normalize("filesmate:tag:3"));
        Assert.Equal(HomeLocation.Uri, paths.GetParent(TagLocation.Uri(3)));
        Assert.True(paths.IsSamePath("filesmate:tag:3", TagLocation.Uri(3)));
        Assert.Equal(@"C:\Work\todo.txt", paths.Combine(TagLocation.Uri(3), @"C:\Work\todo.txt"));
    }

    [Fact]
    public void Captions_use_place_names_instead_of_virtual_uris()
    {
        Assert.Equal(LocationCaption.HomeGlyph, LocationCaption.Glyph(HomeLocation.Uri));
        Assert.Equal(LocationCaption.TagGlyph, LocationCaption.Glyph(TagLocation.Uri(4)));
        Assert.Equal("Inbox", LocationCaption.Title(TagLocation.Uri(4), "Inbox"));
        Assert.NotEqual("filesmate:tag:4", LocationCaption.Title(TagLocation.Uri(4), "Inbox"));
        Assert.False(CloudLocation.IsRoot(TagLocation.Uri(4)));
        Assert.False(CloudLocation.IsRoot(HomeLocation.Uri));
        Assert.Equal("\uE753", CloudLocation.Glyph);
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrWhiteSpace(oneDrive))
        {
            Assert.True(CloudLocation.IsRoot(oneDrive));
            Assert.Equal(CloudLocation.Glyph, LocationCaption.Glyph(oneDrive));
        }
    }
}
