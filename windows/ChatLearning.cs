using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class ChatLearning : UserControl
{
    private const int Capacity = 100;
    private readonly string path;
    private readonly Action<bool> saved;
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 };
    private readonly ListBox list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Label", AccessibleName = "Gesammelte Lernvorschläge" };
    private readonly TextBox question = new TextBox { Width = 340, MaxLength = 150, AccessibleName = "Gelernte Frage" };
    private readonly TextBox answer = new TextBox { Width = 340, MaxLength = 150, AccessibleName = "Gelernte Antwort" };
    private readonly CheckBox active = new CheckBox { Text = "Antwort verwenden", AutoSize = true };
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 30 };
    private readonly Dictionary<string, string> firstViewers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private bool refreshing;
    internal int ResetVersion { get; private set; }

    internal ChatLearning(string directory, Action<bool> saved)
    {
        this.saved = saved;
        path = Path.Combine(directory, "chat-learning.json");
        LoadEntries();
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        var save = new Button { Text = "Änderung speichern", AutoSize = true, Enabled = false };
        var delete = new Button { Text = "Auswahl löschen", AutoSize = true, Enabled = false };
        var reset = new Button { Text = "Lernspeicher zurücksetzen", AutoSize = true };
        var help = new Label { Dock = DockStyle.Top, Height = 49, Text =
            "Der Bot sammelt Antworten auf wiederkehrende Fragen und kann passende Antworten nach übereinstimmenden Vorschlägen für verschiedene Zuschauer wiederverwenden. Hier kannst du jede Antwort prüfen, ändern oder sperren." };
        tools.Controls.AddRange(new Control[] { question, answer, active, save, delete, reset });
        list.SelectedIndexChanged += delegate
        {
            if (refreshing) return;
            var row = list.SelectedItem as Row;
            save.Enabled = delete.Enabled = row != null;
            question.Text = row == null ? "" : row.Question;
            answer.Text = row == null ? "" : row.Entry.Answer;
            active.Checked = row != null && row.Entry.Active;
        };
        save.Click += delegate { Edit(); };
        delete.Click += delegate
        {
            var row = list.SelectedItem as Row;
            if (row == null) return;
            var next = Copy();
            next.Remove(row.Question);
            if (Save(next, true))
            {
                firstViewers.Remove(row.Question);
                RefreshList(null);
            }
        };
        reset.Click += delegate
        {
            if (MessageBox.Show(this, "Alle gesammelten und bearbeiteten Lernvorschläge löschen?", "Lernspeicher zurücksetzen",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Clear();
        };
        Controls.AddRange(new Control[] { list, status, tools, help });
        RefreshList(null);
    }

    internal string AnswerFor(string text)
    {
        Entry entry;
        return entries.TryGetValue(ChatReplies.NormalizeQuestion(text), out entry) && entry.Active ? entry.Answer : null;
    }

    internal void Observe(string text, string result, string viewer)
    {
        var key = ChatReplies.NormalizeQuestion(text);
        var value = ChatReplies.CleanAiReply(result, text);
        if (!Useful(key, value)) return;
        var now = DateTime.UtcNow;
        var next = Copy();
        foreach (var expired in entries.Where(item => !item.Value.Active && !item.Value.Edited && item.Value.UpdatedUtc < now.AddDays(-30)))
            next.Remove(expired.Key);
        Entry current;
        next.TryGetValue(key, out current);
        if (current != null && (current.Active || current.Edited)) return;

        string first;
        var agrees = current != null && string.Equals(ChatReplies.NormalizeQuestion(current.Answer),
            ChatReplies.NormalizeQuestion(value), StringComparison.OrdinalIgnoreCase);
        var activate = agrees && !string.IsNullOrWhiteSpace(viewer) && firstViewers.TryGetValue(key, out first) &&
            !string.Equals(first, viewer, StringComparison.OrdinalIgnoreCase);
        if (current == null && next.Count >= Capacity)
        {
            var oldest = next.Where(item => !item.Value.Active && !item.Value.Edited)
                .OrderBy(item => item.Value.UpdatedUtc).Select(item => item.Key).FirstOrDefault();
            if (oldest == null) return;
            next.Remove(oldest);
        }
        if (current != null && agrees && !activate && now - current.UpdatedUtc < TimeSpan.FromMinutes(5) &&
            (string.IsNullOrWhiteSpace(viewer) || firstViewers.ContainsKey(key))) return;
        next[key] = new Entry { Answer = agrees ? current.Answer : value, Active = activate, UpdatedUtc = now };
        if (!Save(next, false)) return;
        if (!string.IsNullOrWhiteSpace(viewer) && (!agrees || !firstViewers.ContainsKey(key)))
            firstViewers[key] = viewer;
        foreach (var removed in firstViewers.Keys.Where(item => !next.ContainsKey(item)).ToArray()) firstViewers.Remove(removed);
        var selected = list.SelectedItem as Row;
        RefreshList(selected == null ? null : selected.Question, true);
    }

    private static bool Useful(string input, string output)
    {
        if (input.Length > 150 || string.IsNullOrWhiteSpace(output) || output.Length < 8 ||
            !Regex.IsMatch(input, @"\A(?:wie|was|wo|wer|welche\w*|womit|wodurch|warum|kann|können|gibt)\b", RegexOptions.IgnoreCase) ||
            Regex.Matches(input, @"[\p{L}\p{N}]+").Count < 3) return false;
        // Avoid learning answers that depend on today's stream or specific people.
        const string temporary = @"[#@]|\d|\b(?:heute|morgen|gestern|gerade|aktuell|diesmal|gleich|uhr|preis|€|laufzeit|passwort|adresse|ich weiß nicht|keine ahnung)\b";
        return !Regex.IsMatch(input + " " + output, temporary, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private void LoadEntries()
    {
        try
        {
            if (!File.Exists(path)) return;
            var data = json.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
            if (data == null) throw new InvalidDataException();
            foreach (var item in data)
            {
                if (entries.Count == Capacity) break;
                var key = ChatReplies.NormalizeQuestion(item.Key);
                if (key.Length == 0 || key.Length > 150 || item.Value == null) continue;
                var legacy = item.Value as string;
                var entry = legacy == null ? json.ConvertToType<Entry>(item.Value) :
                    new Entry { Answer = legacy, UpdatedUtc = DateTime.UtcNow };
                if (string.IsNullOrWhiteSpace(entry.Answer) || entry.Answer.Length > 150 ||
                    (!entry.Active && !entry.Edited && (!Useful(key, entry.Answer) || entry.UpdatedUtc < DateTime.UtcNow.AddDays(-30)))) continue;
                entries[key] = entry;
            }
        }
        catch (Exception error)
        {
            ErrorLog.Write("Lernvorschläge", "Lernspeicher konnte nicht geladen werden.", error);
            status.Text = "Lernspeicher konnte nicht geladen werden.";
        }
    }

    private void Edit()
    {
        var row = list.SelectedItem as Row;
        if (row == null) return;
        var key = ChatReplies.NormalizeQuestion(question.Text);
        var value = answer.Text.Trim();
        if (key.Length == 0 || value.Length == 0) { status.Text = "Gib eine Frage und eine Antwort ein."; return; }
        if (!key.Equals(row.Question, StringComparison.OrdinalIgnoreCase) && entries.ContainsKey(key))
        {
            status.Text = "Diese Frage ist bereits gespeichert.";
            return;
        }
        var next = Copy();
        next.Remove(row.Question);
        next[key] = new Entry { Answer = value, Active = active.Checked, Edited = true, UpdatedUtc = DateTime.UtcNow };
        if (!Save(next, true)) return;
        firstViewers.Remove(row.Question);
        RefreshList(key);
    }

    private void Clear()
    {
        if (!Save(new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase), true)) return;
        ResetVersion++;
        firstViewers.Clear();
        RefreshList(null);
    }

    private Dictionary<string, Entry> Copy()
    {
        return new Dictionary<string, Entry>(entries, StringComparer.OrdinalIgnoreCase);
    }

    private bool Save(Dictionary<string, Entry> next, bool manual)
    {
        if (!ChatReplies.Persist(path, json.Serialize(next), manual ? saved : (Action<bool>)delegate { }))
        {
            if (manual) status.Text = "Änderung konnte nicht gespeichert werden.";
            return false;
        }
        entries = next;
        if (manual) status.Text = "Einstellung gespeichert.";
        return true;
    }

    private void RefreshList(string selection, bool keepEdits = false)
    {
        refreshing = keepEdits && selection != null && entries.ContainsKey(selection);
        try
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var item in entries.OrderBy(row => row.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var index = list.Items.Add(new Row { Question = item.Key, Entry = item.Value });
                if (string.Equals(item.Key, selection, StringComparison.OrdinalIgnoreCase)) list.SelectedIndex = index;
            }
        }
        finally { list.EndUpdate(); refreshing = false; }
    }

    public sealed class Entry
    {
        public string Answer { get; set; }
        public bool Active { get; set; }
        public bool Edited { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class Row
    {
        internal string Question;
        internal Entry Entry;
        public string Label { get { return (Entry.Active ? "Wird verwendet · " : "Vorschlag · ") + Question; } }
    }

    internal static bool Verify(string directory)
    {
        const string question = "Wie kann man höflich grüßen?";
        const string answer = "Sag zum Beispiel Hallo und frag, wie es der Person geht.";
        var path = Path.Combine(directory, "chat-learning.json");
        File.WriteAllText(path, "{\"Wie schreibt man freundlich\":\"Bleib höflich und antworte in ganzen Sätzen.\"}");
        using (var first = new ChatLearning(directory, delegate { }))
        {
            if (first.entries.Count != 1 || first.AnswerFor("Wie schreibt man freundlich?") != null) return false;
            first.Observe("Wie läufts gerade?", "Das läuft gut und ist spannend.", "erste");
            first.Observe(question, answer, "erste");
            first.Observe(question, answer, "erste");
            if (first.entries.Count != 2 || first.AnswerFor(question) != null) return false;
            first.Observe(question, answer, "zweite");
            if (first.AnswerFor(question) != answer) return false;
        }
        using (var reopened = new ChatLearning(directory, delegate { }))
        {
            if (reopened.AnswerFor(question) != answer) return false;
            reopened.Clear();
            if (reopened.entries.Count != 0 || reopened.ResetVersion != 1) return false;
        }
        using (var reset = new ChatLearning(directory, delegate { })) return reset.entries.Count == 0;
    }
}
