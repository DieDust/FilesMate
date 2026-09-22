#if FILESMATE_UI_TEST
using System.IO.Compression;
using System.Text.Json;
using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.CompactMate;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunArchiveSmokeAsync()
    {
        var evidence = new Dictionary<string, object>();
        using var trace = new System.Diagnostics.TextWriterTraceListener(Path.Combine(AppContext.BaseDirectory, "archive-smoke-trace.log"));
        System.Diagnostics.Trace.Listeners.Add(trace);
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            var host = (FrameworkElement)Content;
            for (var i = 0; i < 100 && !host.IsLoaded; i++) await Task.Delay(50);
            var root = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "test-profile", "archive-" + Guid.NewGuid().ToString("N"))).FullName;
            var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            var output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            var input = Path.Combine(source, "资料.txt");
            File.WriteAllText(input, "archive UI roundtrip");
            var operations = new WindowsLocalFileOperations();
            var compressed = await ArchiveOperationUI.RunBuiltInAsync(host, CompactMateVerb.CompressZip, [input], source, operations);
            evidence["CreateResult"] = compressed is null ? "null" : new { compressed.Errors, compressed.Completed, compressed.Cancelled, Undo = compressed.Undo?.Kind.ToString() };
            Require(compressed is { Errors.Count: 0, Completed.Count: 1, Undo: not null }, "ZIP create result");
            var archive = Path.Combine(source, "资料.zip");
            using (var zip = ZipFile.OpenRead(archive)) Require(zip.GetEntry("资料.txt") is not null, "ZIP contents");
            evidence["CreateZipWithProgress"] = true;
            var extracted = await ArchiveOperationUI.RunBuiltInAsync(host, CompactMateVerb.ExtractHere, [archive], output, operations);
            evidence["ExtractResult"] = extracted is null ? "null" : new { extracted.Errors, extracted.Completed, extracted.Cancelled, Undo = extracted.Undo?.Kind.ToString() };
            Require(extracted is { Errors.Count: 0, Completed.Count: 1, Undo: not null }, "ZIP extract result");
            Require(File.ReadAllText(Path.Combine(output, "资料.txt")) == "archive UI roundtrip", "Extract contents");
            evidence["ExtractZipWithProgress"] = true;

            File.WriteAllText(Path.Combine(output, "资料.txt"), "existing content");
            var work = ArchiveOperationUI.RunBuiltInAsync(host, CompactMateVerb.ExtractHere, [archive], output, operations);
            var conflict = await WaitDialogAsync(dialog => dialog.PrimaryButtonText.Length > 0);
            var replace = FindDescendant<RadioButton>(conflict, button => button.Tag is FileConflictAction.Replace);
            Require(replace is not null, "Replace choice unavailable");
            replace!.IsChecked = true;
            await Task.Delay(30);
            var primary = FindDescendant<Button>(conflict, button => button.Name == "PrimaryButton")
                ?? throw new IOException("Conflict confirmation missing");
            Invoke(primary);
            var replaced = await work;
            Require(replaced is { Errors.Count: 0, Completed.Count: 1, Undo: not null }, "Conflict transfer result");
            Require(File.ReadAllText(Path.Combine(output, "资料.txt")) == "archive UI roundtrip", "Conflict replacement contents");
            evidence["ProgressPausesForReplaceDialog"] = true;

            // Prepare incompressible data in the isolated test profile, then click the real progress Cancel button.
            var large = Path.Combine(source, "cancel.bin");
            await Task.Run(() =>
            {
                var bytes = new byte[65536]; var random = new Random(67);
                using var stream = File.Create(large);
                for (var i = 0; i < 512; i++) { random.NextBytes(bytes); stream.Write(bytes); }
            });
            var cancelling = ArchiveOperationUI.RunBuiltInAsync(host, CompactMateVerb.CompressZip, [large], source, operations);
            var progress = await WaitDialogAsync(dialog => dialog.CloseButtonText == StringTable.Get("Archive_Cancel") && string.IsNullOrEmpty(dialog.PrimaryButtonText));
            var cancel = FindDescendant<Button>(progress, button => button.Name == "CloseButton")
                ?? throw new IOException("Progress cancellation missing");
            Invoke(cancel);
            var cancelled = await cancelling;
            Require(cancelled is { Cancelled: true, Completed.Count: 0 }, "Cancellation result");
            Require(!File.Exists(Path.Combine(source, "cancel.zip")), "Cancelled ZIP exists");
            Require(!Directory.EnumerateDirectories(root, ".filesmate-archive-*", SearchOption.AllDirectories).Any(), "Staging retained");
            Require(!FileOperationLifetime.IsBusy, "Operation lease retained");
            evidence["CancelButtonAndCleanup"] = true;
            evidence["Passed"] = true;

            async Task<ContentDialog> WaitDialogAsync(Func<ContentDialog, bool> predicate)
            {
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(host.XamlRoot))
                    {
                        if (popup.Child is ContentDialog direct && predicate(direct)) return direct;
                        if (FindDescendant<ContentDialog>(popup.Child, predicate) is { } dialog) return dialog;
                    }
                    await Task.Delay(25);
                }
                throw new IOException("Expected archive dialog did not appear");
            }
        }
        catch (Exception error) { evidence["Passed"] = false; evidence["Error"] = error.ToString(); }
        trace.Flush();
        System.Diagnostics.Trace.Listeners.Remove(trace);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "archive-smoke.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        Close();

        static void Require(bool success, string message) { if (!success) throw new IOException(message); }
        static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
}
#endif
