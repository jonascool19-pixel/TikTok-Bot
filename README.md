# TikTok-Bot

Selbst betriebenes Dashboard für TikTok LIVE auf Proxmox. Ein Ubuntu-CT empfängt Ereignisse, erzeugt lokale Sprachausgabe und kann über ein separates Konto im Chat schreiben. Es spielt keine Rolle, ob der Creator vom Handy oder mit TikTok LIVE Studio sendet.

## Installation

Vorausgesetzt ist ein Ubuntu-24.04-CT auf x86-64 mit systemd, Internetzugang . Führe den Befehl als root **im CT** aus:

~~~bash
installer=$(mktemp) && curl -fsSL https://raw.githubusercontent.com/jonascool19-pixel/TikTok-Bot/v0.1.18/install.sh -o "$installer" && bash "$installer"
~~~

Bei einer Neuinstallation wählt der Installer einen freien TCP-Port. Updates behalten den bisherigen Port. Öffne die angezeigte CT-Adresse im Heimnetz. Der Installer aktualisiert die Ubuntu-Pakete, installiert die Anwendung und prüft die HTTP-Erreichbarkeit des Dienstes. Das farbige Abschlussfeld zeigt Dashboard-URL, Benutzer und Startpasswort. Das Startpasswort erscheint nur bei der Ersteinrichtung. Beim ersten Login ist ein Wechsel Pflicht. Wiederholte Installationen lassen die gespeicherten Einstellungen und das Passwort bestehen.

Falls im CT curl fehlt, installiere es zunächst mit apt-get update && apt-get install -y curl. Für die Installation aus einem entpackten GitHub-Actions-Artefakt genügt bash install.sh im Paketverzeichnis. Der Installer erzeugt kein öffentliches Port-Forwarding. Nutze das Dashboard nur in deinem vertrauenswürdigen Heimnetz oder hinter einem HTTPS-Reverse-Proxy. Für HTTPS lässt sich BOT_SECURE_COOKIE=1 in /etc/tiktok-bot.env setzen. Die Mikrofonaufnahme im Browser benötigt HTTPS oder localhost. Über HTTP funktioniert der Upload vorhandener Audioclips.

## TikTok einrichten und testen

