using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class YouTubeMusicControl : UserControl
{
    private const string OpenLabel = "YouTube Music öffnen";
    private const string ConnectedLabel = "YouTube Music verbunden";
    private readonly YouTubeChromePlayer player;
    private readonly SemaphoreSlim commands = new SemaphoreSlim(1);
    private readonly ConnectionButton open = new ConnectionButton { Text = OpenLabel, AutoSize = true };
    private readonly Label status = new Label { Dock = DockStyle.Top, Height = 42,
        Text = "Melde dich bei Bedarf über Google-Konto anmelden an. Öffne danach den Player." };
    private bool opened;

    internal YouTubeMusicControl(string directory)
    {
        player = new YouTubeChromePlayer(Path.Combine(directory, "YouTubeMusicChrome"));
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        var login = new Button { Text = "Google-Konto anmelden", AutoSize = true };
        var field = new TextBox { Width = 310, MaxLength = 250, AccessibleName = "YouTube-Music-Titel oder Link" };
        var search = new Button { Text = "Titel suchen", AutoSize = true };
        var add = new Button { Text = "Zur Warteschlange hinzufügen", AutoSize = true };
        var results = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Title", AccessibleName = "YouTube-Music-Suchergebnisse" };
        var playback = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        AddPlaybackButton(playback, "Vorheriger Titel", "previous");
        AddPlaybackButton(playback, "Wiedergabe / Pause", "toggle-play");
        AddPlaybackButton(playback, "Nächster Titel", "next");
        tools.Controls.AddRange(new Control[] { login, open, field, search, add });
        Controls.AddRange(new Control[] { results, status, tools, playback });

        open.Click += async delegate
        {
            if (!open.Enabled) return;
            open.Enabled = false;
            status.Text = "YouTube Music wird geöffnet …";
            try { await Command("open"); }
            finally { if (!IsDisposed) open.Enabled = true; }
        };
        login.Click += async delegate
        {
            if (!login.Enabled) return;
            login.Enabled = false;
            status.Text = "Chrome für die Google-Anmeldung wird geöffnet …";
            try { await Command("login"); }
            finally { if (!IsDisposed) login.Enabled = true; }
        };
        field.TextChanged += delegate { results.Items.Clear(); };
        search.Click += async delegate
        {
            var query = field.Text.Trim();
            if (query.Length == 0 || !search.Enabled) return;
            search.Enabled = false;
            status.Text = "Suche auf YouTube Music …";
            try
            {
                var tracks = await Search(query, 12);
                if (IsDisposed || tracks == null || query != field.Text.Trim()) return;
                results.Items.Clear();
                results.Items.AddRange(tracks);
                if (results.Items.Count > 0) results.SelectedIndex = 0;
                status.Text = tracks.Length == 0 ? "Keine Treffer." : "Wähle einen Titel zum Einreihen.";
            }
            finally { if (!IsDisposed) search.Enabled = true; }
        };
        add.Click += async delegate
        {
            if (!add.Enabled) return;
            var selected = results.SelectedItem as MusicMatch;
            var id = selected == null ? VideoId(field.Text) : selected.Id;
            if (id == null) { status.Text = "Wähle einen Treffer oder füge einen YouTube-Music-Link ein."; return; }
            add.Enabled = false;
            try { await QueueLiveWish(id); }
            finally { if (!IsDisposed) add.Enabled = true; }
        };
        results.DoubleClick += delegate { add.PerformClick(); };
        field.KeyDown += (sender, args) =>
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            search.PerformClick();
        };
        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += async delegate { if (opened && Visible) await Command("status", quiet: true); };
        VisibleChanged += delegate { timer.Enabled = Visible; };
        Disposed += delegate { timer.Dispose(); };
    }

    internal async Task<MusicMatch[]> Search(string query, int limit = 3)
    {
        var result = await Command("search", query);
        if (result == null || result.state != "ok" || result.tracks == null) return null;
        return result.tracks.Where(track => track != null && VideoId(track.id) == track.id &&
            track.id != null && !string.IsNullOrWhiteSpace(track.title)).Take(limit)
            .Select(track => new MusicMatch(track.id, track.title)).ToArray();
    }

    internal async Task<bool> QueueLiveWish(string videoId)
    {
        if (videoId == null || VideoId(videoId) != videoId) return false;
        var result = await Command("queue", videoId);
        return result != null && (result.state == "ok" || result.state == "starting" || result.state == "playing");
    }

    private void AddPlaybackButton(Control row, string label, string action)
    {
        var button = new Button { Text = label, AutoSize = true };
        button.Click += async delegate { await Command(action); };
        row.Controls.Add(button);
    }

    private async Task<YouTubeChromePlayer.Response> Command(string action, string value = null, bool quiet = false)
    {
        if (!await commands.WaitAsync(quiet ? 0 : 20000)) return null;
        try
        {
            if (IsDisposed) return null;
            var response = action == "login" ? await player.OpenLogin() :
                action == "open" ? await player.Open() : await player.Run(action, value);
            if (action == "login") opened = false;
            if (action == "open") opened = response.state != "finish-login";
            if (!IsDisposed) ShowState(response, action, quiet);
            return response;
        }
        catch (Exception error)
        {
            if (!quiet) ErrorLog.Write("YouTube Music", "Chrome-Befehl " + action + " fehlgeschlagen.", error);
            if (!IsDisposed)
            {
                open.SetConnected(false, OpenLabel, ConnectedLabel);
                if (!quiet) status.Text = error is FileNotFoundException ? "Google Chrome wurde nicht gefunden." :
                    "YouTube Music antwortet gerade nicht. Öffne den Player erneut.";
            }
            return null;
        }
        finally { commands.Release(); }
    }

    private void ShowState(YouTubeChromePlayer.Response response, string action, bool quiet)
    {
        var state = response.state;
        if (state == "ready" || state == "ok" || state == "starting" || state == "playing") open.SetConnected(true, OpenLabel, ConnectedLabel);
        if (state == "closed" || state == "loading" || state == "login" || state == "finish-login" || state == "close-player" || state == "wrong-site" || state == "error")
            open.SetConnected(false, OpenLabel, ConnectedLabel);
        if (quiet && state == "ready")
        {
            if (status.Tag as string == response.account) return;
            status.Tag = response.account;
        }
        else if (quiet && state != "closed" && state != "login") return;

        switch (state)
        {
            case "ready":
                status.Tag = response.account;
                status.Text = response.account == "signed-in" ? "Google-Konto in YouTube Music angemeldet." :
                    response.account == "signed-out" ? "YouTube Music verbunden. Melde dich bei Bedarf über Google-Konto anmelden an." : "YouTube Music verbunden.";
                break;
            case "ok": status.Text = action == "queue" ? "Titel in der Warteschlange bestätigt." : "YouTube Music hat den Befehl angenommen."; break;
            case "starting": status.Text = "Titel geöffnet. Falls nötig, starte die Wiedergabe im Player."; break;
            case "playing": status.Text = "Der Titel wird in YouTube Music abgespielt."; break;
            case "closed": status.Tag = null; status.Text = "Öffne YouTube Music im Bot."; break;
            case "login": status.Tag = null; status.Text = "Bestätige die Cookie-Auswahl im Player. Für die Anmeldung nutze Google-Konto anmelden."; break;
            case "finish-login": status.Tag = null; status.Text = "Melde dich im normalen Chrome-Fenster an, schließe es und klicke dann auf YouTube Music öffnen."; break;
            case "close-player": status.Text = "Schließe das YouTube-Music-Fenster und klicke erneut auf Google-Konto anmelden."; break;
            case "loading": status.Text = "YouTube Music lädt noch."; break;
            case "no-player": status.Text = "Starte zuerst einen Titel in YouTube Music."; break;
            case "no-track": status.Text = "YouTube Music konnte den Titel nicht laden."; break;
            case "not-confirmed": status.Text = "YouTube Music hat den Titel in der Warteschlange nicht bestätigt."; break;
            default: status.Text = "YouTube Music konnte den Befehl nicht ausführen."; break;
        }
        if (!quiet && (state == "error" || state == "not-confirmed" || state == "wrong-site"))
            ErrorLog.Write("YouTube Music", "Chrome-Befehl " + action + ": " + state + ".");
    }

    internal static string VideoId(string input)
    {
        if (input == null) return null;
        var value = input.Trim();
        Uri uri;
        if (Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0)
        {
            if (uri.Host == "youtu.be") value = uri.AbsolutePath.Trim('/');
            else if ((uri.Host == "music.youtube.com" || uri.Host == "www.youtube.com" || uri.Host == "youtube.com") && uri.AbsolutePath == "/watch")
            {
                var match = Regex.Match(uri.Query, @"(?:^\?|&)v=([A-Za-z0-9_-]{11})(?:&|$)");
                value = match.Success ? match.Groups[1].Value : "";
            }
        }
        return Regex.IsMatch(value, @"\A[A-Za-z0-9_-]{11}\z") ? value : null;
    }
}
