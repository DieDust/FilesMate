using System.Runtime.Versioning;

using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

[SupportedOSPlatform("windows")]
public sealed class ShellShortcutTests
{
    [Fact]
    public void UniqueLinkPath_keeps_the_target_folder_and_avoids_collisions()
    {
        var folder = Directory.CreateTempSubdirectory("filesmate-lnk-");
        var target = Path.Combine(folder.FullName, "notes.txt");
        File.WriteAllText(target, "ok");

        var first = ShellShortcut.UniqueLinkPath(target);
        Assert.Equal(Path.Combine(folder.FullName, "notes.txt - Shortcut.lnk"), first);
        File.WriteAllText(first, "taken");
        var second = ShellShortcut.UniqueLinkPath(target);
        Assert.Equal(Path.Combine(folder.FullName, "notes.txt - Shortcut (2).lnk"), second);
    }
}
