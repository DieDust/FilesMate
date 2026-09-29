#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunDetailsTypographySmokeAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated UI-test profile is required.");
            await Wait(() => TabHost.Content is NavigatorPage p && p.IsLoaded && !p.ViewModel.IsLoading, "initial navigator");
            var fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "details-fixture-" + Guid.NewGuid().ToString("N"))).FullName;
            WriteBitmap(Path.Combine(fixture, "Photo-2.bmp"), 2);
            WriteBitmap(Path.Combine(fixture, "Photo-10.bmp"), 10);
            for (var i = 0; i < 40; i++) File.WriteAllText(Path.Combine(fixture, $"Project-research-materials-{i:D2}.txt"), "Preview");
            AddNavigatorTab(fixture);
            await Wait(() => TabHost.Content is NavigatorPage p && p.ViewModel.AddressText == fixture && !p.ViewModel.IsLoading && p.ViewModel.ItemCount == 42, "fixture");
            var navigator = (NavigatorPage)TabHost.Content;
            await Wait(() => ((System.Collections.IDictionary)Field(navigator, "_restoringViews")!).Count == 0, "view restoration");
            var surface = (FileDetailsSurface)navigator.FindName("FileSurface");
            var width = new DetailsColumn(DetailsColumnId.ShellProperty, 136, true, "System.Image.HorizontalSize", "宽度");
            var height = new DetailsColumn(DetailsColumnId.ShellProperty, 136, true, "System.Image.VerticalSize", "高度");
            var columns = DetailsColumn.Defaults().Select(c => c with { Visible = c.Id is DetailsColumnId.Name or DetailsColumnId.Modified or DetailsColumnId.Size }).Concat([width, height]).ToArray();
            surface.SetLayout(FileLayoutKind.Details); surface.SetColumns(columns);
            await App.AppearanceViewModel!.SetFileTypographyAsync(null, 13, 12);
            var expected = await ShellProperties.ReadAsync(Path.Combine(fixture, "Photo-10.bmp"), [width.PropertyName!, height.PropertyName!]);
            report["PropertyValues"] = expected;
            Require(expected[width.PropertyName!].Number == 10, "Fixture metadata is not readable.");
            await Wait(() => Rows().Count() > 8 && Rows().FirstOrDefault(r => r.Entry.Name == "Photo-10.bmp")?.ColumnText(width) == expected[width.PropertyName!].Text, "property cells");
            Require(Rows().Single(r => r.Entry.Name == "Photo-2.bmp").ColumnText(height) == expected[height.PropertyName!].Text, "Second shell column is missing.");
            report["WindowsColumns"] = (await ShellProperties.GetColumnsAsync()).Count;
            report["InstalledFonts"] = (await InstalledFonts.GetAsync()).Length;
            report["IndependentPropertyCells"] = true;
            foreach (var ascending in new[] { true, false })
            {
                var sort = EntrySort.Name with { Column = EntrySortColumn.ShellProperty, PropertyName = width.PropertyName, Ascending = ascending };
                navigator.ViewModel.RestoreSort(sort);
                await Wait(() => navigator.ViewModel.ViewIndex?.Sort == sort, "property sorting");
                var names = navigator.ViewModel.ViewIndex!.Select(i => navigator.ViewModel.Store![i].Name).ToArray();
                Require(names[0] == (ascending ? "Photo-2.bmp" : "Photo-10.bmp"), "Numeric property sorting failed.");
            }
            report["TypedSortingBothDirections"] = true;

            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                var pending = DetailsColumnPicker.ShowAsync(surface, surface.GetColumns());
                await Wait(() => Popups().OfType<DetailsColumnPicker>().Any(), "column picker");
                var picker = Popups().OfType<DetailsColumnPicker>().First();
                var search = (TextBox)Field(picker, "_search")!;
                search.Text = "System.Author";
                await Task.Delay(120);
                var list = (ListView)Field(picker, "_available")!;
                Require(list.Items.Count == 1, "Search must match property canonical names.");
                var choice = (DetailsColumnChoice)list.Items[0];
                var check = PolishDescendants(list).OfType<CheckBox>().First();
                Require((string)check.Content == choice.Title, "Runtime checkbox text binding failed.");
                ((IToggleProvider)new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle)).Toggle();
                await Task.Delay(100);
                Require(choice.IsChecked && picker.Result.Any(c => c.PropertyName == "System.Author" && c.Visible), "Checkbox binding did not update chosen columns.");
                ((NumberBox)Field(picker, "_width")!).Value = 232;
                Require(picker.Result.Single(c => c.PropertyName == "System.Author").Width == 232, "Column width editor failed.");
                var orderList = (ListView)Field(picker, "_order")!;
                Require(orderList.Items.Count == picker.Result.Count(c => c.Visible), "Visible column list did not receive collection changes.");
                orderList.ScrollIntoView(choice);
                await Task.Delay(450);
                Require(PolishDescendants(orderList).OfType<TextBlock>().Any(t => t.Text == choice.Title && t.ActualHeight > 0), "Selected column is not visible in the ordered list.");
                await Capture(Popups().OfType<ContentDialog>().First(), $"choose-details-{theme}.png");
                Popups().OfType<ContentDialog>().First().Hide();
                Require(await pending is null && !surface.GetColumns().Any(c => c.PropertyName == "System.Author"), "Cancel changed live columns.");
            }
            report["PickerSearchToggleWidthCancel"] = true;

            var applyTask = (Task)surface.GetType().GetMethod("ChooseColumnsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(surface, null)!;
            await Wait(() => Popups().OfType<DetailsColumnPicker>().Any(), "apply picker");
            var applyPicker = Popups().OfType<DetailsColumnPicker>().First();
            var categories = (ComboBox)Field(applyPicker, "_category")!;
            categories.SelectedItem = categories.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == "Photo");
            var available = (ListView)Field(applyPicker, "_available")!;
            Require(available.Items.Count > 5 && available.Items.Cast<DetailsColumnChoice>().All(c => c.PropertyName.StartsWith("System.Photo.") || c.PropertyName.StartsWith("System.Image.")), "Photo category contains unrelated fields.");
            categories.SelectedIndex = 0;
            ((TextBox)Field(applyPicker, "_search")!).Text = "System.Author";
            await Wait(() => available.Items.Count == 1 && ((DetailsColumnChoice)available.Items[0]).PropertyName == "System.Author", "filter before apply");
            ((DetailsColumnChoice)available.Items[0]).IsChecked = true;
            var primary = PolishDescendants(Popups().OfType<ContentDialog>().First()).OfType<Button>().Single(b => b.Name == "PrimaryButton");
            ((IInvokeProvider)new ButtonAutomationPeer(primary).GetPattern(PatternInterface.Invoke)).Invoke();
            await applyTask;
            Require(surface.GetColumns().Any(c => c.Visible && c.PropertyName == "System.Author"), "Applying columns did not change the live surface.");
            report["PickerCategoriesAndApply"] = true;

            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Light);
            await App.AppearanceViewModel.SetFileTypographyAsync("Microsoft YaHei UI", 22, 16);
            foreach (var layout in new[] { FileLayoutKind.Details, FileLayoutKind.List, FileLayoutKind.Grid })
            {
                surface.SetLayout(layout);
                await Task.Delay(260);
                var label = layout == FileLayoutKind.Grid
                    ? (TextBlock)PolishDescendants(surface).OfType<FileTile>().First(t => t.EntryId >= 0).FindName("NameText")
                    : (TextBlock)Rows().First().FindName("NameText");
                Require(label.FontSize == 22 && label.FontFamily.Source == "Microsoft YaHei UI", "Typography missing in " + layout);
                if (layout != FileLayoutKind.Grid)
                {
                    var row = Rows().First();
                    Require(Math.Abs(row.ActualHeight - App.AppearanceViewModel.Current.FileRowHeightForScale(surface.XamlRoot.RasterizationScale)) < .01 && row.ActualHeight > 28, "Row height did not grow.");
                }
                else
                {
                    var tile = PolishDescendants(surface).OfType<FileTile>().First(t => t.EntryId >= 0);
                    Require(tile.ActualHeight == surface.GridPreset.WithFontSize(22).ItemHeight && label.MaxHeight >= 70, "Grid text region did not grow.");
                }
                await Capture(Content, $"font-large-{layout}.png");
            }
            surface.SetLayout(FileLayoutKind.Details);
            await Wait(() => Rows().Count() > 5, "details realization after grid");
            surface.FitColumn(null);
            Require(surface.GetColumns().Single(c => c.Id == DetailsColumnId.Name).Width > 240, "Autofit ignored selected font.");
            await App.AppearanceViewModel.SetFileTypographyAsync(null, 13, 12);
            await Task.Delay(180);
            Require(Rows().First().ActualHeight == 28 && ((TextBlock)Rows().First().FindName("NameText")).FontSize == 13, "Reset defaults failed.");
            report["TypographyThreeViewsAndReset"] = true;
            report["FontAwareAutoFit"] = true;

            var toolbar = (AdaptiveCommandToolbar)navigator.FindName("Commands");
            var button = (Button)toolbar.FindName("SortButton");
            var menu = (Flyout)button.Flyout;
            menu.ShowAt(toolbar);
            await Task.Delay(100);
            var ascendingItem = PolishDescendants(menu.Content).OfType<Button>()
                .Single(b => b.Content as string == Localization.StringTable.Get("Sort_Ascending"));
            ((IInvokeProvider)new ButtonAutomationPeer(ascendingItem).GetPattern(PatternInterface.Invoke)).Invoke();
            await Wait(() => navigator.ViewModel.Sort.Ascending, "explicit ascending");
            Require(menu.IsOpen, "Changing direction closed the sort panel.");
            menu.Hide();
            report["ExplicitDirectionMenu"] = true;

            var viewMenu = (MenuFlyout)typeof(Controls.Menus.FileContextFlyout).GetMethod("CreateLayoutFlyout", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [new Microsoft.UI.Xaml.Controls.Flyout(), (Action<FileLayoutKind>)surface.SetLayout, (Action<GridSizePreset>)surface.SetGridSize])!;
            Require(viewMenu.Items.OfType<MenuFlyoutItem>().Count() == 9, "View menu lacks the seven icon sizes and two list modes.");
            viewMenu.ShowAt(surface);
            await Task.Delay(120);
            var largest = viewMenu.Items.OfType<MenuFlyoutItem>().Last();
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(largest).GetPattern(PatternInterface.Invoke)).Invoke();
            await Wait(() => surface.LayoutKind == FileLayoutKind.Grid && surface.GridPreset == GridSizePreset.Maximum, "context icon size");
            viewMenu.Hide(); report["ContextViewPresets"] = true;
            OpenSettings("appearance");
            await Wait(() => PolishDescendants(Content).OfType<AppearancePage>().Any(), "appearance page");
            var appearance = PolishDescendants(Content).OfType<AppearancePage>().First();
            var fontBox = (ComboBox)appearance.FindName("FileFontBox");
            await Wait(() => fontBox.Items.Count > 3, "font choices");
            Require(fontBox.Items.Cast<string>().Contains("Microsoft YaHei UI"), "Installed font chooser missing known font.");
            ((FrameworkElement)appearance.FindName("FileFontCard")).StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
            await Task.Delay(450); await Capture(Content, "file-font-settings-Light.png");
            report["SettingsFontChooser"] = true;
            report["Passed"] = true;
            IEnumerable<FileRow> Rows() => PolishDescendants(surface).OfType<FileRow>().Where(r => r.EntryId >= 0 && r.IsLoaded);
            IEnumerable<DependencyObject> Popups() => VisualTreeHelper.GetOpenPopupsForXamlRoot(surface.XamlRoot).SelectMany(p => PolishDescendants(p.Child));
        }
        catch (Exception error)
        {
            report["Passed"] = false; report["Error"] = error.ToString();
            if (TabHost.Content is NavigatorPage page)
            {
                var surface = (FileDetailsSurface)page.FindName("FileSurface");
                report["CurrentColumns"] = surface.GetColumns();
                report["Rows"] = PolishDescendants(surface).OfType<FileRow>().Where(r => r.EntryId >= 0).Take(6)
                    .Select(r => new { r.Entry.Name, Cells = surface.GetColumns().Where(c => c.Visible).Select(c => new { c.Key, Text = r.ColumnText(c) }).ToArray() }).ToArray();
            }
            try { await Capture(Content, "details-failure.png"); } catch { }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "details-typography-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
        static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
        static async Task Wait(Func<bool> condition, string step)
        { for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(50); } throw new TimeoutException(step); }
        static void WriteBitmap(string path, int width)
        {
            var stride = (width * 3 + 3) / 4 * 4;
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write((ushort)0x4D42); writer.Write(54 + stride * 2); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(width); writer.Write(2); writer.Write((ushort)1); writer.Write((ushort)24);
            writer.Write(0); writer.Write(stride * 2); writer.Write(3780); writer.Write(3780); writer.Write(0); writer.Write(0);
            writer.Write(Enumerable.Repeat((byte)140, stride * 2).ToArray());
        }
    }
}
#endif
