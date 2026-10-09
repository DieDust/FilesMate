using System.Globalization;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Recursive;
using static FilesMate.Core.Tests.Recursive.RecursiveTestData;

namespace FilesMate.Core.Tests.Entries;

public sealed class EntryMetadataFilterTests
{
    private static FileEntryCore Entry(string name, ulong bytes = 512, long modified = 100) => new(1, name, bytes, modified, 0, FileAttributes.Normal, EntryKind.File);

    [Fact]
    public void Name_type_date_and_bytes_intersect_while_type_alternatives_union()
    {
        var metadata = new EntryMetadataFilter { Categories = EntryTypeCategory.Images, Extensions = [".txt"], ModifiedFromUtcTicks = 100,
            ModifiedUntilUtcTicks = 200, MinimumBytes = 512, MaximumBytes = 1024 };
        var filter = new EntryFilter { Query = "report", Metadata = metadata };
        Assert.True(filter.Matches(Entry("REPORT.JPG")));
        Assert.True(filter.Matches(Entry("report.TXT", 1024, 199)));
        Assert.False(filter.Matches(Entry("other.jpg")));
        Assert.False(filter.Matches(Entry("report.zip")));
        Assert.False(filter.Matches(Entry("report.jpg", 511)));
        Assert.False(filter.Matches(Entry("report.jpg", 1025)));
        Assert.False(filter.Matches(Entry("report.jpg", 512, 99)));
        Assert.False(filter.Matches(Entry("report.jpg", 512, 200)));
    }

    [Fact]
    public void Extension_normalization_deduplicates_case_and_canonical_spellings()
    {
        Assert.True(EntryMetadataFilter.TryNormalizeExtensions("jpg; .JPG, *.jpg PNG .png", out var values));
        Assert.Equal(new[] { ".jpg", ".png" }, values);
    }

    [Theory]
    [InlineData("c:\\jpg")][InlineData("../jpg")][InlineData("*.jp?")][InlineData("*jpg")]
    [InlineData(".jpg*")][InlineData(".tar.gz")][InlineData(".")][InlineData("jpg/PNG")]
    public void Invalid_extensions_do_not_supply_a_replacement_condition(string text) => Assert.False(EntryMetadataFilter.TryNormalizeExtensions(text, out _));

    [Theory]
    [InlineData("README", true)][InlineData("read.me", false)][InlineData("FILE.TXT", false)]
    public void No_extension_is_a_file_alternative(string name, bool expected)
        => Assert.Equal(expected, new EntryMetadataFilter { NoExtension = true }.Matches(Entry(name)));

    [Fact]
    public void Directory_type_does_not_count_as_an_extension_or_supply_recursive_size()
    {
        var directory = Directory("folder.jpg") with { Size = 1000 };
        Assert.False(new EntryMetadataFilter { Categories = EntryTypeCategory.Images, NoExtension = true }.Matches(directory));
        Assert.True(new EntryMetadataFilter { Categories = EntryTypeCategory.Directories }.Matches(directory));
        Assert.False(new EntryMetadataFilter { Categories = EntryTypeCategory.Directories, MinimumBytes = 0 }.Matches(directory));
    }

    [Theory]
    [InlineData(2026,3,8,23)][InlineData(2026,11,1,25)][InlineData(2026,10,5,24)]
    public void Date_range_includes_the_full_local_end_day_across_dst(int year,int month,int day,int hours)
    {
        var date = new DateOnly(year,month,day);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Assert.True(EntryMetadataFilter.TryDateRange(date,date,zone,out var start,out var until));
        Assert.Equal(TimeSpan.FromHours(hours).Ticks,until!.Value-start!.Value);
        var filter = new EntryMetadataFilter { ModifiedFromUtcTicks = start, ModifiedUntilUtcTicks = until };
        Assert.True(filter.Matches(Entry("a",modified:start.Value)));
        Assert.True(filter.Matches(Entry("a",modified:until.Value-1)));
        Assert.False(filter.Matches(Entry("a",modified:start.Value-1)));
        Assert.False(filter.Matches(Entry("a",modified:until.Value)));
    }

    [Fact]
    public void Invalid_or_incomplete_dates_and_overflow_keep_the_previous_valid_range()
    {
        var before = new EntryMetadataFilter { ModifiedFromUtcTicks = 10, ModifiedUntilUtcTicks = 20 };
        var current = before;
        if (EntryMetadataFilter.TryDateRange(new DateOnly(2026,10,6),new DateOnly(2026,10,5),TimeZoneInfo.Utc,out var from,out var until)) current = before with { ModifiedFromUtcTicks=from,ModifiedUntilUtcTicks=until };
        Assert.Same(before,current);
        Assert.False(EntryMetadataFilter.TryDateRange(null,null,TimeZoneInfo.Utc,out _,out _));
        Assert.False(EntryMetadataFilter.TryDateRange(DateOnly.MaxValue,DateOnly.MaxValue,TimeZoneInfo.Utc,out _,out _));
    }

