using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class AppTheme
{
    internal static readonly Font TextFont = new Font("Segoe UI", 10F);
    internal static readonly Font HeadingFont = new Font("Segoe UI Semibold", 17F);
    internal static readonly AppTheme[] Designs =
    {
        new AppTheme("Nacht", 0x111521, 0x1B2232, 0xF2F5FC, 0x344158, 0x8DD9FF, 0x223850),
        new AppTheme("Nordlicht", 0x101B20, 0x192A30, 0xEBFAF5, 0x355354, 0x82EAC4, 0x254640),
        new AppTheme("Violett", 0x241B35, 0x332645, 0xFFF4FF, 0x58436D, 0xF4B5FF, 0x513661),
        new AppTheme("Klar", 0xEDF2F9, 0xFFFFFF, 0x1A2A41, 0xAAB9CC, 0x1753A6, 0xD9E8FF)
    };

    internal readonly string Name;
    internal readonly Color Canvas;
    internal readonly Color Surface;
    internal readonly Color Ink;
    internal readonly Color Border;
    internal readonly Color Accent;
    internal readonly Color Selection;

    private AppTheme(string name, int canvas, int surface, int ink, int border, int accent, int selection)
    {
        Name = name;
        Canvas = FromRgb(canvas);
        Surface = FromRgb(surface);
        Ink = FromRgb(ink);
        Border = FromRgb(border);
        Accent = FromRgb(accent);
        Selection = FromRgb(selection);
    }

    private static Color FromRgb(int rgb) { return Color.FromArgb(unchecked((int)0xFF000000) | rgb); }

    internal static AppTheme Find(string name)
    {
        return Array.Find(Designs, theme => theme.Name == name) ?? Designs[0];
    }

    internal void Apply(Control control)
    {
        if (control is Microsoft.Web.WebView2.WinForms.WebView2) return;
        foreach (Control child in control.Controls) Apply(child);
        if (SystemInformation.HighContrast) { control.Invalidate(); return; }
        if (control.Tag is AppTheme) return;
        control.ForeColor = Ink;
        control.BackColor = control is Form || control is TabPage ? Canvas : Surface;
        var button = control as Button;
        if (button != null)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Selection;
            button.Padding = new Padding(7, 3, 7, 3);
        }
        var field = control as TextBoxBase;
        if (field != null) field.BorderStyle = BorderStyle.FixedSingle;
        control.Invalidate();
    }

    internal static TabControl CreateTabs(Func<AppTheme> current)
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, Padding = new Point(17, 8) };
        tabs.DrawItem += (sender, args) =>
        {
            var theme = current();
            var selected = args.Index == tabs.SelectedIndex;
            var highContrast = SystemInformation.HighContrast;
            var background = highContrast ? (selected ? SystemColors.Highlight : SystemColors.Control)
                : (selected ? theme.Selection : theme.Surface);
            var text = highContrast ? (selected ? SystemColors.HighlightText : SystemColors.ControlText)
                : (selected ? theme.Accent : theme.Ink);
            using (var brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
            TextRenderer.DrawText(args.Graphics, tabs.TabPages[args.Index].Text, tabs.Font, args.Bounds, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (selected && !highContrast)
            {
                using (var brush = new SolidBrush(theme.Accent))
                    args.Graphics.FillRectangle(brush, args.Bounds.Left + 2, args.Bounds.Bottom - 3, args.Bounds.Width - 4, 3);
            }
            if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
        };
        return tabs;
    }
}
