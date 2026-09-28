#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.CompactMate;
using FilesMate.Platform.Windows.Operations;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunArchiveMenuSmokeAsync()
    {
        var evidence = new Dictionary<string, object>();
        var root = (Grid)Content;
        FileDetailsSurface? surface = null;
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 1000));
            var folder = Environment.GetEnvironmentVariable("FILESMATE_ARCHIVE_MENU_INPUT")
                ?? throw new IOException("Fixture path missing.");
            var paths = Directory.GetFiles(folder, "*删删删");
            if (paths.Length < 2) throw new IOException("Expected decorated multi-selection fixture.");
            if (!CompactMateHost.TryFind(new CurrentUserRegistry(), out var executable))
                throw new IOException("Installed CompactMate was not discovered.");
            evidence["Provider"] = executable;
            if (!ShellContextMenu.TryCreate(NativeHandle, paths, folder, false, false, out var session))
                throw new IOException("Shell menu could not be created.");
            using (session)
            {
                var compact = session!.Items.FirstOrDefault(item => item.Label == "CompactMate");
                if (compact is null || !compact.Children.Any(child => child.Label.Contains("解压", StringComparison.Ordinal)))
                    throw new IOException("Shell cascade did not populate its extraction commands.");
                evidence["ShellCascade"] = compact.Children.Select(child => child.Label).ToArray();
            }
            surface = new FileDetailsSurface { Width = 1000, Height = 700,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            root.Children.Add(surface);
            await Task.Delay(800);
            Task? launch = null;
            var invocations = 0;
            surface.CommandRequested += (_, id) =>
            {
                if (id != AppCommandId.ExtractHere) throw new IOException("Wrong menu command.");
                invocations++;
                launch = ArchiveOperationUI.RunAsync(surface, CompactMateVerb.ExtractHere, surface.SelectedPaths(),
                    folder, new WindowsLocalFileOperations());
            };
            await surface.InvokeArchiveMenuSmokeAsync(paths);
            if (launch is null || invocations != 1) throw new IOException("Menu did not dispatch one batch.");
            await launch;
            evidence["MenuInvocations"] = invocations;
            evidence["SelectedFiles"] = surface.SelectedPaths().Count;
            evidence["Passed"] = true;
        }
        catch (Exception error) { evidence["Passed"] = false; evidence["Error"] = error.ToString(); }
        finally
        {
            if (surface is not null) root.Children.Remove(surface);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "archive-menu-smoke.json"),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}
#endif
