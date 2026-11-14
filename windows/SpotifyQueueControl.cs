using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class SpotifyQueueControl : UserControl
{
    private const string Redirect = "http://127.0.0.1:43821/spotify/";
    private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    private readonly TextBox clientId = new TextBox { Width = 240, MaxLength = 32, AccessibleName = "Spotify Client-ID" };
    private readonly TextBox track = new TextBox { Width = 340, MaxLength = 250, AccessibleName = "Spotify-Titellink oder URI" };
    private readonly Label status = new Label { AutoSize = true, Text = "Spotify Premium und eine Spotify-App mit Redirect-URI " + Redirect + " erforderlich." };
    private readonly Button connect = new Button { Text = "Spotify verbinden", AutoSize = true };
    private readonly Button add = new Button { Text = "Zur Spotify-Warteschlange hinzufügen", AutoSize = true, Enabled = false };
    private readonly ComboBox results = new ThemeComboBox { Width = 450, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Spotify-Suchergebnisse" };
    private readonly string sessionPath;
    private readonly Action<bool> saved;
    private string accessToken;
    private string refreshToken;
    private string authorizedClient;
    private DateTime expires;

    internal SpotifyQueueControl(string directory, Action<bool> saved)
    {
        var path = Path.Combine(directory, "spotify-session.txt");
        sessionPath = path;
        this.saved = saved;
        var stored = SavedCredential.Read(path);
        if (stored != null)
        {
            var parts = stored.Split(new[] { '\n' }, 2);
            if (parts.Length == 2 && Regex.IsMatch(parts[0], @"\A[0-9a-fA-F]{32}\z") && parts[1].Length > 0)
            {
                authorizedClient = clientId.Text = parts[0];
                refreshToken = parts[1];
                add.Enabled = true;
                status.Text = "Spotify-Verbindung gespeichert. Sie wird beim Einreihen erneuert.";
            }
        }
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };
        var search = new Button { Text = "Titel suchen", AutoSize = true };
        layout.Controls.AddRange(new Control[] { status, new Label { Text = "Spotify Client-ID", AutoSize = true }, clientId, connect,
            new Label { Text = "Titel, Künstler oder Spotify-Link", AutoSize = true }, track, search, results, add });
        Controls.Add(layout);
        connect.Click += async delegate { await Connect(path, saved); };
        search.Click += async delegate { await ShowSearch(track.Text); };
        track.TextChanged += delegate { results.Items.Clear(); };
        add.Click += async delegate
        {
            var selected = results.SelectedItem as Track;
            var uri = selected == null ? TrackUri(track.Text) : selected.Uri;
            if (uri != null) await Enqueue(uri);
            else await ShowSearch(track.Text);
        };
    }

    private async Task Connect(string path, Action<bool> saved)
    {
        var id = clientId.Text.Trim();
        if (!Regex.IsMatch(id, @"\A[0-9a-fA-F]{32}\z"))
        {
            status.Text = "Gib die Client-ID aus dem Spotify-Entwicklerdashboard ein.";
            return;
        }
        connect.Enabled = add.Enabled = false;
        accessToken = null;
        try
        {
            var verifier = RandomValue();
            var state = RandomValue();
            string challenge;
            using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(Redirect);
                listener.Start();
                var login = "https://accounts.spotify.com/authorize?response_type=code&scope=user-modify-playback-state&code_challenge_method=S256" +
                    "&client_id=" + id + "&redirect_uri=" + Uri.EscapeDataString(Redirect) + "&state=" + state + "&code_challenge=" + challenge;
                using (var browser = Process.Start(new ProcessStartInfo(login) { UseShellExecute = true })) { }
                status.Text = "Bestätige Spotify im Standardbrowser.";
                var pending = listener.GetContextAsync();
                if (await Task.WhenAny(pending, Task.Delay(TimeSpan.FromMinutes(2))) != pending) throw new TimeoutException();
                var request = await pending;
                var code = request.Request.QueryString["code"];
                var valid = request.Request.QueryString["state"] == state && !string.IsNullOrEmpty(code);
                var page = Encoding.UTF8.GetBytes(valid ? "Zurück zum TikTok-Bot." : "Spotify-Anmeldung abgebrochen.");
                request.Response.ContentType = "text/plain; charset=utf-8";
                request.Response.Close(page, false);
                if (!valid) { status.Text = "Spotify-Anmeldung abgebrochen."; return; }
                var fields = new System.Collections.Generic.Dictionary<string, string> {
                    { "grant_type", "authorization_code" }, { "client_id", id }, { "code", code },
                    { "redirect_uri", Redirect }, { "code_verifier", verifier }
                };
                using (var content = new FormUrlEncodedContent(fields))
                using (var response = await Client.PostAsync("https://accounts.spotify.com/api/token", content))
                {
                    response.EnsureSuccessStatusCode();
                    var token = new JavaScriptSerializer().Deserialize<Token>(await response.Content.ReadAsStringAsync());
                    accessToken = token.access_token;
                    expires = DateTime.UtcNow.AddSeconds(token.expires_in - 10);
                    authorizedClient = id;
                    refreshToken = token.refresh_token;
                    if (!string.IsNullOrEmpty(refreshToken)) SavedCredential.Write(path, id + "\n" + refreshToken, saved);
                }
                status.Text = "Spotify verbunden. Du kannst einen Titel einreihen.";
                add.Enabled = true;
            }
        }
        catch (Exception error) { Report("Spotify-Anmeldung fehlgeschlagen. Prüfe Client-ID und Redirect-URI.", error); }
        finally { if (!IsDisposed) { connect.Enabled = true; add.Enabled = accessToken != null || refreshToken != null; } }
    }

    internal async Task ShowSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        track.Text = query.Trim();
        var matches = await Search(query);
        if (IsDisposed || track.Text != query.Trim() || matches == null) return;
        results.Items.Clear();
        results.Items.AddRange(matches);
        if (matches.Length > 0) results.SelectedIndex = 0;
        status.Text = matches.Length == 0 ? "Keine Spotify-Titel gefunden." : "Wähle einen Titel und füge ihn zur Warteschlange hinzu.";
    }

    internal async Task<Track[]> Search(string query)
    {
        if (!connect.Enabled) return null;
        add.Enabled = connect.Enabled = false;
        try
        {
            if (!await EnsureToken()) return null;
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/search?type=track&limit=10&q=" + Uri.EscapeDataString(query)))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using (var response = await Client.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized) accessToken = null;
                        status.Text = "Spotify-Suche fehlgeschlagen (HTTP " + (int)response.StatusCode + ").";
                        return null;
                    }
                    return ParseTracks(await response.Content.ReadAsStringAsync());
                }
            }
        }
        catch (Exception error) { Report("Spotify-Suche fehlgeschlagen.", error); return null; }
        finally { RestoreButtons(); }
    }

    internal static Track[] ParseTracks(string json)
    {
        var response = new JavaScriptSerializer().Deserialize<SearchResponse>(json);
        return (response == null || response.tracks == null || response.tracks.items == null ? new Track[0] : response.tracks.items)
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.name) && item.is_playable != false &&
                item.uri != null && TrackUri(item.uri) == item.uri)
            .GroupBy(item => item.uri).Select(group => group.First()).ToArray();
    }

    private void RestoreButtons()
    {
        if (!IsDisposed) { connect.Enabled = true; add.Enabled = accessToken != null || refreshToken != null; }
    }

    private async Task<bool> EnsureToken()
    {
        if (accessToken != null && DateTime.UtcNow < expires) return true;
        if (refreshToken == null) { status.Text = "Bitte Spotify verbinden."; return false; }
        var fields = new Dictionary<string, string> {
            { "grant_type", "refresh_token" }, { "client_id", authorizedClient }, { "refresh_token", refreshToken }
        };
        using (var content = new FormUrlEncodedContent(fields))
        using (var response = await Client.PostAsync("https://accounts.spotify.com/api/token", content))
        {
            if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized)
            { status.Text = "Spotify verlangt eine erneute Kontofreigabe."; return false; }
            response.EnsureSuccessStatusCode();
            var token = new JavaScriptSerializer().Deserialize<Token>(await response.Content.ReadAsStringAsync());
            if (token == null || string.IsNullOrEmpty(token.access_token)) throw new InvalidOperationException();
            accessToken = token.access_token;
            expires = DateTime.UtcNow.AddSeconds(token.expires_in - 10);
            if (!string.IsNullOrEmpty(token.refresh_token) && token.refresh_token != refreshToken)
            {
                refreshToken = token.refresh_token;
                SavedCredential.Write(sessionPath, authorizedClient + "\n" + refreshToken, saved);
            }
            return true;
        }
    }

    internal async Task<bool> Enqueue(string uri)
    {
        if (!connect.Enabled || uri == null) return false;
        add.Enabled = connect.Enabled = false;
        try
        {
            if (!await EnsureToken()) return false;
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.spotify.com/v1/me/player/queue?uri=" + Uri.EscapeDataString(uri)))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using (var response = await Client.SendAsync(request))
                {
                    if (response.IsSuccessStatusCode) status.Text = "Titel in die Spotify-Warteschlange aufgenommen.";
                    else if ((int)response.StatusCode == 404) status.Text = "In Spotify zuerst eine Wiedergabe auf deinem Gerät starten.";
                    else if ((int)response.StatusCode == 401) { accessToken = null; status.Text = "Bitte Spotify erneut verbinden."; }
                    else status.Text = "Spotify hat den Titel abgelehnt (HTTP " + (int)response.StatusCode + ").";
                    return response.IsSuccessStatusCode;
                }
            }
        }
        catch (HttpRequestException error) { Report("Spotify-Warteschlange nicht erreichbar.", error); }
        catch (OperationCanceledException error) { Report("Spotify hat nicht rechtzeitig geantwortet.", error); }
        catch (ArgumentException error) { Report("Spotify hat ungültige Kontodaten geliefert.", error); }
        catch (InvalidOperationException error) { Report("Spotify hat ungültige Kontodaten geliefert.", error); }
        finally { RestoreButtons(); }
        return false;
    }

    internal static string TrackUri(string input)
    {
        var value = input.Trim();
        var match = Regex.Match(value, @"\Aspotify:track:([A-Za-z0-9]{22})\z");
        Uri url;
        if (!match.Success && Uri.TryCreate(value, UriKind.Absolute, out url) && url.Scheme == "https" &&
            url.IsDefaultPort && url.Host == "open.spotify.com" && url.UserInfo.Length == 0)
            match = Regex.Match(url.AbsolutePath, @"\A/(?:intl-[a-zA-Z-]+/)?track/([A-Za-z0-9]{22})/?\z");
        return match.Success ? "spotify:track:" + match.Groups[1].Value : null;
    }

    private void Report(string message, Exception error)
    {
        if (!IsDisposed) status.Text = message;
        ErrorLog.Write("Spotify", message, error);
    }

    private static string RandomValue()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return Base64Url(bytes);
    }

    private static string Base64Url(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class Token
    {
        public string access_token { get; set; }
        public string refresh_token { get; set; }
        public int expires_in { get; set; }
    }

    internal sealed class Track
    {
        public string uri { get; set; }
        public string name { get; set; }
        public Artist[] artists { get; set; }
        public bool? is_playable { get; set; }
        internal string Uri { get { return uri; } }
        public override string ToString() { return name + (artists == null || artists.Length == 0 ? "" : " – " + string.Join(", ", artists.Where(a => a != null).Select(a => a.name))); }
    }

    public sealed class Artist { public string name { get; set; } }
    private sealed class TrackPage { public Track[] items { get; set; } }
    private sealed class SearchResponse { public TrackPage tracks { get; set; } }
}
