using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using FilesMate.Search;
using Forms = System.Windows.Forms;

internal static class Smoke
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--polish-only") return ProductPolishSmoke.Run(args[1]);
        if (args.Length == 2 && args[0] == "--preview-scroll-only") return ProductPolishSmoke.Run(args[1], previewOnly: true);
        if (args.Length > 0 && args[0] == "--media-only")
            return MediaCacheSmoke.Run(args[1], args[2]).GetAwaiter().GetResult();
        if (args.Length == 0 || args[0] == "--activate")
        {
            var marker = Environment.GetEnvironmentVariable("FILESMATE_TRAY_SMOKE_MARKER");
            if (marker is not null) File.WriteAllText(marker, "opened");
            return 0;
        }
        var profile = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(profile);
        var markerPath = Path.Combine(profile, "manager-opened.txt");
        Environment.SetEnvironmentVariable("FILESMATE_TRAY_SMOKE_MARKER", markerPath);
        GlobalSearchConfiguration.Save(new(true, "Ctrl+Alt+Shift+F10"), profile);
        using (var database = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(profile, "search-index.db")};Pooling=False"))
        {
            database.Open();
            using var command = database.CreateCommand();
            command.CommandText = "DROP TABLE IF EXISTS file_name; CREATE TABLE file_name(name TEXT,path TEXT,is_dir TEXT);";
            command.ExecuteNonQuery();
            for (var i = 0; i < 270; i++)
            {
                var path = Path.Combine(profile, $"fixture_{i}.txt");
                File.WriteAllText(path, $"fixture {i}");
                command.CommandText = $"INSERT INTO file_name VALUES ('fixture_{i}.txt',$file,'0');";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$file", path);
                command.ExecuteNonQuery();
            }
            command.CommandText = "INSERT INTO file_name VALUES ('fixture.lnk',$path,'0'),('fixture','D:\\fixture','1');";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$path", Environment.ProcessPath!);
            command.ExecuteNonQuery();
        }
        System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var type = typeof(FilesMate.SearchHost.Program).Assembly.GetType("FilesMate.SearchHost.SearchHost")!;
        using var host = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [app, profile, Environment.ProcessPath!], null)!;
        var tray = (Forms.NotifyIcon)type.GetField("_tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        object? Call(string name, params object[] values) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(host, values);
        Window Palette() => (Window)type.GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        void Click() => typeof(Forms.NotifyIcon).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tray, [new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 0, 0, 0)]);
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                if (args.Contains("--tray-only"))
                {
                    var popup = tray.ContextMenuStrip!;
                    foreach (var theme in new[] { "Light", "Dark" })
                    {
                        File.WriteAllText(Path.Combine(profile, "appearance.json"), "{\"theme\":\"" + theme + "\"}");
                        popup.Show(new System.Drawing.Point(2000, 1100));
                        popup.Items[0].Select();
                        await Task.Delay(200);
                        Assert(popup.Visible && popup.Items[0].Selected, "Tray highlight was not visible");
                        Assert(theme == "Dark" ? popup.BackColor.R < 80 : popup.BackColor.R > 220, "Tray theme mismatch");
                        foreach (var item in popup.Items.OfType<Forms.ToolStripMenuItem>())
                        {
                            Assert(item.Bounds.Left > 0 && item.Bounds.Left == popup.ClientSize.Width - item.Bounds.Right,
                                "Tray item was not centered within the surface");
                            Assert(ReferenceEquals(popup.GetItemAt(item.Bounds.Left + item.Width / 2, item.Bounds.Top + item.Height / 2), item),
                                "Tray hit target did not follow the visible item");
                        }
                        Assert(popup.Items[0].Bounds.Top == popup.ClientSize.Height - popup.Items[popup.Items.Count - 1].Bounds.Bottom,
                            "Tray top and bottom insets differ");
                        File.AppendAllText(Path.Combine(profile, "geometry.txt"),
                            $"{theme}: DPI={popup.DeviceDpi}, bounds={popup.Bounds}, client={popup.ClientRectangle}, display={popup.DisplayRectangle}, padding={popup.Padding}, layout={popup.LayoutStyle}\n" +
                            string.Join("\n", popup.Items.Cast<Forms.ToolStripItem>().Select(item => $"{item.Text}: bounds={item.Bounds}, margin={item.Margin}")) + "\n");
                        using var capture = new System.Drawing.Bitmap(popup.Width + 40, popup.Height + 40);
                        using var graphics = System.Drawing.Graphics.FromImage(capture);
                        graphics.CopyFromScreen(popup.Left - 20, popup.Top - 20, 0, 0, capture.Size);
                        capture.Save(Path.Combine(profile, "tray-" + theme + ".png"));
                        popup.Close();
                    }
                    File.WriteAllText(Path.Combine(profile, "result.json"), "{\"Passed\":true,\"TrayCaptured\":true,\"BalancedInsets\":true,\"HitTargetsAligned\":true,\"BothThemes\":true}");
                    app.Shutdown(0);
                    return;
                }
                if (args.Contains("--documents-only"))
                {
                    Call("Show", false);
                    await DocumentPreviewSmoke.RunAsync(Palette(), profile, () => Call("Show", false), settings => Call("Apply", settings), args.Contains("--document-input"));
                    app.Shutdown(0);
                    return;
                }
                if (args.Contains("--preview-interaction"))
                {
                    Call("Show", false);
                    await PreviewInteractionSmoke.Run(Palette(), profile, args[Array.IndexOf(args,"--preview-interaction")+1]);
                    app.Shutdown(0);
                    return;
                }
                if (args.Contains("--office-only"))
                {
                    Call("Show", false);
                    await OfficePreviewSmoke.Run(Palette(), profile, args[Array.IndexOf(args, "--office-only") + 1]);
                    app.Shutdown(0);
                    return;
                }
                await Task.Delay(600);
                Click();
                Assert(Palette().IsVisible, "Single click did not open search");
                var query = (TextBox)Palette().FindName("QueryBox");
                query.Text = "keep-this-query";
                Call("Show", false);
                Assert(Palette().IsVisible && query.Text == "keep-this-query", "Repeated show closed or cleared search");
                query.Text = "fixture";
                await Task.Delay(450);
                var results = (ListBox)Palette().FindName("Results");
                if (args.Contains("--categories-only"))
                {
                    var window = Palette();
                    void Invoke(string method) => window.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
                    Call("Show", true);
                    Invoke("Categories_Open");
                    ((TextBox)window.FindName("CategoryName")).Text = "自定义文本";
                    ((TextBox)window.FindName("CategoryExtensions")).Text = "TXT, .log";
                    Invoke("Category_Add");
                    var saved = SearchCategories.Load(profile);
                    var custom = saved.Single(c => c.Name == "自定义文本");
                    Assert(custom.Extensions.SequenceEqual(new[] { ".txt", ".log" }), "Custom extensions were not saved");
                    var editors = (StackPanel)window.FindName("CategoryEditors");
                    var lastGrid = (Grid)((Border)editors.Children[editors.Children.Count - 1]).Child;
                    var fields = lastGrid.Children.OfType<StackPanel>().Single();
                    ((TextBox)fields.Children[0]).Text = "日志文档";
                    await Task.Delay(700);
                    Assert(SearchCategories.Load(profile).Single(c => c.Id == custom.Id).Name == "日志文档", "Editing a category did not auto-save");
                    lastGrid.Children.OfType<Button>().Single(b => Equals(b.Content, "↑")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    saved = SearchCategories.Load(profile);
                    Assert(saved[^2].Id == custom.Id, "Category order did not persist");
                    var appGrid = (Grid)((Border)editors.Children[1]).Child;
                    var toggle = appGrid.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single();
                    toggle.IsChecked = false;
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(!SearchCategories.Load(profile).Single(c => c.Builtin == SearchFilter.Apps).Visible, "Visibility did not persist");
                    await Task.Delay(150);
                    var origin = window.PointToScreen(new Point());
                    var scale = System.Windows.Media.VisualTreeHelper.GetDpi(window);
                    using (var capture = new System.Drawing.Bitmap((int)(window.ActualWidth * scale.DpiScaleX), (int)(window.ActualHeight * scale.DpiScaleY)))
                    {
                        using var graphics = System.Drawing.Graphics.FromImage(capture);
                        graphics.CopyFromScreen((int)origin.X, (int)origin.Y, 0, 0, capture.Size);
                        capture.Save(Path.Combine(profile, "categories.png"));
                    }
                    Invoke("Categories_Back");
                    Invoke("Settings_Click");
                    var buttons = (WrapPanel)window.FindName("CategoryButtons");
                    Assert(!buttons.Children.OfType<RadioButton>().Any(b => Equals(b.Content, "应用")), "Hidden category still appears");
                    buttons.Children.OfType<RadioButton>().Single(b => Equals(b.Content, "日志文档")).IsChecked = true;
                    await Task.Delay(600);
                    Assert(results.Items.Count > 0 && results.Items.OfType<FilesMate.SearchHost.SearchRow>().All(r => r.Path.EndsWith(".txt")), "Custom category did not filter results");
                    File.WriteAllText(Path.Combine(profile, "result.json"), "{\"Passed\":true,\"Add\":true,\"AutoSave\":true,\"Order\":true,\"Visibility\":true,\"Filtering\":true}");
                    app.Shutdown(0);
                    return;
                }
                if (args.Contains("--preview-only"))
                {
                    var window = Palette();
                    Call("Show", false);
                    var heading = window.PointToScreen(new Point(30, 30));
                    Assert(InputProbe.OwnsPoint(heading), "Another window covers the test search header");
                    InputProbe.Click(heading, false);
                    query.Text = "fixture";
                    var card = (Border)window.FindName("PreviewCard");
                    var text = (ICSharpCode.AvalonEdit.TextEditor)window.FindName("PreviewText");
                    var image = (System.Windows.Controls.Image)window.FindName("PreviewImage");
                    var icons = window.GetType().GetField("_icons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    for (var wait = 0; wait < 30 && results.Items.Count == 0; wait++) await Task.Delay(100);
                    Assert(window.IsVisible && results.Items.Count > 0, $"Search results were interrupted before preview testing: visible={window.IsVisible}, results={results.Items.Count}, query={query.Text}, status={((TextBlock)window.FindName("StatusText")).Text}, foreground={InputProbe.Foreground():X}, main={new WindowInteropHelper(window).Handle:X}, popup={(PresentationSource.FromVisual(text) as HwndSource)?.Handle:X}");
                    var first = (FilesMate.SearchHost.SearchRow)results.Items[0];
                    var textPath = Path.Combine(profile, "preview.txt");
                    await File.WriteAllTextAsync(textPath, string.Join('\n', Enumerable.Range(1, 400).Select(i => $"第 {i} 行：FilesMate 文本预览，支持滚动阅读。")), System.Text.Encoding.Unicode);
                    var png = Path.Combine(profile, "preview.png");
                    using (var fixture = new System.Drawing.Bitmap(640, 360))
                    {
                        using var graphics = System.Drawing.Graphics.FromImage(fixture);
                        graphics.Clear(System.Drawing.Color.CornflowerBlue);
                        graphics.FillEllipse(System.Drawing.Brushes.Gold, 220, 80, 200, 200);
                        fixture.Save(png);
                    }
                    var textRow = new FilesMate.SearchHost.SearchRow(new NameHit("preview.txt", textPath, false), first.Icon);
                    var pictureRow = new FilesMate.SearchHost.SearchRow(new NameHit("preview.png", png, false), first.Icon);
                    void Preview(FilesMate.SearchHost.SearchRow row) => window.GetType().GetMethod("QueuePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [row, 0]);
                    async Task WaitFor(Func<bool> predicate)
                    {
                        for (var attempt = 0; attempt < 60 && !predicate(); attempt++) await Task.Delay(100);
                        Assert(predicate(), $"Preview did not complete: visible={window.IsVisible}, card={card.IsVisible}, text={text.Text.Length}, focus={window.GetType().GetProperty("LastFocusDismiss", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)}");
                    }
                    foreach (var theme in new[] { "Light", "Dark" })
                    {
                        window.GetType().GetMethod("Dismiss", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, []);
                        File.WriteAllText(Path.Combine(profile, "appearance.json"), "{\"theme\":\"" + theme + "\"}");
                        Call("Show", false);
                        Assert(InputProbe.OwnsPoint(heading), "Another window covers the test search header");
                        InputProbe.Click(heading, false);
                        query.Text = "fixture";
                        await Task.Delay(450);
                        var ink = (System.Windows.Media.SolidColorBrush)window.Resources["Ink"];
                        Assert(theme == "Light" ? ink.Color.R < 100 : ink.Color.R > 180, "Preview theme did not apply");
                        Assert(!card.IsVisible, "Refreshing results loaded an unsolicited preview");
                        Preview(textRow);
                        await WaitFor(() => text.Text.StartsWith("第 1 行"));
                        Assert(card.IsVisible && card.ActualWidth > 200, "Preview pane is too narrow or hidden");
                        text.ScrollToEnd();
                        await Task.Delay(100);
                        Assert(text.VerticalOffset > 0, "Text preview does not scroll");
                        Assert(window.Width <= 660, "Preview changed the original search width");
                        var origin = window.PointToScreen(new Point());
                        var previewOrigin = card.PointToScreen(new Point());
                        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(window);
                        var mainBounds = new Rect(origin.X, origin.Y, window.ActualWidth * scale.DpiScaleX, window.ActualHeight * scale.DpiScaleY);
                        var previewBounds = new Rect(previewOrigin.X, previewOrigin.Y, card.ActualWidth * scale.DpiScaleX, card.ActualHeight * scale.DpiScaleY);
                        Assert(!mainBounds.IntersectsWith(previewBounds), $"Preview overlaps search: {mainBounds}; {previewBounds}");
                        var captureBounds = Rect.Union(mainBounds, previewBounds);
                        origin = captureBounds.TopLeft;
                        using var capture = new System.Drawing.Bitmap((int)captureBounds.Width, (int)captureBounds.Height);
                        using var graphics = System.Drawing.Graphics.FromImage(capture);
                        graphics.CopyFromScreen((int)origin.X, (int)origin.Y, 0, 0, capture.Size);
                        capture.Save(Path.Combine(profile, "preview-" + theme + ".png"));
                        window.GetType().GetMethod("ClearPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, []);
                    }
                    var originalItems = results.ItemsSource;
                    results.ItemsSource = new[] { textRow, pictureRow };
                    results.SelectedItem = textRow;
                    await WaitFor(() => text.Text.StartsWith("第 1 行"));
                    results.ItemsSource = originalItems;
                    var jsonPath = Path.Combine(profile, "format.json");
                    await File.WriteAllTextAsync(jsonPath, "{\n  \"enabled\": true,\n  \"count\": 42\n}");
                    Preview(new FilesMate.SearchHost.SearchRow(new NameHit("format.json", jsonPath, false), first.Icon));
                    await WaitFor(() => text.Text.Contains("enabled"));
                    Assert(text.ShowLineNumbers && text.SyntaxHighlighting is not null, "Formatted text has no syntax highlighting or line numbers");
                    text.Focus();
                    await Task.Delay(200);
                    Assert(window.IsVisible && card.IsVisible, "Focusing detached preview dismissed search");
                    var pointer = Forms.Cursor.Position;
                    var mainHandle = new WindowInteropHelper(window).Handle;
                    var popupHandle = (PresentationSource.FromVisual(text) as HwndSource)?.Handle;
                    var clickPoint = text.PointToScreen(new Point(80, 25));
                    Assert(InputProbe.OwnsPoint(clickPoint), "Another window covers the preview click target");
                    try
                    {
                        InputProbe.Click(clickPoint, false);
                        await Task.Delay(200);
                        Assert(window.IsVisible && card.IsVisible, "Clicking detached preview dismissed search");
                    }
                    finally { Forms.Cursor.Position = pointer; }
                    var expandedWidth = window.Width;
                    query.Text = "fixture_1";
                    Assert(card.IsVisible && window.Width == expandedWidth, "Query refresh collapsed the preview layout");
                    await WaitFor(() => text.Text.StartsWith("fixture 1"));
                    Preview(textRow);
                    Preview(pictureRow);
                    await WaitFor(() => image.Source is not null);
                    Assert(text.Visibility == Visibility.Collapsed, "Stale text replaced the image preview");
                    await (Task)icons.GetType().GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(icons, [pictureRow, System.Threading.CancellationToken.None])!;
                    Assert(!ReferenceEquals(pictureRow.Icon, first.Icon), "Image result still shows fallback artwork");
                    var video = Path.Combine(profile, "preview.mp4");
                    if (File.Exists(video))
                    {
                        var videoRow = new FilesMate.SearchHost.SearchRow(new NameHit("preview.mp4", video, false), first.Icon);
                        Preview(videoRow);
                        await WaitFor(() => image.Source is not null);
                        await (Task)icons.GetType().GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(icons, [videoRow, System.Threading.CancellationToken.None])!;
                        Assert(!ReferenceEquals(videoRow.Icon, first.Icon), "Video result still shows fallback artwork");
                    }
                    window.GetType().GetMethod("Dismiss", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, []);
                    Assert(image.Source is null && text.Text.Length == 0, "Dismiss retained preview content");
                    File.WriteAllText(Path.Combine(profile, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = true, TextScroll = true, BothThemes = true, ImageThumbnail = true, VideoThumbnail = File.Exists(video), StalePreviewCancelled = true, DismissClears = true }));
                    app.Shutdown(0);
                    return;
                }
                if (args.Contains("--apps-only"))
                {
                    var provider = (IGlobalSearchProvider)Palette().GetType().GetField("_provider", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Palette())!;
                    var apps = await provider.SearchAsync("files", default, SearchFilter.Apps);
                    Assert(apps.Hits.Count(hit => hit.Name == "FilesMate" && hit.Application is not null) == 1, "FilesMate launch entry is missing or duplicated");
                    Assert(apps.Hits.All(hit => hit.Application is not null), "Apps filter contains raw file hits");
                    var excel = await provider.SearchAsync("Excel", default, SearchFilter.Apps);
                    Assert(excel.Hits.Count(hit => hit.Name == "Excel") == 1, "Excel entry missing or duplicated");
                    var settings = await provider.SearchAsync("设置", default, SearchFilter.Apps);
                    Assert(settings.Hits.Any(hit => hit.Application?.Id.StartsWith("windows.immersivecontrolpanel", StringComparison.OrdinalIgnoreCase) == true), "Packaged Windows application missing");
                    var catalogType = typeof(FilesMate.SearchHost.Program).Assembly.GetType("FilesMate.SearchHost.WindowsApplicationCatalog")!;
                    var liveCatalog = (IApplicationCatalog)Activator.CreateInstance(catalogType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, [new Func<string>(() => Environment.ProcessPath!)], null)!;
                    var liveProvider = new LauncherSearchProvider(liveCatalog);
                    await liveCatalog.GetAsync(default);
                    var measurements = new System.Collections.Generic.List<object>();
                    foreach (var term in new[] { "files", "excel", "chrome" })
                    {
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        var live = await liveProvider.SearchAsync(term, default);
                        measurements.Add(new { Query = term, Milliseconds = timer.ElapsedMilliseconds, Names = live.Hits.Take(6).Select(hit => hit.Name).ToArray() });
                        Assert(live.Hits.FirstOrDefault()?.Application is not null, "Real indexed query did not prioritize application: " + term);
                        Assert(live.Hits.Count(hit => hit.Name == "FilesMate" && hit.Application is not null) <= 1, "Real query duplicated the FilesMate application");
                    }
                    File.WriteAllText(Path.Combine(profile, "live-query-times.json"), System.Text.Json.JsonSerializer.Serialize(measurements));
                    query.Text = "files";
                    await Task.Delay(700);
                    Assert(results.Items.Count > 0 && ((FilesMate.SearchHost.SearchRow)results.Items[0]).IsApplication, "Application entries did not lead All results");
                    var self = results.Items.Cast<FilesMate.SearchHost.SearchRow>().Single(row => row.Hit.Application?.IsFilesMate == true);
                    Assert(self.DisplayName == "FilesMate" && !self.Location.Contains("\\"), "Application row exposes binary name or build path");
                    var fallback = Palette().GetType().GetField("_icons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Palette())!;
                    var placeholder = fallback.GetType().GetMethod("Fallback", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fallback, [self.Hit]);
                    Assert(self.Icon is System.Windows.Media.Imaging.BitmapImage brand && brand.UriSource.ToString().EndsWith("Assets/FilesMate.png"), "FilesMate branding did not load");
                    var excelHit = excel.Hits.Single(hit => hit.Name == "Excel");
                    var excelFallback = (System.Windows.Media.ImageSource)fallback.GetType().GetMethod("Fallback", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fallback, [excelHit])!;
                    var excelRow = new FilesMate.SearchHost.SearchRow(excelHit, excelFallback);
                    await (Task)fallback.GetType().GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fallback, [excelRow, System.Threading.CancellationToken.None])!;
                    Assert(!ReferenceEquals(excelRow.Icon, excelFallback), "Shell application icon did not load");
                    var origin = Palette().PointToScreen(new Point());
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(Palette());
                    using (var bitmap = new System.Drawing.Bitmap((int)(Palette().ActualWidth * dpi.DpiScaleX), (int)(Palette().ActualHeight * dpi.DpiScaleY)))
                    {
                        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                        graphics.CopyFromScreen((int)origin.X, (int)origin.Y, 0, 0, bitmap.Size);
                        bitmap.Save(Path.Combine(profile, "applications.png"));
                    }
                    results.SelectedItem = self;
                    Palette().GetType().GetMethod("OpenResultMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Palette(), [true]);
                    var appMenu = (ContextMenu)Palette().GetType().GetField("_resultMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Palette())!;
                    Assert(appMenu.Items.OfType<MenuItem>().Any(item => (string?)item.Tag == "Open"), "Application menu cannot launch");
                    Assert(!appMenu.Items.OfType<MenuItem>().Any(item => (string?)item.Tag is "Rename" or "Recycle" or "ShowMore"), "Application entity exposes destructive file commands");
                    appMenu.IsOpen = false;
                    Palette().GetType().GetMethod("InvokeFileCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Palette(), ["Rename"]);
                    Assert(!File.Exists(markerPath), "F2 on application forwarded a file rename");
                    // Exercise the same activation route using a fixture link to
                    // this harmless marker executable, never a user's app/document.
                    var launchLink = Path.Combine(profile, "launch-probe.lnk");
                    FilesMate.Platform.Windows.Shell.ShellShortcut.Create(Environment.ProcessPath!, launchLink);
                    var shellType = typeof(FilesMate.SearchHost.Program).Assembly.GetType("FilesMate.SearchHost.ApplicationShell")!;
                    shellType.GetMethod("Launch", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                        [new ApplicationEntry("probe", "Probe", launchLink)]);
                    for (var attempt = 0; attempt < 30 && !File.Exists(markerPath); attempt++) await Task.Delay(100);
                    Assert(File.Exists(markerPath), "Application launch did not preserve shell shortcut activation");
                    // Discovery is safe to exercise against installed apps. Do not
                    // open/close SystemSettings as a launch fixture: it crashed after
                    // the close probe on 2026-09-14, despite obtaining a window handle.
                    // A visible window alone is not proof of successful activation.
                    File.WriteAllText(Path.Combine(profile, "packaged-launch.txt"), "Not tested: requires an isolated packaged-app fixture.");
                    File.WriteAllText(Path.Combine(profile, "real-apps.json"), System.Text.Json.JsonSerializer.Serialize(apps.Hits.Concat(excel.Hits).Concat(settings.Hits)));
                    File.WriteAllText(Path.Combine(profile, "result.json"), "{\"Passed\":true,\"FriendlyNames\":true,\"FilesMateSingleEntry\":true,\"ExcelDeduplicated\":true,\"PackagedApps\":true,\"ApplicationIcon\":true,\"AppsOnlyFilter\":true,\"ShortcutActivation\":true}");
                    app.Shutdown(0);
                    return;
                }
                if (args.Contains("--file-menu-only"))
                {
                    Palette().AddHandler(System.Windows.Input.Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler((_, e) =>
                        File.AppendAllText(Path.Combine(profile, "keys.txt"), $"{e.Key}/{e.SystemKey} handled={e.Handled} active={Palette().IsActive} focus={System.Windows.Input.Keyboard.FocusedElement}\n")), true);
                    ContextMenu? CurrentMenu() => (ContextMenu?)Palette().GetType().GetField("_resultMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Palette());
                    void CaptureMenu(ContextMenu popup, string name)
                    {
                        var origin = popup.PointToScreen(new Point());
                        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(popup);
                        using var bitmap = new System.Drawing.Bitmap((int)Math.Ceiling(popup.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(popup.ActualHeight * dpi.DpiScaleY));
                        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                        graphics.CopyFromScreen((int)origin.X, (int)origin.Y, 0, 0, bitmap.Size);
                        bitmap.Save(Path.Combine(profile, name + ".png"));
                    }
                    foreach (var theme in new[] { "Dark", "Light" })
                    {
                        Palette().GetType().GetMethod("Dismiss", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Palette(), null);
                        File.WriteAllText(Path.Combine(profile, "appearance.json"), "{\"theme\":\"" + theme + "\"}");
                        Call("Show", false);
                        query.Text = "fixture_";
                        await Task.Delay(450);
                        results.SelectedIndex = 0;
                        query.Focus();
                        InputProbe.Click(query.PointToScreen(new Point(30, query.ActualHeight / 2)), false);
                        await Task.Delay(100);
                        File.AppendAllText(Path.Combine(profile, "keys.txt"), $"window={new WindowInteropHelper(Palette()).Handle} foreground={InputProbe.Foreground()}\n");
                        InputProbe.Key(0x5D);
                        await Task.Delay(180);
                        var popup = CurrentMenu();
                        Assert(popup?.IsOpen == true, $"Apps key with query focus did not open result menu; active={Palette().IsActive}, visible={Palette().IsVisible}, pending={Palette().GetType().GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Palette())}, focus={System.Windows.Input.Keyboard.FocusedElement}");
                        Assert(popup!.PlacementTarget is ListBoxItem, "Keyboard menu did not anchor to selected row");
                        var commands = popup.Items.OfType<MenuItem>().Select(item => item.Tag as string).ToArray();
                        foreach (var required in new[] { "Open", "Reveal", "OpenWith", "CreateShortcut", "AddTags", "AddToShelf", "Compress", "OpenInTerminal", "WhoLocks", "ShowMore" })
                            Assert(commands.Contains(required), "Missing result command: " + required);
                        var buttons = ((StackPanel)((MenuItem)popup.Items[0]).Header).Children.OfType<Button>().ToArray();
                        Assert(buttons.Select(button => (string)button.Tag).SequenceEqual(new[] { "Cut", "Copy", "Rename", "Share", "Recycle", "Properties" }), "File command toolbar differs from manager");
                        CaptureMenu(popup, "file-menu-" + theme);
                        var compress = popup.Items.OfType<MenuItem>().Single(item => (string?)item.Tag == "Compress");
                        compress.IsSubmenuOpen = true;
                        await Task.Delay(120);
                        Assert(compress.Items.Count == 3 && ((MenuItem)compress.Items[0]).IsVisible, "Compression submenu did not render");
                        compress.IsSubmenuOpen = false;
                        InputProbe.Key(0x1B);
                        await Task.Delay(120);
                        Assert(CurrentMenu()?.IsOpen != true && Palette().IsVisible, "Escape did not close only the menu");
                    }
                    ((ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(0)).Focus();
                    results.SelectedItems.Add(results.Items[1]);
                    InputProbe.Key(0x79, shift: true);
                    await Task.Delay(180);
                    Assert(CurrentMenu()?.IsOpen == true && results.SelectedItems.Count == 2, $"Shift+F10 lost the multi-selection or did not open: open={CurrentMenu()?.IsOpen}, count={results.SelectedItems.Count}");
                    Assert(CurrentMenu()!.Items.OfType<MenuItem>().Any(item => (string?)item.Tag == "BatchRename"), "Multi-selection is missing batch rename");
                    InputProbe.Key(0x1B);
                    await Task.Delay(120);
                    ((ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(0)).Focus();
                    InputProbe.Key(0x27);
                    await Task.Delay(120);
                    Assert(CurrentMenu()?.IsOpen == true, "Right arrow on result did not open menu");
                    CurrentMenu()!.IsOpen = false;
                    await Task.Delay(150);
                    query.Focus();
                    results.SelectedItems.Clear();
                    results.SelectedItems.Add(results.Items[0]);
                    results.SelectedItems.Add(results.Items[1]);
                    var mouseRow = (ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(1);
                    InputProbe.Click(mouseRow.PointToScreen(new Point(90, mouseRow.ActualHeight / 2)), true);
                    await Task.Delay(180);
                    Assert(CurrentMenu()?.IsOpen == true && results.SelectedItems.Count == 2, $"Mouse menu lost multi-selection: open={CurrentMenu()?.IsOpen}, count={results.SelectedItems.Count}");
                    CurrentMenu()!.IsOpen = false;
                    File.WriteAllText(Path.Combine(profile, "result.json"), "{\"Passed\":true,\"AppsKey\":true,\"ShiftF10\":true,\"RightArrow\":true,\"MouseMenu\":true,\"EscapeKeepsSearch\":true,\"SharedToolbar\":true,\"MultiSelection\":true,\"Submenu\":true,\"BothThemes\":true}");
                    app.Shutdown(0);
                    return;
                }
                Assert(results.Items.Count == 40, "Initial results not limited to 40");
                results.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                    { RoutedEvent = Control.MouseDoubleClickEvent, Source = results });
                await Task.Delay(100);
                Assert(!File.Exists(markerPath), "Double-clicking list background opened selected file");
                T? Child<T>(DependencyObject parent) where T : DependencyObject
                {
                    for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                    {
                        var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                        if (child is T found) return found;
                        if (Child<T>(child) is { } nested) return nested;
                    }
                    return null;
                }
                var scroll = Child<ScrollViewer>(results)!;
                Assert(Palette().FindName("MoreButton") is null, "Manual load button still exists");
                results.SelectedItems.Add(results.Items[1]);
                var selected = results.SelectedItems.Cast<object>().ToArray();
                for (var page = 0; page < 6; page++)
                {
                    var before = results.Items.Count;
                    scroll.ScrollToEnd();
                    await Task.Delay(350);
                    Assert(results.Items.Count > before, $"Scrolling did not append page {page}; count={results.Items.Count}, visible={Palette().IsVisible}, scroll={scroll.VerticalOffset}/{scroll.ScrollableHeight}, extent={scroll.ExtentHeight}");
                    Assert(selected.All(row => results.SelectedItems.Contains(row)), "Appending lost multi-selection");
                }
                Assert(results.Items.Count == 272, "Paging still has an overall cap");
                Assert(results.Items.Cast<FilesMate.SearchHost.SearchRow>().Select(row => row.Path).Distinct().Count() == 272, "Paging duplicated rows");
                scroll.ScrollToEnd();
                await Task.Delay(150);
                Assert(results.Items.Count == 272, "End-of-results kept loading");
                query.Text = "fixture_1";
                Assert(results.IsEnabled && results.Opacity == 1 && results.Items.Count == 272,
                    "Pending query dimmed or cleared the previous results");
                Palette().GetType().GetMethod("OpenSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Palette(), [false]);
                Assert(Palette().IsVisible && !File.Exists(markerPath), "Pending query opened a stale result");
                await Task.Delay(350);
                Assert(results.Items.Count > 0 && results.Items.Cast<FilesMate.SearchHost.SearchRow>().All(row => row.Name.Contains("fixture_1")),
                    "Updated query did not replace the old results");
                if (args.Contains("--pending-only"))
                {
                    File.WriteAllText(Path.Combine(profile, "result.json"), "{\"Passed\":true,\"PendingResultsKeepAppearance\":true,\"StaleOpenBlocked\":true,\"NewResultsReplaceOld\":true,\"PagingPreservesSelection\":true}");
                    app.Shutdown(0);
                    return;
                }
                query.Text = "fixture";
                var bar = (DockPanel)Palette().FindName("FilterBar");
                var options = bar.Children.OfType<StackPanel>().Single().Children.OfType<RadioButton>();
                options.Single(option => (string)option.Content == "文件夹").IsChecked = true;
                await Task.Delay(350);
                Assert(results.Items.Count == 1 && ((FilesMate.SearchHost.SearchRow)results.Items[0]).Hit.IsDirectory, "Folder filter failed");
                Palette().Close();
                await Task.Delay(600);
                Call("Apply", new GlobalSearchSettings(true, "Ctrl+Alt+Shift+F10", "Files"));
                Click();
                for (var i = 0; i < 30 && !File.Exists(markerPath); i++) await Task.Delay(100);
                Assert(File.Exists(markerPath), "Single click did not open manager");
                Assert(!Palette().IsVisible, "Manager action also opened search");
                Assert(GlobalSearchConfiguration.Load(profile).TrayLeftAction == "Files", "Tray action was not saved");
                var menu = tray.ContextMenuStrip!;
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    File.WriteAllText(Path.Combine(profile, "appearance.json"), "{\"theme\":\"" + theme + "\"}");
                    menu.Show(new System.Drawing.Point(2000, 1100));
                    await Task.Delay(180);
                    Assert(menu.Visible, "Tray menu was dismissed during capture");
                    Assert(theme == "Dark" ? menu.BackColor.R < 80 : menu.BackColor.R > 220, "Tray theme mismatch");
                    File.AppendAllText(Path.Combine(profile, "geometry.txt"), $"{theme}: DPI {menu.DeviceDpi}, size {menu.Size}, first item {menu.Items[0].Size}\n");
                    using var image = new System.Drawing.Bitmap(menu.Width + 20, menu.Height + 20);
                    using var graphics = System.Drawing.Graphics.FromImage(image);
                    graphics.CopyFromScreen(menu.Left - 10, menu.Top - 10, 0, 0, image.Size);
                    image.Save(Path.Combine(profile, "tray-" + theme + ".png"));
                    menu.Close();
                }
                menu.Items[3].PerformClick();
                Assert(Palette().IsVisible && ((FrameworkElement)Palette().FindName("SettingsPanel")).IsVisible, "Tray settings action failed");
                var startup = (System.Windows.Controls.Primitives.ToggleButton)Palette().FindName("StartupBox");
                Assert(startup.IsChecked == true, "Startup did not default on");
                startup.IsChecked = false;
                Assert(!GlobalSearchConfiguration.Load(profile).StartAtLogin, "Startup opt-out did not auto-save");
                var resident = (System.Windows.Controls.Primitives.ToggleButton)Palette().FindName("ResidentBox");
                resident.IsChecked = false;
                Assert(!GlobalSearchConfiguration.Load(profile).Enabled, "Residency did not auto-save");
                Assert(Palette().IsVisible, "Disabling residency closed the settings before user dismissed it");
                resident.IsChecked = true;
                using (var locked = File.Open(GlobalSearchConfiguration.SettingsPath(profile), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    resident.IsChecked = false;
                    Assert(resident.IsChecked == true && GlobalSearchConfiguration.Load(profile).Enabled, "Failed save did not restore the toggle");
                    Assert(((TextBlock)Palette().FindName("SettingsStatus")).Text.Contains("未能保存"), "Failed save gave no feedback");
                }
                var traySearch = (RadioButton)Palette().FindName("TraySearchOption");
                traySearch.IsChecked = true;
                Assert(GlobalSearchConfiguration.Load(profile).TrayLeftAction == "Search", "Tray choice did not auto-save");
                var hotkey = (TextBox)Palette().FindName("HotkeyBox");
                hotkey.Text = "Ctrl+Alt+Shift+F9";
                await Task.Delay(650);
                Assert(GlobalSearchConfiguration.Load(profile).Hotkey == "Ctrl+Alt+Shift+F9", "Shortcut did not auto-save");
                hotkey.Text = "invalid";
                await Task.Delay(650);
                Assert(GlobalSearchConfiguration.Load(profile).Hotkey == "Ctrl+Alt+Shift+F9", "Invalid shortcut overwrote working binding");
                Assert(((TextBlock)Palette().FindName("SettingsStatus")).Text.Length > 0, "Invalid shortcut gave no feedback");
                hotkey.Text = "Ctrl+Alt+Shift+F10";
                await Task.Delay(650);
                Assert(!GlobalSearchConfiguration.Load(profile).StartAtLogin, "Other settings changes re-enabled startup");
                startup.IsChecked = true;
                Assert(GlobalSearchConfiguration.Load(profile).StartAtLogin, "Startup did not auto-save on");
                if (args.Contains("--startup-only"))
                {
                    File.WriteAllText(Path.Combine(profile, "result.json"), "{\"Passed\":true,\"StartupDefaultOn\":true,\"StartupAutoSave\":true,\"OptOutSurvivesOtherChanges\":true}");
                    app.Shutdown(0);
                    return;
                }
                var windowType = Palette().GetType();
                object? WindowCall(string name, params object[] values) => windowType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Palette(), values);
                WindowCall("Ranking_Click", new object(), new RoutedEventArgs());
                var ranks = (System.Collections.IList)((ItemsControl)Palette().FindName("RankingItems")).ItemsSource;
                var firstRank = ranks[0];
                WindowCall("MoveRank", new Button { DataContext = firstRank }, 1);
                Assert(SearchRankingConfiguration.Load(profile)[1] == FilesMate.App.Models.SearchHitKind.Program, "Arrow reorder did not auto-save");
                WindowCall("RankingReset_Click", new object(), new RoutedEventArgs());
                Assert(SearchRankingConfiguration.Load(profile)[0] == FilesMate.App.Models.SearchHitKind.Program, "Reset did not auto-save application priority");
                Palette().UpdateLayout();
                var rankingItems = (ItemsControl)Palette().FindName("RankingItems");
                var firstCard = Child<Border>((DependencyObject)rankingItems.ItemContainerGenerator.ContainerFromIndex(0))!;
                var thirdCard = Child<Border>((DependencyObject)rankingItems.ItemContainerGenerator.ContainerFromIndex(2))!;
                var dragFrom = firstCard.PointToScreen(new Point(100, firstCard.ActualHeight / 2));
                var dragTo = thirdCard.PointToScreen(new Point(100, thirdCard.ActualHeight - 4));
                await Task.Run(() => InputProbe.Drag(dragFrom, dragTo));
                await Task.Delay(180);
                Assert(SearchRankingConfiguration.Load(profile)[2] == FilesMate.App.Models.SearchHitKind.Program, "Dragging rank cards did not save order");
                WindowCall("RankingReset_Click", new object(), new RoutedEventArgs());
                WindowCall("ShowSettings", false, "");
                query.Text = "fixture_";
                await Task.Delay(400);
                results.SelectedItems.Clear();
                results.SelectedItems.Add(results.Items[0]);
                results.SelectedItems.Add(results.Items[1]);
                results.UpdateLayout();
                var sourceRow = (ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(0);
                var sourcePoint = sourceRow.PointToScreen(new Point(100, sourceRow.ActualHeight / 2));
                InputProbe.Click(sourcePoint, true);
                await Task.Delay(180);
                var openMenu = System.Windows.PresentationSource.CurrentSources.Cast<System.Windows.PresentationSource>()
                    .Select(source => source.RootVisual).OfType<DependencyObject>().Select(visual => Child<ContextMenu>(visual)).FirstOrDefault(menu => menu?.IsOpen == true);
                Assert(openMenu is not null, "Right click did not open result menu");
                Assert(results.SelectedItems.Count == 2, "Right click discarded multi-selection");
                var menuOrigin = openMenu!.PointToScreen(new Point(0, 0));
                var menuScale = System.Windows.Media.VisualTreeHelper.GetDpi(openMenu);
                using (var bitmap = new System.Drawing.Bitmap((int)(openMenu.ActualWidth * menuScale.DpiScaleX), (int)(openMenu.ActualHeight * menuScale.DpiScaleY)))
                {
                    using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                    graphics.CopyFromScreen((int)menuOrigin.X, (int)menuOrigin.Y, 0, 0, bitmap.Size);
                    bitmap.Save(Path.Combine(profile, "result-menu.png"));
                }
                openMenu.IsOpen = false;
                await Task.Delay(100);
                var received = Array.Empty<string>();
                var target = new Window { Title = "FilesMate test drop target", Width = 260, Height = 130, Left = 40, Top = 40,
                    Topmost = true, AllowDrop = true, Content = new TextBlock { Text = "FilesMate 拖放测试", Margin = new Thickness(20) } };
                target.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
                target.Drop += (_, e) =>
                {
                    received = (string[])e.Data.GetData(DataFormats.FileDrop);
                    var destination = Path.Combine(profile, "drop-target");
                    Directory.CreateDirectory(destination);
                    foreach (var file in received) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                };
                windowType.GetField("_dragging", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Palette(), true);
                target.Show();
                Call("Show", false);
                windowType.GetField("_dragging", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Palette(), false);
                await Task.Delay(100);
                var targetPoint = target.PointToScreen(new Point(100, 70));
                await Task.Run(() => InputProbe.Drag(sourcePoint, targetPoint));
                await Task.Delay(150);
                Assert(received.Length == 2, "OLE drag did not deliver both selected files");
                Assert(received.All(path => File.Exists(path) && File.ReadAllText(path) == File.ReadAllText(Path.Combine(profile, "drop-target", Path.GetFileName(path)))), "Drag did not copy intact files or removed originals");
                target.Close();
                File.WriteAllText(Path.Combine(profile, "result.json"), "{\"Passed\":true,\"RankDragAutoSave\":true,\"MultiFileOleCopy\":true,\"ResultContextMenu\":true,\"SingleClickSearch\":true,\"SingleClickManager\":true,\"ExplicitShowPreservesQuery\":true,\"TrayLightDark\":true,\"TraySettings\":true,\"ScrollPages272\":true,\"AppendPreservesMultiSelection\":true,\"AutoSave\":true,\"InvalidShortcutRetainsBinding\":true,\"StaleOpenBlocked\":true,\"PendingResultsKeepAppearance\":true,\"BackgroundDoubleClickSafe\":true}");
                app.Shutdown(0);
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(profile, "failure.txt"), error.ToString()); app.Shutdown(1); }
        }));
        return app.Run();
    }
}

internal static class InputProbe
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    internal static bool OwnsRootPoint(Point point)
    {
        GetWindowThreadProcessId(GetAncestor(WindowFromPoint(new System.Drawing.Point((int)point.X, (int)point.Y)), 2), out var pid);
        return pid == Environment.ProcessId;
    }
    internal static bool OwnsPoint(Point point)
    {
        GetWindowThreadProcessId(WindowFromPoint(new System.Drawing.Point((int)point.X, (int)point.Y)), out var pid);
        return pid == Environment.ProcessId;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Keyboard { public ushort Code, Scan; public uint Flags, Time; public IntPtr Extra; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Mouse { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 40)]
    private struct Input { [System.Runtime.InteropServices.FieldOffset(0)] public uint Type; [System.Runtime.InteropServices.FieldOffset(8)] public Mouse Mouse; [System.Runtime.InteropServices.FieldOffset(8)] public Keyboard Keyboard; }
    internal static void Key(ushort code, bool shift = false)
    {
        void Send(ushort key, uint flags)
        {
            if (SendInput(1, [new Input { Type = 1, Keyboard = new Keyboard { Code = key, Flags = flags } }], 40) != 1)
                throw new InvalidOperationException("Keyboard injection failed");
        }
        if (shift) Send(0x10, 0);
        Send(code, 0);
        Send(code, 2);
        if (shift) Send(0x10, 2);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] internal static extern IntPtr Foreground();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    private static void Button(uint flags) => SendInput(1, [new Input { Mouse = new Mouse { Flags = flags } }], 40);
    internal static void Click(Point point, bool right)
    { SetCursorPos((int)point.X, (int)point.Y); Button(right ? 8u : 2u); Button(right ? 16u : 4u); }
    internal static void Drag(Point from, Point to)
    {
        SetCursorPos((int)from.X, (int)from.Y);
        System.Threading.Thread.Sleep(100);
        Button(2);
        try
        {
            for (var i = 1; i <= 30; i++)
            {
                SetCursorPos((int)(from.X + (to.X - from.X) * i / 30), (int)(from.Y + (to.Y - from.Y) * i / 30));
                System.Threading.Thread.Sleep(20);
            }
        }
        finally { Button(4); }
    }
}
