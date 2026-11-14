using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

internal sealed class YouTubeQueue : UserControl
{
    private readonly WebView2 search = new WebView2 { Dock = DockStyle.Fill };
    private readonly WebView2 player = new WebView2 { Dock = DockStyle.Fill };
    private readonly ListBox wishes = new ListBox { Width = 230, Height = 82, AccessibleName = "Offene YouTube-Wünsche" };
    private readonly ListBox queue = new ListBox { Width = 230, Height = 82, AccessibleName = "YouTube-Warteschlange" };
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

    internal YouTubeQueue(string directory)
    {
        var profile = Path.Combine(directory, "YouTubeProfile");
        search.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = profile };
        player.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = profile };
        search.CoreWebView2InitializationCompleted += delegate
        {
            if (search.CoreWebView2 != null) search.CoreWebView2.IsMuted = true;
        };
        search.VisibleChanged += delegate
        {
            if (search.Visible && search.Source == null) Navigate(search, "https://www.youtube.com/");
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

        var lists = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        var pending = ListPanel("Offene Chatwünsche", wishes);
        Button(pending, "Wunsch suchen", FindWish);
        Button(pending, "Wunsch entfernen", () => { if (wishes.SelectedIndex >= 0) wishes.Items.RemoveAt(wishes.SelectedIndex); });
        wishes.DoubleClick += delegate { FindWish(); };
        var upcoming = ListPanel("Warteschlange", queue);
        Button(upcoming, "Auswahl entfernen", () => { if (queue.SelectedIndex >= 0) queue.Items.RemoveAt(queue.SelectedIndex); });
        lists.Controls.Add(pending);
        lists.Controls.Add(upcoming);
        var results = new TabPage("Suche");
        results.Controls.Add(search);
        playback.Controls.Add(player);
        views.AddPages(results, playback);
        Controls.AddRange(new Control[] { views, status, lists, tools });
        timer.Tick += async delegate { await CheckPlayer(); };
    }

    private static FlowLayoutPanel ListPanel(string label, ListBox list)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { Text = label, AutoSize = true });
        panel.Controls.Add(list);
        return panel;
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
            return "YouTube-Link in die Warteschlange übernommen.";
        }
        wishes.Items.Add(title);
        if (wishes.Items.Count == 1)
        {
            wishes.SelectedIndex = 0;
            FindWish();
        }
        return "YouTube-Wunsch gespeichert. Öffne den passenden Treffer und klicke auf Offenes Video hinzufügen.";
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
        var id = YouTubeMusicControl.VideoId(query.Trim());
        views.SelectedTab = results;
        Navigate(search, id == null ? "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query.Trim()) : Address(id));
    }

    private void AddInput(bool openVideo)
    {
        var id = YouTubeMusicControl.VideoId(openVideo ? (search.Source == null ? "" : search.Source.AbsoluteUri) : input.Text.Trim());
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

    private static string Address(string id) { return "https://www.youtube.com/watch?v=" + id; }

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
        current = YouTubeMusicControl.VideoId(address);
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
                if (location.origin !== 'https://www.youtube.com' || location.pathname !== '/watch' ||
                    new URLSearchParams(location.search).get('v') !== '" + id + @"') return 'other';
                const p = document.querySelector('#movie_player');
                const v = p?.querySelector('video');
                if (!v || p.classList.contains('ad-showing')) return 'waiting';
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
