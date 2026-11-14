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
    private readonly ComboBox wishSource = new ThemeComboBox { Width = 185, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Quelle für LIVE-Musikwünsche" };
    private readonly YouTubeMusicControl pear;
    private readonly YouTubeQueue youtube;
    private readonly SpotifyQueueControl spotifyQueueControl;
    private readonly Queue<RemoteWish> remotePending = new Queue<RemoteWish>();
    private int wishSession;
    private bool processingRemote;
    private readonly CheckBox autoPlay = new CheckBox { Text = "Musikwünsche lokal automatisch abspielen", AutoSize = true };
    private readonly List<FileInfo> library = new List<FileInfo>();
    private readonly Dictionary<string, Choice> choices = new Dictionary<string, Choice>(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox prompt = new TextBox { Dock = DockStyle.Bottom, ReadOnly = true, AccessibleName = "Nachricht für den LIVE-Chat" };
    private readonly Timer reminders = new Timer { Interval = 900000 };
    private readonly Func<bool> chatAvailable;
    private const string Hint = "Du kannst dir Musik wünschen: #Musikwunsch Künstler – Titel";
    internal bool RequestsEnabled { get { return collect.Checked; } }
    internal event Action<string> WishAdded;
    internal Func<string, Task<string>> SendChoice;

    internal MusicQueue(Func<bool> chatAvailable, string directory, Action<bool> saved)
    {
        this.chatAvailable = chatAvailable;
        pear = new YouTubeMusicControl(directory, saved) { Dock = DockStyle.Bottom, Height = 155 };
        youtube = new YouTubeQueue(directory) { Dock = DockStyle.Fill };
        wishSource.Items.AddRange(new object[] { "Lokale Dateien", "Spotify", "YouTube Music", "YouTube" });
        var sourcePath = Path.Combine(directory, "music-wish-source.txt");
        wishSource.SelectedIndex = 0;
        try
        {
            if (File.Exists(sourcePath))
            {
                var savedSource = File.ReadAllText(sourcePath).Trim();
                if (wishSource.Items.Contains(savedSource)) wishSource.SelectedItem = savedSource;
            }
            else
            {
                var legacy = Path.Combine(directory, "spotify-live-wishes.txt");
                bool wasSpotify;
                if (File.Exists(legacy) && bool.TryParse(File.ReadAllText(legacy), out wasSpotify) && wasSpotify)
                    wishSource.SelectedItem = "Spotify";
            }
        }
        catch (IOException error) { ErrorLog.Write("Musik", "Musikquelle konnte nicht geladen werden.", error); }
        catch (UnauthorizedAccessException error) { ErrorLog.Write("Musik", "Musikquelle konnte nicht geladen werden.", error); }
        wishSource.SelectionChangeCommitted += delegate
        {
            ResetChoices();
            ChatReplies.Persist(sourcePath, (string)wishSource.SelectedItem, saved);
        };
        spotifyQueueControl = new SpotifyQueueControl(directory, saved) { Dock = DockStyle.Fill };
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
        var volumePath = Path.Combine(directory, "music-volume.txt");
        try
        {
            int value;
            if (File.Exists(volumePath) && int.TryParse(File.ReadAllText(volumePath), out value) && value >= 0 && value <= 100)
                volume.Value = value;
        }
        catch (IOException error) { ErrorLog.Write("Musik", "Lautstärke konnte nicht geladen werden.", error); }
        catch (UnauthorizedAccessException error) { ErrorLog.Write("Musik", "Lautstärke konnte nicht geladen werden.", error); }
        tools.Controls.Add(new Label { Text = "Lautstärke %", AutoSize = true });
        tools.Controls.Add(volume);
        player.Volume = (double)volume.Value / 100;
        volume.ValueChanged += delegate
        {
            player.Volume = (double)volume.Value / 100;
            ChatReplies.Persist(volumePath, ((int)volume.Value).ToString(), saved);
        };
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
        AddButton(searchTools, "Wunsch entfernen", () => RemoveWish(requests.SelectedItem as string));
        AddButton(searchTools, "Chattext kopieren", delegate { if (prompt.Text.Length > 0) Clipboard.SetText(prompt.Text); });
        requests.DoubleClick += delegate { Search(requests.SelectedItem as string); };
        var sources = new AppTabs();
        var local = new TabPage("Lokale Dateien");
        local.Controls.AddRange(new Control[] { queue, status, tools });
        var remote = new TabPage("Spotify und Musikwünsche");
        remote.Controls.AddRange(new Control[] { requests, searchTools, spotify, spotifyStatus });
        var youtubeMusic = new TabPage("YouTube Music und Musikwünsche");
        var ytTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        var ytQuery = new TextBox { Width = 220, MaxLength = 150, AccessibleName = "YouTube Music: Titel oder Interpret" };
        var ytStatus = new Label { Dock = DockStyle.Bottom, Height = 30 };
        ytTools.Controls.Add(ytQuery);
        AddButton(ytTools, "YouTube Music öffnen / anmelden", () => OpenYouTube("https://music.youtube.com/"));
        AddButton(ytTools, "Suchen", () => SearchYouTubeMusic(ytQuery.Text));
        AddButton(ytTools, "Wunsch hinzufügen", delegate
        {
            if (AddWish(ytQuery.Text, feedback: ytStatus))
            {
                youtubeRequests.SelectedItem = ytQuery.Text.Trim();
                ytQuery.Clear();
                ytQuery.Focus();
            }
        });
        AddButton(ytTools, "Wunsch suchen", () => SearchYouTubeMusic(youtubeRequests.SelectedItem as string));
        AddButton(ytTools, "Wunsch entfernen", delegate
        {
            if (RemoveWish(youtubeRequests.SelectedItem as string)) ytStatus.Text = "Wunsch entfernt.";
        });
        ytQuery.KeyDown += (sender, args) =>
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            SearchYouTubeMusic(ytQuery.Text);
        };
        youtubeRequests.DoubleClick += delegate { SearchYouTubeMusic(youtubeRequests.SelectedItem as string); };
        youtubeMusic.Controls.AddRange(new Control[] { youtubeRequests, pear, ytStatus, ytTools });
        var spotifyQueue = new TabPage("Spotify-Warteschlange");
        spotifyQueue.Controls.Add(spotifyQueueControl);
        AddButton(searchTools, "Wunsch in Spotify-Warteschlange", async delegate
        {
            var title = requests.SelectedItem as string;
            if (title == null) return;
            sources.SelectedTab = spotifyQueue;
            var uri = SpotifyQueueControl.TrackUri(title);
            if (uri != null) await spotifyQueueControl.Enqueue(uri);
            else await spotifyQueueControl.ShowSearch(title);
        });
        var youtubeTab = new TabPage("YouTube");
        youtubeTab.Controls.Add(youtube);
        sources.AddPages(local, remote, spotifyQueue, youtubeMusic, youtubeTab);
        var wishOptions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        wishOptions.Controls.AddRange(new Control[] { collect, new Label { Text = "Musikquelle", AutoSize = true }, wishSource, autoPlay });
        Controls.AddRange(new Control[] { sources, prompt, wishOptions });
        collect.CheckedChanged += delegate
        {
            ResetChoices();
            reminders.Enabled = collect.Checked;
        };
        reminders.Tick += delegate { if (this.chatAvailable() && !choices.Values.Any(c => c.Expires > DateTime.UtcNow)) prompt.Text = Hint; };
    }

    internal void BindSettings(Action<string, CheckBox> bind)
    {
        bind("collect-music-requests", collect);
        bind("autoplay-music-requests", autoPlay);
    }

    internal bool ConsiderRequest(string handle, string message)
    {
        if (!collect.Checked) return false;
        Choice choice;
        int number;
        if (!string.IsNullOrEmpty(handle) && choices.TryGetValue(handle, out choice) &&
            choice.Expires > DateTime.UtcNow && Regex.IsMatch(message, @"\A[123]\z") && int.TryParse(message, out number))
        {
            if (number <= (choice.Remote != null ? choice.Remote.Length : choice.Tracks.Length))
            {
                choices.Remove(handle);
                if (choice.Remote != null) QueueRemote(handle, choice.Remote[number - 1].Id, choice.Remote[number - 1]);
                else QueueRequest(choice.Tracks[number - 1]);
            }
            return true;
        }
        var match = Regex.Match(message, @"\A(?:#musikwunsch|!song|!wunsch)\s+(.{2,150})\z", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        var title = match.Groups[1].Value.Trim();
        if (title.Length < 2) return true;
        if (AddWish(title, false) == false && requests.Items.Count >= 100) return true;
        var source = wishSource.SelectedItem as string;
        if (source != "Lokale Dateien")
        {
            if (!string.IsNullOrEmpty(handle)) choices.Remove(handle);
            QueueRemote(handle, title);
            return true;
        }
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

    internal void ResetChoices()
    {
        choices.Clear();
        remotePending.Clear();
        wishSession++;
        prompt.Text = collect.Checked ? Hint : "";
    }

    private sealed class RemoteWish
    {
        internal string Source, Handle, Query;
        internal MusicMatch Selected;
    }

    private async void QueueRemote(string handle, string query, MusicMatch selected = null)
    {
        if (remotePending.Count >= 20) { prompt.Text = "Musikwunschliste ausgelastet. Bitte warte kurz."; return; }
        remotePending.Enqueue(new RemoteWish { Source = (string)wishSource.SelectedItem, Handle = handle, Query = query, Selected = selected });
        if (processingRemote) return;
        processingRemote = true;
        try
        {
            while (remotePending.Count > 0 && !IsDisposed)
            {
                var wish = remotePending.Dequeue();
                var session = wishSession;
                if (!chatAvailable()) continue;
                var source = wish.Source;
                var id = wish.Selected == null
                    ? (source == "Spotify" ? SpotifyQueueControl.TrackUri(wish.Query) :
                        wish.Query.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? YouTubeMusicControl.VideoId(wish.Query) : null)
                    : wish.Selected.Id;
                if (id == null)
                {
                    MusicMatch[] tracks;
                    if (source == "Spotify")
                    {
                        var matches = await spotifyQueueControl.Search(wish.Query);
                        tracks = matches == null ? null : matches.Take(3).Select(track => new MusicMatch(track.Uri, track.ToString())).ToArray();
                    }
                    else if (source == "YouTube Music") tracks = await pear.Search(wish.Query);
                    else tracks = await youtube.SearchLive(wish.Query);
                    if (!WishIsCurrent(session)) continue;
                    if (tracks == null || tracks.Length == 0)
                    {
                        prompt.Text = tracks == null ? source + "-Suche derzeit nicht verfügbar." : "Keine Treffer auf " + source + ".";
                        if (source == "YouTube") prompt.Text += " " + youtube.AddLiveWish(wish.Query, null);
                        continue;
                    }
                    if (tracks.Length > 1)
                    {
                        await OfferChoice(wish.Handle, tracks, session);
                        continue;
                    }
                    id = tracks[0].Id;
                }
                if (!WishIsCurrent(session)) continue;
                if (source == "YouTube") { prompt.Text = youtube.AddLiveWish(wish.Query, id); continue; }
                var added = source == "Spotify" ? await spotifyQueueControl.Enqueue(id) : await pear.QueueLiveWish(id);
                if (WishIsCurrent(session))
                    prompt.Text = added ? "Titel in die Warteschlange von " + source + " aufgenommen."
                        : "Titel konnte nicht eingereiht werden. Prüfe die Verbindung zu " + source + ".";
            }
        }
        finally { processingRemote = false; }
    }

    private bool WishIsCurrent(int session)
    {
        return !IsDisposed && session == wishSession && chatAvailable();
    }

    private async Task OfferChoice(string handle, MusicMatch[] options, int session)
    {
        if (string.IsNullOrEmpty(handle)) { prompt.Text = "Mehrere Treffer. Der Zuschauer konnte nicht zugeordnet werden."; return; }
        foreach (var expired in choices.Where(pair => pair.Value.Expires <= DateTime.UtcNow).Select(pair => pair.Key).ToArray())
            choices.Remove(expired);
        if (choices.Count >= 100 && !choices.ContainsKey(handle)) { prompt.Text = "Zu viele offene Musikwünsche."; return; }
        choices[handle] = new Choice { Remote = options, Expires = DateTime.UtcNow.AddMinutes(3) };
        var reply = "@" + handle + " Wähle mit " + (options.Length == 2 ? "1 oder 2: " : "1, 2 oder 3: ");
        var max = (150 - reply.Length - (options.Length - 1) * 3) / options.Length;
        var question = reply + string.Join(" | ", options.Select((item, index) =>
        {
            var label = (index + 1) + ") " + item.Title;
            return label.Length <= max ? label : label.Substring(0, max - (char.IsHighSurrogate(label[max - 2]) ? 2 : 1)) + "…";
        }));
        prompt.Text = question;
        if (SendChoice == null) return;
        var delivery = await SendChoice(question);
        if (WishIsCurrent(session) && prompt.Text == question && delivery != null) spotifyStatus.Text = delivery;
    }

    private bool AddWish(string text, bool manual = true, Label feedback = null)
    {
        var message = feedback ?? spotifyStatus;
        var title = text.Trim();
        if (title.Length < 2)
        {
            if (manual) message.Text = "Gib einen Titel oder Künstler ein.";
            return false;
        }
        if (requests.Items.Cast<string>().Any(existing => string.Equals(existing, title, StringComparison.OrdinalIgnoreCase)))
        {
            if (manual) message.Text = "Dieser Wunsch steht bereits in der Liste.";
            return false;
        }
        if (requests.Items.Count >= 100)
        {
            if (manual) message.Text = "Die Wunschliste ist voll.";
            return false;
        }
        var index = requests.Items.Add(title);
        youtubeRequests.Items.Add(title);
        if (WishAdded != null) WishAdded(title);
        if (manual)
        {
            requests.SelectedIndex = index;
            message.Text = "Wunsch in der Bot-Liste gespeichert.";
        }
        return true;
    }

    private bool RemoveWish(string title)
    {
        if (title == null) return false;
        requests.Items.Remove(title);
        youtubeRequests.Items.Remove(title);
        return true;
    }

    private static void SearchYouTubeMusic(string title)
    {
        if (!string.IsNullOrWhiteSpace(title))
            OpenYouTube("https://music.youtube.com/search?q=" + Uri.EscapeDataString(title.Trim()));
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
        internal MusicMatch[] Remote;
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
