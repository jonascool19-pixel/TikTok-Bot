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

Der Windows-Build und die Veröffentlichung als EXE werden nach Abstimmung des GitHub-Actions-Ablaufs eingerichtet.
