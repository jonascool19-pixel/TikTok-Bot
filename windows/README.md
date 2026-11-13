# TikTok-Bot für Windows

Der Prototyp öffnet einen TikTok-LIVE in einem WinForms-Fenster mit Microsoft Edge WebView2. Er akzeptiert `@Name` und vollständige Links im Format `https://www.tiktok.com/@name/live`. Verkürzte Freigabelinks werden noch nicht aufgelöst.

## Prototyp starten

Voraussetzungen sind Windows PowerShell 5.1, die installierte [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) und das entpackte [WebView2-SDK 1.0.3595.46](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3595.46). Die NuGet-Datei lässt sich als ZIP entpacken. Übergib den Ordner, der `lib` und `runtimes` enthält.

```powershell
powershell.exe -NoProfile -STA -File .\windows\Start-Prototype.ps1 -WebView2Sdk C:\Pfad\zum\entpackten\SDK
```

Öffne **Bot-Konto anmelden** und melde dich direkt auf der TikTok-Seite an. Danach kannst du einen LIVE öffnen und den Chat in der eingebetteten TikTok-Seite bedienen. WebView2 speichert seine Sitzung unter `%LOCALAPPDATA%\TikTok-Bot\WindowsPrototype\BotProfile`. Navigations- und Browserfehler stehen bei Bedarf in `connection.log` neben dem Profil. Das Protokoll enthält keine Chattexte, Zugangsdaten oder Ziel-URLs.

## Prüfstand

- Der Smoke-Test (`-SmokeTest`) prüft gültige und ungültige LIVE-Adressen, den Start der WebView2-Ansicht, das Laden eines lokalen Testdokuments und das Anlegen eines separaten Testprofils.
- Eine separate Probe mit frischem WebView2-Profil hat die öffentliche TikTok-Anmeldeseite am 13. November 2026 mit HTTP 200 geladen.
- Der manuelle Chatversand in Windows-Chrome war im eigenen LIVE erfolgreich. Für WebView2 stehen Anmeldung, Wiederaufnahme der Sitzung nach Neustart und Zustellung an ein zweites Konto noch zur Prüfung aus. Die Prüfung erfolgt jeweils in einem vom Handy und einem von LIVE Studio gestarteten LIVE.
- Geplant sind die eigene Kommentaransicht, Vorlesen, Geschenkansagen und Begrüßungen, Audioeinstellungen und Aufnahmen, lokale Musik mit Wunschliste, Sicherung der Einstellungen und Diagnose für Audio und Chatversand. Beim Handy-LIVE wird die PC-Audioausgabe über einen separaten Audioeingang geprüft.
- Automatische Chatantworten benötigen einen unterstützten Sendeweg. Die öffentliche [TikTok-Entwicklerübersicht](https://developers.tiktok.com/) dokumentiert dafür derzeit keine LIVE-Chat-Schnittstelle.

## Windows-Installer

Der Workflow **Windows installer** wird in GitHub Actions manuell für einen Versions-Tag wie `windows-v0.1.0` gestartet. GitHub setzt voraus, dass der Workflow auf dem Standardbranch verfügbar ist. Er baut einen x64-Installer, prüft Installation, App-Start und Deinstallation und stellt das ZIP und dessen SHA-256-Prüfsumme als Artefakt bereit. Das Setup installiert die App für den aktuellen Benutzer und legt einen Startmenü-Eintrag an. Auf dem Ziel-PC muss die WebView2 Runtime installiert sein.

## Musik und freie Chatantworten

- Ein lokaler Player soll Dateien aus deiner für den Stream freigegebenen Musikbibliothek abspielen. Ein Musikwunsch im Chat wird einem Titel aus dieser Bibliothek zugeordnet und landet in der Warteschlange.
- Der Bot soll auf normale Nachrichten wie „Hey“ und „Wie heißt das Spiel?“ reagieren. Bei Fragen zum Spiel kann ein bildfähiges Modell einen aktuellen Ausschnitt des freigegebenen Spielfensters auswerten. Ein manuell hinterlegter Spieltitel dient als verlässliche Quelle. Ist der Titel unklar, kann der Bot kurz nachfragen.
- Der automatische Versand setzt einen bestätigten, unterstützten TikTok-Sendeweg voraus. Antwortlogik und Bilderkennung lassen sich unabhängig davon testen.

In der Windows-EXE öffnet **Chatantworten testen** die Vorschau für freie Antworten. Gib die vollständige URL eines Chat-Completions-Endpunkts, das Modell und eine beliebige Testnachricht ein. HTTP ist für Endpunkte auf diesem Rechner möglich, für entfernte Endpunkte wird HTTPS verwendet. Du kannst einen API-Schlüssel und den aktuellen Spieltitel angeben. Die Eingaben bleiben für die Dauer des Dialogs im Arbeitsspeicher. Beim Test gehen die eingegebene Nachricht und der Spieltitel an den gewählten Endpunkt. Das Ergebnis erscheint als Vorschlag im Dialog.
