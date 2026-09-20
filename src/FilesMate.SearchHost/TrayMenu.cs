using Loc = FilesMate.App.Localization.StringTable;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using ResourceDictionary = System.Windows.ResourceDictionary;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Forms = System.Windows.Forms;
using Color = System.Drawing.Color;

namespace FilesMate.SearchHost;

/// <summary>The native tray interaction with FilesMate's shared theme colors.</summary>
internal sealed class TrayMenu : Forms.ContextMenuStrip
{
    private readonly string _profile;
    private readonly MenuRenderer _renderer = new();

    internal TrayMenu(string profile, Action search, Action settings, Action files, Action exit)
    {
        _profile = profile;
        Renderer = _renderer;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        AutoSize = false;
        DropShadowEnabled = true;
        Padding = new Forms.Padding(6);
        Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10);
        Add(Loc.Get("GlobalSearch"), "\uE721", search);
        Add(Loc.Get("OpenFilesMate"), "\uE8B7", files);
        Items.Add(new MenuSeparator());
        Add(Loc.Get("Search_Settings"), "\uE713", settings);
        Add(Loc.Get("ExitBackgroundSearch"), "\uE7E8", exit);
        Opening += (_, _) => RefreshTheme();
        SizeChanged += (_, _) => RoundOutline();
    }

    private void Add(string text, string glyph, Action action)
    {
        var item = new MenuItem(text) { Tag = glyph, AutoSize = false };
        item.Click += (_, _) => action();
        Items.Add(item);
    }

    private void RefreshTheme()
    {
        var resources = new ResourceDictionary();
        PaletteAppearance.Apply(resources, PaletteAppearance.Load(_profile));
        Color Read(string key)
        {
            var c = ((SolidColorBrush)resources[key]).Color;
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }
        _renderer.Surface = Read("Surface");
        _renderer.Ink = Read("Ink");
        _renderer.SelectedInk = Read("SelectedInk");
        _renderer.Line = Read("Line");
        _renderer.Hover = Read("Selected");
        BackColor = _renderer.Surface;
        ForeColor = _renderer.Ink;
        var scale = DeviceDpi / 96f;
        var inset = (int)Math.Round(6 * scale);
        var textWidth = Items.OfType<MenuItem>().Select(item => Forms.TextRenderer.MeasureText(item.Text, Font).Width).DefaultIfEmpty(0).Max();
        Size = new Size(Math.Max((int)Math.Round(220 * scale), textWidth + (int)Math.Round(64 * scale)) + inset * 2,
            Items.OfType<MenuItem>().Count() * (int)Math.Round(38 * scale) +
            Items.OfType<MenuSeparator>().Count() * (int)Math.Round(14 * scale) + inset * 2);
        PerformLayout();
        RoundOutline();
    }

    protected override void OnLayout(Forms.LayoutEventArgs e)
    {
        base.OnLayout(e);
        // ToolStripDropDownMenu resets padding and places menu items at X=0.
        // Position the actual hit bounds, text and highlight together inside
        // the rounded surface instead of compensating in the renderer.
        var scale = DeviceDpi / 96f;
        var inset = (int)Math.Round(6 * scale);
        var y = inset;
        foreach (Forms.ToolStripItem item in Items)
        {
            var height = (int)Math.Round((item is MenuSeparator ? 14 : 38) * scale);
            var bounds = new Rectangle(inset, y, Math.Max(1, ClientSize.Width - inset * 2), height);
            if (item is MenuItem menuItem) menuItem.Place(bounds);
            else if (item is MenuSeparator separator) separator.Place(bounds);
            y += height;
        }
    }

    private sealed class MenuItem(string text) : Forms.ToolStripMenuItem(text)
    {
        internal void Place(Rectangle bounds)
        {
            // ToolStripMenuItem.SetBounds subtracts the owner's text margin.
            bounds.X += Owner?.Padding.Left ?? 0;
            SetBounds(bounds);
        }
    }

    private sealed class MenuSeparator : Forms.ToolStripSeparator
    {
        internal void Place(Rectangle bounds) => SetBounds(bounds);
    }

    private void RoundOutline()
    {
        if (Width < 1 || Height < 1) return;
        using var path = MenuRenderer.Round(new RectangleF(0, 0, Width, Height), 12 * DeviceDpi / 96f);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Font.Dispose();
        base.Dispose(disposing);
    }

    private sealed class MenuRenderer : Forms.ToolStripRenderer
    {
        internal Color Surface, Ink, SelectedInk, Line, Hover;
        internal static GraphicsPath Round(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        { using var fill = new SolidBrush(Surface); e.Graphics.FillRectangle(fill, e.AffectedBounds); }
        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Round(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 12 * e.ToolStrip.DeviceDpi / 96f);
            using var pen = new Pen(Line);
            e.Graphics.DrawPath(pen, path);
        }
        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Round(new RectangleF(0, 0, e.Item.Width, e.Item.Height), 7 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f);
            using var fill = new SolidBrush(Hover);
            e.Graphics.FillPath(fill, path);
        }
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            var flags = Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.NoPrefix;
            var ink = e.Item.Selected ? SelectedInk : Ink;
            using var glyphFont = new Font("Segoe Fluent Icons", 11);
            Forms.TextRenderer.DrawText(e.Graphics, e.Item.Tag as string, glyphFont,
                new Rectangle((int)(12 * scale), 0, (int)(24 * scale), e.Item.Height), ink, flags);
            Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont,
                new Rectangle((int)(44 * scale), 0, e.Item.Width - (int)(50 * scale), e.Item.Height), ink, flags);
        }
        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            var inset = (int)Math.Round(6 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f);
            using var pen = new Pen(Line);
            e.Graphics.DrawLine(pen, inset, e.Item.Height / 2, e.Item.Width - inset, e.Item.Height / 2);
        }
    }
}
