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
    private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
    private readonly Label status = new Label { Dock = DockStyle.Top, Height = 44 };
    private readonly Button check = new Button { Text = "Nach Updates suchen", AutoSize = true };
    private readonly Button install = new Button { Text = "Update installieren", AutoSize = true, Enabled = false };
    private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Top, Height = 20, Visible = false };
    private Asset available;
    private readonly Version current;
    private Version notified;
    internal event Action<string> UpdateAvailable;

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
        Controls.AddRange(new Control[] { progress, status, tools });
        check.Click += async delegate { await Check(); };
        install.Click += async delegate { await Install(); };
    }

    internal async Task Check()
    {
        if (IsDisposed || !check.Enabled) return;
        check.Enabled = install.Enabled = false;
        available = null;
        status.Text = "Suche nach einem Windows-Update …";
        try
        {
            Asset release;
            using (var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20)))
            using (var response = await Client.GetAsync("https://github.com/" + Repo + "/releases/latest/download/windows-update.json", timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                release = new JavaScriptSerializer { MaxJsonLength = 8192 }.Deserialize<Asset>(await response.Content.ReadAsStringAsync());
            }
            var latest = ValidateRelease(release);
            if (latest > current) available = release;
            status.Text = available == null ? "Kein neueres Windows-Update veröffentlicht. Installiert: " + current.ToString(3)
                : "Windows-Update " + latest.ToString(3) + " verfügbar.";
            install.Enabled = available != null;
            if (available != null && latest != notified)
            {
                notified = latest;
                if (UpdateAvailable != null) UpdateAvailable(latest.ToString(3));
            }
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
        status.Text = "Download wird vorbereitet …";
        progress.Style = ProgressBarStyle.Marquee;
        progress.Visible = true;
        string folder = null;
        string file = null;
        var setupStarted = false;
        try
        {
            folder = Path.Combine(Path.GetTempPath(), "TikTokBot-Update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            file = Path.Combine(folder, "TikTok-Bot-Setup.exe");
            using (var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(15)))
            using (var response = await Client.GetAsync(asset.browser_download_url, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                var total = asset.size > 0 ? asset.size : response.Content.Headers.ContentLength.GetValueOrDefault();
                if (total > 0)
                {
                    progress.Style = ProgressBarStyle.Continuous;
                    progress.Value = 0;
                    status.Text = "Download: 0 %";
                }
                var watch = Stopwatch.StartNew();
                long received = 0;
                long updatedAt = -250;
                var buffer = new byte[128 * 1024];
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var destination = File.Create(file))
                {
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) != 0)
                    {
                        await destination.WriteAsync(buffer, 0, read, timeout.Token);
                        received += read;
                        if (received > total) throw new InvalidDataException();
                        if (IsDisposed) return;
                        if (watch.ElapsedMilliseconds - updatedAt < 250 && received != total) continue;
                        updatedAt = watch.ElapsedMilliseconds;
                        if (total > 0)
                        {
                            var percent = (int)Math.Min(100, received * 100.0 / total);
                            if (progress.Style != ProgressBarStyle.Continuous) progress.Style = ProgressBarStyle.Continuous;
                            progress.Value = percent;
                            var eta = watch.Elapsed.TotalSeconds >= 2 && received > 0 ?
                                TimeSpan.FromSeconds(Math.Min(86400, Math.Max(0, (total - received) * watch.Elapsed.TotalSeconds / received))) : (TimeSpan?)null;
                            status.Text = "Download: " + percent + " % · " + (eta.HasValue ? "noch ca. " + eta.Value.ToString(@"hh\:mm\:ss") : "Restzeit wird berechnet");
                        }
                        else status.Text = "Download: " + (received / 1048576.0).ToString("0.0") + " MiB übertragen.";
                    }
                    if (received != total) throw new InvalidDataException();
                }
            }
            status.Text = "Download: 100 % · Prüfsumme wird geprüft …";
            progress.Style = ProgressBarStyle.Marquee;
            var digest = await Task.Run(() =>
            {
                using (var sha = SHA256.Create())
                using (var input = File.OpenRead(file))
                    return "sha256:" + BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
            });
            if (!digest.Equals(asset.digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            if (IsDisposed) return;
            var installedPath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            using (var setup = Process.Start(new ProcessStartInfo(file,
                "/SILENT /SP- /NORESTART /UPDATEBOT=1 /DIR=\"" + installedPath + "\"") { UseShellExecute = true }))
            {
                if (setup == null) throw new InvalidOperationException("Setup-Prozess fehlt.");
                setupStarted = true;
            }
            FindForm().Close();
        }
        catch (HttpRequestException error) { ReportError("Das Setup konnte nicht geladen werden.", error); }
        catch (OperationCanceledException error) { ReportError("Der Download hat zu lange gedauert.", error); }
        catch (InvalidDataException error) { ReportError("Der Download ist unvollständig oder die Prüfsumme stimmt nicht.", error); }
        catch (IOException error) { ReportError("Das Setup konnte nicht gespeichert werden.", error); }
        catch (UnauthorizedAccessException error) { ReportError("Das Setup konnte nicht gespeichert werden.", error); }
        catch (System.ComponentModel.Win32Exception error) { ReportError("Das Setup wurde nicht gestartet.", error); }
        catch (InvalidOperationException error) { ReportError("Das Setup wurde nicht gestartet.", error); }
        finally
        {
            if (!setupStarted && folder != null)
            {
                try
                {
                    if (file != null) File.Delete(file);
                    if (Directory.Exists(folder)) Directory.Delete(folder);
                }
                catch (IOException error) { ErrorLog.Write("Updates", "Temporärer Download konnte nicht entfernt werden.", error); }
                catch (UnauthorizedAccessException error) { ErrorLog.Write("Updates", "Temporärer Download konnte nicht entfernt werden.", error); }
            }
            if (!IsDisposed)
            {
                progress.Visible = false;
                check.Enabled = install.Enabled = true;
            }
        }
    }

    private void ReportError(string message, Exception error)
    {
        status.Text = message;
        ErrorLog.Write("Updates", message, error);
    }

    internal static Version ValidateRelease(Asset release)
    {
        Version version;
        if (release == null || release.version == null ||
            !System.Text.RegularExpressions.Regex.IsMatch(release.version, @"\A\d+\.\d+\.\d+\z") ||
            !Version.TryParse(release.version + ".0", out version) || release.size <= 0 || release.digest == null ||
            !System.Text.RegularExpressions.Regex.IsMatch(release.digest, @"\Asha256:[a-fA-F0-9]{64}\z"))
            throw new InvalidOperationException("Ungültige Angaben in windows-update.json.");
        release.browser_download_url = "https://github.com/" + Repo + "/releases/download/windows-v" + version.ToString(3) + "/TikTok-Bot-Setup.exe";
        return version;
    }

    internal sealed class Asset
    {
        public string version { get; set; }
        public string browser_download_url { get; set; }
        public string digest { get; set; }
        public long size { get; set; }
    }
}
