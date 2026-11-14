using System;
using System.Drawing;
using System.Windows.Forms;

internal static class AppTheme
{
    private static readonly Color Surface = Color.FromArgb(247, 249, 253);
    private static readonly Color Ink = Color.FromArgb(26, 40, 62);
    private static readonly Color Border = Color.FromArgb(209, 220, 234);
    internal static readonly Font TextFont = new Font("Segoe UI", 10F);

    internal static void Apply(Control control)
    {
        if (control is Microsoft.Web.WebView2.WinForms.WebView2) return;
        foreach (Control child in control.Controls) Apply(child);
        if (SystemInformation.HighContrast) return;
        control.ForeColor = Ink;
        control.BackColor = Surface;
        var button = control as Button;
        if (button != null)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.White;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(230, 240, 255);
            button.Padding = new Padding(7, 3, 7, 3);
        }
        if (control is TextBoxBase || control is ListBox || control is ListView || control is NumericUpDown || control is ComboBox)
            control.BackColor = Color.White;
    }
}
