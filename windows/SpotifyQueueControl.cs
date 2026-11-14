using System;
using System.Diagnostics;
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
    private readonly Button add = new Button { Text = "In Spotify einreihen", AutoSize = true, Enabled = false };
    private string accessToken;
    private DateTime expires;

    internal SpotifyQueueControl()
    {
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };
        layout.Controls.AddRange(new Control[] { status, new Label { Text = "Spotify Client-ID", AutoSize = true }, clientId, connect,
            new Label { Text = "Spotify-Titellink oder URI", AutoSize = true }, track, add });
        Controls.Add(layout);
        connect.Click += async delegate { await Connect(); };
        add.Click += async delegate { await Enqueue(); };
    }

    private async Task Connect()
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
                }
                status.Text = "Spotify verbunden. Du kannst einen Titel einreihen.";
                add.Enabled = true;
            }
        }
        catch (Exception error) { Report("Spotify-Anmeldung fehlgeschlagen. Prüfe Client-ID und Redirect-URI.", error); }
        finally { if (!IsDisposed) connect.Enabled = true; }
    }

    private async Task Enqueue()
    {
        var uri = TrackUri(track.Text);
        if (uri == null) { status.Text = "Gib einen Spotify-Link zu einem Titel oder eine spotify:track-URI ein."; return; }
        if (string.IsNullOrEmpty(accessToken) || DateTime.UtcNow >= expires)
        { status.Text = "Bitte Spotify erneut verbinden."; add.Enabled = false; return; }
        add.Enabled = connect.Enabled = false;
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.spotify.com/v1/me/player/queue?uri=" + Uri.EscapeDataString(uri)))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using (var response = await Client.SendAsync(request))
                {
                    if (response.IsSuccessStatusCode) status.Text = "Titel in die Spotify-Warteschlange aufgenommen.";
                    else if ((int)response.StatusCode == 404) status.Text = "In Spotify zuerst eine Wiedergabe auf deinem Gerät starten.";
                    else if ((int)response.StatusCode == 401) { accessToken = null; status.Text = "Bitte Spotify erneut verbinden."; }
                    else status.Text = "Spotify hat den Titel abgelehnt (HTTP " + (int)response.StatusCode + ").";
                }
            }
        }
        catch (HttpRequestException error) { Report("Spotify-Warteschlange nicht erreichbar.", error); }
        catch (OperationCanceledException error) { Report("Spotify hat nicht rechtzeitig geantwortet.", error); }
        finally { if (!IsDisposed) { connect.Enabled = true; add.Enabled = accessToken != null; } }
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
        public int expires_in { get; set; }
    }
}
