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
    private readonly WebView2 search = new WebView2 { Dock = DockStyle.Fill };
    private readonly WebView2 liveSearch = new WebView2 { Width = 320, Height = 240, Visible = false };
    private readonly WebView2 player = new WebView2 { Dock = DockStyle.Fill };
    private readonly ListBox wishes = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Offene YouTube-Wünsche" };
    private readonly ListBox queue = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "YouTube-Warteschlange" };
    private readonly TextBox input = new TextBox { Width = 285, AccessibleName = "Videotitel oder YouTube-Link" };
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 32 };
    private readonly AppTabs views = new AppTabs();
    private readonly TabPage results = new TabPage("Suche");
    private readonly TabPage playback = new TabPage("Player");
    private readonly Timer timer = new Timer { Interval = 1500 };
    private string current;
    private int generation;
    private bool polling;
    private int startAttempts;
    private bool wishSearch;

    private readonly string origin;
    private readonly bool music;

    internal YouTubeQueue(string directory, bool music = false)
    {
        this.music = music;
        origin = music ? "https://music.youtube.com" : "https://www.youtube.com";
        var profile = Path.Combine(directory, "YouTubeProfile");
        search.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = profile };
        player.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = profile };
        search.CoreWebView2InitializationCompleted += delegate
        {
            if (search.CoreWebView2 != null) search.CoreWebView2.IsMuted = true;
        };
        liveSearch.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = profile };
        Controls.Add(liveSearch);
        search.VisibleChanged += delegate
        {
            if (search.Visible && search.Source == null) Navigate(search, origin + "/");
        };
        player.NavigationCompleted += (sender, args) =>
        {
            if (!args.IsSuccess && current != null)
                status.Text = "Video konnte nicht geöffnet werden. Wähle Weiter, um es zu überspringen.";
        };

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        tools.Controls.Add(input);
        Button(tools, "Suchen", () => Find(input.Text));
        Button(tools, "Link hinzufügen", () => AddInput(false));
        Button(tools, "Offenes Video hinzufügen", () => AddInput(true));
        Button(tools, "Abspielen", () => { views.SelectedTab = playback; if (current == null) Next(); else startAttempts = 3; });
        Button(tools, "Weiter", Next);
        Button(tools, "Stopp", Stop);

        var lists = new TableLayoutPanel { Dock = DockStyle.Right, Width = 255, ColumnCount = 1, RowCount = 2, Padding = new Padding(6), AutoScroll = true };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var pending = ListPanel("Offene Chatwünsche", wishes);
        Button(pending.Actions, "Wunsch suchen", FindWish);
        Button(pending.Actions, "Wunsch entfernen", () => { if (wishes.SelectedIndex >= 0) wishes.Items.RemoveAt(wishes.SelectedIndex); });
        wishes.DoubleClick += delegate { FindWish(); };
        var upcoming = ListPanel("Warteschlange", queue);
        Button(upcoming.Actions, "Auswahl entfernen", () => { if (queue.SelectedIndex >= 0) queue.Items.RemoveAt(queue.SelectedIndex); });
        lists.Controls.Add(pending.Panel, 0, 0);
        lists.Controls.Add(upcoming.Panel, 0, 1);
        results.Controls.Add(search);
        playback.Controls.Add(player);
        views.AddPages(results, playback);
        Controls.AddRange(new Control[] { views, status, lists, tools });
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
            Enqueue(id);
            return (music ? "YouTube Music" : "YouTube") + ": Titel in die Warteschlange übernommen.";
        }
        wishes.Items.Add(title);
        if (wishes.Items.Count == 1)
        {
            wishes.SelectedIndex = 0;
            FindWish();
        }
        return "YouTube-Wunsch gespeichert. Öffne den passenden Treffer und klicke auf Offenes Video hinzufügen.";
    }

    internal async Task<MusicMatch[]> SearchLive(string query)
    {
        try
        {
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
                    .Where(row => row.Id != null).GroupBy(row => row.Id).Select(group => group.First()).Take(3).ToArray();
                if (tracks.Length > 0) return tracks;
            }
        }
        catch (Exception error)
        {
            if (!IsDisposed) ErrorLog.Write("YouTube", "LIVE-Suche fehlgeschlagen.", error);
        }
        return null;
    }

    private sealed class SearchRow
    {
        public string Link { get; set; }
        public string Title { get; set; }
    }

    private void FindWish()
    {
        var title = wishes.SelectedItem as string;
        if (title == null) return;
        input.Text = title;
        Find(title);
        wishSearch = true;
    }

    private void Find(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        wishSearch = false;
        var id = VideoId(query.Trim());
        views.SelectedTab = results;
        Navigate(search, id == null ? SearchAddress(query.Trim()) : Address(id));
    }

    private void AddInput(bool openVideo)
    {
        var id = VideoId(openVideo ? (search.Source == null ? "" : search.Source.AbsoluteUri) : input.Text.Trim());
        if (id == null)
        {
            status.Text = openVideo ? "Öffne zuerst ein Video in der Suche." : "Füge einen gültigen YouTube-Videolink ein.";
            return;
        }
        if (openVideo && wishSearch && string.Equals(wishes.SelectedItem as string, input.Text, StringComparison.Ordinal))
        {
            wishes.Items.RemoveAt(wishes.SelectedIndex);
            wishSearch = false;
        }
        Enqueue(id);
    }

    private void Enqueue(string id)
    {
        queue.Items.Add(Address(id));
        if (current == null) Next();
        else status.Text = "Video eingereiht. Wartende Videos: " + queue.Items.Count;
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
        var address = (string)queue.Items[0];
        queue.Items.RemoveAt(0);
        current = VideoId(address);
        generation++;
        startAttempts = 10;
        status.Text = "YouTube-Video wird geöffnet. Wenn es nicht startet, klicke im Player auf Wiedergabe.";
        if (Visible) views.SelectedTab = playback;
        Navigate(player, address);
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
        if (polling || current == null || player.CoreWebView2 == null) return;
        var version = generation;
        var id = current;
        polling = true;
        try
        {
            // Ignore ads and any video opened directly in the player.
            var result = await player.CoreWebView2.ExecuteScriptAsync(@"(() => {
                if (location.origin !== '" + origin + @"' || location.pathname !== '/watch' ||
                    new URLSearchParams(location.search).get('v') !== '" + id + @"') return 'other';
                const p = document.querySelector('#movie_player, ytmusic-player');
                const v = p?.querySelector('video') || document.querySelector('video.html5-main-video');
                if (!v || v.closest('.ad-showing') || p?.classList.contains('ad-showing')) return 'waiting';
                if (v.ended) return 'ended';
                if (!v.paused) return 'playing';
                " + (startAttempts > 0 ? "v.play().catch(() => {});" : "") + @"
                return 'paused';
            })()");
            if (IsDisposed || version != generation) return;
            if (result == "\"ended\"") Next();
            else if (result == "\"playing\"" && startAttempts > 0) { startAttempts = 0; status.Text = "YouTube spielt. Wartende Videos: " + queue.Items.Count; }
            else if (result == "\"paused\"" && startAttempts > 0) startAttempts--;
        }
        catch (InvalidOperationException error)
        {
            timer.Stop();
            ErrorLog.Write("YouTube", "Wiedergabestatus konnte nicht gelesen werden.", error);
        }
        finally { polling = false; }
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
