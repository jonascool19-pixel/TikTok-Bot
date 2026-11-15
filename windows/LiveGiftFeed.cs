using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class LiveGiftFeed : IDisposable
{
    private readonly Control owner;
    private readonly Action<string, string, int> onGift;
    private readonly Timer retry = new Timer { Interval = 30000 };
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 2048 };
    private Process worker;
    private string target;
    private bool disposed;
    internal event Action<int?> AudienceChanged;

    internal LiveGiftFeed(Control owner, Action<string, string, int> onGift)
    {
        this.owner = owner;
        this.onGift = onGift;
        retry.Tick += delegate { if (worker == null) Start(); };
    }

    internal void Watch(string address)
    {
        var match = Regex.Match(address ?? "", @"\Ahttps://www\.tiktok\.com/@([a-zA-Z0-9_.]{1,64})/live\z");
        var next = match.Success ? match.Groups[1].Value : null;
        if (string.Equals(target, next, StringComparison.OrdinalIgnoreCase)) return;
        Stop();
        target = next;
        retry.Enabled = target != null;
        if (target != null) Start();
    }

    private void Start()
    {
        if (disposed || target == null) return;
        var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gifts");
        var process = new Process();
        process.StartInfo = new ProcessStartInfo(Path.Combine(folder, "node.exe"), "receive.mjs " + target)
        {
            WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        process.StartInfo.EnvironmentVariables.Remove("NODE_OPTIONS");
        process.StartInfo.EnvironmentVariables.Remove("NODE_PATH");
        process.StartInfo.EnvironmentVariables.Remove("SIGN_API_KEY");
        process.OutputDataReceived += (sender, args) =>
        {
            if (!owner.IsHandleCreated || owner.IsDisposed) return;
            try
            {
                owner.BeginInvoke((Action)delegate
                {
                    if (disposed || worker != process) return;
                    if (args.Data == null) { Stop(); return; }
                    Receive(args.Data);
                });
            }
            catch (InvalidOperationException) { }
        };
        process.ErrorDataReceived += delegate { };
        try
        {
            process.Start();
            worker = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception error)
        {
            if (worker == process) Stop();
            else process.Dispose();
            ErrorLog.Write("Geschenke", "Geschenkempfänger konnte nicht gestartet werden.", error);
        }
    }

    private void Receive(string line)
    {
        if (!line.StartsWith("{", StringComparison.Ordinal) || line.Length > 2048) return;
        GiftMessage message;
        try { message = json.Deserialize<GiftMessage>(line); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        if (message == null) return;
        switch (message.kind)
        {
            case "connected": ErrorLog.Write("Geschenke", "TikTok-LIVE-Geschenkempfang verbunden."); break;
            case "offline": break;
            case "disconnected": ErrorLog.Write("Geschenke", "TikTok-LIVE-Geschenkempfang getrennt."); break;
            case "catalog-error": ErrorLog.Write("Geschenke", "Geschenkkatalog konnte nicht geladen werden."); break;
            case "timeout":
            case "error": ErrorLog.Write("Geschenke", "TikTok-LIVE-Ereignisverbindung fehlgeschlagen. Verbindung wird erneut versucht."); break;
            case "audience":
                if (message.count >= 0 && AudienceChanged != null) AudienceChanged(message.count);
                break;
            case "gift":
                if (!string.IsNullOrWhiteSpace(message.name) && message.name.Length <= 80 &&
                    !string.IsNullOrWhiteSpace(message.title) && message.title.Length <= 80)
                {
                    ErrorLog.Write("Geschenke", "TikTok-LIVE-Geschenk empfangen.");
                    onGift(message.name, message.title, Math.Max(1, Math.Min(9999, message.count)));
                }
                break;
        }
    }

    private void Stop()
    {
        var process = worker;
        worker = null;
        if (process == null) return;
        if (AudienceChanged != null) AudienceChanged(null);
        try { process.StandardInput.Close(); }
        catch (InvalidOperationException) { }
        catch (IOException) { }
        finally { process.Dispose(); }
    }

    public void Dispose()
    {
        disposed = true;
        retry.Dispose();
        Stop();
    }

    private sealed class GiftMessage
    {
        public string kind { get; set; }
        public string name { get; set; }
        public string title { get; set; }
        public int count { get; set; }
    }
}
