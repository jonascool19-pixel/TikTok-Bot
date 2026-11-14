using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class Updates : UserControl
{
    private const string Repo = "jonascool19-pixel/TikTok-Bot";
    private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(15), MaxResponseContentBufferSize = 512 * 1024 * 1024 };
    private readonly Label status = new Label { Dock = DockStyle.Top, Height = 44 };
    private readonly Button check = new Button { Text = "Nach Updates suchen", AutoSize = true };
    private readonly Button install = new Button { Text = "Update installieren", AutoSize = true, Enabled = false };
    private Asset available;
    private readonly Version current;

    static Updates()
    {
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("TikTok-Bot-Windows");
    }

    internal Updates()
    {
        current = typeof(Updates).Assembly.GetName().Version;
        status.Text = "Installierte Version: " + current.ToString(3);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        tools.Controls.AddRange(new Control[] { check, install });
        Controls.AddRange(new Control[] { status, tools });
        check.Click += async delegate { await Check(); };
        install.Click += async delegate { await Install(); };
    }

    private async Task Check()
    {
        check.Enabled = install.Enabled = false;
        available = null;
        status.Text = "Suche nach einem Windows-Update …";
        try
        {
            string json;
            using (var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20)))
            using (var response = await Client.GetAsync("https://api.github.com/repos/" + Repo + "/releases?per_page=50", timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                json = await response.Content.ReadAsStringAsync();
            }
            var releases = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Release[]>(json);
            var latest = current;
            foreach (var release in releases)
            {
                Version version;
                if (release.draft || release.prerelease || release.tag_name == null ||
                    !System.Text.RegularExpressions.Regex.IsMatch(release.tag_name, @"\Awindows-v\d+\.\d+\.\d+\z") ||
                    !Version.TryParse(release.tag_name.Substring(9) + ".0", out version) || version <= latest) continue;
                var url = "https://github.com/" + Repo + "/releases/download/" + release.tag_name + "/TikTok-Bot-Setup.exe";
                foreach (var asset in release.assets ?? new Asset[0])
                {
                    if (asset.browser_download_url != url || asset.digest == null ||
                        !System.Text.RegularExpressions.Regex.IsMatch(asset.digest, @"\Asha256:[a-fA-F0-9]{64}\z")) continue;
                    available = asset;
                    latest = version;
                    break;
                }
            }
            status.Text = available == null ? "Kein neueres Windows-Update veröffentlicht. Installiert: " + current.ToString(3)
                : "Windows-Update " + latest.ToString(3) + " verfügbar.";
            install.Enabled = available != null;
        }
        catch (HttpRequestException error) { ReportError("Updatesuche fehlgeschlagen. ", error); }
        catch (OperationCanceledException error) { ReportError("Die Updatesuche hat zu lange gedauert.", error); }
        catch (ArgumentException error) { ReportError("GitHub hat ungültige Update-Daten geliefert.", error); }
        catch (InvalidOperationException error) { ReportError("GitHub hat ungültige Update-Daten geliefert.", error); }
        finally { check.Enabled = true; }
    }

    private async Task Install()
    {
        var asset = available;
        if (asset == null) return;
        check.Enabled = install.Enabled = false;
        status.Text = "Setup wird geladen und geprüft …";
        try
        {
            var bytes = await Client.GetByteArrayAsync(asset.browser_download_url);
            using (var sha = SHA256.Create())
                if (!("sha256:" + BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""))
                    .Equals(asset.digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException();
            if (IsDisposed) return;
            var folder = Path.Combine(Path.GetTempPath(), "TikTokBot-Update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "TikTok-Bot-Setup.exe");
            File.WriteAllBytes(file, bytes);
            var installedPath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            using (var setup = Process.Start(new ProcessStartInfo(file, "/DIR=\"" + installedPath + "\"") { UseShellExecute = true })) { }
            FindForm().Close();
        }
        catch (HttpRequestException error) { ReportError("Das Setup konnte nicht geladen werden.", error); }
        catch (OperationCanceledException error) { ReportError("Der Download hat zu lange gedauert.", error); }
        catch (InvalidDataException error) { ReportError("Die Prüfsumme des Setups stimmt nicht.", error); }
        catch (IOException error) { ReportError("Das Setup konnte nicht gespeichert werden.", error); }
        catch (UnauthorizedAccessException error) { ReportError("Das Setup konnte nicht gespeichert werden.", error); }
        catch (System.ComponentModel.Win32Exception error) { ReportError("Das Setup wurde nicht gestartet.", error); }
        finally { if (!IsDisposed) check.Enabled = install.Enabled = true; }
    }

    private void ReportError(string message, Exception error)
    {
        status.Text = message;
        ErrorLog.Write("Updates", message, error);
    }

    private sealed class Release
    {
        public string tag_name { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public Asset[] assets { get; set; }
    }

    private sealed class Asset
    {
        public string browser_download_url { get; set; }
        public string digest { get; set; }
    }
}
