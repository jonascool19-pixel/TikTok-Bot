# TikTok-Bot

Selbst betriebenes Dashboard für TikTok LIVE auf Proxmox. Ein Ubuntu-CT empfängt Ereignisse, erzeugt lokale Sprachausgabe und kann über ein separates Konto im Chat schreiben. Es spielt keine Rolle, ob der Creator vom Handy oder mit TikTok LIVE Studio sendet.

## Installation

Vorausgesetzt ist ein Ubuntu-24.04-CT auf x86-64 mit systemd, Internetzugang . Führe den Befehl als root **im CT** aus:

~~~bash
installer=$(mktemp) && curl -fsSL https://raw.githubusercontent.com/jonascool19-pixel/TikTok-Bot/v0.1.19/install.sh -o "$installer" && bash "$installer"
~~~

Bei einer Neuinstallation wählt der Installer einen freien TCP-Port. Updates behalten den bisherigen Port. Öffne die angezeigte CT-Adresse im Heimnetz. Der Installer aktualisiert die Ubuntu-Pakete, installiert die Anwendung und prüft die HTTP-Erreichbarkeit des Dienstes. Das farbige Abschlussfeld zeigt Dashboard-URL, Benutzer und Startpasswort. Das Startpasswort erscheint nur bei der Ersteinrichtung. Beim ersten Login ist ein Wechsel Pflicht. Wiederholte Installationen lassen die gespeicherten Einstellungen und das Passwort bestehen.

Falls im CT curl fehlt, installiere es zunächst mit apt-get update && apt-get install -y curl. Für die Installation aus einem entpackten GitHub-Actions-Artefakt genügt bash install.sh im Paketverzeichnis. Der Installer erzeugt kein öffentliches Port-Forwarding. Nutze das Dashboard nur in deinem vertrauenswürdigen Heimnetz oder hinter einem HTTPS-Reverse-Proxy. Für HTTPS lässt sich BOT_SECURE_COOKIE=1 in /etc/tiktok-bot.env setzen. Die Mikrofonaufnahme im Browser benötigt HTTPS oder localhost. Über HTTP funktioniert der Upload vorhandener Audioclips.

## TikTok verbinden

Trage unter TikTok-Verbindung deinen Creator-Namen ein und speichere. Öffne das Dashboard über HTTPS und nutze **Bot-Konto verbinden**. Melde dich im angezeigten TikTok-Browser mit dem separaten Bot-Konto an und wähle **Bot-Konto übernehmen**. Aktiviere anschließend den Versand und speichere.

Die Anmeldung bleibt im Browserprofil des CT. Die Verbindung liest sichtbare Textkommentare. Geschenk- und Beitrittsereignisse lassen sich simulieren, werden über diese Verbindung aber noch nicht erfasst. TikTok-Seitenänderungen und die Chromium-Sandbox des CT können den Betrieb beeinflussen.

Unter Audio kannst du Thorsten, Kerstin oder Amy auswählen. Der Fehlerlog aktualisiert sich alle zehn Sekunden im geöffneten Reiter. Datenverbrauch zeigt gemessene HTTP-Bytes des TikTok-Browsers. CPU und RAM verwenden die Linux-cgroup-Werte.

## Backup und Fehlerdiagnose

Exportiere im Dashboard ein verschlüsseltes Backup der Einstellungen und Aufnahmen. Zugangsdaten lassen sich optional einschließen. Beim Wiederherstellen bleibt das Admin-Passwort der Zielinstallation bestehen. Chatversand und freie Antworten müssen erneut aktiviert werden. Prüfe auch, ob die TikTok-Sitzung noch gültig ist.

Das Dashboard zeigt die letzten 200 Einträge aus dem begrenzten Fehlerlog und kann sie als JSONL exportieren. Die Anwendung protokolliert Fehler für TikTok, Versand, Audio, KI, Anmeldung und Wiederherstellung ohne Chattexte, Sitzungsdaten oder Schlüssel. Installationsprobleme findest du in /var/log/tiktok-bot-install.log. Dienstmeldungen sind über journalctl -u tiktok-bot verfügbar.

## Entwicklung

npm ci installiert die festgeschriebenen Abhängigkeiten. npm test führt die automatisierten Logiktests aus. Zum lokalen Start legst du das Admin-Passwort mit node src/server.js --init an und startest den Server mit node src/server.js. Ohne weitere Konfiguration hört er nur auf 127.0.0.1:8080. Für Sprachausgabe muss espeak-ng vorhanden sein. GitHub Actions prüft den Dienst auf Ubuntu und stellt für Versions-Tags ein Archiv samt Prüfsumme bereit.
