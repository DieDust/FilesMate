using FilesMate.App.Commands;
using FilesMate.App.Services;
using FilesMate.Platform.Windows.CompactMate;

namespace FilesMate.App.Tests.Commands;

public sealed class CompactMateSessionTests
{
    [Fact]
    public void VerbFor_maps_files_style_commands_onto_compactmate()
    {
        Assert.Equal(CompactMateVerb.SmartExtract, CompactMateSession.VerbFor(AppCommandId.SmartExtract));
        Assert.Equal(CompactMateVerb.ExtractHere, CompactMateSession.VerbFor(AppCommandId.ExtractHere));
        Assert.Equal(CompactMateVerb.ExtractToFolder, CompactMateSession.VerbFor(AppCommandId.ExtractToFolder));
        Assert.Equal(CompactMateVerb.ExtractToOther, CompactMateSession.VerbFor(AppCommandId.ExtractToOther));
        Assert.Equal(CompactMateVerb.CompressZip, CompactMateSession.VerbFor(AppCommandId.CompressZip));
        Assert.Equal(CompactMateVerb.Compress7z, CompactMateSession.VerbFor(AppCommandId.Compress7z));
        Assert.Equal(CompactMateVerb.CompressNew, CompactMateSession.VerbFor(AppCommandId.CompressNew));
        Assert.Equal(CompactMateVerb.Open, CompactMateSession.VerbFor(AppCommandId.OpenInCompactMate));
        Assert.Null(CompactMateSession.VerbFor(AppCommandId.Copy));
    }
}
