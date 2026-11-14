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
        "Gibt es heute eine Challenge?", "Okay", "Alles klar", "GG", "Haha", "Danke!", "Bin wieder da.",
        "Kann ich mir einen Song wünschen?", "Wie geht ein Musikwunsch?", "Was ist der Liedwunsch-Befehl?",
        "Kann man Songs requesten?", "Wie wähle ich zwischen mehreren Versionen?",
        "Warum wurde mein Musikwunsch nicht gefunden?", "Spielt der Bot Spotify im Stream?",
        "Was kannst du als Bot?", "Liest der Bot Kommentare vor?", "Welche Stimmen gibt es?",
        "Bist du ein Bot?", "Werden meine Kommentare gespeichert?", "Was bedeutet der grüne LIVE-Punkt?"
    };
    private readonly CheckBox enabled = new CheckBox { Text = "LIVE-Kommentare automatisch prüfen", AutoSize = true, Checked = true };
    private readonly TextBox game = new TextBox { Width = 210, MaxLength = 100, AccessibleName = "Spielname optional" };
    private readonly TextBox sample = new TextBox { Width = 260, MaxLength = 500, AccessibleName = "Testkommentar" };
    private readonly ListBox history = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Antwortvorschläge" };
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 30, Text = "Vorschläge erscheinen hier. Du sendest sie im TikTok-Chat." };
    private readonly Button copy = new Button { Text = "Antwort kopieren", AutoSize = true };

    internal ChatReplies(Func<bool> requestsEnabled)
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
        var gift = new Button { Text = "Geschenk-Dank testen", AutoSize = true };
        gift.Click += delegate { Add(GiftThanks("Alex", "Rose", 2)); };
        tools.Controls.AddRange(new Control[] { enabled, new Label { Text = "Spiel (optional)", AutoSize = true }, game,
            example, new Label { Text = "Testkommentar", AutoSize = true }, sample, test, copy, gift });
        Controls.AddRange(new Control[] { history, status, tools });
        test.Click += delegate
        {
            var result = Suggest(sample.Text, game.Text, requestsEnabled());
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

    internal void ConsiderLiveComment(string text, bool requestsEnabled)
    {
        if (!enabled.Checked) return;
        var result = Suggest(text, game.Text, requestsEnabled);
        if (result != null) Add(result);
    }

    private void Add(string reply)
    {
        if (history.Items.Count >= 100) history.Items.RemoveAt(0);
        history.Items.Add(reply);
        history.SelectedIndex = history.Items.Count - 1;
        status.Text = "Antwortvorschlag bereit.";
    }

    internal static string Suggest(string text, string game, bool requestsEnabled = false)
    {
        var message = text.Trim().ToLowerInvariant();
        if (Regex.IsMatch(message, @"\A(?:wie|was|kann|können|warum|welch)"))
        {
            if (Regex.IsMatch(message, @"mehrere (?:treffer|versionen)|zwischen .*versionen"))
                return "Wenn der Bot mehrere Versionen vorschlägt, antworte mit 1, 2 oder 3.";
            if (Regex.IsMatch(message, @"musikwunsch|musikwünsche|liedwunsch|(?:lied|song|musik|titel).*wünsch|(?:song|lied).*request"))
            {
                if (!requestsEnabled) return "Musikwünsche sind in diesem LIVE gerade deaktiviert.";
                if (Regex.IsMatch(message, @"nicht gefunden|kein treffer"))
                    return "Der Bot sucht in der lokalen Musikbibliothek. Versuch es mit Künstler und genauem Titel.";
                return "Schreib #Musikwunsch Künstler – Titel in den Chat, zum Beispiel #Musikwunsch Nena – 99 Luftballons.";
            }
            if (message.Contains("grüne") && message.Contains("live")) return "Der grüne Punkt bedeutet, dass der Bot den LIVE-Chat mitliest.";
            if (message.Contains("stimmen")) return "Es gibt deutsche Offline-Stimmen wie Kerstin und mehrere Sprechweisen von Thorsten.";
            if (message.Contains("wie funktioniert der bot") || message.Contains("was kannst du"))
                return "Ich lese LIVE-Kommentare mit und erstelle Antworten. Wenn Musikwünsche aktiv sind, suche ich in der lokalen Musikbibliothek.";
        }
        if (message == "bist du ein bot?" || message == "bist du ein bot") return "Ja, ich bin der Chat-Bot für diesen LIVE.";
        if (message.StartsWith("liest der bot kommentare")) return "Der Bot kann LIVE-Kommentare vorlesen, wenn die Sprachausgabe eingeschaltet ist.";
        if (message.StartsWith("spielt der bot spotify")) return "Die automatische Wiedergabe von Musikwünschen nutzt die lokale Musikbibliothek.";
        if (Regex.IsMatch(message, @"\A(?:hey+|hallo|hi|moin|servus|guten (?:morgen|tag|abend))[\s!.]*\z"))
            return "Hi, willkommen im LIVE!";
        if (Regex.IsMatch(message, @"\b(?:wie hei[ßs]t (?:das|dein|dieses) spiel|was (?:ist das für ein spiel|spielst du)|welches (?:spiel|game)|wie hei[ßs]t (?:das|dein) game)\b"))
            return string.IsNullOrWhiteSpace(game) ? "Der Spielname ist hier gerade nicht hinterlegt. Frag am besten den Streamer." : "Das Spiel heißt " + game.Trim() + ".";
        return null;
    }

    internal static string GiftThanks(string name, string gift, int count)
    {
        return "Danke, " + name + ", für " + (count > 1 ? count + " × " : "") + gift + "!";
    }
}
