using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class BatchRenamePlannerTests
{
    [Fact]
    public void Find_replace_prefix_suffix_and_extension_are_composed()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMateRename", Guid.NewGuid().ToString("N"));
        var plan = BatchRenamePlanner.Plan(
            [Path.Combine(root, "draft-one.txt")],
            new BatchRenameRule { Find = "draft", Replace = "final", Prefix = "2026-", Suffix = "-ready" });

        Assert.True(plan.IsValid);
        Assert.EndsWith("2026-final-one-ready.txt", plan.Entries[0].Target, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_targets_existing_targets_and_invalid_names_are_reported()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMateRename", Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "a.txt");
        var second = Path.Combine(root, "b.txt");
        var duplicate = BatchRenamePlanner.Plan([first, second], new BatchRenameRule { Prefix = "same-" });
        Assert.True(duplicate.IsValid);

        var collision = BatchRenamePlanner.Plan([first], new BatchRenameRule { Replace = "", Find = "a" }, target => target.EndsWith(".txt", StringComparison.Ordinal));
        Assert.False(collision.IsValid);

        var invalid = BatchRenamePlanner.Plan([first], new BatchRenameRule { Prefix = "bad:" });
        Assert.False(invalid.IsValid);
        Assert.Contains("valid file name", invalid.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Numbering_regex_and_case_rules_are_deterministic()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMateRename", Guid.NewGuid().ToString("N"));
        var plan = BatchRenamePlanner.Plan(
            [Path.Combine(root, "photo-a.JPG"), Path.Combine(root, "photo-b.JPG")],
            new BatchRenameRule
            {
                RegexPattern = "photo-(.)",
                RegexReplacement = "shot-$1",
                NumberStart = 1,
                NumberWidth = 3,
                Lowercase = true,
            });

        Assert.Equal("shot-a 001.JPG", Path.GetFileName(plan.Entries[0].Target));
        Assert.Equal("shot-b 002.JPG", Path.GetFileName(plan.Entries[1].Target));
    }

    [Fact]
    public void Case_only_changes_are_executable_and_preserve_extensions()
    {
        var plan = BatchRenamePlanner.Plan([Path.GetFullPath("Report.PDF")], new BatchRenameRule { Lowercase = true });
        Assert.True(plan.Entries[0].RequiresRename);
        Assert.Equal("report.PDF", Path.GetFileName(plan.Entries[0].Target));
        var whole = BatchRenamePlanner.Plan([Path.GetFullPath("Report.PDF")], new BatchRenameRule { Lowercase = true, IncludeExtension = true });
        Assert.Equal("report.pdf", Path.GetFileName(whole.Entries[0].Target));
    }

    [Fact]
    public void Unified_numbering_preserves_order_extensions_and_dotted_folder_names()
    {
        var sources = new[] { Path.GetFullPath("z.jpg"), Path.GetFullPath("a.png"), Path.GetFullPath("release.v2") };
        var plan = BatchRenamePlanner.Plan(sources, new BatchRenameRule { BaseName = "Photo", NumberStart = 9, NumberWidth = 3 },
            isDirectory: p => p == sources[2]);
        Assert.Equal(new[] { "Photo 009.jpg", "Photo 010.png", "Photo 011" }, plan.Entries.Select(e => Path.GetFileName(e.Target)));
        var folder = BatchRenamePlanner.Plan([sources[2]], new BatchRenameRule { Suffix = "-backup" }, isDirectory: _ => true);
        Assert.Equal("release.v2-backup", Path.GetFileName(folder.Entries[0].Target));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("aux.txt")]
    [InlineData("LPT1")]
    [InlineData("COM¹.txt")]
    [InlineData("bad\u0001name")]
    [InlineData("trailing.")]
    public void Invalid_windows_names_are_rejected(string name)
    {
        var plan = BatchRenamePlanner.Plan([Path.GetFullPath("original")], new BatchRenameRule { BaseName = name });
        Assert.False(plan.IsValid);
    }

    [Fact]
    public void All_conflicting_rows_are_marked_and_existing_files_are_protected()
    {
        var sources = new[] { Path.GetFullPath("one.txt"), Path.GetFullPath("two.txt") };
        var duplicate = BatchRenamePlanner.Plan(sources, new BatchRenameRule { BaseName = "same" });
        Assert.All(duplicate.Entries, e => Assert.NotNull(e.Error));
        var existing = BatchRenamePlanner.Plan(sources, new BatchRenameRule { Prefix = "new-" }, _ => true);
        Assert.All(existing.Entries, e => Assert.NotNull(e.Error));
        var unchanged = BatchRenamePlanner.Plan(sources, new BatchRenameRule(), _ => true);
        Assert.True(unchanged.IsValid);
        Assert.All(unchanged.Entries, e => Assert.False(e.RequiresRename));
    }

    [Fact]
    public void Case_sensitive_replacement_does_not_change_nonmatching_names()
    {
        var plan = BatchRenamePlanner.Plan([Path.GetFullPath("Photo.jpg")],
            new BatchRenameRule { Find = "photo", Replace = "shot", CaseSensitive = true });
        Assert.False(plan.Entries[0].RequiresRename);
    }
}
