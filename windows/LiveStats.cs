using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class LiveStats : UserControl
{
    private const int Limit = 150;
    private sealed class Activity
    {
        internal string Label;
        internal int Count;
        internal DateTime First;
        internal DateTime Last;
    }

    private readonly Dictionary<string, Activity> viewers = new Dictionary<string, Activity>(StringComparer.OrdinalIgnoreCase);
    private readonly Label summary = new Label { Dock = DockStyle.Top, Height = 70 };
    private readonly ListView ranking = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly ListView archive = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, AccessibleName = "Verlauf abgeschlossener LIVEs" };
    private readonly Timer refresh = new Timer { Interval = 3000 };
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 96 * 1024 * 1024 };
    private readonly string path;
    private readonly List<Stream> history;
    private Stream current;
    private string room;
    private bool chatConnected;
    private bool eventsConnected;
    private int? webAudience;
    private int? eventAudience;
    private DateTime eventUpdated;
    private DateTime inactivity;
    private DateTime lastSave;
    private bool dirty;
    private bool rankingDirty = true;
    private HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

    internal LiveStats(string directory)
    {
        path = Path.Combine(directory, "live-history.json");
        history = LoadHistory();
        ranking.Columns.Add("Chatkonto", 237);
        ranking.Columns.Add("Kommentare", 100);
        ranking.Columns.Add("Erster Kommentar", 130);
        ranking.Columns.Add("Letzter Kommentar", 130);
        archive.Columns.Add("LIVE seit", 144);
        archive.Columns.Add("Zuletzt erfasst", 144);
        archive.Columns.Add("Konto", 138);
        archive.Columns.Add("Kommentare", 94);
        archive.Columns.Add("Chatkonten", 86);
        archive.Columns.Add("Spitze", 75);
        archive.Columns.Add("TikTok gesamt", 109);
        archive.Columns.Add("Geschenke", 86);

        var pages = new AppTabs { Dock = DockStyle.Fill };
        var activePage = new TabPage("Aktuell");
        activePage.Controls.AddRange(new Control[] { ranking, summary });
        var pastPage = new TabPage("Verlauf");
        pastPage.Controls.AddRange(new Control[] {
            archive, new Label { Dock = DockStyle.Top, Height = 42, Text = "Spitze zeigt die höchste beobachtete Zahl gleichzeitiger Zuschauer. TikTok gesamt erscheint, wenn TikTok den Gesamtwert liefert." }
        });
        pages.AddPages(activePage, pastPage);
        Controls.Add(pages);
        refresh.Tick += delegate
        {
            var now = DateTime.UtcNow;
            if (current != null && !chatConnected && !eventsConnected && inactivity != DateTime.MinValue &&
                now - inactivity >= TimeSpan.FromSeconds(90)) End();
            if (dirty && now - lastSave >= TimeSpan.FromSeconds(15)) SaveHistory();
            Render();
        };
        refresh.Start();
        Disposed += delegate { refresh.Dispose(); };
        Render();
        RenderHistory();
    }

    internal void SetRoom(string target)
    {
        if (string.IsNullOrEmpty(target) || string.Equals(room, target, StringComparison.OrdinalIgnoreCase)) return;
        End();
        room = target;
        chatConnected = eventsConnected = false;
    }

    internal void ChatConnection(string target, bool connected)
    {
        if (connected && !string.IsNullOrEmpty(target))
        {
            SetRoom(target);
            chatConnected = true;
            Begin(null, null);
        }
        else chatConnected = false;
        CheckInactivity();
    }

    internal void EventConnection(string target, string roomId, int? started)
    {
        if (string.IsNullOrEmpty(target)) return;
        SetRoom(target);
        eventsConnected = true;
        Begin(Regex.IsMatch(roomId ?? "", @"\A\d{1,32}\z") ? roomId : null, started);
        CheckInactivity();
    }

    internal void EventDisconnected()
    {
        eventsConnected = false;
        eventAudience = null;
        CheckInactivity();
    }

    private void CheckInactivity()
    {
        if (chatConnected || eventsConnected) inactivity = DateTime.MinValue;
        else if (inactivity == DateTime.MinValue) inactivity = DateTime.UtcNow;
    }

    private void Begin(string roomId, int? started)
    {
        if (current != null && current.RoomId != null && roomId != null && current.RoomId != roomId) End();
        if (current == null)
        {
            current = roomId == null ? null : history.FirstOrDefault(item => item.RoomId == roomId &&
                string.Equals(item.Room, room, StringComparison.OrdinalIgnoreCase));
            if (current == null)
            {
                current = new Stream { Id = Guid.NewGuid().ToString("N"), Room = room, StartedUtc = DateTime.UtcNow };
                history.Add(current);
                if (history.Count > Limit) history.RemoveAt(0);
            }
            seen = new HashSet<string>(current.ChatAccounts, StringComparer.Ordinal);
            RenderHistory();
        }
        else if (roomId != null && current.RoomId == null)
        {
            var previous = history.FirstOrDefault(item => item.RoomId == roomId &&
                string.Equals(item.Room, room, StringComparison.OrdinalIgnoreCase));
            if (previous != null)
            {
                previous.Comments += current.Comments;
                previous.Gifts += current.Gifts;
                if (current.Peak.HasValue && (!previous.Peak.HasValue || current.Peak > previous.Peak)) previous.Peak = current.Peak;
                if (current.Total.HasValue && (!previous.Total.HasValue || current.Total > previous.Total)) previous.Total = current.Total;
                history.Remove(current);
                current = previous;
                seen = new HashSet<string>(current.ChatAccounts, StringComparer.Ordinal);
                foreach (var identity in viewers.Keys) RememberAccount(identity);
                RenderHistory();
            }
        }
        if (roomId != null) current.RoomId = roomId;
        if (started.HasValue)
        {
            var time = DateTimeOffset.FromUnixTimeSeconds(started.Value).UtcDateTime;
            if (time <= DateTime.UtcNow && time >= DateTime.UtcNow.AddDays(-7) && time < current.StartedUtc) current.StartedUtc = time;
        }
        Touch();
    }

    internal void End()
    {
        if (current == null) return;
        current = null;
        eventAudience = webAudience = null;
        viewers.Clear();
        rankingDirty = true;
        inactivity = DateTime.MinValue;
        SaveHistory();
        Render();
        RenderHistory();
    }

    internal void StreamEnded()
    {
        eventsConnected = chatConnected = false;
        End();
    }

    internal void SetAudience(string value)
    {
        webAudience = ParseAudience(value);
        UpdateAudience(webAudience, null);
    }

    internal void SetEventAudience(int? value, int? total)
    {
        if (IsDisposed) return;
        if (value.HasValue)
        {
            eventAudience = value;
            eventUpdated = DateTime.UtcNow;
        }
        UpdateAudience(value, total);
    }

    private void UpdateAudience(int? simultaneous, int? total)
    {
        if (current == null || (!simultaneous.HasValue && !total.HasValue)) return;
        if (simultaneous.HasValue && (!current.Peak.HasValue || simultaneous > current.Peak)) current.Peak = simultaneous;
        if (total.HasValue && (!current.Total.HasValue || total > current.Total)) current.Total = total;
        Touch();
    }

    internal static int? ParseAudience(string text)
    {
        var match = Regex.Match(text ?? "", @"\A\s*(?<number>\d+(?:[.,\u00a0\u202f ]\d+)*)(?:\s*(?<unit>tsd\.?|k|mio\.?|m))?(?:\s*(?:zuschauer(?:\*?in(?:nen)?)?|viewers?))?\s*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var number = Regex.Replace(match.Groups["number"].Value, @"\s", "");
        if (unit.Length == 0)
        {
            int count;
            return int.TryParse(number.Replace(".", "").Replace(",", ""), out count) ? (int?)count : null;
        }
        decimal amount;
        if (!decimal.TryParse(number.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)) return null;
        amount *= unit.StartsWith("m", StringComparison.Ordinal) ? 1000000 : 1000;
        return amount <= int.MaxValue ? (int?)amount : null;
    }

    internal void Record(string handle, string name)
    {
        if (current == null) return;
        current.Comments++;
        var identity = string.IsNullOrWhiteSpace(handle) ? null : "@" + handle.Trim();
        if (identity != null)
        {
            var now = DateTime.Now;
            Activity entry;
            if (!viewers.TryGetValue(identity, out entry))
            {
                entry = new Activity { Label = name + " (" + identity + ")", First = now };
                viewers.Add(identity, entry);
                RememberAccount(identity);
            }
            entry.Count++;
            entry.Last = now;
            rankingDirty = true;
        }
        Touch();
    }

    private void RememberAccount(string identity)
    {
        if (current.ChatAccounts.Count >= 10000) return;
        using (var hash = SHA256.Create())
        {
            var key = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(current.Id + identity.ToUpperInvariant())));
            if (seen.Add(key)) current.ChatAccounts.Add(key);
        }
    }

    internal void RecordGift(int count)
    {
        if (current == null) return;
        current.Gifts += count;
        Touch();
    }

    private void Touch()
    {
        current.LastUtc = DateTime.UtcNow;
        dirty = true;
    }

    private List<Stream> LoadHistory()
    {
        try
        {
            if (File.Exists(path))
                return (json.Deserialize<List<Stream>>(File.ReadAllText(path)) ?? new List<Stream>())
                    .Where(item => item != null && item.ChatAccounts != null && item.Room != null && item.Id != null)
                    .OrderByDescending(item => item.StartedUtc).Take(Limit).OrderBy(item => item.StartedUtc).ToList();
        }
        catch (Exception error) { ErrorLog.Write("LIVE-Auswertung", "LIVE-Verlauf konnte nicht geladen werden.", error); }
        return new List<Stream>();
    }

    private void SaveHistory()
    {
        lastSave = DateTime.UtcNow;
        if (dirty && ChatReplies.Persist(path, json.Serialize(history), delegate { })) dirty = false;
    }

    private void Render()
    {
        var simultaneous = eventAudience.HasValue && DateTime.UtcNow - eventUpdated < TimeSpan.FromMinutes(1) ? eventAudience : webAudience;
        summary.Text = "Zuschauer aktuell: " + Count(simultaneous) + "    Spitze: " + Count(current == null ? null : current.Peak) +
            "    TikTok gesamt: " + Count(current == null ? null : current.Total) + Environment.NewLine +
            "Chatkonten: " + (current == null ? 0 : current.ChatAccounts.Count) + "    Kommentare: " + (current == null ? 0 : current.Comments) +
            "    Geschenke: " + (current == null ? 0 : current.Gifts);
        if (!rankingDirty) return;
        rankingDirty = false;
        ranking.BeginUpdate();
        try
        {
            ranking.Items.Clear();
            foreach (var entry in viewers.Values.OrderByDescending(item => item.Count).ThenBy(item => item.First).Take(100))
                ranking.Items.Add(new ListViewItem(new[] { entry.Label, entry.Count.ToString(), entry.First.ToString("HH:mm:ss"), entry.Last.ToString("HH:mm:ss") }));
        }
        finally { ranking.EndUpdate(); }
    }

    private void RenderHistory()
    {
        archive.BeginUpdate();
        try
        {
            archive.Items.Clear();
            foreach (var stream in history.Where(item => item != current).OrderByDescending(item => item.StartedUtc))
                archive.Items.Add(new ListViewItem(new[] {
                    stream.StartedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), stream.LastUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                    stream.Room.Replace("https://www.tiktok.com/", "").Replace("/live", ""),
                    stream.Comments.ToString("N0"), stream.ChatAccounts.Count.ToString("N0"), Count(stream.Peak),
                    Count(stream.Total), stream.Gifts.ToString("N0")
                }));
        }
        finally { archive.EndUpdate(); }
    }

    private static string Count(int? value) { return value.HasValue ? value.Value.ToString("N0") : "—"; }

    public sealed class Stream
    {
        public string Id { get; set; }
        public string Room { get; set; }
        public string RoomId { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime LastUtc { get; set; }
        public int? Peak { get; set; }
        public int? Total { get; set; }
        public long Comments { get; set; }
        public long Gifts { get; set; }
        public List<string> ChatAccounts { get; set; }
        public Stream() { ChatAccounts = new List<string>(); }
    }

    internal static bool Verify(string directory)
    {
        const string account = "https://www.tiktok.com/@test/live";
        if (ParseAudience("1,2 Tsd.") != 1200 || ParseAudience("1.234 Zuschauer*innen") != 1234 || ParseAudience("online") != null) return false;
        using (var stats = new LiveStats(directory))
        {
            stats.EventConnection(account, "1234", null);
            stats.SetEventAudience(70, 105);
            stats.SetEventAudience(65, 140);
            stats.Record("erste", "Erste");
            stats.Record("erste", "Erste");
            stats.Record("zweite", "Zweite");
            stats.RecordGift(5);
            stats.StreamEnded();
        }
        using (var stats = new LiveStats(directory))
        {
            if (stats.history.Count != 1 || stats.archive.Items.Count != 1) return false;
            var first = stats.history[0];
            if (first.Peak != 70 || first.Total != 140 || first.ChatAccounts.Count != 2 || first.Comments != 3 || first.Gifts != 5) return false;
            stats.ChatConnection(account, true);
            stats.Record("erste", "Erste");
            stats.EventConnection(account, "1234", null);
            stats.StreamEnded();
            return stats.history.Count == 1 && stats.history[0].ChatAccounts.Count == 2 && stats.history[0].Comments == 4;
        }
    }
}
