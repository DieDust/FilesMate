using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

public sealed class DriveShellTests
{
    [Fact]
    public void Map_and_disconnect_use_the_windows_rundll_shortcuts()
    {
        var map = DriveShell.MapStartInfo();
        Assert.Equal(DriveShell.Rundll, Path.GetFileName(map.FileName), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(DriveShell.MapArguments, map.Arguments);
        Assert.False(map.UseShellExecute);

        var disconnect = DriveShell.DisconnectDialogStartInfo();
        Assert.Equal(DriveShell.DisconnectArguments, disconnect.Arguments);
    }

    [Fact]
    public void Letter_disconnect_targets_net_use()
    {
        var info = DriveShell.DisconnectLetterStartInfo(@"Z:\");
        Assert.Equal("net.exe", info.FileName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("use Z: /delete /y", info.Arguments);
        Assert.True(info.CreateNoWindow);
    }

    [Fact]
    public void Drive_letter_and_kind_helpers_ignore_invalid_roots()
    {
        Assert.Equal("C", DriveShell.DriveLetter(@"c:\"));
        Assert.Null(DriveShell.DriveLetter("OneDrive"));
        Assert.False(DriveShell.IsNetwork(null));
        Assert.False(DriveShell.IsRemovable("\\\\server\\share"));
    }
}