    [Theory]
    [InlineData("1","2",1024,1024,2048)]
    [InlineData("0.5","1",1024,512,1024)]
    [InlineData("1.1","2.9",1,2,2)]
    public void Size_values_convert_to_inclusive_integer_byte_boundaries(string low,string high,ulong unit,ulong expectedLow,ulong expectedHigh)
    {
        Assert.True(EntryMetadataFilter.TryByteRange(low,high,unit,CultureInfo.InvariantCulture,out var min,out var max));
        Assert.Equal(expectedLow,min); Assert.Equal(expectedHigh,max);
        var filter = new EntryMetadataFilter { MinimumBytes = min, MaximumBytes = max };
        Assert.True(filter.Matches(Entry("a",expectedLow))); Assert.True(filter.Matches(Entry("a",expectedHigh)));
        Assert.False(filter.Matches(Entry("a",expectedLow-1))); Assert.False(filter.Matches(Entry("a",expectedHigh+1)));
    }

    [Theory]
    [InlineData("-1","2")][InlineData("3","2")][InlineData(""," ")][InlineData("NaN","2")]
    [InlineData("18446744073709551616","")][InlineData("0.5","0.9")]
    public void Invalid_custom_size_does_not_replace_previous_valid_range(string low,string high)
    {
        var current = new EntryMetadataFilter { MinimumBytes=5,MaximumBytes=10 }; var previous=current;
        if(EntryMetadataFilter.TryByteRange(low,high,1,CultureInfo.InvariantCulture,out var min,out var max)) current=current with{ MinimumBytes=min,MaximumBytes=max };
        Assert.Same(previous,current);
    }

    [Fact]
    public void One_chip_or_all_metadata_can_be_cleared_without_clearing_query_or_other_conditions()
    {
        var filter = new EntryFilter { Query="report",Metadata=new(){Categories=EntryTypeCategory.Images,MinimumBytes=512,ModifiedFromUtcTicks=100} };
        var removed = filter with { Metadata=filter.Metadata with{Categories=EntryTypeCategory.None} };
        Assert.Equal("report",removed.Query); Assert.Equal((ulong)512,removed.Metadata.MinimumBytes); Assert.Equal(100,removed.Metadata.ModifiedFromUtcTicks);
        Assert.Equal("report",(filter with{Metadata=EntryMetadataFilter.None}).Query);
    }

    [Fact]
    public async Task Metadata_filter_reveals_matching_descendants_without_rescanning_or_selecting_context_ancestors()
    {
        var source=Tree((Root,[Directory("parent")]),(Root+@"\parent",[Directory("deeper")]),(Root+@"\parent\deeper",[File("match.JPG",512),File("other.txt")]));
        var snapshot=await ScanAsync(source);
        var calls=source.Requests.Count;
        var parent=Id(snapshot,@"parent");
        var projection=Project(snapshot,filter:new(){Metadata=new(){Categories=EntryTypeCategory.Images,MinimumBytes=512}},collapsed:new HashSet<int>{parent});
        Assert.Equal(new[]{"parent",@"parent\deeper",@"parent\deeper\match.JPG"},Paths(projection));
        Assert.Equal(1,projection.MatchCount); Assert.True(projection.IsFiltering);
        Assert.False(projection.IsSelectable(parent)); Assert.True(projection.IsContextOnly(parent));
        Assert.True(projection.IsSelectable(Id(snapshot,@"parent\deeper\match.JPG")));
        Assert.Same(snapshot,projection.Snapshot); Assert.Equal(calls,source.Requests.Count);
    }

    [Fact]
    public async Task Zero_metadata_result_keeps_partial_limits_and_cancellation_honest()
    {
        var source=Tree((Root,[Directory("A"),File("root.txt")]),(Root+@"\A",[File("deep.txt"),File("omitted.txt")]));
        var snapshot=await ScanAsync(source,new(){MaximumNodes=3});
        var zero=Project(snapshot,filter:new(){Metadata=new(){Extensions=[".png"]}});
        Assert.Equal(0,zero.MatchCount); Assert.Empty(zero.Index); Assert.Same(snapshot,zero.Snapshot);
        Assert.Equal(RecursiveSessionState.Partial,zero.State); Assert.True(zero.Snapshot.LimitReached);
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>snapshot.BuildProjection(EntrySort.Name,new(){Metadata=new(){NoExtension=true}},StringComparer.OrdinalIgnoreCase,cancellationToken:cancellation.Token));
    }

    [Fact]
    public async Task Ordinary_folder_scope_contains_direct_children_only_while_recursive_scope_includes_deep_hits()
    {
        var source=Tree((Root,[Directory("A"),File("root.jpg")]),(Root+@"\A",[File("deep.jpg")]));
        var snapshot=await ScanAsync(source);
        var store=new EntryStore();
        store.Append([Entry("root.jpg"),Directory("A") with{Id=2}]);
        var filter=new EntryFilter{Metadata=new(){Categories=EntryTypeCategory.Images}};
        var direct=EntryViewIndex.Build(store,EntrySort.Name,filter,StringComparer.OrdinalIgnoreCase,1);
        Assert.Equal("root.jpg",direct.Snapshot[Assert.Single(direct)].Name);
        Assert.Equal(2,Project(snapshot,filter:filter).MatchCount);
    }
}
