using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class YouTubeAccount : UserControl
{
    private const string Redirect = "http://127.0.0.1:43822/";
    private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string path;
    private readonly Button connect = new Button { Text = "Konto anmelden", AutoSize = true };
    private readonly Button check = new Button { Text = "Konto prüfen", AutoSize = true };
    private readonly Label status = new Label { AutoSize = true, Text = "Google-Konto: nicht verbunden" };
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536 };
    private HttpListener activeListener;

    internal YouTubeAccount(string directory)
    {
        path = Path.Combine(directory, "youtube-google-session.txt");
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        row.Controls.AddRange(new Control[] { connect, check, status });
        Controls.Add(row);
        connect.Click += async delegate { await ChangeAccount(true); };
        check.Click += async delegate { await ChangeAccount(false); };
        VisibleChanged += async delegate { if (Visible) await ChangeAccount(false); };
    }

    private async Task ChangeAccount(bool authorize)
    {
        if (!connect.Enabled || IsDisposed) return;
        connect.Enabled = check.Enabled = false;
        try
        {
            DesktopClient desktop;
            if (authorize)
            {
                using (var picker = new OpenFileDialog { Title = "Google Desktop-OAuth-Client auswählen", Filter = "Google OAuth-Client (*.json)|*.json" })
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    if (new FileInfo(picker.FileName).Length > 65536) throw new InvalidDataException();
                    var document = json.Deserialize<ClientDocument>(File.ReadAllText(picker.FileName));
                    desktop = document == null ? null : document.installed;
                }
            }
            else
            {
                var stored = SavedCredential.Read(path);
                if (stored == null) { status.Text = "Google-Konto: nicht verbunden"; return; }
                desktop = json.Deserialize<DesktopClient>(stored);
            }
            if (desktop == null || string.IsNullOrWhiteSpace(desktop.client_id) ||
                !desktop.client_id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(desktop.client_secret))
            {
                status.Text = "Google-Client-JSON vom Typ Desktop-App erforderlich.";
                return;
            }
            var fields = new Dictionary<string, string> { { "client_id", desktop.client_id }, { "client_secret", desktop.client_secret } };
            if (authorize)
            {
                var verifier = RandomValue();
                var code = await GetCode(desktop.client_id, verifier);
                if (IsDisposed) return;
                if (string.IsNullOrEmpty(code)) { status.Text = "Google-Freigabe abgebrochen."; return; }
                fields.Add("grant_type", "authorization_code");
                fields.Add("redirect_uri", Redirect);
                fields.Add("code", code);
                fields.Add("code_verifier", verifier);
            }
            else
            {
                fields.Add("grant_type", "refresh_token");
                fields.Add("refresh_token", desktop.refresh_token);
            }
            status.Text = "Google-Konto wird geprüft …";
            TokenResponse token;
            using (var content = new FormUrlEncodedContent(fields))
            using (var response = await Client.PostAsync("https://oauth2.googleapis.com/token", content))
            {
                response.EnsureSuccessStatusCode();
                token = json.Deserialize<TokenResponse>(await response.Content.ReadAsStringAsync());
            }
            if (token == null || string.IsNullOrWhiteSpace(token.access_token)) throw new InvalidDataException();
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/channels?part=id&mine=true"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.access_token);
                using (var response = await Client.SendAsync(request)) response.EnsureSuccessStatusCode();
            }
            if (IsDisposed) return;
            if (authorize)
            {
                if (string.IsNullOrWhiteSpace(token.refresh_token)) throw new InvalidDataException();
                desktop.refresh_token = token.refresh_token;
                SavedCredential.Write(path, json.Serialize(desktop), ok =>
                    status.Text = ok ? "Google-Konto: verbunden" : "Freigabe konnte nicht gespeichert werden.");
            }
            else status.Text = "Google-Konto: verbunden";
        }
        catch (Exception error)
        {
            if (IsDisposed) return;
            status.Text = "Google-Verbindung nicht bestätigt. Details im Fehlerlog.";
            ErrorLog.Write("YouTube-Konto", "Google-Freigabe oder YouTube-API-Prüfung fehlgeschlagen.", error);
        }
        finally { if (!IsDisposed) connect.Enabled = check.Enabled = true; }
    }

    private async Task<string> GetCode(string clientId, string verifier)
    {
        var state = RandomValue();
        string challenge;
        using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        using (var listener = new HttpListener())
        {
            activeListener = listener;
            try
            {
                listener.Prefixes.Add(Redirect);
                listener.Start();
                var url = "https://accounts.google.com/o/oauth2/v2/auth?response_type=code&access_type=offline&prompt=consent" +
                    "&scope=" + Uri.EscapeDataString("https://www.googleapis.com/auth/youtube.readonly") +
                    "&client_id=" + Uri.EscapeDataString(clientId) + "&redirect_uri=" + Uri.EscapeDataString(Redirect) +
                    "&state=" + state + "&code_challenge_method=S256&code_challenge=" + challenge;
                using (var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
                status.Text = "Google-Freigabe im Browser bestätigen …";
                var deadline = Task.Delay(TimeSpan.FromMinutes(3));
                while (!IsDisposed)
                {
                    var pending = listener.GetContextAsync();
                    if (await Task.WhenAny(pending, deadline) != pending) throw new TimeoutException();
                    var context = await pending;
                    var matches = context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/" &&
                        string.Equals(context.Request.QueryString["state"], state, StringComparison.Ordinal);
                    var code = matches ? context.Request.QueryString["code"] : null;
                    context.Response.StatusCode = matches ? 200 : 400;
                    context.Response.Headers["Cache-Control"] = "no-store";
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    context.Response.Close(Encoding.UTF8.GetBytes(matches ? "Zurück zum TikTok-Bot." : "Ungültiger Rückruf."), false);
                    if (matches) return code;
                }
                return null;
            }
            finally { activeListener = null; }
        }
    }

    private static string RandomValue()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return Base64Url(bytes);
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && activeListener != null) activeListener.Close();
        base.Dispose(disposing);
    }

    private sealed class ClientDocument { public DesktopClient installed { get; set; } }
    private sealed class DesktopClient
    {
        public string client_id { get; set; }
        public string client_secret { get; set; }
        public string refresh_token { get; set; }
    }
    private sealed class TokenResponse
    {
        public string access_token { get; set; }
        public string refresh_token { get; set; }
    }
}
