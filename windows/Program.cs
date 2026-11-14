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
    private readonly Label account = new Label { Dock = DockStyle.Bottom, Height = 26, Text = "Bot-Konto: noch nicht erkannt" };
    private readonly Timer accountTimer = new Timer { Interval = 4000 };
    private bool checkingAccount;
    private readonly Timer liveTimer = new Timer { Interval = 60000 };
    private readonly CheckBox monitor = new CheckBox { Text = "LIVE automatisch prüfen", AutoSize = true };
    private readonly CheckBox muteLive = new CheckBox { Text = "TikTok-LIVE-Ton stumm", AutoSize = true, Checked = true };
    private string watchedRoom;
    private string botHandle;
    private string configuredHandle;
    private bool missingAccountLogged;
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 32, Text = "TikTok-Ansicht wird gestartet …" };
    private readonly bool smokeTest;
    private readonly Label accountState = new Label { AutoSize = true, Text = "Bot-Konto: Anmeldung wird geprüft" };
    private readonly string data;
    internal int ExitCode { get; private set; }

    internal BotWindow(bool smokeTest)
    {
        this.smokeTest = smokeTest;
        ExitCode = smokeTest ? 1 : 0;
        data = smokeTest ? Path.Combine(Path.GetTempPath(), "TikTokBot-Smoke-" + Guid.NewGuid()) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TikTok-Bot", "WindowsPrototype");
        ErrorLog.Configure(data);
        comments.LoadVoicePreferences(data);
        browser.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = Path.Combine(data, "BotProfile") };
        Text = "TikTok-Bot";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 820);
        MinimumSize = new Size(1000, 430);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        toolbar.Controls.Add(new Label { Text = "Festes LIVE-Konto", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
        room.ReadOnly = true;
        var editTarget = new Button { Text = "Streamer ändern", AutoSize = true };
        editTarget.Click += delegate
        {
            if (room.ReadOnly)
            {
                room.ReadOnly = false;
                editTarget.Text = "Streamer speichern";
                room.Focus();
                room.SelectAll();
                return;
            }
            var target = LiveAddress(room.Text);
            if (target == null)
            {
                status.Text = "Gib einen @Namen oder einen vollständigen TikTok-LIVE-Link ein.";
                return;
            }
            if (!SaveMonitor(target)) return;
            room.ReadOnly = true;
            editTarget.Text = "Streamer ändern";
            room.Text = target;
            if (!monitor.Checked)
            {
                monitor.Checked = true;
                return;
            }
            if (browser.CoreWebView2 != null) browser.CoreWebView2.Navigate(target);
        };
        var music = new MusicQueue(() => comments.ChatAvailable) { Dock = DockStyle.Fill };
        var replies = new ChatReplies(() => music.RequestsEnabled) { Dock = DockStyle.Fill };
        var profile = new Button { Text = "Profil öffnen", AutoSize = true, Enabled = false };
        profile.Click += delegate
        {
            var target = watchedRoom;
            if (target == null)
            {
                status.Text = "Gib einen @Namen oder einen vollständigen TikTok-LIVE-Link ein.";
                return;
            }
            browser.CoreWebView2.Navigate(target.Substring(0, target.Length - "/live".Length));
        };
        open.EnabledChanged += delegate { profile.Enabled = open.Enabled; };
        toolbar.Controls.AddRange(new Control[] { room, editTarget, open, profile, login });
        toolbar.Controls.Add(accountState);
        var notices = new ContextMenuStrip();
        var bell = new Button { Text = "🔔", Dock = DockStyle.Right, Width = 58, AccessibleName = "Benachrichtigungen" };
        var unread = 0;
        var newestNotice = "";
        var previousNotice = "";
        Action<string> notify = message =>
        {
            previousNotice = newestNotice;
            newestNotice = message;
            unread = Math.Min(2, unread + 1);
            bell.Text = "🔔 " + unread;
            bell.BackColor = Color.LightGoldenrodYellow;
        };
        bell.Click += delegate
        {
            notices.Items.Clear();
            notices.Items.Add(string.IsNullOrEmpty(newestNotice) ? "Keine Benachrichtigungen" : newestNotice).Enabled = false;
            if (!string.IsNullOrEmpty(previousNotice)) notices.Items.Add(previousNotice).Enabled = false;
            unread = 0;
            bell.Text = "🔔";
            bell.BackColor = SystemColors.Control;
            notices.Show(bell, new Point(0, bell.Height));
        };
        var header = new Panel { Dock = DockStyle.Top, Height = 52 };
        toolbar.Dock = DockStyle.Fill;
        header.Controls.AddRange(new Control[] { toolbar, bell });
        music.WishAdded += title => notify("Musikwunsch: " + title);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var liveTab = new TabPage("TikTok-Webansicht");
        liveTab.Controls.Add(browser);
        var chatTab = new TabPage("Kommentare und Stimme");
        chatTab.Controls.Add(comments);
        var musicTab = new TabPage("Musik");
        comments.ViewerComment += (handle, message) =>
        {
            if (!string.IsNullOrEmpty(handle) && string.Equals("@" + handle, botHandle ?? configuredHandle, StringComparison.OrdinalIgnoreCase)) return;
            if (!music.ConsiderRequest(handle, message)) replies.ConsiderLiveComment(message, music.RequestsEnabled);
        };
        browser.NavigationStarting += delegate { music.ResetChoices(); replies.ResetLiveContext(); };
        musicTab.Controls.Add(music);
        var updatesTab = new TabPage("Updates");
        var updater = new Updates { Dock = DockStyle.Fill };
        updater.UpdateAvailable += version => notify("Update " + version + " verfügbar.");
        updatesTab.Controls.Add(updater);
        var errorTab = new TabPage("Fehlerlog");
        errorTab.Controls.Add(new ErrorLogView { Dock = DockStyle.Fill });
        var statsTab = new TabPage("LIVE-Auswertung");
        var stats = new LiveStats { Dock = DockStyle.Fill };
        statsTab.Controls.Add(stats);
        comments.ViewerActivity += stats.Record;
        comments.AudienceChanged += stats.SetAudience;
        browser.NavigationStarting += (sender, args) =>
        {
            if (IsAllowedRoom(args.Uri, watchedRoom)) stats.SetRoom(watchedRoom);
            stats.SetAudience(null);
        };
        var repliesTab = new TabPage("Chatantworten");
        repliesTab.Controls.Add(replies);
        var settingsTab = new TabPage("Einstellungen");
        var menu = new ListBox { Dock = DockStyle.Left, Width = 200, IntegralHeight = false, AccessibleName = "Einstellungsbereiche" };
        var settingsContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) };
        settingsTab.Padding = new Padding(12);
        settingsTab.Controls.AddRange(new Control[] { settingsContent, menu });
        TabPage[] settingsPages = null;
        Action<TabPage> openSetting = page =>
        {
            tabs.SelectedTab = settingsTab;
            menu.SelectedIndex = Array.IndexOf(settingsPages, page);
        };
        var youtubeTab = new TabPage("YouTube");
        var openYouTube = new Button { Text = "YouTube öffnen / anmelden", AutoSize = true, Location = new Point(12, 12) };
        openYouTube.Click += delegate { MusicQueue.OpenYouTube("https://www.youtube.com/"); };
        youtubeTab.Controls.Add(openYouTube);
        tabs.TabPages.AddRange(new[] { chatTab, repliesTab, musicTab, youtubeTab, statsTab, settingsTab });
        tabs.SelectedTab = chatTab;
        Controls.AddRange(new Control[] { tabs, status, account, header });
        var liveOptions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 0) };
        var liveStatus = new Label { AutoSize = true, Margin = new Padding(3, 6, 12, 0) };
        Action<bool> updateLiveStatus = connected =>
        {
            liveStatus.ForeColor = connected ? Color.DarkGreen : Color.Firebrick;
            liveStatus.Text = connected ? "● LIVE verbunden" : "● Nicht live";
        };
        updateLiveStatus(false);
        comments.ConnectionChanged += updateLiveStatus;
        var nickname = new Button { Text = "Bot-Profil / Spitzname ändern", AutoSize = true };
        nickname.Click += delegate
        {
            var core = browser.CoreWebView2;
            var name = configuredHandle ?? botHandle;
            if (core != null)
            {
                openSetting(liveTab);
                core.Navigate(name == null ? "https://www.tiktok.com/login" : "https://www.tiktok.com/" + name);
                status.Text = "Wähle auf deinem TikTok-Profil „Profil bearbeiten“, um den Spitznamen zu ändern.";
            }
        };
        muteLive.CheckedChanged += delegate
        {
            if (browser.CoreWebView2 != null) browser.CoreWebView2.IsMuted = muteLive.Checked;
        };
        liveOptions.Controls.AddRange(new Control[] { liveStatus, monitor, nickname, muteLive });
        ConfigureLiveMonitor(() => tabs.SelectedTab == settingsTab && menu.SelectedIndex == 2);
        var accountTab = new TabPage("Bot-Konto");
        var accountTools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };
        var signIn = new Button { Text = "Bei TikTok anmelden", AutoSize = true };
        signIn.Click += delegate { if (login.Enabled) login.PerformClick(); };
        var configured = new TextBox { Width = 260, AccessibleName = "Bot-Konto @Name" };
        var saveAccount = new Button { Text = "@Name speichern", AutoSize = true };
        var accountHelp = new Label { AutoSize = true, Text = "Die TikTok-Sitzung wird im lokalen Browserprofil gespeichert." };
        var accountPath = Path.Combine(data, "bot-account.txt");
        if (!smokeTest && File.Exists(accountPath))
        {
            try
            {
                configured.Text = File.ReadAllText(accountPath).Trim();
                if (Regex.IsMatch(configured.Text, @"\A@[a-zA-Z0-9_.]{1,64}\z")) configuredHandle = configured.Text;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        saveAccount.Click += delegate
        {
            var name = configured.Text.Trim();
            if (!Regex.IsMatch(name, @"\A@[a-zA-Z0-9_.]{1,64}\z"))
            {
                accountHelp.Text = "Gib den öffentlichen TikTok-@Namen ein.";
                return;
            }
            try
            {
                Directory.CreateDirectory(data);
                File.WriteAllText(accountPath, name);
                configuredHandle = name;
                missingAccountLogged = false;
                accountHelp.Text = "Gespeichert: " + name + ". Die Anmeldung erfolgt bei TikTok.";
            }
            catch (IOException error) { Report("Bot-Konto konnte nicht gespeichert werden.", error.GetType().Name); }
            catch (UnauthorizedAccessException error) { Report("Bot-Konto konnte nicht gespeichert werden.", error.GetType().Name); }
        };
        var diagnostics = new Button { Text = "Fehlerprotokoll anzeigen", AutoSize = true };
        diagnostics.Click += delegate { openSetting(errorTab); };
        accountTools.Controls.AddRange(new Control[] { signIn, new Label { Text = "Öffentlicher @Name des Bot-Kontos", AutoSize = true }, configured, saveAccount, accountHelp, diagnostics });
        accountTab.Controls.Add(accountTools);
        var intervalsTab = new TabPage("Prüfintervalle");
        var intervals = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        var updateTimer = new Timer();
        AddInterval(intervals, "Updates prüfen (Minuten)", "update-interval", 60, updateTimer);
        AddInterval(intervals, "LIVE prüfen (Minuten)", "live-interval", 1, liveTimer);
        updateTimer.Tick += async delegate { await updater.Check(); };
        if (!smokeTest)
            Shown += async delegate { updateTimer.Start(); await updater.Check(); };
        Disposed += delegate { updateTimer.Dispose(); notices.Dispose(); };
        intervalsTab.Controls.Add(intervals);
        settingsPages = new[] { accountTab, updatesTab, liveTab, errorTab, intervalsTab };
        foreach (var page in settingsPages)
        {
            menu.Items.Add(page.Text);
            var panel = new Panel { Dock = DockStyle.Fill, Visible = false };
            while (page.Controls.Count > 0) panel.Controls.Add(page.Controls[0]);
            settingsContent.Controls.Add(panel);
            page.Dispose();
        }
        menu.SelectedIndexChanged += delegate
        {
            for (var index = 0; index < settingsPages.Length; index++)
                settingsContent.Controls[index].Visible = index == menu.SelectedIndex;
        };
        menu.SelectedIndex = 0;
        Controls.Add(liveOptions);
        AcceptButton = open;
        open.Click += delegate
        {
            var target = watchedRoom;
            if (target == null)
            {
                status.Text = "Speichere zuerst ein festes LIVE-Konto über „Streamer ändern“.";
                return;
            }
            browser.CoreWebView2.Navigate(target);
        };
        login.Click += async delegate
        {
            if (!checkingAccount && !await RefreshAccount() && !IsDisposed)
            {
                openSetting(liveTab);
                browser.CoreWebView2.Navigate("https://www.tiktok.com/login");
            }
        };
        Font = AppTheme.TextFont;
        AppTheme.Apply(this);
        updateLiveStatus(false);
        Shown += InitializeBrowser;
    }

    private void AddInterval(Control panel, string label, string file, int fallback, Timer timer)
    {
        var path = Path.Combine(data, file + ".txt");
        var minutes = fallback;
        try
        {
            int saved;
            if (File.Exists(path) && int.TryParse(File.ReadAllText(path), out saved) && saved >= 1 && saved <= 240) minutes = saved;
        }
        catch (IOException error) { ErrorLog.Write("Einstellungen", "Prüfintervall konnte nicht geladen werden.", error); }
        catch (UnauthorizedAccessException error) { ErrorLog.Write("Einstellungen", "Prüfintervall konnte nicht geladen werden.", error); }
        var field = new NumericUpDown { Minimum = 1, Maximum = 240, Value = minutes, Width = 100, AccessibleName = label };
        timer.Interval = minutes * 60000;
        field.ValueChanged += delegate
        {
            timer.Interval = (int)field.Value * 60000;
            try { Directory.CreateDirectory(data); File.WriteAllText(path, ((int)field.Value).ToString()); }
            catch (IOException error) { Report("Prüfintervall konnte nicht gespeichert werden.", error.GetType().Name); }
            catch (UnauthorizedAccessException error) { Report("Prüfintervall konnte nicht gespeichert werden.", error.GetType().Name); }
        };
        panel.Controls.Add(new Label { Text = label, AutoSize = true });
        panel.Controls.Add(field);
    }

    private void ConfigureLiveMonitor(Func<bool> webViewSelected)
    {
        if (smokeTest) return;
        var file = Path.Combine(data, "watched-live.txt");
        try
        {
            if (File.Exists(file)) watchedRoom = LiveAddress(File.ReadAllText(file));
            if (watchedRoom != null)
            {
                room.Text = watchedRoom;
                var preference = Path.Combine(data, "live-monitor-disabled");
                monitor.Checked = !File.Exists(preference);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        monitor.CheckedChanged += delegate
        {
            if (monitor.Checked && watchedRoom == null)
            {
                monitor.Checked = false;
                status.Text = "Speichere zuerst ein festes LIVE-Konto.";
                return;
            }
            try
            {
                Directory.CreateDirectory(data);
                var preference = Path.Combine(data, "live-monitor-disabled");
                if (monitor.Checked) File.Delete(preference);
                else File.WriteAllText(preference, "");
            }
            catch (IOException error) { Report("LIVE-Einstellung konnte nicht gespeichert werden.", error.GetType().Name); }
            catch (UnauthorizedAccessException error) { Report("LIVE-Einstellung konnte nicht gespeichert werden.", error.GetType().Name); }
            if (monitor.Checked && browser.CoreWebView2 != null) browser.CoreWebView2.Navigate(watchedRoom);
        };
        liveTimer.Tick += async delegate
        {
            var core = browser.CoreWebView2;
            var target = watchedRoom;
            if (core == null || target == null || !monitor.Checked) return;
            var source = core.Source;
            // Leave login, profile editing and manually opened pages alone.
            var currentRoom = LiveAddress(source ?? "");
            if (currentRoom == null) return;
            if (!string.Equals(currentRoom, target, StringComparison.OrdinalIgnoreCase))
            {
                if (!webViewSelected()) core.Navigate(target);
                return;
            }
            try
            {
                await core.ExecuteScriptAsync("document.readyState");
                if (!IsDisposed && watchedRoom == target && core.Source == source && !comments.ChatAvailable)
                    core.Navigate(target);
            }
            catch (InvalidOperationException) { }
        };
    }

    private bool SaveMonitor(string target)
    {
        try
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "watched-live.txt"), target);
            watchedRoom = target;
            comments.SetConnection(false);
            return true;
        }
        catch (IOException error) { Report("LIVE-Ziel konnte nicht gespeichert werden.", error.GetType().Name); }
        catch (UnauthorizedAccessException error) { Report("LIVE-Ziel konnte nicht gespeichert werden.", error.GetType().Name); }
        return false;
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

    private static bool IsAllowedRoom(string address, string target)
    {
        Uri uri;
        return target != null && Uri.TryCreate(address, UriKind.Absolute, out uri) &&
            uri.Host == "www.tiktok.com" && string.Equals(LiveAddress(address), target, StringComparison.OrdinalIgnoreCase);
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
            core.IsMuted = muteLive.Checked;
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
                            await LiveComments.VerifyReader(core) && VerifyErrorLog() && LiveComments.VerifyVoicePreferences(data) &&
                            await ChatReplies.VerifyBundledModel())
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
            core.NavigationCompleted += async (source, result) =>
            {
                if (result.HttpStatusCode == 404)
                    status.Text = "TikTok hat diese Seite nicht gefunden. Prüfe den @Namen.";
                else if (result.IsSuccess && result.HttpStatusCode < 400)
                {
                    status.Text = "TikTok-Seite geladen.";
                    var address = core.Source;
                    var handle = Regex.Match(address, @"\Ahttps://www\.tiktok\.com/(@[a-zA-Z0-9_.]{1,64})/?(?:[?#].*)?\z");
                    if (handle.Success)
                    {
                        try
                        {
                            var title = await core.ExecuteScriptAsync("document.querySelector('[data-e2e=user-title]')?.textContent.trim() ?? null");
                            if (IsDisposed || core.Source != address) return;
                            var expected = handle.Groups[1].Value;
                            status.Text = title.Equals("\"" + expected.TrimStart('@') + "\"", StringComparison.OrdinalIgnoreCase) ||
                                title.Equals("\"" + expected + "\"", StringComparison.OrdinalIgnoreCase)
                                ? "TikTok-Profil bestätigt: " + expected
                                : "TikTok-Profil konnte nicht eindeutig bestätigt werden.";
                        }
                        catch (InvalidOperationException) { }
                    }
                }
                else
                    Report("Seite konnte nicht geladen werden.", result.WebErrorStatus + ", HTTP " + result.HttpStatusCode);
            };
            core.ProcessFailed += (source, result) =>
            {
                comments.SetConnection(false);
                Report("Browseransicht ausgefallen. Starte die App neu.", result.ProcessFailedKind.ToString());
            };
            comments.Attach(core, address => IsAllowedRoom(address, watchedRoom));
            accountTimer.Tick += CheckAccount;
            accountTimer.Start();
            open.Enabled = login.Enabled = true;
            status.Text = "Öffne deinen LIVE oder melde das Bot-Konto an.";
            liveTimer.Start();
            if (watchedRoom != null && monitor.Checked) core.Navigate(watchedRoom);
        }
        catch (Exception error)
        {
            if (IsDisposed) return;
            Report("WebView2 konnte nicht gestartet werden. Prüfe die installierte WebView2 Runtime.", error.GetType().Name);
            if (smokeTest) Close();
        }
    }

    private static bool VerifyErrorLog()
    {
        const string secret = "chat-key-must-not-appear";
        ErrorLog.Write("Starttest", "Absichtlicher Testfehler.", new IOException(secret));
        var entries = ErrorLog.Read();
        ErrorLog.Clear();
        return entries.Contains("Starttest | Absichtlicher Testfehler. | IOException") && !entries.Contains(secret) &&
            ErrorLog.Read() == "Keine Fehler protokolliert." &&
            ChatReplies.Suggest("Moin!", "") != null &&
            ChatReplies.Suggest("Wie heißt das Spiel?", "Minecraft") == "Das Spiel heißt Minecraft." &&
            ChatReplies.Suggest("Wie kann ich mir ein Lied wünschen?", "", true).Contains("#Musikwunsch") &&
            ChatReplies.Suggest("Kann ich mir einen Song wünschen?", "", false).Contains("deaktiviert") &&
            ChatReplies.Suggest("Wo sehe ich meine Anfrage?", "") == null &&
            ChatReplies.GiftThanks("Alex", "Rose", 2) == "Danke, Alex, für 2 × Rose!" &&
            ChatReplies.Suggest("okay", "") == null &&
            ChatReplies.CleanAiReply(" KEINE_ANTWORT. ") == null &&
            ChatReplies.CleanAiReply(" Klar, gerne! ") == "Klar, gerne!" &&
            IsAllowedRoom("https://www.tiktok.com/@Creator/live?share=1", LiveAddress("@creator")) &&
            !IsAllowedRoom("https://www.tiktok.com/@other/live", LiveAddress("@creator")) &&
            !IsAllowedRoom("https://www.tiktok.com/@creator/live", null) &&
            !LiveComments.IsPlaybackAdvancing(null, 3) &&
            !LiveComments.IsPlaybackAdvancing(3, null) &&
            !LiveComments.IsPlaybackAdvancing(3, 3) &&
            LiveComments.IsPlaybackAdvancing(3, 5);
    }

    private async void CheckAccount(object sender, EventArgs args)
    {
        await RefreshAccount();
    }

    private async System.Threading.Tasks.Task<bool> RefreshAccount()
    {
        var core = browser.CoreWebView2;
        if (checkingAccount || core == null) return false;
        checkingAccount = true;
        try
        {
            var cookies = await core.CookieManager.GetCookiesAsync("https://www.tiktok.com/");
            var signedIn = false;
            foreach (var cookie in cookies)
                if ((cookie.Name == "sessionid" || cookie.Name == "sessionid_ss") && !string.IsNullOrEmpty(cookie.Value) &&
                    (cookie.IsSession || cookie.Expires.ToUniversalTime() > DateTime.UtcNow))
                    signedIn = true;
            var result = await core.ExecuteScriptAsync(
                @"(() => {
                    if (location.origin !== 'https://www.tiktok.com') return null;
                    const link = document.querySelector('[data-e2e=""profile-icon""] a[href^=""/@""], a[data-e2e=""profile-icon""][href^=""/@""], a[data-e2e=""profile-link""][href^=""/@""]');
                    return link ? link.getAttribute('href').split(/[/?#]/)[1] : null;
                })()");
            if (IsDisposed) return false;
            var handle = Regex.Match(result, @"\A""(@[a-zA-Z0-9_.]{1,64})""\z");
            botHandle = handle.Success ? handle.Groups[1].Value : null;
            if (signedIn || botHandle != null)
            {
                accountState.Text = "Bot-Konto angemeldet";
                account.Text = accountState.Text + (botHandle == null ? "" : " · " + botHandle);
                if (botHandle != null && configuredHandle != null && !string.Equals(botHandle, configuredHandle, StringComparison.OrdinalIgnoreCase))
                    account.Text += " · Gespeichertes Konto: " + configuredHandle;
                missingAccountLogged = false;
                return true;
            }
            else
            {
                accountState.Text = "Bot-Konto nicht angemeldet";
                account.Text = configuredHandle == null ? accountState.Text :
                    "Bot-Konto: " + configuredHandle + " gespeichert · Anmeldung konnte nicht bestätigt werden";
                if (configuredHandle != null && !missingAccountLogged)
                {
                    WriteLog("Bot-Konto", "TikTok zeigte keinen Kontonamen zur Bestätigung der Anmeldung.");
                    missingAccountLogged = true;
                }
            }
        }
        catch (InvalidOperationException error)
        {
            if (!IsDisposed) ErrorLog.Write("Bot-Konto", "TikTok-Sitzung konnte nicht geprüft werden.", error);
        }
        finally { checkingAccount = false; }
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            accountTimer.Dispose();
            liveTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Report(string message, string details)
    {
        status.Text = message;
        WriteLog(message, details);
    }

    private void WriteLog(string message, string details)
    {
        ErrorLog.Write("TikTok / Verbindung", message + " " + details);
    }
}
