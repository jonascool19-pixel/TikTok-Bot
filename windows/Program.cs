using System;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var window = new BotWindow(args.Length == 1 && args[0] == "--smoke-test"))
        {
            Application.Run(window);
            return window.ExitCode;
        }
    }
}

internal sealed class BotWindow : Form
{
    private readonly WebView2 browser = new WebView2 { Dock = DockStyle.Fill };
    private readonly LiveComments comments = new LiveComments { Dock = DockStyle.Fill };
    private readonly TextBox room = new TextBox { Width = 275, AccessibleName = "@Name oder LIVE-Link" };
    private readonly Button open = new Button { Text = "LIVE öffnen", AutoSize = true, Enabled = false };
    private readonly Button login = new Button { Text = "Bot-Konto anmelden", AutoSize = true, Enabled = false };
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 32, Text = "TikTok-Ansicht wird gestartet …" };
    private readonly bool smokeTest;
    private ChatReplies replyDialog;
    private readonly string data;
    internal int ExitCode { get; private set; }

    internal BotWindow(bool smokeTest)
    {
        this.smokeTest = smokeTest;
        ExitCode = smokeTest ? 1 : 0;
        data = smokeTest ? Path.Combine(Path.GetTempPath(), "TikTokBot-Smoke-" + Guid.NewGuid()) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TikTok-Bot", "WindowsPrototype");
        browser.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = Path.Combine(data, "BotProfile") };
        Text = "TikTok-Bot";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 820);
        MinimumSize = new Size(790, 430);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        toolbar.Controls.Add(new Label { Text = "@Name oder LIVE-Link", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
        var replies = new Button { Text = "Chatantworten testen", AutoSize = true };
        replies.Click += delegate
        {
            if (replyDialog != null)
            {
                replyDialog.Activate();
                return;
            }
            var dialog = new ChatReplies();
            replyDialog = dialog;
            comments.CommentReceived += dialog.ConsiderLiveComment;
            dialog.FormClosed += delegate
            {
                comments.CommentReceived -= dialog.ConsiderLiveComment;
                replyDialog = null;
            };
            dialog.Show(this);
        };
        toolbar.Controls.AddRange(new Control[] { room, open, login, replies });
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var liveTab = new TabPage("TikTok LIVE");
        liveTab.Controls.Add(browser);
        var chatTab = new TabPage("Kommentare und Stimme");
        chatTab.Controls.Add(comments);
        var musicTab = new TabPage("Musik");
        musicTab.Controls.Add(new MusicQueue { Dock = DockStyle.Fill });
        tabs.TabPages.AddRange(new[] { liveTab, chatTab, musicTab });
        Controls.AddRange(new Control[] { tabs, status, toolbar });
        AcceptButton = open;
        open.Click += delegate
        {
            var target = LiveAddress(room.Text);
            if (target == null)
            {
                status.Text = "Gib einen @Namen oder einen vollständigen TikTok-LIVE-Link ein.";
                return;
            }
            browser.CoreWebView2.Navigate(target);
        };
        login.Click += delegate { browser.CoreWebView2.Navigate("https://www.tiktok.com/login"); };
        Shown += InitializeBrowser;
    }

    private static string LiveAddress(string input)
    {
        input = input.Trim();
        var handle = Regex.Match(input, @"\A@([a-zA-Z0-9_.]{1,64})\z");
        Uri uri;
        if (!handle.Success && Uri.TryCreate(input, UriKind.Absolute, out uri) &&
            uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
            (uri.Host == "www.tiktok.com" || uri.Host == "tiktok.com"))
            handle = Regex.Match(uri.AbsolutePath, @"\A/@([a-zA-Z0-9_.]{1,64})/live/?\z", RegexOptions.IgnoreCase);
        return handle.Success ? "https://www.tiktok.com/@" + handle.Groups[1].Value + "/live" : null;
    }

    private async void InitializeBrowser(object sender, EventArgs args)
    {
        try
        {
            Directory.CreateDirectory(data);
            await browser.EnsureCoreWebView2Async();
            var core = browser.CoreWebView2;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            if (smokeTest)
            {
                core.NavigationCompleted += async (source, result) =>
                {
                    try
                    {
                        if (result.IsSuccess && core.DocumentTitle == "TikTok-Bot Smoke" &&
                            Directory.Exists(browser.CreationProperties.UserDataFolder) &&
                            LiveAddress("@creator") == "https://www.tiktok.com/@creator/live" &&
                            LiveAddress("https://tiktok.com/@creator/live?share=1") == "https://www.tiktok.com/@creator/live" &&
                            LiveAddress("https://www.tiktok.com.example.org/@creator/live") == null &&
                            await LiveComments.VerifyReader(core))
                            ExitCode = 0;
                    }
                    finally { Close(); }
                };
                core.NavigateToString("<title>TikTok-Bot Smoke</title><div data-e2e='chat-room'>" +
                    "<div data-e2e='chat-message'><b data-e2e='message-owner-name'>Zuschauer</b>" +
                    "<span class='break-words align-middle'>Vorhandener Kommentar</span></div></div>");
                return;
            }

            core.NavigationStarting += delegate { status.Text = "Seite wird geladen …"; };
            core.NavigationCompleted += (source, result) =>
            {
                if (result.IsSuccess && result.HttpStatusCode < 400)
                    status.Text = "TikTok-Seite geladen.";
                else
                    Report("Seite konnte nicht geladen werden.", result.WebErrorStatus + ", HTTP " + result.HttpStatusCode);
            };
            core.ProcessFailed += (source, result) => Report("Browseransicht ausgefallen. Starte die App neu.", result.ProcessFailedKind.ToString());
            comments.Attach(core);
            open.Enabled = login.Enabled = true;
            status.Text = "Öffne deinen LIVE oder melde das Bot-Konto an.";
        }
        catch (Exception error)
        {
            if (IsDisposed) return;
            Report("WebView2 konnte nicht gestartet werden. Prüfe die installierte WebView2 Runtime.", error.GetType().Name);
            if (smokeTest) Close();
        }
    }

    private void Report(string message, string details)
    {
        status.Text = message;
        try { File.AppendAllText(Path.Combine(data, "connection.log"), DateTime.UtcNow.ToString("O") + " " + message + " " + details + Environment.NewLine); }
        catch (IOException) { status.Text += " Das Fehlerprotokoll ist nicht verfügbar."; }
        catch (UnauthorizedAccessException) { status.Text += " Das Fehlerprotokoll ist nicht verfügbar."; }
    }
}
