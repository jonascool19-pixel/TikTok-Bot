using System;
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

    internal YouTubeMusicControl(string directory, Action<bool> saved)
    {
        var path = Path.Combine(directory, "pear-session.txt");
        token = SavedCredential.Read(path);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var check = new Button { Text = "YouTube Music Desktop prüfen", AutoSize = true };
        var track = new TextBox { Width = 310, MaxLength = 250, AccessibleName = "YouTube Music Titellink" };
        var add = new Button { Text = "In Desktop-Warteschlange", AutoSize = true };
        controls.Controls.AddRange(new Control[] { check, track, add, status });
        Controls.Add(controls);
        check.Click += async delegate { await Execute(controls, null, path, saved); };
        add.Click += async delegate
        {
            var id = VideoId(track.Text);
            if (id == null) { status.Text = "Gib einen YouTube- oder YouTube-Music-Titellink ein."; return; }
            await Execute(controls, id, path, saved);
        };
    }

    private async Task Execute(Control controls, string videoId, string path, Action<bool> saved)
    {
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
                        return;
                    }
                    response.EnsureSuccessStatusCode();
                    status.Text = videoId == null ? "Pear Desktop verbunden." : "Pear Desktop hat den Titel angenommen.";
                }
            }
        }
        catch (Exception error)
        {
            status.Text = "Pear Desktop nicht erreichbar. Prüfe, ob der lokale API Server läuft.";
            ErrorLog.Write("YouTube Music", "Pear-Desktop-Anfrage fehlgeschlagen.", error);
        }
        finally { if (!IsDisposed) controls.Enabled = true; }
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
