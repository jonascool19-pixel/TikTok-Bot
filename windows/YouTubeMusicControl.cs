using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class YouTubeMusicControl : UserControl
{
    private const string Root = "http://127.0.0.1:26538/";
    private static readonly HttpClient Client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
    private readonly Label status = new Label { AutoSize = true, Text = "Pear Desktop: API Server auf 127.0.0.1:26538 aktivieren." };
    private string token;
    private bool busy;
    private bool useChrome;
    private readonly ConnectionButton chrome = new ConnectionButton { Text = "Mit Chrome verbinden", AutoSize = true };

    internal async Task<bool> QueueLiveWish(string videoId)
    {
        if (busy || (!useChrome && token == null) || VideoId(videoId) != videoId) return false;
        busy = true;
        try
        {
            if (useChrome)
            {
                var result = await BrowserCommand("queue", videoId);
                return result != null && (result.state == "ok" || result.state == "starting");
            }
            using (var request = new HttpRequestMessage(HttpMethod.Post, Root + "api/v1/queue"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent("{\"videoId\":\"" + videoId + "\",\"insertPosition\":\"INSERT_AT_END\"}", Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    return true;
                }
            }
        }
        catch (Exception error)
        {
            ErrorLog.Write("YouTube Music", "LIVE-Musikwunsch konnte nicht eingereiht werden.", error);
            return false;
        }
        finally { busy = false; }
    }

    internal async Task<MusicMatch[]> Search(string query, int limit = 3)
    {
        if (busy || (!useChrome && token == null)) return null;
        busy = true;
        try
        {
            if (useChrome)
            {
                var result = await BrowserCommand("search", query);
                if (result == null || result.state != "ok" || result.tracks == null) return null;
                return result.tracks.Where(track => track != null && track.id != null && VideoId(track.id) == track.id &&
                    !string.IsNullOrWhiteSpace(track.title)).Take(limit).Select(track => new MusicMatch(track.id, track.title)).ToArray();
            }
            using (var request = new HttpRequestMessage(HttpMethod.Post, Root + "api/v1/search"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(new JavaScriptSerializer().Serialize(new { query }), Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    return ParseSearch(await response.Content.ReadAsStringAsync(), limit);
                }
            }
        }
        catch (Exception error)
        {
            ErrorLog.Write("YouTube Music", "LIVE-Suche über Pear Desktop fehlgeschlagen.", error);
            return null;
        }
        finally { busy = false; }
    }

    internal static MusicMatch[] ParseSearch(string json, int limit = 3)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        return FindTracks(serializer.DeserializeObject(json)).GroupBy(track => track.Id)
            .Select(group => group.First()).Take(limit).ToArray();
    }

    private static IEnumerable<MusicMatch> FindTracks(object node)
    {
        var map = node as IDictionary<string, object>;
        if (map != null)
        {
            foreach (var pair in map)
            {
                if (pair.Key == "musicResponsiveListItemRenderer")
                {
                    var row = pair.Value as IDictionary<string, object>;
                    var id = Field(Field(row, "playlistItemData") as IDictionary<string, object>, "videoId") as string;
                    var columns = Field(row, "flexColumns") as object[];
                    if (id == null || VideoId(id) != id || columns == null || columns.Length == 0) continue;
                    var column = columns[0] as IDictionary<string, object>;
                    var renderer = Field(column, "musicResponsiveListItemFlexColumnRenderer") as IDictionary<string, object>;
                    var runs = Field(Field(renderer, "text") as IDictionary<string, object>, "runs") as object[];
                    if (runs == null) continue;
                    var title = string.Concat(runs.Select(run => Field(run as IDictionary<string, object>, "text") as string)).Trim();
                    if (title.Length > 0) yield return new MusicMatch(id, title);
                }
                else foreach (var track in FindTracks(pair.Value)) yield return track;
            }
        }
        else
        {
            var items = node as IList;
            if (items != null)
                foreach (var item in items)
                    foreach (var track in FindTracks(item)) yield return track;
        }
    }

    private static object Field(IDictionary<string, object> map, string key)
    {
        object value;
        return map != null && map.TryGetValue(key, out value) ? value : null;
    }

    internal YouTubeMusicControl(string directory, Action<bool> saved)
    {
        var path = Path.Combine(directory, "pear-session.txt");
        var playerPath = Path.Combine(directory, "youtube-music-player.txt");
        token = SavedCredential.Read(path);
        try { useChrome = File.Exists(playerPath) && File.ReadAllText(playerPath).Trim() == "chrome"; }
        catch (IOException error) { ErrorLog.Write("YouTube Music", "Player-Einstellung konnte nicht gelesen werden.", error); }
        catch (UnauthorizedAccessException error) { ErrorLog.Write("YouTube Music", "Player-Einstellung konnte nicht gelesen werden.", error); }
        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        var check = new Button { Text = "Pear Desktop verbinden / prüfen", AutoSize = true };
        var setup = new Button { Text = "Chrome-Erweiterung einrichten", AutoSize = true };
        var track = new TextBox { Width = 310, MaxLength = 250, AccessibleName = "YouTube-Music-Titel oder Link" };
        var search = new Button { Text = "Titel suchen", AutoSize = true };
        var add = new Button { Text = "Zur Desktop-Warteschlange hinzufügen", AutoSize = true };
        var results = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Title", AccessibleName = "YouTube-Music-Suchergebnisse" };
        status.AutoSize = false;
        status.Dock = DockStyle.Top;
        status.Height = 35;
        var playback = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        AddPlaybackButton(playback, "Vorheriger Titel", "previous");
        AddPlaybackButton(playback, "Wiedergabe / Pause", "toggle-play");
        AddPlaybackButton(playback, "Nächster Titel", "next");
        controls.Controls.AddRange(new Control[] { chrome, setup, check, track, search, add });
        Controls.AddRange(new Control[] { results, status, controls, playback });
        chrome.Click += async delegate
        {
            if (busy) return;
            busy = true;
            try
            {
                status.Text = "Prüfe die Chrome-Verbindung …";
                var result = await BrowserCommand("status");
                if (result != null && result.state == "no-tab" && !IsDisposed)
                {
                    OpenChrome("https://music.youtube.com/");
                    await Task.Delay(2000);
                    result = await BrowserCommand("status");
                }
                if (IsDisposed || result == null || result.state != "ready") return;
                if (!useChrome) ChatReplies.Persist(playerPath, "chrome", saved);
                useChrome = true;
            }
            catch (Exception error)
            {
                if (!IsDisposed) status.Text = "Chrome konnte nicht geöffnet werden.";
                ErrorLog.Write("YouTube Music", "Chrome konnte nicht geöffnet werden.", error);
            }
            finally { busy = false; }
        };
        setup.Click += delegate
        {
            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "youtube-extension");
            try
            {
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
                Clipboard.SetText(folder);
                OpenChrome("chrome://extensions/");
                status.Text = "Aktiviere den Entwicklermodus und wähle Entpackte Erweiterung laden. Der Ordner liegt in der Zwischenablage.";
            }
            catch (Exception error)
            {
                status.Text = "Öffne chrome://extensions/ und lade diesen Ordner: " + folder;
                ErrorLog.Write("YouTube Music", "Chrome-Einrichtung konnte nicht geöffnet werden.", error);
            }
        };
        check.Click += async delegate
        {
            if (!await Execute(controls, null, path, saved) || IsDisposed) return;
            if (useChrome) ChatReplies.Persist(playerPath, "pear", saved);
            useChrome = false;
            chrome.SetConnected(false, "Mit Chrome verbinden", "Chrome verbunden");
        };
        var timer = new Timer { Interval = 15000 };
        timer.Tick += async delegate
        {
            if (!Visible || !useChrome || busy) return;
            busy = true;
            try { await BrowserCommand("status", quiet: true); }
            finally { busy = false; }
        };
        VisibleChanged += delegate { timer.Enabled = Visible; };
        Disposed += delegate { timer.Dispose(); };
        track.TextChanged += delegate { results.Items.Clear(); };
        search.Click += async delegate
        {
            var query = track.Text.Trim();
            if (query.Length == 0 || busy) return;
            if (!useChrome && token == null) await Execute(controls, null, path, saved);
            if (IsDisposed || (!useChrome && token == null) || query != track.Text.Trim()) return;
            status.Text = "Suche auf YouTube Music …";
            var tracks = await Search(query, 12);
            if (IsDisposed || query != track.Text.Trim()) return;
            results.Items.Clear();
            if (tracks == null)
            {
                if (!useChrome) status.Text = "Suche fehlgeschlagen. Prüfe die Pear-Desktop-Verbindung.";
                return;
            }
            results.Items.AddRange(tracks);
            if (results.Items.Count > 0) results.SelectedIndex = 0;
            status.Text = tracks.Length == 0 ? "Keine Treffer." : "Wähle einen Titel zum Einreihen.";
        };
        add.Click += async delegate
        {
            var selected = results.SelectedItem as MusicMatch;
            var id = selected == null ? VideoId(track.Text) : selected.Id;
            if (id == null) { status.Text = "Wähle einen Treffer oder füge einen YouTube-Music-Link ein."; return; }
            if (useChrome) await QueueLiveWish(id);
            else await Execute(controls, id, path, saved);
        };
        results.DoubleClick += delegate { add.PerformClick(); };
        track.KeyDown += (sender, args) =>
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            search.PerformClick();
        };
    }

    private static void OpenChrome(string url)
    {
        using (Process.Start(new ProcessStartInfo("chrome.exe", url) { UseShellExecute = true })) { }
    }

    private async Task<YouTubeBrowserBridge.BrowserResponse> BrowserCommand(string action, string value = null, bool quiet = false)
    {
        YouTubeBrowserBridge.BrowserResponse response = null;
        try { response = await YouTubeBrowserBridge.Send(action, value); }
        catch (Exception error) { if (!quiet) ErrorLog.Write("YouTube Music", "Chrome-Anfrage fehlgeschlagen.", error); }
        if (IsDisposed) return response;
        var state = response == null ? null : response.state;
        var connected = state == "ready" || state == "ok" || state == "starting";
        if (connected || state == null || state == "no-tab" || state == "loading" || state == "wrong-site" || state == "error")
            chrome.SetConnected(connected, "Mit Chrome verbinden", "Chrome verbunden");
        if (quiet) return response;
        switch (state)
        {
            case "ready":
                status.Text = response.account == "signed-out" ? "Chrome verbunden. Melde dich bei Bedarf in YouTube Music an." :
                    response.account == "signed-in" ? "Chrome verbunden. Google-Konto in YouTube Music angemeldet." : "Chrome verbunden.";
                break;
            case "ok": status.Text = action == "queue" ? "Titel in der Chrome-Warteschlange bestätigt." : "YouTube Music hat den Befehl angenommen."; break;
            case "starting": status.Text = "Titel in Chrome geöffnet. Falls nötig, starte die Wiedergabe im Player."; break;
            case "no-tab": status.Text = "Öffne YouTube Music in Chrome."; break;
            case "loading": status.Text = "YouTube Music lädt noch. Prüfe die Verbindung erneut."; break;
            case "no-player": status.Text = "Starte zuerst einen Titel in YouTube Music."; break;
            case "no-track": status.Text = "YouTube Music konnte den Titel nicht laden."; break;
            case "not-confirmed": status.Text = "YouTube Music hat den Titel in der Warteschlange nicht bestätigt."; break;
            case null: status.Text = "Chrome antwortet nicht. Aktiviere die mitgelieferte Erweiterung im Chrome-Profil."; break;
            default: status.Text = "YouTube Music konnte den Befehl in Chrome nicht ausführen."; break;
        }
        if (!connected && state != "no-tab" && state != "loading")
            ErrorLog.Write("YouTube Music", "Chrome-Befehl " + action + ": " + (state ?? "keine Verbindung") + ".");
        return response;
    }

    private void AddPlaybackButton(Control row, string label, string route)
    {
        var button = new Button { Text = label, AutoSize = true };
        button.Click += async delegate
        {
            if (busy) return;
            if (useChrome)
            {
                busy = true;
                try { await BrowserCommand(route); }
                finally { busy = false; }
                return;
            }
            if (token == null) { status.Text = "Verbinde zuerst Pear Desktop."; return; }
            busy = true;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, Root + "api/v1/" + route))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using (var response = await Client.SendAsync(request))
                    {
                        if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403) token = null;
                        response.EnsureSuccessStatusCode();
                    }
                }
                if (!IsDisposed) status.Text = "Pear Desktop hat den Wiedergabebefehl angenommen.";
            }
            catch (Exception error)
            {
                if (!IsDisposed) status.Text = "Wiedergabebefehl fehlgeschlagen. Prüfe die Verbindung.";
                ErrorLog.Write("YouTube Music", "Pear Desktop konnte die Wiedergabe nicht steuern.", error);
            }
            finally { busy = false; }
        };
        row.Controls.Add(button);
    }

    private async Task<bool> Execute(Control controls, string videoId, string path, Action<bool> saved)
    {
        if (busy) return false;
        busy = true;
        controls.Enabled = false;
        try
        {
            if (token == null)
            {
                status.Text = "Bestätige die Verbindung in Pear Desktop.";
                using (var response = await Client.PostAsync(Root + "auth/TikTok-Bot", null))
                {
                    response.EnsureSuccessStatusCode();
                    var login = new JavaScriptSerializer().Deserialize<Login>(await response.Content.ReadAsStringAsync());
                    if (login == null || string.IsNullOrWhiteSpace(login.accessToken)) throw new InvalidDataException();
                    token = login.accessToken;
                    SavedCredential.Write(path, token, saved);
                }
            }
            using (var request = new HttpRequestMessage(videoId == null ? HttpMethod.Get : HttpMethod.Post, Root + "api/v1/queue"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (videoId != null) request.Content = new StringContent("{\"videoId\":\"" + videoId + "\",\"insertPosition\":\"INSERT_AT_END\"}", Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request))
                {
                    if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
                    {
                        token = null;
                        status.Text = "Pear Desktop möchte eine erneute Verbindung. Klicke auf Prüfen.";
                        return false;
                    }
                    response.EnsureSuccessStatusCode();
                    status.Text = videoId == null ? "Pear Desktop verbunden." : "Pear Desktop hat den Titel angenommen.";
                    return true;
                }
            }
        }
        catch (Exception error)
        {
            status.Text = "Pear Desktop nicht erreichbar. Prüfe, ob der lokale API Server läuft.";
            ErrorLog.Write("YouTube Music", "Pear-Desktop-Anfrage fehlgeschlagen.", error);
            return false;
        }
        finally { busy = false; if (!IsDisposed) controls.Enabled = true; }
    }

    internal static string VideoId(string input)
    {
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

    private sealed class Login { public string accessToken { get; set; } }
}
