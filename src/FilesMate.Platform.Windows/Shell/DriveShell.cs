using System.Diagnostics;

namespace FilesMate.Platform.Windows.Shell;

public static class DriveShell
{
    public const string Rundll = "rundll32.exe";
    public const string MapArguments = "shell32.dll,SHHelpShortcuts_RunDLL Connect";
    public const string DisconnectArguments = "shell32.dll,SHHelpShortcuts_RunDLL Disconnect";

    public static ProcessStartInfo MapStartInfo() => RundllInfo(MapArguments);

    public static ProcessStartInfo DisconnectDialogStartInfo() => RundllInfo(DisconnectArguments);

    public static ProcessStartInfo DisconnectLetterStartInfo(string root)
    {
        var letter = DriveLetter(root)
            ?? throw new ArgumentException("A drive root is required.", nameof(root));
        return new ProcessStartInfo
        {
            FileName = "net.exe",
            Arguments = $"use {letter}: /delete /y",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    public static ProcessStartInfo EjectStartInfo(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var letter = DriveLetter(root);
        if (letter is null || !string.Equals(root.TrimEnd('\\'), letter + ":", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A drive root is required.", nameof(root));
        return new ProcessStartInfo
        {
            FileName = letter + ":\\",
            Verb = "eject",
            UseShellExecute = true,
        };
    }

    public static void MapNetworkDrive() => Start(MapStartInfo());

    public static void DisconnectNetworkDrive() => Start(DisconnectDialogStartInfo());

    public static void DisconnectLetter(string root) => Start(DisconnectLetterStartInfo(root));

    public static void Eject(string root) => Start(EjectStartInfo(root));
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static Task EjectAsync(string root) => ShellLocation.OnSta(() => { Eject(root); return true; });

    public static string? DriveLetter(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || root.Length < 2 || root[1] != ':')
        {
            return null;
        }

        var letter = char.ToUpperInvariant(root[0]);
        return char.IsAsciiLetter(letter) ? letter.ToString() : null;
    }

    public static bool IsRemovable(string? root) =>
        TryDrive(root, out var drive) && drive.DriveType is DriveType.Removable or DriveType.CDRom;

    public static bool IsNetwork(string? root) =>
        TryDrive(root, out var drive) && drive.DriveType == DriveType.Network;

    private static bool TryDrive(string? root, out DriveInfo drive)
    {
        drive = null!;
        var letter = DriveLetter(root);
        if (letter is null)
        {
            return false;
        }

        try
        {
            drive = new DriveInfo(letter);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ProcessStartInfo RundllInfo(string arguments) =>
        new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, Rundll),
            Arguments = arguments,
            UseShellExecute = false,
        };

    private static void Start(ProcessStartInfo info)
    {
        using var started = Process.Start(info);
        if (started is null && info.UseShellExecute)
        {
            return;
        }

        if (started is null)
        {
            throw new InvalidOperationException("Drive action failed.");
        }
    }
}
