using System.Text.Json;
using System.Text.RegularExpressions;
using FilesMate.Core.Configuration;
using FilesMate.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    // Presentation-only diagnostics in an explicitly isolated D profile. The
    // ordinary update checker, trust, downloader and installer are untouched.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _announcementDiagnosticsTimer;
    private readonly HashSet<string> _announcementDiagnosticRequests = [];
    private bool _announcementDiagnosticReading;

    internal void StartAnnouncementDiagnostics()
    {
        if (!RuntimeProfile.IsIsolated || _announcementDiagnosticsTimer is not null) return;
        var directory = Path.Combine(RuntimeProfile.DirectoryPath, "diagnostics", "announcement-requests");
        if (!Directory.Exists(directory)) return;
        _announcementDiagnosticsTimer = DispatcherQueue.CreateTimer();
        _announcementDiagnosticsTimer.Interval = TimeSpan.FromMilliseconds(300);
        _announcementDiagnosticsTimer.Tick += async (_, _) =>
        {
            if (_announcementDiagnosticReading || _windowClosed) return;
            _announcementDiagnosticReading = true;
            try
            {
                foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.json").Where(f => !_announcementDiagnosticRequests.Contains(f.Name)).Take(16))
                {
                    if (!Regex.IsMatch(file.Name, "^[A-Za-z0-9_-]{1,64}\\.json$") || file.Length > 8192
                        || (file.Attributes & FileAttributes.ReparsePoint) != 0 || !_announcementDiagnosticRequests.Add(file.Name)) continue;
                    var request = JsonSerializer.Deserialize<AnnouncementDiagnosticRequest>(await File.ReadAllTextAsync(file.FullName),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Empty announcement diagnostic request");
                    var ack = Path.Combine(RuntimeProfile.DirectoryPath, "diagnostics", "announcement-ack-" + Path.GetFileNameWithoutExtension(file.Name) + ".json");
                    if (request.Kind == "highlights") _ = ShowReleaseHighlightsAsync(automatic: request.Automatic);
                    else if (request.Kind == "available")
                    {
                        var release = new UpdateRelease(1, "FilesMate", "1.1.999.0", "1.1.999-preview.20",
                            "FilesMate-Setup-1.1.999-preview.20-win-x64.exe", 1, new string('0', 64), "Isolated presentation fixture");
                        UpdateManifest.Validate(release);
                        _ = QueueAnnouncement(new("diagnostic-update:" + release.Version, false, false, release.DisplayVersion, () =>
                        {
                            File.WriteAllText(ack + ".action.json", JsonSerializer.Serialize(new { Action = "UpdatePrimaryInvoked", NoDownload = true, NoInstaller = true }));
                            return Task.CompletedTask;
                        }, new(TaskCreationOptions.RunContinuationsAsynchronously)));
                    }
                    else if (request.Kind == "scale") _announcementCard?.SetDiagnosticTextScale(request.Scale);
                    else if (request.Kind == "close") await CloseAnnouncementAsync(false, true);
                    else if (request.Kind != "inspect") throw new InvalidDataException("Unsupported presentation request");
                    await Task.Delay(200);
                    PositionAnnouncement();
                    File.WriteAllText(ack, JsonSerializer.Serialize(new
                    {
                        Pid = Environment.ProcessId, Window = NativeHandle.ToInt64(), Request = request.Kind,
                        IsolatedProfile = RuntimeProfile.DirectoryPath, NoNativeSettings = true, NoNetworkOrInstallerForFixture = true,
                        Current = _announcementCurrent?.Key, Pending = _announcements.Select(p => p.Key).ToArray(),
                        Width = _announcementCard?.ActualWidth, Height = _announcementCard?.ActualHeight,
                        Visible = _announcementCard?.Visibility == Visibility.Visible,
                        ModalOpen = AnnouncementModalOpen(), PausedForModal = _announcementPausedForModal,
                        SettingsVisible = SettingsOverlay.Visibility == Visibility.Visible,
                        SameSettingsParent = _announcementRoot is not null && ReferenceEquals(VisualTreeHelper.GetParent(SettingsOverlay), _announcementRoot),
                        SettingsZ = Canvas.GetZIndex(SettingsOverlay), AnnouncementZ = _announcementCard is null ? 0 : Canvas.GetZIndex(_announcementCard),
                    }));
                }
            }
            catch (Exception error) { App.LogFailure("Announcement presentation diagnostics", error); }
            finally { _announcementDiagnosticReading = false; }
        };
        _announcementDiagnosticsTimer.Start();
        Closed += (_, _) => _announcementDiagnosticsTimer.Stop();
    }

    private sealed record AnnouncementDiagnosticRequest(string Kind, bool Automatic = false, double Scale = 1);
}
