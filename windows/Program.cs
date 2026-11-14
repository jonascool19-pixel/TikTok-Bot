using System;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        Application.ThreadException += (sender, args) =>
        {
            ErrorLog.Write("Windows-App", "Unerwarteter Fehler auf der Oberfläche.", args.Exception);
            MessageBox.Show("Ein unerwarteter Fehler ist aufgetreten. Details stehen im Fehlerlog.", "TikTok-Bot");
        };
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            ErrorLog.Write("Windows-App", "Die App ist unerwartet abgebrochen.", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, args) =>
            ErrorLog.Write("Windows-App", "Eine Hintergrundaufgabe ist fehlgeschlagen.", args.Exception.GetBaseException());
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var window = new BotWindow(arguments.Length == 1 && arguments[0] == "--smoke-test"))
        {
            Application.Run(window);
            return window.ExitCode;
        }
    }
}

internal sealed class BotWindow : Form
{
    private readonly WebView2 browser = new WebView2();
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
    private readonly Label settingsFeedback = new Label { Dock = DockStyle.Bottom, Height = 25, Visible = false };
    private AppTheme theme = AppTheme.Designs[0];
    private bool? saveSucceeded;
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
        var music = new MusicQueue(() => comments.ChatAvailable, data, ShowSettingsFeedback) { Dock = DockStyle.Fill };
        var replies = new ChatReplies(() => music.RequestsEnabled, data, ShowSettingsFeedback) { Dock = DockStyle.Fill };
        BindSetting("mute-live", muteLive);
        music.BindSettings(BindSetting);
        replies.BindSettings(BindSetting);
        replies.SendLiveReply = SendLiveReply;
        music.SendChoice = message => replies.AutoSendEnabled ? SendLiveReply(message, true) : System.Threading.Tasks.Task.FromResult<string>(null);
        comments.VoiceSettingsSaved += ShowSettingsFeedback;
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
        var bell = new Button { Text = "🔔", Dock = DockStyle.Right, Width = 74, AccessibleName = "Benachrichtigungen" };
        var messages = new System.Collections.Generic.List<Tuple<string>>();
        var unread = 0;
        Action refreshBell = delegate
        {
            bell.Text = unread == 0 ? "🔔" : "🔔 " + unread;
            bell.AccessibleName = "Benachrichtigungen, " + unread + " ungelesen";
            bell.BackColor = SystemInformation.HighContrast ? SystemColors.Control :
                unread > 0 ? theme.Selection : theme.Surface;
            if (!SystemInformation.HighContrast) bell.ForeColor = unread > 0 ? theme.Accent : theme.Ink;
        };
        Action<string> notify = message =>
        {
            if (messages.Count == 100) messages.RemoveAt(0);
            messages.Add(Tuple.Create(message));
            unread = Math.Min(messages.Count, unread + 1);
            refreshBell();
        };
        bell.Click += delegate
        {
            while (notices.Items.Count > 0) notices.Items[0].Dispose();
            unread = 0;
            refreshBell();
            if (messages.Count == 0) notices.Items.Add("Keine Benachrichtigungen").Enabled = false;
            else
            {
                notices.Items.Add("Alle löschen", null, (sender, args) =>
                {
                    messages.Clear();
                    unread = 0;
                    refreshBell();
                });
                notices.Items.Add(new ToolStripSeparator());
                foreach (var message in messages)
                {
                    var entry = new ToolStripMenuItem(message.Item1.Replace("&", "&&"));
                    entry.DropDownItems.Add("Benachrichtigung löschen", null, (sender, args) =>
                    {
                        var index = messages.FindIndex(item => ReferenceEquals(item, message));
                        if (index < 0) return;
                        if (index >= messages.Count - unread) unread--;
                        messages.RemoveAt(index);
                        refreshBell();
                    });
                    notices.Items.Insert(2, entry);
                }
            }
            notices.Show(bell, new Point(0, bell.Height));
        };
        var header = new Panel { Dock = DockStyle.Top, Height = 94, Padding = new Padding(14, 5, 14, 4) };
        var brand = new Label { Dock = DockStyle.Top, Height = 36, Text = "TikTok-Bot", Font = AppTheme.HeadingFont };
        toolbar.Dock = DockStyle.Fill;
        header.Controls.AddRange(new Control[] { toolbar, bell, brand });
        toolbar.Padding = new Padding(0, 3, 0, 0);
        music.WishAdded += title => notify("Musikwunsch: " + title);
        var tabs = new AppTabs();
        var liveTab = new TabPage("TikTok-Webansicht");
        var webSlot = new Panel { Dock = DockStyle.Fill };
        liveTab.Controls.Add(webSlot);
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
        var menu = new ListBox { Dock = DockStyle.Left, Width = 200, IntegralHeight = false, ItemHeight = 36, DrawMode = DrawMode.OwnerDrawFixed, AccessibleName = "Einstellungsbereiche" };
        menu.DrawItem += (sender, args) =>
        {
            if (args.Index < 0) return;
            var selected = (args.State & DrawItemState.Selected) != 0;
            var contrast = SystemInformation.HighContrast;
            using (var brush = new SolidBrush(contrast ? (selected ? SystemColors.Highlight : SystemColors.Window) :
                (selected ? theme.Selection : theme.Surface))) args.Graphics.FillRectangle(brush, args.Bounds);
            var bounds = new Rectangle(args.Bounds.X + 12, args.Bounds.Y, args.Bounds.Width - 24, args.Bounds.Height);
            var ink = contrast ? (selected ? SystemColors.HighlightText : SystemColors.WindowText) : (selected ? theme.Accent : theme.Ink);
            TextRenderer.DrawText(args.Graphics, menu.Items[args.Index].ToString(), menu.Font, bounds, ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            args.DrawFocusRectangle();
        };
        var settingsContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) };
        settingsTab.Padding = new Padding(12);
        settingsTab.Controls.AddRange(new Control[] { settingsContent, menu });
        var settingsPanels = new System.Collections.Generic.Dictionary<string, Panel>(StringComparer.Ordinal);
        Action<TabPage> openSetting = page =>
        {
            tabs.SelectedTab = settingsTab;
            menu.SelectedItem = page.Text;
        };
        tabs.AddPages(chatTab, repliesTab, musicTab, statsTab, settingsTab);
        tabs.SelectedTab = chatTab;
        Controls.AddRange(new Control[] { tabs, settingsFeedback, status, account, header });
        var liveOptions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 0) };
        var liveStatus = new Label { AutoSize = true, Margin = new Padding(3, 6, 12, 0) };
        Action<bool> updateLiveStatus = connected =>
        {
            liveStatus.ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText :
                connected ? (theme.Name == "Klar" ? Color.DarkGreen : Color.PaleGreen) :
                (theme.Name == "Klar" ? Color.Firebrick : Color.LightSalmon);
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
        ConfigureLiveMonitor(() => tabs.SelectedTab == settingsTab && Equals(menu.SelectedItem, liveTab.Text));
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
            catch (IOException error) { ErrorLog.Write("Bot-Konto", "Bot-Konto konnte nicht geladen werden.", error); }
            catch (UnauthorizedAccessException error) { ErrorLog.Write("Bot-Konto", "Bot-Konto konnte nicht geladen werden.", error); }
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
                ShowSettingsFeedback(true);
                configuredHandle = name;
                missingAccountLogged = false;
                accountHelp.Text = "Gespeichert: " + name + ". Die Anmeldung erfolgt bei TikTok.";
            }
            catch (IOException error) { ShowSettingsFeedback(false); Report("Bot-Konto konnte nicht gespeichert werden.", error.GetType().Name); }
            catch (UnauthorizedAccessException error) { ShowSettingsFeedback(false); Report("Bot-Konto konnte nicht gespeichert werden.", error.GetType().Name); }
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
        var designsTab = new TabPage("Designs");
        var designOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };
        var designTitle = new Label { Text = "Design auswählen", Font = AppTheme.HeadingFont, AutoSize = true };
        var designPicker = new ThemeComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, AccessibleName = "Design" };
        var designPath = Path.Combine(data, "design.txt");
        try { if (File.Exists(designPath)) theme = AppTheme.Find(File.ReadAllText(designPath).Trim()); }
        catch (IOException error) { ErrorLog.Write("Designs", "Design konnte nicht geladen werden.", error); }
        catch (UnauthorizedAccessException error) { ErrorLog.Write("Designs", "Design konnte nicht geladen werden.", error); }
        foreach (var design in AppTheme.Designs) designPicker.Items.Add(design.Name);
        designPicker.SelectedItem = theme.Name;
        Action applyDesign = delegate
        {
            var next = AppTheme.Find(designPicker.SelectedItem as string);
            if (!SavePreference(designPath, next.Name)) { designPicker.SelectedItem = theme.Name; return; }
            theme = next;
            theme.Apply(this);
            if (!SystemInformation.HighContrast) brand.ForeColor = theme.Accent;
            updateLiveStatus(comments.ChatAvailable);
            refreshBell();
            if (saveSucceeded.HasValue) ShowSettingsFeedback(saveSucceeded.Value);
        };
        designPicker.SelectionChangeCommitted += delegate { applyDesign(); };
        var previews = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        foreach (var design in AppTheme.Designs)
        {
            var preview = new Button { Text = design.Name, Tag = design, Width = 110, Height = 41, FlatStyle = FlatStyle.Flat,
                BackColor = design.Selection, ForeColor = design.Accent, UseVisualStyleBackColor = false };
            preview.FlatAppearance.BorderColor = design.Border;
            if (SystemInformation.HighContrast)
            {
                preview.BackColor = SystemColors.Control;
                preview.ForeColor = SystemColors.ControlText;
            }
            preview.Click += delegate
            {
                designPicker.SelectedItem = design.Name;
                applyDesign();
            };
            previews.Controls.Add(preview);
        }
        designOptions.Controls.AddRange(new Control[] { designTitle, designPicker, previews });
        designsTab.Controls.Add(designOptions);
        var helpTab = new TabPage("Anleitung");
        helpTab.Controls.Add(new BotHelp { Dock = DockStyle.Fill });
        var settingsPages = new[] { accountTab, updatesTab, liveTab, errorTab, intervalsTab, designsTab, helpTab };
        foreach (var page in settingsPages)
        {
            menu.Items.Add(page.Text);
            var panel = new Panel { Dock = DockStyle.Fill, Visible = false };
            while (page.Controls.Count > 0) panel.Controls.Add(page.Controls[0]);
            settingsContent.Controls.Add(panel);
            settingsPanels.Add(page.Text, panel);
        }
        // Keep the LIVE renderer mounted when another page covers it.
        Controls.Add(browser);
        Action layoutBrowser = delegate
        {
            if (tabs.SelectedTab == settingsTab && Equals(menu.SelectedItem, liveTab.Text))
            {
                browser.Bounds = RectangleToClient(webSlot.RectangleToScreen(webSlot.ClientRectangle));
                browser.BringToFront();
            }
            else
            {
                browser.Bounds = tabs.Bounds;
                browser.SendToBack();
            }
        };
        tabs.SelectionChanged += layoutBrowser;
        webSlot.SizeChanged += delegate { layoutBrowser(); };
        tabs.SizeChanged += delegate { layoutBrowser(); };
        Shown += delegate { layoutBrowser(); };
        menu.SelectedIndexChanged += delegate
        {
            foreach (var entry in settingsPanels)
                entry.Value.Visible = Equals(entry.Key, menu.SelectedItem);
            layoutBrowser();
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
        theme.Apply(this);
        if (!SystemInformation.HighContrast) brand.ForeColor = theme.Accent;
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
            SavePreference(path, ((int)field.Value).ToString());
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
                var savedMonitor = Path.Combine(data, "live-monitor.txt");
                bool enabled;
                monitor.Checked = File.Exists(savedMonitor) && bool.TryParse(File.ReadAllText(savedMonitor), out enabled)
                    ? enabled : !File.Exists(preference);
            }
        }
        catch (IOException error) { ErrorLog.Write("LIVE-Prüfung", "LIVE-Einstellungen konnten nicht geladen werden.", error); }
        catch (UnauthorizedAccessException error) { ErrorLog.Write("LIVE-Prüfung", "LIVE-Einstellungen konnten nicht geladen werden.", error); }
        monitor.CheckedChanged += delegate
        {
            if (monitor.Checked && watchedRoom == null)
            {
                monitor.Checked = false;
                status.Text = "Speichere zuerst ein festes LIVE-Konto.";
                return;
            }
            SavePreference(Path.Combine(data, "live-monitor.txt"), monitor.Checked.ToString());
            if (monitor.Checked && browser.CoreWebView2 != null) browser.CoreWebView2.Navigate(watchedRoom);
        };
        liveTimer.Tick += delegate
        {
            var core = browser.CoreWebView2;
            var target = watchedRoom;
            if (core == null || target == null || !monitor.Checked || comments.ChatAvailable) return;
            // Leave sign-in and profile editing visible while the user works there.
            if (webViewSelected() && !IsAllowedRoom(core.Source, target)) return;
            try { core.Navigate(target); }
            catch (InvalidOperationException error) { ErrorLog.Write("LIVE-Prüfung", "Das gespeicherte LIVE konnte nicht geöffnet werden.", error); }
        };
    }

    private bool SaveMonitor(string target)
    {
        if (!SavePreference(Path.Combine(data, "watched-live.txt"), target)) return false;
        watchedRoom = target;
        comments.SetConnection(false);
        return true;
    }

    private void BindSetting(string name, CheckBox field)
    {
        var path = Path.Combine(data, name + ".txt");
        try
        {
            bool saved;
            if (File.Exists(path) && bool.TryParse(File.ReadAllText(path), out saved)) field.Checked = saved;
        }
        catch (IOException error) { ErrorLog.Write("Einstellungen", "Einstellung konnte nicht gelesen werden.", error); }
        catch (UnauthorizedAccessException error) { ErrorLog.Write("Einstellungen", "Einstellung konnte nicht gelesen werden.", error); }
        field.CheckedChanged += delegate { SavePreference(path, field.Checked.ToString()); };
    }

    private bool SavePreference(string path, string value)
    {
        return ChatReplies.Persist(path, value, ShowSettingsFeedback);
    }

    private void ShowSettingsFeedback(bool saved)
    {
        saveSucceeded = saved;
        settingsFeedback.Text = saved ? "Einstellung gespeichert" : "Einstellung konnte nicht gespeichert werden";
        settingsFeedback.ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : saved ? theme.Accent :
            (theme.Name == "Klar" ? Color.Firebrick : Color.LightSalmon);
        settingsFeedback.Visible = true;
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
                        catch (InvalidOperationException error) { ErrorLog.Write("TikTok-Profil", "Profil konnte nicht überprüft werden.", error); }
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
            ChatReplies.Suggest("Wie geht es dir?", "") == "Mir geht’s gut, danke! Und dir?" &&
            ChatReplies.Suggest("Na, wie geht’s dir heute?", "") == "Mir geht’s gut, danke! Und dir?" &&
            ChatReplies.Suggest("Alles gut bei dir?", "") == "Mir geht’s gut, danke! Und dir?" &&
            ChatReplies.Suggest("Wie heißt das Spiel?", "Minecraft") == "Das Spiel heißt Minecraft." &&
            ChatReplies.Suggest("Wie kann ich mir ein Lied wünschen?", "", true).Contains("#Musikwunsch") &&
            ChatReplies.Suggest("Wie mache ich Musik?", "", true).Contains("#Musikwunsch") &&
            ChatReplies.Suggest("Wie mache ich Musik?", "", false).Contains("deaktiviert") &&
            ChatReplies.FindSavedAnswer("Wo finde ich denn die Regeln?", new System.Collections.Generic.Dictionary<string, string> { { "Wo finde ich die Regeln", "Im Profil." } }) == "Im Profil." &&
            ChatReplies.FindSavedAnswer("Wo finde ich nicht die Regeln?", new System.Collections.Generic.Dictionary<string, string> { { "Wo finde ich die Regeln", "Im Profil." } }) == null &&
            ChatReplies.Suggest("Kann ich mir einen Song wünschen?", "", false).Contains("deaktiviert") &&
            ChatReplies.Suggest("Wo sehe ich meine Anfrage?", "") == null &&
            ChatReplies.GiftThanks("Alex", "Rose", 2) == "Danke, Alex, für 2 × Rose!" &&
            ChatReplies.Suggest("okay", "") == null &&
            SpotifyQueueControl.ParseTracks("{\"tracks\":{\"items\":[{\"uri\":\"spotify:track:1234567890123456789012\",\"name\":\"Test\"},{\"uri\":\"spotify:track:1234567890123456789012\",\"name\":\"Duplikat\"},{\"uri\":\"spotify:track:1234567890123456789013\",\"name\":\"Gesperrt\",\"is_playable\":false}]}}").Length == 1 &&
            ChatReplies.CleanAiReply(" KEINE_ANTWORT. ") == null &&
            ChatReplies.CleanAiReply(" Klar, gerne! ") == "Klar, gerne!" &&
            ChatReplies.CleanAiReply("Antwort: Wir spielen ETS2.") == "Wir spielen ETS2." &&
            ChatReplies.CleanAiReply("Vorher: Kommentar (Spiel: ETS2)") == null &&
            ChatReplies.CleanAiReply("{\"kommentar\":\"Welches Spiel?\"}") == null &&
            ChatReplies.CleanAiReply("Welches Spiel?", "Welches Spiel?") == null &&
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

    private bool sendingReply;
    private DateTime nextReplyTime;

    private System.Threading.Tasks.Task<string> SendLiveReply(string reply) { return SendLiveReply(reply, false); }

    private async System.Threading.Tasks.Task<string> SendLiveReply(string reply, bool musicChoice)
    {
        const string fallback = "Antwortvorschlag bereit. ";
        var core = browser.CoreWebView2;
        var target = watchedRoom;
        if (core == null || !comments.ChatAvailable || !IsAllowedRoom(core.Source, target))
            return fallback + "Das gespeicherte LIVE ist nicht verbunden.";
        if (sendingReply || (!musicChoice && DateTime.UtcNow < nextReplyTime))
            return fallback + "Sendepause aktiv.";
        sendingReply = true;
        try
        {
            var cookies = await core.CookieManager.GetCookiesAsync("https://www.tiktok.com/");
            if (IsDisposed || !comments.ChatAvailable || target != watchedRoom || !IsAllowedRoom(core.Source, target))
                return fallback + "Das gespeicherte LIVE ist nicht verbunden.";
            var signedIn = false;
            foreach (var cookie in cookies)
                if ((cookie.Name == "sessionid" || cookie.Name == "sessionid_ss") && !string.IsNullOrEmpty(cookie.Value) &&
                    (cookie.IsSession || cookie.Expires.ToUniversalTime() > DateTime.UtcNow)) signedIn = true;
            if (!signedIn) return fallback + "Melde zuerst das Bot-Konto bei TikTok an.";
            if (configuredHandle != null && botHandle != null && !string.Equals(configuredHandle, botHandle, StringComparison.OrdinalIgnoreCase))
                return fallback + "Das angemeldete Konto weicht vom gespeicherten Bot-Konto ab.";

            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            var result = await core.ExecuteScriptAsync(@"(() => {
                if ((location.origin + location.pathname.replace(/\/$/, '')).toLowerCase() !== " + json.Serialize(target.ToLowerInvariant()) + @") return 'room';
                const root = document.querySelector('[data-e2e=live-chat-container], [data-e2e=chat-room]');
                if (!root) return 'missing-chat';
                const editorSelector = '[data-e2e=chat-input][contenteditable], [data-e2e=chat-input] [contenteditable], ' +
                    '[contenteditable][role=textbox], [contenteditable][maxlength=""150""]';
                const sendSelector = '[data-e2e=chat-send], [data-e2e=chat-send-button], [data-e2e=chat-send-btn], ' +
                    '[aria-label=""Send message""], [aria-label=""Senden""], [aria-label=""Send""], ' +
                    '[data-e2e=live-chat-send]';
                const visible = node => node.getClientRects().length > 0 && getComputedStyle(node).visibility === 'visible';
                let scope = root;
                let editor, send;
                for (let depth = 0; scope && scope !== document.body && depth < 5; depth++, scope = scope.parentElement) {
                    const editors = Array.from(scope.querySelectorAll(editorSelector)).filter(e => e.isContentEditable && visible(e));
                    if (editors.length > 1) return 'ambiguous';
                    if (!editors.length) continue;
                    editor = editors[0];
                    const buttons = Array.from(scope.querySelectorAll(sendSelector)).filter(visible);
                    const candidates = buttons.filter(node => !buttons.some(other => other !== node && node.contains(other)));
                    if (candidates.length > 1) return 'ambiguous';
                    if (candidates.length === 1) { send = candidates[0]; break; }
                    const form = editor.closest(""form"");
                    if (form && scope.contains(form)) {
                        const submit = Array.from(form.querySelectorAll(""button[type=submit]"")).filter(visible);
                        if (submit.length === 1) { send = submit[0]; break; }
                    }
                }
                if (!editor) return 'missing-editor';

                if ((editor.innerText || '').replace(/[\u200B\uFEFF]/g, '').trim()) return 'draft';
                editor.focus({ preventScroll: true });
                const reply = " + json.Serialize(reply) + @";
                if (document.activeElement !== editor) return 'focus';
                if (!document.execCommand('insertText', false, reply) || (editor.innerText || '').trim() !== reply) return 'insert';
                if (!send) return 'enter';
                if (send.disabled || send.getAttribute('aria-disabled') === 'true') return 'disabled';
                send.click();
                return 'submitted';
            })()");
            if (result == "\"enter\"")
            {
                if (IsDisposed || target != watchedRoom || !comments.ChatAvailable || !IsAllowedRoom(core.Source, target))
                    return fallback + "Das gespeicherte LIVE ist nicht verbunden.";
                // Send a key to this WebView only. Web pages can ignore synthetic DOM keyboard events.
                const string enter = "\"key\":\"Enter\",\"code\":\"Enter\",\"windowsVirtualKeyCode\":13";
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyDown\",\"text\":\"\\r\"," + enter + "}");
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyUp\"," + enter + "}");
                nextReplyTime = DateTime.UtcNow.AddSeconds(12);
                await System.Threading.Tasks.Task.Delay(500);
                if (IsDisposed || target != watchedRoom || !IsAllowedRoom(core.Source, target))
                    return fallback + "Das gespeicherte LIVE ist nicht verbunden.";
                var cleared = await core.ExecuteScriptAsync(@"(() => {
                    const editor = document.activeElement;
                    return !!editor && editor.isContentEditable &&
                        !(editor.innerText || '').replace(/[\u200B\uFEFF]/g, '').trim();
                })()");
                if (cleared == "true") result = "\"submitted\"";
                else
                {
                    ErrorLog.Write("Chatantworten", "TikTok hat die Enter-Eingabe nicht bestätigt (enter-unconfirmed).");
                    return fallback + "Versand per Enter nicht bestätigt. Prüfe den LIVE-Chat.";
                }
            }
            if (result == "\"submitted\"")
            {
                nextReplyTime = DateTime.UtcNow.AddSeconds(12);
                return "Antwort an TikTok übergeben. Die Veröffentlichung siehst du im LIVE-Chat.";
            }
            switch (result)
            {
                case "\"room\"":
                    return fallback + "Das gespeicherte LIVE ist nicht verbunden.";
                case "\"draft\"":
                    return fallback + "Im TikTok-Chatfeld steht bereits ein Entwurf.";
                case "\"missing-chat\"":
                case "\"missing-editor\"":
                case "\"missing-button\"":
                case "\"ambiguous\"":
                case "\"focus\"":
                case "\"insert\"":
                case "\"disabled\"":
                    ErrorLog.Write("Chatantworten", "TikTok-Sendeversuch nicht möglich (" + result.Trim('"') + ").");
                    return fallback + "TikTok-Sendefeld nicht verfügbar. Details stehen im Fehlerlog.";
                default:
                    ErrorLog.Write("Chatantworten", "TikTok-Sendeversuch lieferte keinen verwertbaren Status.");
                    return fallback + "TikTok-Sendeversuch fehlgeschlagen.";
            }
        }
        catch (InvalidOperationException error)
        {
            if (!IsDisposed) ErrorLog.Write("Chatantworten", "TikTok-Sendeversuch fehlgeschlagen.", error);
            return fallback + "TikTok-Sendeversuch fehlgeschlagen.";
        }
        finally { sendingReply = false; }
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
