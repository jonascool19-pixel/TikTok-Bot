# TikTok-Bot für Windows

Desktop-App für TikTok LIVE auf Windows x64. Öffne einen LIVE per `@Name` oder vollständigem LIVE-Link. Das Bot-Konto meldest du in der TikTok-Ansicht an. WebView2 speichert die Sitzung lokal.

## Installation und Updates

Entpacke das Windows-Paket aus dem [GitHub-Release](https://github.com/jonascool19-pixel/TikTok-Bot/releases) und starte `TikTok-Bot-Setup.exe`. Das Setup installiert für den aktuellen Benutzer. Du kannst ein Desktop-Symbol hinzufügen. Windows benötigt die Microsoft Edge WebView2 Runtime.

Im Reiter **Updates** sucht die App nach veröffentlichten Windows-Versionen dieses Repositorys, prüft das heruntergeladene Setup per SHA-256 und startet die Installation im bestehenden Ordner. Das TikTok-Profil, das gespeicherte Bot-Konto und das LIVE-Ziel liegen separat unter `%LOCALAPPDATA%\TikTok-Bot\WindowsPrototype`.

## Chat, Stimme und Musik

- **Kommentare und Stimme** zeigt LIVE-Kommentare an und kann sie mit lokalen deutschen Stimmen vorlesen. Der eingebettete LIVE-Ton ist standardmäßig stumm.
- **Chatantworten** erstellt bei aktiviertem LIVE-Modus Antwortvorschläge. Das Modell entscheidet anhand des Kommentars, ob eine Antwort sinnvoll ist. Zum Ausprobieren kannst du auch eine Nachricht eingeben. Den Vorschlag sendest du im TikTok-Chat.
- **Musik** nimmt auf Wunsch `#Musikwunsch Künstler – Titel` an und durchsucht die über **Dateien hinzufügen** geladene lokale Bibliothek. Ein eindeutiger Treffer landet in der Warteschlange. Bei mehreren Treffern kannst du die vorgeschlagene Chatnachricht kopieren. Der Zuschauer kann mit einer Zahl antworten, sofern TikTok den @Namen liefert. Bei eingeschalteter automatischer Wiedergabe startet ein Treffer, wenn gerade kein lokaler Titel läuft. Der Hinweistext für Musikwünsche steht zum Kopieren bereit.
- Spotify-Suche und -Steuerung öffnen bzw. steuern Spotify Desktop separat.

## Build

GitHub Actions baut manuell anhand eines Tags `windows-vX.Y.Z`. Der Lauf prüft Setup, Update über eine bestehende Installation, App-Start, Desktop-Verknüpfung und lokale Sprachausgabe. Einzelheiten stehen im [Workflow](.github/workflows/windows.yml).
