using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class MusicCommands : UserControl
{
    internal const string DefaultWish = "#Musikwunsch";
    internal const string DefaultAnswer = "#Antwort";
    internal string Wish { get; private set; }
    internal string Answer { get; private set; }
    internal event Action Changed;
    private readonly TextBox wish = new TextBox { Width = 190, MaxLength = 25, AccessibleName = "Musikwunsch-Befehl" };
    private readonly TextBox answer = new TextBox { Width = 190, MaxLength = 25, AccessibleName = "Auswahlbefehl" };
    private readonly Label status = new Label { AutoSize = true };
    private readonly string path;
    private readonly Action<bool> saved;
    private readonly JavaScriptSerializer json = new JavaScriptSerializer();

    internal MusicCommands(string directory, Action<bool> saved)
    {
        this.saved = saved;
        path = Path.Combine(directory, "music-commands.json");
        Wish = DefaultWish;
        Answer = DefaultAnswer;
        try
        {
            if (File.Exists(path))
            {
                var values = json.Deserialize<string[]>(File.ReadAllText(path));
                if (values == null || values.Length != 2 || !Valid(values[0], values[1])) throw new InvalidDataException();
                Wish = values[0];
                Answer = values[1];
            }
        }
        catch (Exception error) { ErrorLog.Write("Befehle", "Befehle konnten nicht geladen werden.", error); }
        UpdateFields();
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var save = new Button { AutoSize = true, Text = "Befehle speichern" };
        save.Click += delegate { Store(); };
        var defaults = new Button { AutoSize = true, Text = "Standardbefehle wiederherstellen" };
        defaults.Click += delegate { wish.Text = DefaultWish; answer.Text = DefaultAnswer; Store(); };
        layout.Controls.AddRange(new Control[] {
            new Label { Text = "Chatbefehle", AutoSize = true, Font = AppTheme.HeadingFont },
            new Label { Text = "Musikwunsch (danach Künstler und Titel)", AutoSize = true }, wish,
            new Label { Text = "Titelauswahl (danach 1, 2 oder 3)", AutoSize = true }, answer, save, defaults, status,
            new Label { Text = "Änderungen gelten sofort nach dem Speichern. Bei Bedarf kannst du die App oben rechts neu starten.",
                AutoSize = true, MaximumSize = new System.Drawing.Size(650, 0) } });
        Controls.Add(layout);
    }

    private void Store()
    {
        var w = "#" + wish.Text.Trim().TrimStart('#').Trim();
        var a = "#" + answer.Text.Trim().TrimStart('#').Trim();
        if (!Valid(w, a))
        {
            status.Text = "Verwende zwei unterschiedliche Hashtags mit je 1–24 Buchstaben, Ziffern oder Unterstrichen.";
            return;
        }
        if (!ChatReplies.Persist(path, json.Serialize(new[] { w, a }), saved)) return;
        Wish = w;
        Answer = a;
        UpdateFields();
        if (Changed != null) Changed();
    }

    private void UpdateFields()
    {
        wish.Text = Wish;
        answer.Text = Answer;
        status.Text = "Beispiele: " + Wish + " Nena – 99 Luftballons     " + Answer + " 2";
    }

    private static bool Valid(string wish, string answer)
    {
        const string pattern = @"\A#[\p{L}\p{Nd}_]{1,24}\z";
        return wish != null && answer != null && Regex.IsMatch(wish, pattern) && Regex.IsMatch(answer, pattern) &&
            !wish.Equals(answer, StringComparison.OrdinalIgnoreCase);
    }

    internal bool IsCommand(string text) { return IsCommand(text, Wish, Answer); }

    internal static bool IsCommand(string text, string wish, string answer)
    {
        var clean = Clean(text);
        // Recognition here only suppresses chat replies. Playback still requires the strict parser.
        if (Regex.IsMatch(clean, @"\A!(?:song|wunsch)(?![\p{L}\p{Nd}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        var match = Regex.Match(clean, @"\A#([\p{L}\p{Nd}_]+)");
        if (!match.Success)
        {
            var selection = Regex.Match(clean, @"\A([\p{L}\p{Nd}_]+)\s*[:=]?\s+(?:\d+|eins|zwei|drei)(?:\s|[.!?]|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return selection.Success && (LooksLike(selection.Groups[1].Value, answer) ||
                LooksLike(selection.Groups[1].Value, DefaultAnswer));
        }
        var hashtag = match.Groups[1].Value;
        if (hashtag.Length == 0) return false;
        if (("#" + hashtag).Equals(wish, StringComparison.OrdinalIgnoreCase) ||
            ("#" + hashtag).Equals(answer, StringComparison.OrdinalIgnoreCase)) return true;
        hashtag = Regex.Replace(hashtag, @"[_\p{Nd}]+\z", "");
        return hashtag.Equals("antworten", StringComparison.OrdinalIgnoreCase) ||
            LooksLike(hashtag, wish) || LooksLike(hashtag, answer) ||
            LooksLike(hashtag, DefaultWish) || LooksLike(hashtag, DefaultAnswer);
    }

    private static bool LooksLike(string input, string command)
    {
        var expected = command.Substring(1);
        if (input.Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
        if (expected.Length < 5 || Math.Abs(input.Length - expected.Length) > 1) return false;
        input = input.ToUpperInvariant();
        expected = expected.ToUpperInvariant();
        if (input.Length == expected.Length)
        {
            var position = 0;
            while (position < input.Length && input[position] == expected[position]) position++;
            if (position == input.Length) return true;
            return input.Substring(position + 1) == expected.Substring(position + 1) ||
                (position + 1 < input.Length && input[position] == expected[position + 1] &&
                input[position + 1] == expected[position] && input.Substring(position + 2) == expected.Substring(position + 2));
        }
        var longer = input.Length > expected.Length ? input : expected;
        var shorter = input.Length > expected.Length ? expected : input;
        var common = 0;
        while (common < shorter.Length && longer[common] == shorter[common]) common++;
        return longer.Substring(common + 1) == shorter.Substring(common);
    }

    internal static string WishPattern(string command) { return "(?:" + Regex.Escape(command) + "|!song|!wunsch)"; }

    internal static Match Match(string text, string pattern)
    {
        return Regex.Match(Clean(text), @"\A" + pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static string Clean(string text)
    {
        var clean = Regex.Replace(text ?? "", @"[\p{Cf}\uFE0F]", "").Trim().Replace('＃', '#');
        return Regex.Replace(clean, @"\A#\s+", "#");
    }

    internal static bool Verify(string directory)
    {
        using (var settings = new MusicCommands(directory, delegate { }))
        {
            settings.wish.Text = "#Musik";
            settings.answer.Text = "#Wahl";
            settings.Store();
        }
        using (var settings = new MusicCommands(directory, delegate { }))
            return settings.Wish == "#Musik" && settings.Answer == "#Wahl" &&
                settings.IsCommand("#musik Lied") && Selection("#wahl 2", settings.Answer).Success;
    }

    internal static Match Selection(string text, string answer)
    {
        return Match(text, Regex.Escape(answer) + @"\s+([123])\z");
    }
}
