using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    private readonly ListBox youtubeRequests = new ListBox { Dock = DockStyle.Fill, AccessibleName = "YouTube Music Musikwünsche" };
    private readonly CheckBox collect = new CheckBox { Text = "#Musikwunsch aus LIVE-Chat übernehmen", AutoSize = true };
    private readonly CheckBox autoPlay = new CheckBox { Text = "Musikwünsche lokal automatisch abspielen", AutoSize = true };
    private readonly List<FileInfo> library = new List<FileInfo>();
    private readonly Dictionary<string, Choice> choices = new Dictionary<string, Choice>(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox prompt = new TextBox { Dock = DockStyle.Bottom, ReadOnly = true, AccessibleName = "Nachricht für den LIVE-Chat" };
    private readonly Timer reminders = new Timer { Interval = 900000 };
    private readonly Func<bool> chatAvailable;
    private const string Hint = "Du kannst dir Musik wünschen: #Musikwunsch Künstler – Titel";
    internal bool RequestsEnabled { get { return collect.Checked; } }
    internal event Action<string> WishAdded;

    internal MusicQueue(Func<bool> chatAvailable)
    {
        this.chatAvailable = chatAvailable;
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
        player.MediaFailed += (sender, error) =>
        {
            if (!playing) return;
            Stop();
            status.Text = "Datei konnte nicht abgespielt werden. Wähle Abspielen für den nächsten Titel.";
            ErrorLog.Write("Musik", "Lokale Datei konnte nicht abgespielt werden.", error.ErrorException);
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
        AddButton(searchTools, "Wunsch im Bot speichern", delegate
        {
            if (!AddWish(query.Text)) return;
            query.Clear();
            query.Focus();
        });
        query.KeyDown += (sender, args) =>
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            if (AddWish(query.Text)) query.Clear();
        };
        AddButton(searchTools, "Wunsch öffnen", () => Search(requests.SelectedItem as string));
        AddButton(searchTools, "Wunsch entfernen", delegate
        {
            if (requests.SelectedIndex < 0) return;
            youtubeRequests.Items.Remove(requests.SelectedItem);
            requests.Items.RemoveAt(requests.SelectedIndex);
        });
        searchTools.Controls.Add(collect);
        searchTools.Controls.Add(autoPlay);
        AddButton(searchTools, "Chattext kopieren", delegate { if (prompt.Text.Length > 0) Clipboard.SetText(prompt.Text); });
        requests.DoubleClick += delegate { Search(requests.SelectedItem as string); };
        var sources = new TabControl { Dock = DockStyle.Fill };
        var local = new TabPage("Lokale Dateien");
        local.Controls.AddRange(new Control[] { queue, status, tools });
        var remote = new TabPage("Spotify und Musikwünsche");
        remote.Controls.AddRange(new Control[] { requests, searchTools, spotify, spotifyStatus, prompt });
        var youtubeMusic = new TabPage("YouTube Music und Musikwünsche");
        var ytTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        AddButton(ytTools, "YouTube Music öffnen / anmelden", () => OpenYouTube("https://music.youtube.com/"));
        AddButton(ytTools, "Wunsch suchen", () =>
        {
            var title = youtubeRequests.SelectedItem as string;
            if (title != null) OpenYouTube("https://music.youtube.com/search?q=" + Uri.EscapeDataString(title));
        });
        youtubeMusic.Controls.AddRange(new Control[] { youtubeRequests, ytTools });
        var spotifyQueue = new TabPage("Spotify-Warteschlange");
        spotifyQueue.Controls.Add(new SpotifyQueueControl { Dock = DockStyle.Fill });
        sources.TabPages.AddRange(new[] { local, remote, spotifyQueue, youtubeMusic });
        Controls.Add(sources);
        collect.CheckedChanged += delegate
        {
            choices.Clear();
            prompt.Text = collect.Checked ? Hint : "";
            reminders.Enabled = collect.Checked;
        };
        reminders.Tick += delegate { if (this.chatAvailable() && !choices.Values.Any(c => c.Expires > DateTime.UtcNow)) prompt.Text = Hint; };
    }

    internal bool ConsiderRequest(string handle, string message)
    {
        if (!collect.Checked) return false;
        Choice choice;
        int number;
        if (!string.IsNullOrEmpty(handle) && choices.TryGetValue(handle, out choice) &&
            choice.Expires > DateTime.UtcNow && Regex.IsMatch(message, @"\A[123]\z") && int.TryParse(message, out number))
        {
            if (number <= choice.Tracks.Length)
            {
                choices.Remove(handle);
                QueueRequest(choice.Tracks[number - 1]);
            }
            return true;
        }
        var match = Regex.Match(message, @"\A(?:#musikwunsch|!song|!wunsch)\s+(.{2,150})\z", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        var title = match.Groups[1].Value.Trim();
        if (title.Length < 2) return true;
        AddWish(title, false);
        var terms = Regex.Split(title.ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(s => s.Length > 0).ToArray();
        if (terms.Length == 0) return true;
        var tracks = library.Where(file => File.Exists(file.FullName) &&
            terms.All(term => Path.GetFileNameWithoutExtension(file.Name).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        if (!string.IsNullOrEmpty(handle)) choices.Remove(handle);
        foreach (var expired in choices.Where(pair => pair.Value.Expires <= DateTime.UtcNow).Select(pair => pair.Key).ToArray())
            choices.Remove(expired);
        if (tracks.Length == 1) QueueRequest(tracks[0]);
        else if (tracks.Length > 1 && !string.IsNullOrEmpty(handle) && choices.Count < 100)
        {
            choices[handle] = new Choice { Tracks = tracks, Expires = DateTime.UtcNow.AddMinutes(3) };
            var names = tracks.Select((file, index) => (index + 1) + ") " + Path.GetFileNameWithoutExtension(file.Name));
            var reply = "@" + handle + " Wähle mit " + (tracks.Length == 2 ? "1 oder 2: " : "1, 2 oder 3: ");
            var max = Math.Max(8, (150 - reply.Length - 6) / tracks.Length);
            prompt.Text = reply + string.Join(" | ", names.Select(name => name.Length > max ? name.Substring(0, max - 1) + "…" : name));
        }
        else if (tracks.Length > 1) prompt.Text = "Mehrere lokale Treffer. Der Zuschauer konnte nicht eindeutig zugeordnet werden.";
        else prompt.Text = "Kein lokaler Treffer für „" + title + "“.";
        return true;
    }

    internal void ResetChoices() { choices.Clear(); prompt.Text = collect.Checked ? Hint : ""; }

    private bool AddWish(string text, bool manual = true)
    {
        var title = text.Trim();
        if (title.Length < 2)
        {
            if (manual) spotifyStatus.Text = "Gib einen Titel oder Künstler ein.";
            return false;
        }
        if (requests.Items.Cast<string>().Any(existing => string.Equals(existing, title, StringComparison.OrdinalIgnoreCase)))
        {
            if (manual) spotifyStatus.Text = "Dieser Wunsch steht bereits in der Liste.";
            return false;
        }
        if (requests.Items.Count >= 100)
        {
            if (manual) spotifyStatus.Text = "Die Wunschliste ist voll.";
            return false;
        }
        var index = requests.Items.Add(title);
        youtubeRequests.Items.Add(title);
        if (WishAdded != null) WishAdded(title);
        if (manual)
        {
            requests.SelectedIndex = index;
            spotifyStatus.Text = "Im Bot gespeichert. Öffne den Wunsch und füge den Titel in Spotify zur Warteschlange hinzu.";
        }
        return true;
    }

    internal static void OpenYouTube(string address)
    {
        try { using (var launched = Process.Start(new ProcessStartInfo(address) { UseShellExecute = true })) { } }
        catch (System.ComponentModel.Win32Exception error) { ErrorLog.Write("YouTube", "YouTube konnte nicht im Browser geöffnet werden.", error); }
    }

    private void QueueRequest(FileInfo file)
    {
        if (queue.Items.Count < 100) queue.Items.Add(file);
        if (autoPlay.Checked && !playing) Next();
    }

    private sealed class Choice
    {
        internal FileInfo[] Tracks;
        internal DateTime Expires;
    }

    private void Search(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        try
        {
            var url = "spotify:search:" + Uri.EscapeDataString(title.Trim());
            using (var window = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
            spotifyStatus.Text = "Suche an Spotify Desktop übergeben.";
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            spotifyStatus.Text = "Spotify Desktop konnte nicht geöffnet werden. Prüfe die Installation.";
            ErrorLog.Write("Spotify", spotifyStatus.Text, error);
        }
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
            if (result != 0) ErrorLog.Write("Spotify", spotifyStatus.Text + " (Status " + result + ")");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            if (!IsDisposed) spotifyStatus.Text = "Windows-Mediensteuerung nicht verfügbar.";
            ErrorLog.Write("Spotify", "Windows-Mediensteuerung konnte nicht gestartet werden.", error);
        }
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
            foreach (var path in picker.FileNames)
            {
                var file = new FileInfo(path);
                if (!library.Any(item => string.Equals(item.FullName, file.FullName, StringComparison.OrdinalIgnoreCase))) library.Add(file);
                queue.Items.Add(file);
            }
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
            ErrorLog.Write("Musik", "Lokale Audiodatei wurde nicht gefunden.");
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
        if (disposing) { player.Close(); reminders.Dispose(); }
        base.Dispose(disposing);
    }
}