1. Trage deinen Creator-@Namen im Dashboard ein. Die Verbindung nutzt den inoffiziellen [TikTok LIVE Connector](https://ttlc.docs.isaackogan.com/). Öffentliche Streams können normalerweise ohne TikTok-Sitzung empfangen werden. Ein Euler-Stream-Schlüssel kann je nach Anbieterzugang und Limits nötig sein.
2. Erstelle für Chatantworten ein separates [TikTok-Konto](https://www.tiktok.com/signup) mit einer dauerhaft erreichbaren E-Mail-Adresse. Setze den Anzeigenamen im TikTok-Profil.
3. Hinterlege im Dashboard den @Namen, eine aktive Sitzung dieses Kontos gemäß der [Anmeldedokumentation](https://ttlc.docs.isaackogan.com/configuration/authentication/) und einen Euler-Stream-Schlüssel mit Freigabe für Chatversand. Der [Sendeweg](https://ttlc.docs.isaackogan.com/guides/send-chat/) verwendet den Drittanbieter und übermittelt beim Senden die Bot-Sitzungsdaten. Der Connector weist für den Sendeweg auf einen kostenpflichtigen Tarif hin. Das öffentliche Repo und die lokale Sprachausgabe setzen kein Abo voraus.
4. Prüfe Kommentare, Geschenke, Begrüßungen und Stimmen im Soundcheck. Die Simulation sendet keine echten Nachrichten. Für die echte Verbindung starte deinen LIVE und sende eine Testnachricht. Prüfe im TikTok-Chat, ob sie unter dem Bot-Konto erscheint.
5. Öffne die Wiedergabeseite auf einem Gerät im Heimnetz und aktiviere die Ausgabe. Für das Publikum muss dieser Ton in den Audiomix deines Streaming-Setups gelangen. Beim Streaming vom Handy hängt das von deiner Audiohardware ab.

Für natürliche lokale Sprachausgabe stehen Thorsten, Kerstin und Amy (Piper) zur Auswahl. Weitere Sprachen und Stimmeffekte laufen über eSpeak NG. Du kannst stattdessen eigene Audioclips für Ereignisse hinterlegen. Geschenke können einen gesprochenen oder geschriebenen Dank auslösen. Begrüßungen erfolgen nur für tatsächlich empfangene Beitrittsereignisse und haben eine Sendepause. Feste Chatantworten benötigen kein Sprachmodell. Für freie Antworten kannst du einen OpenAI-kompatiblen Chat-Completions-Endpunkt konfigurieren. Ausgewählte Texte gehen dann an diesen Dienst. Meldet sich der Creator im Chat, verwirft der Bot die noch wartenden Antworten.

## Bot-Konto per QR-Code verbinden

Die QR-Anmeldung über Euler Stream übernimmt den Bot-Namen und verbindet das Konto für den LIVE-Chat. Sie benötigt eine einmalige Einrichtung pro Installation:

1. Öffne das Dashboard über seinen HTTPS-Hostnamen. Melde dich mit einem separaten [Euler-Stream-Konto](https://www.eulerstream.com/register) an und richte dort einen OAuth-Client ein und registriere `https://DEIN-HOST/api/tiktok/euler/callback`. Der Client benötigt die Freigaben `user:info` und `webcast:chat`.
2. Ergänze die Datei `/etc/tiktok-bot.env` (nur für root lesbar):

~~~dotenv
BOT_SECURE_COOKIE=1
BOT_EULER_CLIENT_ID=DEINE-CLIENT-ID
BOT_EULER_CLIENT_SECRET=DEIN-CLIENT-SECRET
BOT_EULER_REDIRECT_URI=https://DEIN-HOST/api/tiktok/euler/callback
BOT_EULER_VAULT_KEY=DEIN-ZUFAELLIGER-64-STELLIGER-HEX-SCHLUESSEL
~~~

3. Erzeuge den Schlüssel mit `openssl rand -hex 32` und starte danach den Bot-Dienst neu. Hinterlege deinen Creator-Namen. Klicke auf **Bot-Konto verbinden** und bestätige die Anmeldung in TikTok mit deinem separaten Bot-Konto.

Für den Versuch ohne Handy lässt du die Euler-Anmeldung geöffnet und wählst einen Screenshot des QR-Codes unter **QR-Code am PC öffnen** aus. Der Browser liest das Bild lokal und zeigt einen Link an, wenn der Code einen HTTPS-Link zu TikTok enthält. Ob TikTok die Freigabe im Browser anbietet, zeigt der echte Anmeldeversuch.

Der Bot speichert die Verbindung verschlüsselt und erneuert den Zugriff bei Bedarf. Nach Ablauf des Refresh-Tokens musst du das Konto erneut verbinden. **Bot-Sitzung entfernen** löscht die lokale Verbindung. Die Freigabe kannst du zusätzlich bei Euler Stream widerrufen. Der Dashboard-Export enthält diese OAuth-Verbindung nicht. [Euler Stream OAuth](https://www.eulerstream.com/docs/oauth) · [Versandberechtigung](https://www.eulerstream.com/docs/oauth/scopes)

## TikTok-Profilname per Login Kit

Wenn du bereits HTTPS für das Dashboard eingerichtet hast, kannst du über TikToks Login Kit den @Namen und den Anzeigenamen des Bot-Kontos übernehmen. Registriere dafür in deiner TikTok-Developer-App die HTTPS-Rücksprungadresse `https://DEIN-HOST/api/tiktok/profile/callback` und lass die Berechtigungen `user.info.basic` und `user.info.profile` für die App freigeben. Ergänze diese Werte in `/etc/tiktok-bot.env`:

~~~dotenv
BOT_SECURE_COOKIE=1
BOT_TIKTOK_CLIENT_KEY=DEIN-TIKTOK-CLIENT-KEY
BOT_TIKTOK_CLIENT_SECRET=DEIN-TIKTOK-CLIENT-SECRET
BOT_TIKTOK_REDIRECT_URI=https://DEIN-HOST/api/tiktok/profile/callback
~~~

Nach dem Neustart des Dienstes erscheint unter **TikTok-Verbindung** der Button „TikTok-Konto verbinden“ für die Namensübernahme. Der Creator-Name bleibt unverändert. Login Kit erteilt keine Berechtigung zum Schreiben im LIVE-Chat. Dafür bleibt der in Schritt 3 beschriebene Zugang nötig. Beim Wechsel auf einen anderen Bot-@Namen wird der Versand deaktiviert, bis du dessen Sitzung einrichtest. [TikTok-Profilfelder](https://developers.tiktok.com/docs/en/tiktok-api-v2-get-user-info)

## Backup und Fehlerdiagnose

Exportiere im Dashboard ein verschlüsseltes Backup der Einstellungen und Aufnahmen. Zugangsdaten lassen sich optional einschließen. Beim Wiederherstellen bleibt das Admin-Passwort der Zielinstallation bestehen. Chatversand und freie Antworten müssen erneut aktiviert werden. Prüfe auch, ob die TikTok-Sitzung noch gültig ist.

Das Dashboard zeigt die letzten 200 Einträge aus dem begrenzten Fehlerlog und kann sie als JSONL exportieren. Die Anwendung protokolliert Fehler für TikTok, Versand, Audio, KI, Anmeldung und Wiederherstellung ohne Chattexte, Sitzungsdaten oder Schlüssel. Installationsprobleme findest du in /var/log/tiktok-bot-install.log. Dienstmeldungen sind über journalctl -u tiktok-bot verfügbar.

## Entwicklung

npm ci installiert die festgeschriebenen Abhängigkeiten. npm test führt die automatisierten Logiktests aus. Zum lokalen Start legst du das Admin-Passwort mit node src/server.js --init an und startest den Server mit node src/server.js. Ohne weitere Konfiguration hört er nur auf 127.0.0.1:8080. Für Sprachausgabe muss espeak-ng vorhanden sein. GitHub Actions prüft den Dienst auf Ubuntu und stellt für Versions-Tags ein Archiv samt Prüfsumme bereit.

## Lokaler TikTok-Browser (Vorschau)

Unter TikTok-Verbindung den Browsermodus aktivieren und speichern. Für die Anmeldung über HTTPS das Admin-Passwort erneut eingeben, dich in der angezeigten TikTok-Seite anmelden und Bot-Konto übernehmen wählen. Das Chromium-Profil bleibt lokal. Für Chatversand den Versand aktivieren und speichern. Der Modus nutzt keine Euler-API und liest sichtbare Textkommentare. Geschenke und Beitritte werden in diesem Modus noch nicht erfasst. TikTok-Seitenänderungen und die Sandbox-Konfiguration des Proxmox-CT können den Betrieb beeinflussen.
