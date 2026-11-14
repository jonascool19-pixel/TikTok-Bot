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
        control.BackColor = control is Form || control is AppTabs || (control is Panel && Equals(control.Tag, "canvas")) ? Canvas : Surface;
        var button = control as Button;
        if (button != null)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Selection;
            button.Padding = new Padding(7, 3, 7, 3);
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Selection;
            button.SizeChanged -= ResizeButton;
            button.SizeChanged += ResizeButton;
            Round(button, 9);
        }
        var lists = control as ListBox;
        if (lists != null) { lists.BorderStyle = BorderStyle.None; lists.BackColor = Canvas; }
        var listView = control as ListView;
        if (listView != null) listView.BorderStyle = BorderStyle.None;
        var field = control as TextBoxBase;
        if (field != null) field.BorderStyle = BorderStyle.FixedSingle;
        control.Invalidate();
        var tabs = control as AppTabs;
        if (tabs != null) tabs.ApplyPalette(this);
    }

    private static void ResizeButton(object sender, EventArgs args) { Round((Button)sender, 9); }

    internal static void Round(Button button, int radius)
    {
        if (button.Width < radius * 2 || button.Height < radius * 2) return;
        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            var size = radius * 2;
            var right = button.Width - size;
            var bottom = button.Height - size;
            path.AddArc(0, 0, size, size, 180, 90);
            path.AddArc(right, 0, size, size, 270, 90);
            path.AddArc(right, bottom, size, size, 0, 90);
            path.AddArc(0, bottom, size, size, 90, 90);
            path.CloseFigure();
            var old = button.Region;
            button.Region = new Region(path);
            if (old != null) old.Dispose();
        }
    }
}

internal sealed class AppTabs : UserControl
{
    private readonly FlowLayoutPanel navigation = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 4) };
    private readonly Panel content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 8), Tag = "canvas" };
    private readonly System.Collections.Generic.Dictionary<TabPage, Button> buttons = new System.Collections.Generic.Dictionary<TabPage, Button>();
    private readonly System.Collections.Generic.Dictionary<TabPage, Panel> pages = new System.Collections.Generic.Dictionary<TabPage, Panel>();
    private TabPage selected;

    internal AppTabs()
    {
        Dock = DockStyle.Fill;
        Controls.AddRange(new Control[] { content, navigation });
    }

    internal void AddPages(params TabPage[] entries)
    {
        foreach (var page in entries)
        {
            var token = page;
            var panel = new Panel { Dock = DockStyle.Fill, Padding = page.Padding, Visible = false };
            while (page.Controls.Count > 0) panel.Controls.Add(page.Controls[0]);
            content.Controls.Add(panel);
            pages.Add(page, panel);
            var button = new Button { Text = page.Text, AutoSize = true, MinimumSize = new Size(0, 36), Tag = "navigation" };
            button.Click += delegate { SelectedTab = token; };
            buttons.Add(page, button);
            navigation.Controls.Add(button);
        }
        if (selected == null && entries.Length > 0) SelectedTab = entries[0];
    }

    internal TabPage SelectedTab
    {
        get { return selected; }
        set
        {
            if (value == selected || !pages.ContainsKey(value)) return;
            if (selected != null) pages[selected].Hide();
            selected = value;
            pages[value].Show();
            pages[value].BringToFront();
            UpdateSelection();
        }
    }

    private AppTheme palette = AppTheme.Designs[0];

    internal void ApplyPalette(AppTheme theme)
    {
        palette = theme;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        foreach (var entry in buttons)
        {
            var active = entry.Key == selected;
            var button = entry.Value;
            button.AccessibleDescription = active ? "Aktiver Bereich" : "";
            if (SystemInformation.HighContrast)
            {
                button.BackColor = active ? SystemColors.Highlight : SystemColors.Control;
                button.ForeColor = active ? SystemColors.HighlightText : SystemColors.ControlText;
            }
            else
            {
                button.BackColor = active ? palette.Accent : palette.Canvas;
                button.ForeColor = active ? palette.Canvas : palette.Ink;
                button.FlatAppearance.MouseOverBackColor = active ? palette.Accent : palette.Selection;
            }
        }
    }
}

internal sealed class ThemeComboBox : ComboBox
{
    internal ThemeComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
    }

    protected override void OnDrawItem(DrawItemEventArgs args)
    {
        var selected = (args.State & DrawItemState.Selected) != 0 && (args.State & DrawItemState.ComboBoxEdit) == 0;
        using (var brush = new SolidBrush(selected ? SystemColors.Highlight : BackColor))
            args.Graphics.FillRectangle(brush, args.Bounds);
        if (args.Index >= 0)
            TextRenderer.DrawText(args.Graphics, GetItemText(Items[args.Index]), Font, args.Bounds,
                selected ? SystemColors.HighlightText : ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != 0x000F || SystemInformation.HighContrast || Width < 30 || Height < 10) return;
        using (var graphics = Graphics.FromHwnd(Handle))
        using (var fill = new SolidBrush(BackColor))
        using (var pen = new Pen(ForeColor))
        {
            graphics.FillRectangle(fill, ClientRectangle);
            var text = SelectedIndex < 0 ? "" : GetItemText(SelectedItem);
            TextRenderer.DrawText(graphics, text, Font, new Rectangle(7, 0, Width - 32, Height),
                Enabled ? ForeColor : SystemColors.GrayText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            var x = Width - 15;
            var y = Height / 2;
            graphics.DrawLines(pen, new[] { new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2) });
            if (Focused) ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
        }
    }
}
