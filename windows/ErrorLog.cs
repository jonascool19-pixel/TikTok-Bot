using System;
using System.IO;
using System.Windows.Forms;

internal static class ErrorLog
{
    private static readonly object Sync = new object();
    private static string path;
    private static string lastEntry;
    private static DateTime lastTime;
    private static bool writeFailed;

    internal static void Configure(string directory)
    {
        path = Path.Combine(directory, "connection.log");
    }

    internal static void Write(string area, string message, Exception error = null)
    {
        lock (Sync)
        {
            if (path == null) return;
            var entry = area + " | " + message + (error == null ? "" : " | " + error.GetType().Name + " (0x" + error.HResult.ToString("X8") + ")");
            var now = DateTime.UtcNow;
            if (entry == lastEntry && now - lastTime < TimeSpan.FromSeconds(30)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path) && new FileInfo(path).Length >= 256 * 1024)
                {
                    File.Copy(path, path + ".1", true);
                    File.Delete(path);
                }
                File.AppendAllText(path, now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") + " | " + entry + Environment.NewLine);
                lastEntry = entry;
                lastTime = now;
                writeFailed = false;
            }
            catch (IOException) { writeFailed = true; }
            catch (UnauthorizedAccessException) { writeFailed = true; }
        }
    }

    internal static string Read()
    {
        lock (Sync)
        {
            try
            {
                var previous = File.Exists(path + ".1") ? File.ReadAllText(path + ".1") : "";
                var current = File.Exists(path) ? File.ReadAllText(path) : "";
                var entries = previous + current;
                if (entries.Length == 0) entries = "Keine Fehler protokolliert.";
                return writeFailed ? "Das Protokoll konnte zuletzt nicht gespeichert werden." + Environment.NewLine + entries : entries;
            }
            catch (IOException) { return "Das Fehlerprotokoll konnte nicht gelesen werden."; }
            catch (UnauthorizedAccessException) { return "Der Zugriff auf das Fehlerprotokoll fehlt."; }
        }
    }
}

internal sealed class ErrorLogView : UserControl
{
    internal ErrorLogView()
    {
        var entries = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, MaxLength = 0, AccessibleName = "Fehlerprotokoll" };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var refresh = new Button { Text = "Aktualisieren", AutoSize = true };
        var copy = new Button { Text = "Protokoll kopieren", AutoSize = true };
        Action load = delegate
        {
            var text = ErrorLog.Read();
            if (entries.Text == text) return;
            entries.Text = text;
            entries.SelectionStart = entries.TextLength;
            entries.ScrollToCaret();
        };
        refresh.Click += delegate { load(); };
        copy.Click += delegate
        {
            try { Clipboard.SetText(entries.Text); }
            catch (System.Runtime.InteropServices.ExternalException error)
            {
                ErrorLog.Write("Fehlerlog", "Zwischenablage vorübergehend nicht verfügbar.", error);
                load();
            }
        };
        var timer = new Timer { Interval = 3000 };
        timer.Tick += delegate { load(); };
        VisibleChanged += delegate { timer.Enabled = Visible; if (Visible) load(); };
        Disposed += delegate { timer.Dispose(); };
        tools.Controls.AddRange(new Control[] { refresh, copy });
        Controls.AddRange(new Control[] { entries, tools });
        load();
    }
}
