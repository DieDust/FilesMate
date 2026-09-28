using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FilesMate.App.Models;
using FilesMate.Search;
using FilesMate.SearchHost;

internal static class SpotlightSmoke
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Field(object target, string name) => target.GetType().GetField(name, Flags)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }

    internal static int Run(string output, string language = "zh-CN")
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(language);
        FilesMate.App.Localization.StringTable.UseUiCulture = true;
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Inactive WPF popup tests may leave a nested dispatcher frame.
        // Unwind it after closing the fixture windows so the harness exits.
        app.Startup += async (_, _) =>
        {
            try { await Verify(output); app.Shutdown(0); Dispatcher.ExitAllFrames(); }
            catch (Exception error) { File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); app.Shutdown(1); Dispatcher.ExitAllFrames(); }
        };
        var exitCode = app.Run();
        return exitCode;
    }

    private static async Task Verify(string output)
    {
        var host = RuntimeHelpers.GetUninitializedObject(typeof(PaletteWindow).Assembly.GetType("FilesMate.SearchHost.SearchHost")!);
        Set(host, "<Profile>k__BackingField", output);
        Set(host, "<Settings>k__BackingField", new GlobalSearchSettings(StartAtLogin: false, PreviewEnabled: true));
        Set(host, "_app", Application.Current);
        var provider = new Provider(output);
        var window = (PaletteWindow)Activator.CreateInstance(typeof(PaletteWindow), Flags, null, [host, provider], null)!;
        var area = RuntimeHelpers.GetUninitializedObject(typeof(PaletteWindow).GetField("_workArea", Flags)!.FieldType);
        area.GetType().GetField("Left")!.SetValue(area, -10000);
        area.GetType().GetField("Right")!.SetValue(area, -7600);
        area.GetType().GetField("Bottom")!.SetValue(area, 1600);
        Set(window, "_workArea", area);
        Set(window, "_opening", true);
        window.ShowActivated = false;
        window.Left = window.Top = -10000;
        window.Show();
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        var query = (TextBox)window.FindName("QueryBox");
        var results = (ListBox)window.FindName("Results");
        var preview = (Popup)window.FindName("PreviewPopup");
        var tabs = (StackPanel)window.FindName("CategoryButtons");
        var appearance = typeof(PaletteWindow).Assembly.GetType("FilesMate.SearchHost.PaletteAppearance")!;
        foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
        {
            var settings = AppearanceSettings.Default with { Theme = theme, Backdrop = BackdropKind.Solid, ReduceMotion = ReduceMotionKind.On };
            Set(window, "_appearance", settings);
            appearance.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [window.Resources, settings]);
            query.Text = "";
            await Task.Delay(120);
            window.UpdateLayout();
            Check(Math.Abs(window.ActualWidth - 620) <= 1 && window.ActualHeight is >= 64 and <= 68,
                $"Search input did not enlarge slightly: {window.ActualWidth} x {window.ActualHeight}");
            Check(query.FontSize == 19, "Search input did not use the slightly larger text size");
            VerifyInputAlignment(window);
            var header = (Grid)window.FindName("SearchHeader");
            Check(Math.Abs(header.TranslatePoint(new Point(0, header.ActualHeight / 2), window).Y - window.ActualHeight / 2) <= 1,
                "Empty search input is not centered in its frame");
            var emptyHeight = window.ActualHeight;
            var activity = (ProgressBar)window.FindName("SearchActivity");
            activity.Visibility = Visibility.Visible;
            window.UpdateLayout();
            Check(Math.Abs(window.ActualHeight - emptyHeight) <= 1, "Search activity shifts the input layout");
            VerifyInputAlignment(window);
            activity.Visibility = Visibility.Hidden;
            Capture(window, Path.Combine(output, $"spotlight-input-{theme}.png"));
            query.Text = "设计";
            await Wait(() => results.Items.Count == 6 && !(bool)Field(window, "_pending"));
            await Task.Delay(250);
            window.UpdateLayout();
            Check(!preview.IsOpen, "A fresh query should not force preview focus before user selection");
            Check(tabs.Children.Count == 7 && tabs.ActualHeight < 40, "All categories must stay available in one row");
            Check(window.FindName("CategoryOverflowButton") is null, "Duplicate category menu is still present");
            if (CultureInfo.CurrentUICulture.Name == "zh-CN")
                Check(!((Button)window.FindName("CategoryNextButton")).IsVisible, "Default categories should fit without scrolling");
            var count = (TextBlock)window.FindName("CountLabel");
            Check(count.TranslatePoint(new Point(0, count.ActualHeight), window).Y <= results.TranslatePoint(new Point(), window).Y,
                "Result count should sit above the results");
            var row = (ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(0);
            Check(row.ActualHeight <= 48, "Result rows should retain their compact height");
            Check(window.ActualHeight - results.TranslatePoint(new Point(0, results.ActualHeight), window).Y <= 12,
                "An empty footer still occupies the bottom of the palette");
            VerifyGeometry(window);
            Capture(window, Path.Combine(output, $"spotlight-{theme}.png"));
            var rowButtons = Visuals(row).OfType<Button>().ToArray();
            Check(rowButtons.Length == 2 && rowButtons.All(b => b.IsVisible && b.IsEnabled), "Selected result lost its reveal/copy buttons");
            results.SelectedIndex = 1;
            await Wait(() => preview.IsOpen && Field(window, "_previewText") is not null);
            await Task.Delay(150);
            Check(((ICSharpCode.AvalonEdit.TextEditor)Field(window, "_previewText")).Text.Contains("设计"), "Selecting a result no longer opens its preview");
            results.SelectedIndex = 0;
            await Wait(() => ((TextBlock)window.FindName("PreviewTitle")).Text == "设计笔记.txt");
            Call(window, "Preview_Close", window, new RoutedEventArgs());
            Check(!preview.IsOpen && window.IsVisible, "Preview close dismissed the entire search");
            Call(window, "ReleaseTextPreview");
            Call(window, "OpenResultMenu", true);
            var menu = (ContextMenu)Field(window, "_resultMenu");
            Check(menu.IsOpen && menu.Items.Count > 5, "Shared file actions are unavailable");
            // The fixture is intentionally inactive; suppress the real menu's
            // focus-loss dismissal while its close callback drains.
            Set(window, "_dragging", true);
            menu.IsOpen = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Set(window, "_dragging", false);
            query.Text = "no-match";
            await Wait(() => results.Items.Count == 0 && !(bool)Field(window, "_pending"));
            Check(!preview.IsOpen, "Empty results retain the old preview");
        }
        query.Text = "设计";
        await Wait(() => results.Items.Count == 6 && !(bool)Field(window, "_pending"));
        foreach (var filter in new[] { SearchFilter.Documents, SearchFilter.Images, SearchFilter.Folders, SearchFilter.Media, SearchFilter.Apps, SearchFilter.Executables, SearchFilter.All })
        {
            var tab = tabs.Children.OfType<RadioButton>().Single(b => ((SearchCategory)b.Tag).Builtin == filter);
            Select(tab);
            await Wait(() => !(bool)Field(window, "_pending"));
            Check((SearchFilter)Field(window, "_filter") == filter, "Clicking a category did not change the filter");
            var expected = filter switch { SearchFilter.All => 6, SearchFilter.Documents => 4, SearchFilter.Images or SearchFilter.Folders => 1, _ => 0 };
            Check(results.Items.Count == expected && results.Items.OfType<SearchRow>().All(r => SearchFilters.Matches(filter, r.Path, r.Hit.IsDirectory)), "Category displayed mismatched results");
        }
        var extra = SearchCategories.Defaults();
        for (var i = 0; i < 15; i++) extra.Add(new SearchCategory("custom-" + i, "自定义类型 " + i, null, [".txt"]));
        Set(window, "_categories", extra);
        area.GetType().GetField("Right")!.SetValue(area, -10000 + (int)(440 * VisualTreeHelper.GetDpi(window).DpiScaleX) + 40);
        Set(window, "_workArea", area);
        Call(window, "Center");
        Call(window, "RenderCategoryButtons");
        window.UpdateLayout();
        await Task.Delay(150);
        Check(tabs.Children.Count == extra.Count && tabs.ActualHeight < 40, "Custom categories must remain in the same scrollable row");
        var categoryScroll = (ScrollViewer)window.FindName("CategoryScroll");
        var previous = (Button)window.FindName("CategoryPreviousButton");
        var next = (Button)window.FindName("CategoryNextButton");
        Check(previous.IsVisible && !previous.IsEnabled && next.IsEnabled, "Category arrows do not reflect the start of the strip");
        var categoryOrder = tabs.Children.OfType<RadioButton>().Select(b => ((SearchCategory)b.Tag).Id).ToArray();
        for (var i = 0; i < 45 && next.IsEnabled; i++)
        {
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(30);
        }
        Check(!next.IsEnabled && previous.IsEnabled, "Category arrows cannot reach the end");
        Check((string)Field(window, "_categoryId") == "All", "Scrolling categories should not change the active filter");
        var lastCategory = tabs.Children.OfType<RadioButton>().Last();
        CheckVisibleCategory(lastCategory);
        Select(lastCategory);
        await Wait(() => results.Items.Count == 2 && !(bool)Field(window, "_pending"));
        Check(results.Items.OfType<SearchRow>().All(r => r.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)), "Custom category did not filter indexed files");
        Check(categoryOrder.SequenceEqual(tabs.Children.OfType<RadioButton>().Select(b => ((SearchCategory)b.Tag).Id)), "Selecting a category reordered or replaced tabs");
        var offset = categoryScroll.HorizontalOffset;
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        categoryScroll.RaiseEvent(wheel);
        await Task.Delay(100);
        Check(wheel.Handled && categoryScroll.HorizontalOffset < offset, "Mouse wheel did not scroll the category strip");
        Check((string)Field(window, "_categoryId") == extra[^1].Id, "Mouse wheel changed the category selection");
        // Resizing must reveal the selection without rebuilding or replacing tabs.
        area.GetType().GetField("Right")!.SetValue(area, -10000 + (int)(380 * VisualTreeHelper.GetDpi(window).DpiScaleX) + 40);
        Set(window, "_workArea", area); Call(window, "Center");
        await Task.Delay(150);
        CheckVisibleCategory(lastCategory);
        VerifyGeometry(window);
        Capture(window, Path.Combine(output, "spotlight-narrow.png"));
        Select(tabs.Children.OfType<RadioButton>().First());
        await Wait(() => results.Items.Count == 6 && !(bool)Field(window, "_pending"));
        await Task.Delay(100);
        Check(categoryScroll.HorizontalOffset <= 1, "Selecting the first category did not scroll it into view");
        Set(window, "_categories", SearchCategories.Defaults()); Call(window, "RenderCategoryButtons");
        foreach (var width in new[] { 380, 440, 660 })
        {
            area.GetType().GetField("Right")!.SetValue(area, -10000 + (int)(width * VisualTreeHelper.GetDpi(window).DpiScaleX) + 40);
            Set(window, "_workArea", area); Call(window, "Center");
            query.Text = "long-name-" + width;
            await Wait(() => results.Items.Count == 1 && !(bool)Field(window, "_pending"));
            await Task.Delay(100); window.UpdateLayout();
            VerifyGeometry(window);
            Capture(window, Path.Combine(output, $"spotlight-long-name-{width}.png"));
        }
        var categoriesOverflow = tabs.ActualWidth > ((Grid)window.FindName("CategoryStrip")).ActualWidth + 1;
        Check(previous.IsVisible == categoriesOverflow && next.IsVisible == categoriesOverflow, "Category arrows did not update after resizing");
        Set(host, "_stopping", true);
        Call(window, "Dismiss", false);
        window.Close();
        File.WriteAllText(Path.Combine(output, "result.json"), "{\"Passed\":true,\"Themes\":2,\"InputSlightlyLarger620x66\":true,\"InputTextAndIconsCentered\":true,\"WindowHorizontallyCentered\":true,\"ActivityDoesNotShiftInput\":true,\"CompactResultRowsPreserved\":true,\"CountAndMoreAboveResults\":true,\"NoBottomFooter\":true,\"SingleRowCategories\":true,\"NoDuplicateCategoryMenu\":true,\"ArrowsOnlyWhenCategoriesOverflow\":true,\"MouseWheelScrollsWithoutSelecting\":true,\"SelectedCategoryVisibleAfterResize\":true,\"StableCategoryOrder\":true,\"CustomCategoryFiltersIndexedFiles\":true,\"SelectionPreviewsFile\":true,\"PreviewFollowsSelection\":true,\"RowRevealAndCopyPreserved\":true,\"SharedActions\":true,\"NoStalePreview\":true,\"UnclippedIconsAndHitTargets\":true,\"TypeActionSeparation\":true,\"PillWithoutArrow\":true,\"LongNamesAt380440660\":true}");

        void CheckVisibleCategory(RadioButton button)
        {
            var left = button.TranslatePoint(new Point(), categoryScroll).X;
            Check(left >= -1 && left + button.ActualWidth <= categoryScroll.ViewportWidth + 1, "Selected category is clipped outside the scroll viewport");
        }
    }

    private static void Select(RadioButton button)
        => ((ISelectionItemProvider)new RadioButtonAutomationPeer(button).GetPattern(PatternInterface.SelectionItem)).Select();

    private static void VerifyInputAlignment(PaletteWindow window)
    {
        window.UpdateLayout();
        var header = (Grid)window.FindName("SearchHeader");
        var query = (TextBox)window.FindName("QueryBox");
        var content = (ScrollViewer)query.Template.FindName("PART_ContentHost", query);
        var elements = new[] { content, (FrameworkElement)window.FindName("Placeholder"),
            (FrameworkElement)window.FindName("SearchIcon"), (FrameworkElement)window.FindName("SearchSettingsButton"),
            (FrameworkElement)window.FindName("SearchCloseButton") };
        foreach (var element in elements.Where(e => e.IsVisible))
        {
            var center = element.TranslatePoint(new Point(0, element.ActualHeight / 2), header).Y;
            Check(Math.Abs(center - header.ActualHeight / 2) <= 1, $"{element.Name} is not vertically centered: {center:0.##}");
        }
        var area = Field(window, "_workArea");
        var left = (int)area.GetType().GetField("Left")!.GetValue(area)!;
        var right = (int)area.GetType().GetField("Right")!.GetValue(area)!;
        Check(GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds), "Search window has no native bounds");
        Check(Math.Abs((bounds.Left + bounds.Right) / 2d - (left + right) / 2d) <= 1,
            "Search window is not horizontally centered in the current work area");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    private static void VerifyGeometry(PaletteWindow window)
    {
        window.UpdateLayout();
        VerifyInputAlignment(window);
        foreach (var name in new[] { "SearchSettingsButton", "SearchCloseButton", "CategoryPreviousButton", "CategoryNextButton", "ClearButton", "MoreResultsButton" })
            VerifyButton((Button)window.FindName(name));
        var results = (ListBox)window.FindName("Results");
        var row = (ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(results.SelectedIndex);
        var kind = Visuals(row).OfType<TextBlock>().Single(t => t.Name == "ResultKind");
        var actions = Visuals(row).OfType<StackPanel>().Single(p => p.Name == "ResultActionStrip");
        var typeRight = kind.TranslatePoint(new Point(kind.ActualWidth, 0), row).X;
        var actionLeft = actions.TranslatePoint(new Point(), row).X;
        Check(actionLeft - typeRight >= 14, "File type touches the action group");
        foreach (var button in Visuals(actions).OfType<Button>()) VerifyButton(button);
        var pill = (Button)window.FindName("MoreResultsButton");
        Check(pill.TranslatePoint(new Point(0, pill.ActualHeight), window).Y <= results.TranslatePoint(new Point(), window).Y,
            "More results should sit above the results");
        Check(pill.ActualHeight <= 27, $"More results is too tall: {pill.ActualHeight}");
        Check(!pill.Content.ToString()!.Contains('→') && pill.Background is SolidColorBrush { Color.A: > 0 },
            "More results must have a visible button surface without an arrow");
        var pillBorder = (Border)pill.Template.FindName("PillBorder", pill);
        Check(pillBorder.CornerRadius.TopLeft >= pill.ActualHeight / 2 - 1, "More results is not a pill");
        var header = (Grid)window.FindName("SearchHeader");
        var query = (TextBox)window.FindName("QueryBox");
        var settings = (Button)window.FindName("SearchSettingsButton");
        Check(query.TranslatePoint(new Point(query.ActualWidth, 0), header).X < settings.TranslatePoint(new Point(), header).X,
            "Search input overlaps settings");

        void VerifyButton(Button button)
        {
            if (!button.IsVisible) return;
            var text = Visuals(button).OfType<TextBlock>().First(t => t.Text.Length > 0);
            var measured = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
                text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            Check(text.ActualWidth + 1 >= measured.WidthIncludingTrailingWhitespace,
                $"{button.Name}: text/glyph is clipped ({text.ActualWidth:0.##} < {measured.WidthIncludingTrailingWhitespace:0.##})");
            var origin = text.TranslatePoint(new Point(), button);
            Check(origin.X >= 0 && origin.X + measured.WidthIncludingTrailingWhitespace <= button.ActualWidth + 1,
                button.Name + ": content escapes the click target");
            if (!button.IsEnabled) return;
            var center = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), window);
            var hit = window.InputHitTest(center) as DependencyObject;
            while (hit is not null && hit != button) hit = VisualTreeHelper.GetParent(hit);
            Check(hit == button, button.Name + ": visible center does not hit the button");
        }
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Wait(Func<bool> ready)
    {
        for (var i = 0; i < 120; i++) { if (ready()) return; await Task.Delay(50); }
        throw new TimeoutException("Search UI did not reach the expected state");
    }
    private static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var shot = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        shot.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(shot));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class Provider : IGlobalSearchProvider
    {
        private readonly NameHit[] _hits;
        internal Provider(string output)
        {
            var folder = Path.Combine(output, "工作项目", "FilesMate"); Directory.CreateDirectory(folder);
            var names = new[] { "设计笔记.txt", "交互设计.txt", "界面设计说明.md", "设计规范.pdf", "首页设计.png", "设计资源" };
            _hits = names.Select((name, i) => new NameHit(name, Path.Combine(folder, name), i == 5)).ToArray();
            foreach (var hit in _hits.Take(3)) File.WriteAllText(hit.Path, "设计记录\n\n搜索应该轻盈、清晰，让文件成为页面的主角。\n\n1. 统一间距与对齐\n2. 清楚的文字层级\n3. 按需出现的操作与预览");
            Directory.CreateDirectory(_hits[5].Path);
            using var database = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(output, "search-index.db")};Pooling=False");
            database.Open();
            using var command = database.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS file_name(name TEXT,path TEXT,is_dir TEXT); DELETE FROM file_name;";
            command.ExecuteNonQuery();
            foreach (var hit in _hits)
            {
                command.CommandText = "INSERT INTO file_name VALUES ($name,$path,$dir);";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$name", hit.Name);
                command.Parameters.AddWithValue("$path", hit.Path);
                command.Parameters.AddWithValue("$dir", hit.IsDirectory ? "1" : "0");
                command.ExecuteNonQuery();
            }
        }
        public Task<GlobalSearchResponse> SearchAsync(string query, CancellationToken token, SearchFilter filter = SearchFilter.All, int limit = 40, int offset = 0)
            => Task.FromResult(new GlobalSearchResponse(query == "no-match" ? [] : query.StartsWith("long-name-", StringComparison.Ordinal)
                ? [new NameHit(new string('档', 80) + ".extension_with_a_long_name", _hits[0].Path, false)]
                : _hits.Where(hit => SearchFilters.Matches(filter, hit.Path, hit.IsDirectory)).ToArray(), false));
}
}
