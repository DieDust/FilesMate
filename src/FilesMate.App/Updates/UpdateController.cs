using Loc = FilesMate.App.Localization.StringTable;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using FilesMate.App.Services;
using FilesMate.Core.Updates;
using FilesMate.Platform.Windows.Processes;

namespace FilesMate.App.Updates;

internal sealed class UpdateController
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly UpdateSettingsStore _settings = new(Program.SettingsPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate", "updates.json")));
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    public UpdateRelease? Available { get; private set; }
    public bool IsInstalling { get; private set; }
#if FILESMATE_UI_TEST
    internal Action<ProcessStartInfo>? TestInstallerStart;
    internal Func<IProgress<double>, CancellationToken, Task<string>>? TestDownload;
#endif
    public bool AutomaticallyCheck => _settings.Load().AutomaticallyCheck;
    public void SetAutomatic(bool enabled) => _settings.SetAutomatic(enabled);
    public static Version CurrentVersion => Version.Parse(typeof(App).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "1.0.0.0");
    private UpdateClient Client => new(_http, new Uri(UpdateTrust.FeedDirectory), UpdateTrust.PublicKey);

    public async Task<UpdateRelease?> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
        await _checkGate.WaitAsync(cancellationToken);
        try
        {
            if (!_settings.TryReserveDailyCheck(DateOnly.FromDateTime(DateTime.Now), manual))
            {
                if (manual) throw new IOException(Loc.Get("Update_CheckStateFailed"));
                return null;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var release = await Client.CheckAsync(timeout.Token);
            Available = release.ParsedVersion > CurrentVersion && Environment.OSVersion.Version.Build >= release.MinimumWindowsBuild ? release : null;
            return Available;
        }
        finally { _checkGate.Release(); }
    }

    public async Task<string> DownloadAsync(UpdateRelease release, IProgress<double> progress, CancellationToken cancellationToken)
    {
#if FILESMATE_UI_TEST
        if (TestDownload is { } testDownload) return await testDownload(progress, cancellationToken);
#endif
        var cache = Program.SettingsPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate", "Updates", "cache"));
        Directory.CreateDirectory(cache);
        // Only our generated download files, and only stale ones; never traverse user folders.
        foreach (var file in Directory.EnumerateFiles(cache))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _) || Path.GetExtension(file) is not (".part" or ".exe")) continue;
            try { if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        return await Client.DownloadAsync(release, cache, progress, timeout.Token);
    }

    public async Task InstallAsync(UpdateRelease release, string installer, CancellationToken cancellationToken = default)
    {
        if (IsInstalling) throw new InvalidOperationException(Loc.Get("Update_Busy"));
        if (release.ParsedVersion <= CurrentVersion) throw new InvalidOperationException(Loc.Get("Update_VersionNotNewer"));
        if (FileOperationLifetime.IsBusy || !App.CanInstallUpdate()) throw new InvalidOperationException(Loc.Get("Update_FinishOperations"));
        IsInstalling = true;
        try
        {
            // Keep writes/deletion excluded between the final verification and process creation.
            await using var file = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await SHA256.HashDataAsync(file, cancellationToken);
            if (file.Length != release.Size || !CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(release.Sha256)))
                throw new InvalidDataException(Loc.Get("Update_InstallerInvalid"));
            if (FileOperationLifetime.IsBusy || !App.CanInstallUpdate()) throw new InvalidOperationException(Loc.Get("Update_FinishOperations"));
            var start = new ProcessStartInfo(installer) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(installer)! };
            foreach (var argument in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/FILESMATEUPDATE=1", "/DIR=" + AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) }) start.ArgumentList.Add(argument);
#if FILESMATE_UI_TEST
            if (TestInstallerStart is { } testStart) { testStart(start); IsInstalling = false; return; }
#endif
            cancellationToken.ThrowIfCancellationRequested();
            // The installer, and the FilesMate processes it relaunches, must survive whatever job this process
            // was started in; an update is the wrong moment to be terminated along with a terminal or IDE.
            DetachedProcess.Start(installer, start.ArgumentList);
            App.CloseForUpdate();
        }
        catch { IsInstalling = false; throw; }
    }
}
