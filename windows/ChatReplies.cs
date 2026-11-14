using System;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal sealed class ChatReplies : UserControl
{
    private static readonly string[] Examples =
    {
        "Hallo!", "Hey", "Hi!", "Moin!", "Servus!", "Guten Morgen", "Guten Tag", "Guten Abend",
        "Wie heißt das Spiel?", "Was spielst du gerade", "Welches Game ist das?", "Was ist das für ein Spiel?",
        "Wie heißt dein Game", "Kannst du die Regeln erklären?", "Wann bist du wieder live?",
        "Wo finde ich deine Clips?", "Wie lange streamst du heute?", "Wer spielt mit?",
        "Warum ist das gerade passiert?", "Welche Map ist das?", "Kann man mitspielen?",
        "Wie funktioniert der Bot?", "Wie kann ich mir ein Lied wünschen?",
        "Gibt es heute eine Challenge?", "Okay", "Alles klar", "GG", "Haha", "Danke!", "Bin wieder da."
    };
    private readonly CheckBox enabled = new CheckBox { Text = "LIVE-Kommentare automatisch prüfen", AutoSize = true, Checked = true };
    private readonly TextBox game = new TextBox { Width = 210, MaxLength = 100, AccessibleName = "Spielname optional" };
    private readonly TextBox sample = new TextBox { Width = 260, MaxLength = 500, AccessibleName = "Testkommentar" };
    private readonly ListBox history = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Antwortvorschläge" };
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 30, Text = "Vorschläge erscheinen hier. Du sendest sie im TikTok-Chat." };
    private readonly Button copy = new Button { Text = "Antwort kopieren", AutoSize = true };

    internal ChatReplies()
    {
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        var example = new ComboBox { Width = 290, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Beispiel auswählen" };
        example.Items.Add("Beispiel auswählen …");
        example.Items.AddRange(Examples);
        example.SelectedIndex = 0;
        example.SelectionChangeCommitted += delegate
        {
            if (example.SelectedIndex <= 0) return;
            sample.Text = (string)example.SelectedItem;
            example.SelectedIndex = 0;
            sample.Focus();
            sample.SelectionStart = sample.TextLength;
        };
        var test = new Button { Text = "Testen", AutoSize = true };
        tools.Controls.AddRange(new Control[] { enabled, new Label { Text = "Spiel (optional)", AutoSize = true }, game,
            example, new Label { Text = "Testkommentar", AutoSize = true }, sample, test, copy });
        Controls.AddRange(new Control[] { history, status, tools });
        test.Click += delegate
        {
            var result = Suggest(sample.Text, game.Text);
            if (result != null) Add(result);
            else status.Text = "Für diesen Kommentar ist keine Antwort vorgesehen.";
        };
        copy.Click += delegate
        {
            var result = history.SelectedItem as string;
            if (result == null) return;
            try { Clipboard.SetText(result); }
            catch (System.Runtime.InteropServices.ExternalException error)
            {
                status.Text = "Die Zwischenablage ist vorübergehend nicht verfügbar.";
                ErrorLog.Write("Chatantworten", status.Text, error);
            }
        };
    }

    internal void ConsiderLiveComment(string text)
    {
        if (!enabled.Checked) return;
        var result = Suggest(text, game.Text);
        if (result != null) Add(result);
    }

    private void Add(string reply)
    {
        if (history.Items.Count >= 100) history.Items.RemoveAt(0);
        history.Items.Add(reply);
        history.SelectedIndex = history.Items.Count - 1;
        status.Text = "Antwortvorschlag bereit.";
    }

    internal static string Suggest(string text, string game)
    {
        var message = text.Trim().ToLowerInvariant();
        if (Regex.IsMatch(message, @"\A(?:hey+|hallo|hi|moin|servus|guten (?:morgen|tag|abend))[\s!.]*\z"))
            return "Hi, willkommen im LIVE!";
        if (Regex.IsMatch(message, @"\b(?:wie hei[ßs]t (?:das|dein|dieses) spiel|was (?:ist das für ein spiel|spielst du)|welches (?:spiel|game)|wie hei[ßs]t (?:das|dein) game)\b"))
            return string.IsNullOrWhiteSpace(game) ? "Der Spielname ist hier gerade nicht hinterlegt. Frag am besten den Streamer." : "Das Spiel heißt " + game.Trim() + ".";
        if (message.EndsWith("?") || Regex.IsMatch(message, @"\A(?:wer|wie|was|wann|wo|warum|welche[rs]?|kannst du)\s+"))
            return "Das kann ich gerade nicht sicher beantworten. Frag am besten den Streamer.";
        return null;
    }
}
