using System.Text.RegularExpressions;
using System.Linq;
using System.Web.Script.Serialization;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

internal sealed class YouTubeQueue : UserControl
{
    private readonly ListBox searchResults = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Title", AccessibleName = "Suchergebnisse" };
    private readonly WebView2 liveSearch = new WebView2 { Width = 320, Height = 240, Visible = false };
    private readonly WebView2 player = new WebView2 { Dock = DockStyle.Fill };
    private readonly ListBox wishes = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Offene YouTube-Wünsche" };
    private readonly ListBox queue = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DisplayMember = "Title", AccessibleName = "YouTube-Warteschlange" };
    private readonly TextBox input = new TextBox { Width = 285, AccessibleName = "Videotitel oder YouTube-Link" };
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 32, Text = "Warteschlange leer. Suche einen Titel oder füge einen Link hinzu." };
    private readonly AppTabs views = new AppTabs();
    private readonly TabPage results = new TabPage("Suche");
    private readonly TabPage playback = new TabPage("Player");
    private readonly Timer timer = new Timer { Interval = 1500 };
    private string current;
    private int generation;
    private bool polling;
    private int startAttempts;
    private string searchedWish;
    private int searchVersion;
    private readonly System.Threading.SemaphoreSlim searchGate = new System.Threading.SemaphoreSlim(1);
    private int loadTicks;

    private const string PlayerHost = "youtube-player.tiktok-bot.example";
    private bool useWatchPage;
    private readonly string origin;
    private readonly bool music;

    internal YouTubeQueue(string directory, bool music = false)
    {
        this.music = music;
        origin = music ? "https://music.youtube.com" : "https://www.youtube.com";
        var profile = Path.Combine(directory, "YouTubeProfile");
        player.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = profile };
        liveSearch.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = profile };
        Controls.Add(liveSearch);
        player.CoreWebView2InitializationCompleted += (sender, args) =>
        {
            if (args.IsSuccess && !music)
                player.CoreWebView2.SetVirtualHostNameToFolderMapping(PlayerHost,
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "youtube-player"),
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.DenyCors);
        };
        player.NavigationCompleted += (sender, args) =>
        {
            if (!args.IsSuccess && current != null && args.WebErrorStatus != Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.OperationCanceled)
                status.Text = "Video konnte nicht geöffnet werden. Wähle Weiter, um es zu überspringen.";
        };

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        tools.Controls.Add(input);
        Button(tools, "Suchen", async () => await Find(input.Text, null));
        Button(tools, "Link hinzufügen", AddInput);

        Button(tools, "Abspielen", () => { views.SelectedTab = playback; if (current == null) Next(); else startAttempts = 3; });
        Button(tools, "Weiter", Next);
        Button(tools, "Stopp", Stop);
        Button(tools, "Webseite öffnen", () => ShowWebsite(profile));

        var lists = new TableLayoutPanel { Dock = DockStyle.Right, Width = 255, ColumnCount = 1, RowCount = 2, Padding = new Padding(6), AutoScroll = true };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var pending = ListPanel("Offene Chatwünsche", wishes);
        Button(pending.Actions, "Wunsch suchen", async () => await FindWish());
        Button(pending.Actions, "Wunsch entfernen", () => { if (wishes.SelectedIndex >= 0) wishes.Items.RemoveAt(wishes.SelectedIndex); });
        wishes.DoubleClick += async delegate { await FindWish(); };
        var upcoming = ListPanel("Warteschlange", queue);
        Button(upcoming.Actions, "Auswahl entfernen", () => { if (queue.SelectedIndex >= 0) queue.Items.RemoveAt(queue.SelectedIndex); });
        lists.Controls.Add(pending.Panel, 0, 0);
        lists.Controls.Add(upcoming.Panel, 0, 1);
        var matchActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        Button(matchActions, "Treffer abspielen", () => UseResult(true));
        Button(matchActions, "Zur Warteschlange hinzufügen", () => UseResult(false));
        searchResults.DoubleClick += delegate { UseResult(false); };
        results.Controls.AddRange(new Control[] { searchResults, matchActions });
        input.KeyDown += async (sender, args) =>
        {
            if (args.KeyCode != Keys.Enter) return;
            args.SuppressKeyPress = true;
            await Find(input.Text, null);
        };
        var account = new YouTubeAccount(directory) { Dock = DockStyle.Top, Height = 43 };
        playback.Controls.Add(player);
        views.AddPages(results, playback);
        Controls.AddRange(new Control[] { views, status, lists, tools, account });
        timer.Tick += async delegate { await CheckPlayer(); };
    }

    private sealed class QueuePanel
    {
        internal readonly Panel Panel = new Panel { Dock = DockStyle.Fill, MinimumSize = new System.Drawing.Size(0, 95) };
        internal readonly FlowLayoutPanel Actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };

        internal QueuePanel(string title, ListBox list)
        {
            Panel.Controls.AddRange(new Control[] { list, Actions, new Label { Text = title, Dock = DockStyle.Top, Height = 23 } });
        }
    }

    private static QueuePanel ListPanel(string label, ListBox list)
    {
        return new QueuePanel(label, list);
    }

    private static void Button(Control panel, string label, Action action)
    {
        var button = new Button { Text = label, AutoSize = true };
        button.Click += delegate { action(); };
        panel.Controls.Add(button);
    }

    internal string AddLiveWish(string title, string id)
    {
        if (id != null)
        {
            Enqueue(id, title);
            return (music ? "YouTube Music" : "YouTube") + ": Titel in die Warteschlange übernommen.";
        }
        wishes.Items.Add(title);
        return "YouTube-Wunsch gespeichert. Wähle ihn unter Offene Chatwünsche und suche erneut.";
    }

    internal Task<MusicMatch[]> SearchLive(string query) { return SearchTracks(query, 3); }

    private async Task<MusicMatch[]> SearchTracks(string query, int limit)
    {
        await searchGate.WaitAsync();
        try
        {
            if (IsDisposed) return null;
            var expected = new Uri(SearchAddress(query));
            await liveSearch.EnsureCoreWebView2Async();
            if (IsDisposed) return null;
            liveSearch.CoreWebView2.IsMuted = true;
            liveSearch.Source = expected;
            for (var attempt = 0; attempt < 24; attempt++)
            {
                await Task.Delay(500);
                if (IsDisposed) return null;
                var result = await liveSearch.CoreWebView2.ExecuteScriptAsync(@"(() => {
                    if (location.origin !== '" + origin + @"' || location.pathname !== '" + expected.AbsolutePath + @"' ||
                        new URLSearchParams(location.search).get('" + (music ? "q" : "search_query") + @"') !== " +
                        new JavaScriptSerializer().Serialize(query) + @") return null;
                    return Array.from(document.querySelectorAll('" + (music ? "ytmusic-responsive-list-item-renderer a[href*=\"watch?v=\"], ytmusic-card-shelf-renderer a[href*=\"watch?v=\"]" : "ytd-video-renderer a#video-title[href^=\"/watch?v=\"]") + @"'))
                        .filter(a => a.textContent.trim()).slice(0, 12).map(a => ({ Link: a.href, Title: a.textContent.trim() }));
                })()");
                var rows = new JavaScriptSerializer().Deserialize<SearchRow[]>(result);
                if (rows == null || rows.Length == 0) continue;
                var tracks = rows.Where(row => !string.IsNullOrWhiteSpace(row.Link) && !string.IsNullOrWhiteSpace(row.Title))
                    .Select(row => new MusicMatch(VideoId(row.Link), row.Title))
                    .Where(row => row.Id != null).GroupBy(row => row.Id).Select(group => group.First()).Take(limit).ToArray();
                if (tracks.Length > 0) return tracks;
            }
        }
        catch (Exception error)
        {
            if (!IsDisposed) ErrorLog.Write("YouTube", "LIVE-Suche fehlgeschlagen.", error);
        }
        finally { searchGate.Release(); }
        return null;
    }

    private sealed class SearchRow
    {
        public string Link { get; set; }
        public string Title { get; set; }
    }

    private async Task FindWish()
    {
        var title = wishes.SelectedItem as string;
        if (title == null) return;
        input.Text = title;
        await Find(title, title);
    }

    private async Task Find(string query, string wish)
    {
        query = query.Trim();
        if (query.Length == 0) return;
        var version = ++searchVersion;
        searchedWish = wish;
        views.SelectedTab = results;
        searchResults.Items.Clear();
        status.Text = "Suche läuft …";
        var id = VideoId(query);
        var matches = id == null ? await SearchTracks(query, 12) : new[] { new MusicMatch(id, query) };
        if (IsDisposed || version != searchVersion) return;
        if (matches == null || matches.Length == 0)
        {
            status.Text = "Keine Treffer verfügbar. Öffne bei Bedarf die Webseite für den Cookie-Dialog oder füge einen Videolink hinzu.";
            return;
        }
        searchResults.Items.AddRange(matches);
        searchResults.SelectedIndex = 0;
        status.Text = matches.Length + " Treffer. Wähle Abspielen oder Zur Warteschlange hinzufügen.";
    }

    private void AddInput()
    {
        var id = VideoId(input.Text);
        if (id == null) { status.Text = "Füge einen gültigen YouTube-Videolink ein."; return; }
        Enqueue(id);
    }

    private void UseResult(bool playNow)
    {
        var selected = searchResults.SelectedItem as MusicMatch;
        if (selected == null) { status.Text = "Wähle zuerst einen Suchtreffer."; return; }
        if (searchedWish != null) wishes.Items.Remove(searchedWish);
        searchedWish = null;
        Enqueue(selected.Id, selected.Title, playNow);
    }

    private void Enqueue(string id, string title = null, bool playNow = false)
    {
        var item = new MusicMatch(id, string.IsNullOrWhiteSpace(title) ? Address(id) : title);
        if (playNow) queue.Items.Insert(0, item);
        else queue.Items.Add(item);
        if (current == null || playNow) Next();
        else status.Text = "Eingereiht: " + item.Title;
    }

    private string Address(string id) { return origin + "/watch?v=" + id; }

    private string SearchAddress(string query) { return origin + (music ? "/search?q=" : "/results?search_query=") + Uri.EscapeDataString(query); }

    private void Next()
    {
        if (queue.Items.Count == 0)
        {
            Stop();
            status.Text = "Die YouTube-Warteschlange ist leer.";
            return;
        }
        var item = (MusicMatch)queue.Items[0];
        queue.Items.RemoveAt(0);
        current = item.Id;
        loadTicks = 0;
        generation++;
        startAttempts = 10;
        status.Text = "YouTube-Video wird geöffnet. Wenn es nicht startet, klicke im Player auf Wiedergabe.";
        if (Visible) views.SelectedTab = playback;
        useWatchPage = music;
        Navigate(player, useWatchPage ? Address(current) : "https://" + PlayerHost + "/youtube-player.html?v=" + current);
        timer.Start();
    }

    private void Stop()
    {
        current = null;
        generation++;
        timer.Stop();
        Navigate(player, "about:blank");
        status.Text = "YouTube-Wiedergabe gestoppt.";
    }

    private async Task CheckPlayer()
    {
        if (polling || current == null) return;
        if (loadTicks >= 0 && ++loadTicks == 14) status.Text = "Player lädt ungewöhnlich lange. Prüfe die Videoseite oder wähle Weiter.";
        if (player.CoreWebView2 == null) return;
        var version = generation;
        var id = current;
        polling = true;
        try
        {
            // Ignore ads and any video opened directly in the player.
            var script = useWatchPage ? @"(() => {
                if (location.origin !== '" + origin + @"' || location.pathname !== '/watch' ||
                    new URLSearchParams(location.search).get('v') !== '" + id + @"') return 'other';
                const p = document.querySelector('#movie_player, ytmusic-player');
                const v = p?.querySelector('video') || document.querySelector('video.html5-main-video');
                if (document.querySelector("".ytp-error, ytmusic-player-error-message-renderer"")) return 'error';
                if (!v || v.closest('.ad-showing') || p?.classList.contains('ad-showing')) return 'waiting';
                if (v.ended) return 'ended';
                if (!v.paused) return 'playing';
                " + (startAttempts > 0 ? "v.play().catch(() => {});" : "") + @"
                return 'paused';
            })()" : "(() => { if (location.host !== '" + PlayerHost + "' || new URLSearchParams(location.search).get('v') !== '" + id + "') return 'other'; " +
                (startAttempts > 0 ? "if (window.playerState === 'ready') window.youtubePlayer.playVideo(); " : "") + "return window.playerState || 'waiting'; })()";
            var result = await player.CoreWebView2.ExecuteScriptAsync(script);
            if (IsDisposed || version != generation) return;
            if (result == "\"ended\"") Next();
            else if (!useWatchPage && (result == "\"error\"" || (loadTicks >= 14 && result != "\"playing\"" && result != "\"paused\"")))
            {
                useWatchPage = true;
                loadTicks = 0;
                Navigate(player, Address(id));
            }
            else if (result == "\"error\"") { timer.Stop(); status.Text = "Dieses Video lässt sich hier nicht abspielen. Wähle Weiter."; }
            else if (result == "\"playing\"" && (startAttempts > 0 || loadTicks > 0)) { startAttempts = 0; loadTicks = -1; status.Text = "YouTube spielt. Wartende Videos: " + queue.Items.Count; }
            else if (result == "\"paused\"" && startAttempts > 0 && --startAttempts == 0)
                status.Text = "Automatischer Start nicht bestätigt. Klicke im Player auf Wiedergabe.";
        }
        catch (InvalidOperationException error)
        {
            timer.Stop();
            ErrorLog.Write("YouTube", "Wiedergabestatus konnte nicht gelesen werden.", error);
        }
        finally { polling = false; }
    }

    private void ShowWebsite(string profile)
    {
        using (var dialog = new Form { Text = music ? "YouTube Music" : "YouTube", Width = 1120, Height = 760, StartPosition = FormStartPosition.CenterParent })
        {
            var site = new WebView2 { Dock = DockStyle.Fill };
            site.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = profile };
            site.CoreWebView2InitializationCompleted += (sender, args) =>
            {
                if (!args.IsSuccess) return;
                site.CoreWebView2.IsMuted = true;
                site.CoreWebView2.NavigationStarting += (source, navigation) =>
                {
                    Uri url;
                    if (Uri.TryCreate(navigation.Uri, UriKind.Absolute, out url) && url.Host == "accounts.google.com")
                        navigation.Cancel = true;
                };
            };
            dialog.Controls.Add(site);
            Navigate(site, origin + "/");
            dialog.ShowDialog(this);
        }
    }

    private void Navigate(WebView2 view, string address)
    {
        try { view.Source = new Uri(address); }
        catch (InvalidOperationException error)
        {
            status.Text = "Die YouTube-Ansicht konnte nicht geöffnet werden.";
            ErrorLog.Write("YouTube", status.Text, error);
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
