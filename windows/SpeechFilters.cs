using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class SpeechFilters : UserControl
{
    private readonly ListBox list = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Gespeicherte Vorlesesperren", DisplayMember = "Label" };
    private readonly TextBox term = new TextBox { Width = 260, MaxLength = 150, AccessibleName = "Text der Vorlesesperre" };
    private readonly CheckBox prefix = new CheckBox { AutoSize = true, Text = "Nur am Nachrichtenanfang" };
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 28 };
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536 };
    private readonly string path;
    private readonly string titleText;
    private readonly bool forReplies;
    private readonly Action<bool> saved;
    private List<Rule> rules = new List<Rule>
    {
        new Rule { Text = "#Musikwunsch", Prefix = true },
        new Rule { Text = "#Antwort" }
    };

    internal SpeechFilters(string directory, Action<bool> saved, bool forReplies = false)
    {
        this.saved = saved;
        this.forReplies = forReplies;
        titleText = forReplies ? "KI-Antwortfilter" : "Vorlesesperren";
        if (forReplies) rules.Clear();
        path = Path.Combine(directory, forReplies ? "reply-filters.json" : "speech-filters.json");
        list.AccessibleName = titleText;
        term.AccessibleName = "Text der Regel";
        try
        {
            if (File.Exists(path))
            {
                var loaded = json.Deserialize<List<Rule>>(File.ReadAllText(path));
                if (loaded == null || loaded.Count > 100 || loaded.Any(rule => rule == null ||
                    string.IsNullOrWhiteSpace(rule.Text) || rule.Text.Length > 150)) throw new InvalidDataException();
                rules = loaded;
            }
        }
        catch (Exception error)
        {
            status.Text = titleText + " konnten nicht geladen werden.";
            ErrorLog.Write(titleText, "Regeln konnten nicht geladen werden.", error);
        }

        var title = new Label { Dock = DockStyle.Top, Height = 39, Text = titleText, Font = AppTheme.HeadingFont };
        var help = new Label { Dock = DockStyle.Top, Height = 42, Text =
            forReplies ? "Treffer erhalten keine Chatantwort. Musikbefehle und typische Tippfehler werden bereits ausgefiltert. Gültige Musikbefehle funktionieren weiterhin." :
            "Trifft eine Regel zu, bleibt die ganze Nachricht stumm. Chat und Musikwünsche werden weiterhin verarbeitet." };
        var editor = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 5, 0, 8) };
        editor.Controls.AddRange(new Control[] { term, prefix });
        AddButton(editor, "Hinzufügen", () => SaveRule(false));
        AddButton(editor, "Änderung speichern", () => SaveRule(true));
        AddButton(editor, "Auswahl löschen", RemoveRule);
        list.SelectedIndexChanged += delegate
        {
            var rule = list.SelectedItem as Rule;
            term.Text = rule == null ? "" : rule.Text;
            prefix.Checked = rule != null && rule.Prefix;
        };
        Controls.AddRange(new Control[] { list, status, editor, help, title });
        RefreshRules(-1);
    }

    internal bool ShouldRead(string message)
    {
        var text = forReplies ? MusicCommands.Clean(message) : message.TrimStart();
        return !rules.Any(rule => rule.Prefix
            ? text.StartsWith(rule.Text, StringComparison.OrdinalIgnoreCase)
            : text.IndexOf(rule.Text, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void SaveRule(bool replace)
    {
        var value = forReplies ? MusicCommands.Clean(term.Text) : term.Text.Trim();
        var index = replace ? list.SelectedIndex : rules.Count;
        if (value.Length == 0) { status.Text = "Gib den Text ein, den du sperren möchtest."; return; }
        if (index < 0) { status.Text = "Wähle zuerst eine Regel aus."; return; }
        if (!replace && rules.Count >= 100) { status.Text = "Die Liste enthält bereits 100 Regeln."; return; }
        if (rules.Where((rule, position) => !replace || position != index).Any(rule =>
            rule.Prefix == prefix.Checked && string.Equals(rule.Text, value, StringComparison.OrdinalIgnoreCase)))
        {
            status.Text = "Diese Regel steht bereits in der Liste.";
            return;
        }
        var updated = new List<Rule>(rules);
        var entry = new Rule { Text = value, Prefix = prefix.Checked };
        if (replace) updated[index] = entry;
        else updated.Add(entry);
        Store(updated, index);
    }

    private void RemoveRule()
    {
        if (list.SelectedIndex < 0) { status.Text = "Wähle zuerst eine Regel aus."; return; }
        var updated = new List<Rule>(rules);
        updated.RemoveAt(list.SelectedIndex);
        Store(updated, -1);
    }

    private bool Store(List<Rule> updated, int selection)
    {
        if (!ChatReplies.Persist(path, json.Serialize(updated.Select(rule => new { rule.Text, rule.Prefix }).ToArray()), saved))
        {
            status.Text = "Änderung konnte nicht gespeichert werden.";
            return false;
        }
        rules = updated;
        RefreshRules(selection);
        status.Text = "Einstellung gespeichert.";
        return true;
    }

    private void RefreshRules(int selection)
    {
        list.Items.Clear();
        list.Items.AddRange(rules.ToArray());
        if (selection >= 0) list.SelectedIndex = selection;
    }

    private static void AddButton(Control parent, string caption, Action action)
    {
        var button = new Button { Text = caption, AutoSize = true };
        button.Click += delegate { action(); };
        parent.Controls.Add(button);
    }

    internal static bool Verify(string directory)
    {
        using (var current = new SpeechFilters(directory, delegate { }))
        {
            if (current.ShouldRead("#Antwort 2") || current.ShouldRead("@Mello | #ANTWORT 1 = Lied A") ||
                current.ShouldRead("  #Musikwunsch Artist – Titel") || !current.ShouldRead("Moin")) return false;
            current.term.Text = "Testwort";
            current.SaveRule(false);
            if (current.ShouldRead("Ein Testwort im Text")) return false;
            current.term.Text = "Anderes Wort";
            current.prefix.Checked = true;
            current.SaveRule(true);
        }
        using (var reopened = new SpeechFilters(directory, delegate { }))
        {
            if (reopened.ShouldRead("Anderes Wort am Anfang") || !reopened.ShouldRead("Ein Anderes Wort im Text") ||
                !reopened.ShouldRead("Testwort")) return false;
            while (reopened.list.Items.Count > 0)
            {
                reopened.list.SelectedIndex = 0;
                var count = reopened.list.Items.Count;
                reopened.RemoveRule();
                if (reopened.list.Items.Count != count - 1) return false;
            }
        }
        using (var empty = new SpeechFilters(directory, delegate { }))
            if (!empty.ShouldRead("#Antwort 2")) return false;
        using (var replies = new SpeechFilters(directory, delegate { }, true))
        {
            replies.term.Text = "#Testregel";
            replies.prefix.Checked = true;
            replies.SaveRule(false);
        }
        using (var replies = new SpeechFilters(directory, delegate { }, true))
        using (var speech = new SpeechFilters(directory, delegate { }))
            return !replies.ShouldRead(" ＃TESTREGEL ?") && replies.ShouldRead("Was heißt #Testregel?") && speech.ShouldRead("#Testregel");
    }

    private sealed class Rule
    {
        public string Text { get; set; }
        public bool Prefix { get; set; }
        public string Label { get { return Text + (Prefix ? "  ·  Nachrichtenanfang" : "  ·  An beliebiger Stelle"); } }
    }
}
