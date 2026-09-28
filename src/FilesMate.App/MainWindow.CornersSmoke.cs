#if FILESMATE_UI_TEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunCornersSmokeAsync()
    {
        var samples = new List<object>();
        try
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            foreach (var theme in new[] { Models.AppThemeKind.Light, Models.AppThemeKind.Dark })
            {
                await App.AppearanceViewModel!.SetThemeAsync(theme);
                foreach (var width in new[] { 1600, 1100 })
                {
                    AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 1000));
                    OpenSettings("about");
                    await Task.Delay(900);
                    await Capture(SettingsPanel, $"settings-corners-{theme}-{width}.png");
                    await Capture(Content, $"settings-corners-window-{theme}-{width}.png");
                    var bitmap = new RenderTargetBitmap();
                    await bitmap.RenderAsync(SettingsPanel);
                    var pixels = (await bitmap.GetPixelsAsync()).ToArray();
                    var w = bitmap.PixelWidth;
                    var h = bitmap.PixelHeight;
                    var alpha = new int[] { pixels[3], pixels[(w - 1) * 4 + 3], pixels[((h - 1) * w) * 4 + 3], pixels[(w * h - 1) * 4 + 3] };
                    var edgeAlpha = new int[] { pixels[(w / 2) * 4 + 3], pixels[(h / 2 * w) * 4 + 3],
                        pixels[(h / 2 * w + w - 1) * 4 + 3], pixels[((h - 1) * w + w / 2) * 4 + 3] };
                    // The curved edge must retain partially covered pixels (antialiasing).
                    var partial = 0;
                    var cornerSize = (int)Math.Ceiling(16 * SettingsPanel.XamlRoot.RasterizationScale);
                    for (var y = 0; y < cornerSize; y++)
                        for (var x = 0; x < cornerSize; x++)
                        {
                            var a = pixels[(y * w + x) * 4 + 3];
                            if (a is > 0 and < 255) partial++;
                        }
                    samples.Add(new { theme, width, w, h, alpha, edgeAlpha, partial });
                    if (alpha.Any(a => a != 0)) throw new InvalidOperationException("Settings content paints outside its rounded corners.");
                    if (partial < 8) throw new InvalidOperationException("Rounded edge has lost its antialiasing.");
                    if (edgeAlpha.Any(a => a < 250)) throw new InvalidOperationException("Sharp content behind settings leaks through its outline.");
                    CloseSettings();
                    await Task.Delay(200);
                }
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "corners-results.json"), JsonSerializer.Serialize(new { Passed = true, samples }));
            await App.AppearanceViewModel!.SetThemeAsync(Models.AppThemeKind.Light);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 1000));
            OpenSettings("about");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "corners-results.json"), JsonSerializer.Serialize(new { Passed = false, Error = error.ToString(), samples }));
        }
    }
}
#endif
