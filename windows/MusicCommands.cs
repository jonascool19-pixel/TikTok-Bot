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
            new Label { Text = "Titelauswahl (danach 1, 2 oder 3)", AutoSize = true }, answer, save, defaults, status });
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
        return Match(text, WishPattern(wish) + @"(?:\s|$)").Success ||
            Match(text, Regex.Escape(answer) + @"(?:\s|$)").Success;
    }

    internal static string WishPattern(string command) { return "(?:" + Regex.Escape(command) + "|!song|!wunsch)"; }

    internal static Match Match(string text, string pattern)
    {
        var clean = Regex.Replace(text.Trim(), @"[\p{Cf}\uFE0F]", "");
        clean = Regex.Replace(clean, @"\A#\s+", "#");
        return Regex.Match(clean, @"\A" + pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        return Match(text, "(?:" + Regex.Escape(answer) + @"\s*)?([123])\z");
    }
}
