using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media;
using System.Threading.Tasks;

internal sealed class MusicQueue : UserControl
{
    private readonly MediaPlayer player = new MediaPlayer();
    private readonly ListBox queue = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Musikwarteschlange" };
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 28, Text = "Keine Wiedergabe" };
    private bool playing;
    private readonly Label spotifyStatus = new Label { Dock = DockStyle.Bottom, Height = 27, Text = "Spotify Desktop" };
    private readonly ListBox requests = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Musikwünsche" };
    private readonly CheckBox collect = new CheckBox { Text = "!song und !wunsch aus dem LIVE-Chat übernehmen", AutoSize = true };

    internal MusicQueue()
    {
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        AddButton(tools, "Dateien hinzufügen", AddFiles);
        AddButton(tools, "Abspielen", delegate { if (!playing) Next(); });
        AddButton(tools, "Überspringen", Next);
        AddButton(tools, "Stopp", Stop);
        AddButton(tools, "Auswahl entfernen", delegate
        {
            if (queue.SelectedIndex >= 0) queue.Items.RemoveAt(queue.SelectedIndex);
        });
        var volume = new NumericUpDown { Maximum = 100, Value = 35, Width = 55, AccessibleName = "Musiklautstärke" };
        tools.Controls.Add(new Label { Text = "Lautstärke %", AutoSize = true });
        tools.Controls.Add(volume);
        player.Volume = (double)volume.Value / 100;
        volume.ValueChanged += delegate { player.Volume = (double)volume.Value / 100; };
        player.MediaEnded += delegate { if (playing) Next(); };
        player.MediaFailed += delegate
        {
            if (!playing) return;
            Stop();
            status.Text = "Datei konnte nicht abgespielt werden. Wähle Abspielen für den nächsten Titel.";
        };
        queue.DisplayMember = "Name";
        var spotify = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        AddButton(spotify, "Spotify prüfen", () => ControlSpotify(spotify, "Status"));
        AddButton(spotify, "Spotify: Wiedergabe / Pause", () => ControlSpotify(spotify, "Toggle"));
        AddButton(spotify, "Vorheriger Titel", () => ControlSpotify(spotify, "Previous"));
        AddButton(spotify, "Nächster Titel", () => ControlSpotify(spotify, "Next"));
        var searchTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        var query = new TextBox { Width = 220, MaxLength = 150, AccessibleName = "Titel oder Interpret" };
        searchTools.Controls.Add(query);
        AddButton(searchTools, "Auf Spotify suchen", () => Search(query.Text));
        AddButton(searchTools, "Wunsch öffnen", () => Search(requests.SelectedItem as string));
        AddButton(searchTools, "Wunsch entfernen", delegate { if (requests.SelectedIndex >= 0) requests.Items.RemoveAt(requests.SelectedIndex); });
        searchTools.Controls.Add(collect);
        requests.DoubleClick += delegate { Search(requests.SelectedItem as string); };
        var sources = new TabControl { Dock = DockStyle.Fill };
        var local = new TabPage("Lokale Dateien");
        local.Controls.AddRange(new Control[] { queue, status, tools });
        var remote = new TabPage("Spotify und Musikwünsche");
        remote.Controls.AddRange(new Control[] { requests, searchTools, spotify, spotifyStatus });
        sources.TabPages.AddRange(new[] { local, remote });
        Controls.Add(sources);
    }

    internal void ConsiderRequest(string message)
    {
        if (!collect.Checked || requests.Items.Count >= 100) return;
        var match = System.Text.RegularExpressions.Regex.Match(message, @"\A!(?:song|wunsch)\s+(.{2,150})\z",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return;
        var title = match.Groups[1].Value.Trim();
        if (title.Length >= 2 && !requests.Items.Contains(title)) requests.Items.Add(title);
    }

    private void Search(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        try
        {
            var url = "https://open.spotify.com/search/" + Uri.EscapeDataString(title.Trim());
            using (var window = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
            spotifyStatus.Text = "Spotify-Suche wurde geöffnet.";
        }
        catch (System.ComponentModel.Win32Exception) { spotifyStatus.Text = "Spotify-Suche konnte nicht geöffnet werden."; }
    }

    private async void ControlSpotify(Control buttons, string action)
    {
        buttons.Enabled = false;
        try
        {
            var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpotifyControl.ps1");
            var shell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var result = await Task.Run(() =>
            {
                using (var process = Process.Start(new ProcessStartInfo(shell, "-NoProfile -NonInteractive -File \"" + script + "\" -Action " + action)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    if (process.WaitForExit(8000)) return process.ExitCode;
                    process.Kill();
                    return 4;
                }
            });
            if (IsDisposed) return;
            switch (result)
            {
                case 0: spotifyStatus.Text = action == "Status" ? "Spotify Desktop verbunden." : "Spotify hat den Befehl angenommen."; break;
                case 2: spotifyStatus.Text = "Starte Spotify Desktop und spiele dort einen Titel an."; break;
                case 3: spotifyStatus.Text = "Spotify unterstützt diesen Befehl für den aktuellen Titel nicht."; break;
                default: spotifyStatus.Text = "Windows-Mediensteuerung nicht verfügbar."; break;
            }
        }
        catch (System.ComponentModel.Win32Exception) { if (!IsDisposed) spotifyStatus.Text = "Windows-Mediensteuerung nicht verfügbar."; }
        finally { if (!IsDisposed) buttons.Enabled = true; }
    }

    private static void AddButton(Control tools, string caption, Action action)
    {
        var button = new Button { Text = caption, AutoSize = true };
        button.Click += delegate { action(); };
        tools.Controls.Add(button);
    }

    private void AddFiles()
    {
        using (var picker = new OpenFileDialog { Multiselect = true, Filter = "Audio|*.mp3;*.wav;*.wma;*.m4a;*.aac" })
        {
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            foreach (var file in picker.FileNames) queue.Items.Add(new FileInfo(file));
        }
    }

    private void Next()
    {
        Stop();
        if (queue.Items.Count == 0) return;
        var track = (FileInfo)queue.Items[0];
        queue.Items.RemoveAt(0);
        if (!track.Exists)
        {
            status.Text = "Datei nicht gefunden: " + track.Name;
            return;
        }
        player.Open(new Uri(track.FullName));
        player.Play();
        playing = true;
        status.Text = "Läuft: " + track.Name;
    }

    private void Stop()
    {
        playing = false;
        player.Close();
        status.Text = "Keine Wiedergabe";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) player.Close();
        base.Dispose(disposing);
    }
}
