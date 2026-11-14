using System.Drawing;
using System.Windows.Forms;

internal sealed class MusicHelp : UserControl
{
    private static readonly string[][] Chapters =
    {
        new[] { "Musikwünsche einschalten", "1. Öffne Musik und aktiviere #Musikwunsch aus LIVE-Chat übernehmen.\n\n2. Wähle bei Musikquelle genau einen Dienst. Die Auswahl wird gespeichert.\n\n3. Zuschauer schreiben zum Beispiel #Musikwunsch Nena – 99 Luftballons. Die Glocke meldet neue Wünsche lautlos." },
        new[] { "Lokale Dateien", "1. Öffne Musik → Lokale Dateien und füge deine Audiodateien hinzu.\n\n2. Wähle als Musikquelle Lokale Dateien. Der Bot sucht in den geladenen Dateinamen.\n\n3. Aktiviere Musikwünsche lokal automatisch abspielen, wenn der Bot die lokale Warteschlange selbst starten soll." },
        new[] { "Spotify Premium", "1. Erstelle im Spotify-Entwicklerdashboard eine App und wähle Web API. Registriere exakt http://127.0.0.1:43821/spotify/ als Redirect-URI.\n\n2. Trage die Client-ID unter Musik → Spotify-Warteschlange ein und klicke auf Spotify verbinden. Bestätige die Freigabe im Browser.\n\n3. Starte in Spotify einen Titel und wähle Spotify als Musikquelle. Einen eindeutigen Treffer reiht der Bot ein. Alternativ kannst du einen Titellink eingeben und auf Zur Spotify-Warteschlange hinzufügen klicken." },
        new[] { "Mehrere Treffer", "Bei mehreren Spotify- oder lokalen Treffern kann der Bot bis zu drei Varianten anbieten.\n\nKopiere den Vorschlag mit Chattext kopieren im Bereich Spotify und Musikwünsche und sende ihn im TikTok-Chat. Wenn TikTok den @Namen liefert, kann die anfragende Person innerhalb von drei Minuten mit 1, 2 oder 3 wählen." },
        new[] { "YouTube Music", "Für die Desktop-Warteschlange brauchst du Pear Desktop. Aktiviere dort das Plugin API Server auf 127.0.0.1:26538. Klicke im Bot unter Musik → YouTube Music und Musikwünsche auf YouTube Music Desktop prüfen und bestätige die Verbindung in Pear Desktop.\n\nWähle YouTube Music als Musikquelle. Ein YouTube- oder YouTube-Music-Titellink im Musikwunsch kann direkt eingereiht werden. Wünsche als Text stehen zur manuellen Suche in der Wunschliste." },
        new[] { "YouTube", "Öffne Musik → YouTube für die eingebettete Ansicht. Dort kannst du nach Videos suchen. Für die Anmeldung kannst du YouTube im Standardbrowser öffnen.\n\nWenn YouTube als Musikquelle gewählt ist, öffnet der Bot einen Titellink oder die Trefferliste zum Wunsch in der eingebetteten Ansicht. Die Auswahl und Wiedergabe eines Suchtreffers steuerst du dort." },
        new[] { "Wenn etwas hakt", "Prüfe zuerst den LIVE-Status und die gewählte Musikquelle. Für Spotify muss bereits eine Wiedergabe aktiv sein. Für die Desktop-Warteschlange von YouTube Music muss Pear Desktop mit aktivem API Server laufen.\n\nWeitere Hinweise findest du unter Einstellungen → Fehlerlog." }
    };

    internal MusicHelp()
    {
        var title = new Label { Dock = DockStyle.Top, Height = 39, Text = "Musik und Musikwünsche", Font = AppTheme.HeadingFont };
        var chapter = new ThemeComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Kapitel auswählen" };
        var article = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, AccessibleName = "Anleitung zum gewählten Kapitel" };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 16, 0, 0) };
        body.Controls.Add(article);
        foreach (var entry in Chapters) chapter.Items.Add(entry[0]);
        chapter.SelectedIndexChanged += delegate
        {
            if (chapter.SelectedIndex < 0) return;
            article.Text = Chapters[chapter.SelectedIndex][1].Replace("\n", "\r\n");
            article.SelectionStart = 0;
            article.ScrollToCaret();
        };
        Controls.AddRange(new Control[] { body, chapter, title });
        chapter.SelectedIndex = 0;
    }
}
