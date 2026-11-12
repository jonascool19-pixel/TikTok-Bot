# TikTok-Bot

Eigenes Dashboard und Assistent für TikTok LIVE in einem Ubuntu-Container auf Proxmox. Das Projekt befindet sich im Aufbau. Es gibt noch kein Installationspaket.

Die erste Komponente verarbeitet Kommentare, Geschenke und Beitritte. Aus denselben normalisierten Ereignissen sollen später sowohl der Soundcheck als auch die TikTok-Verbindung Aktionen auslösen.

## Lokal prüfen

Mit Node.js ab Version 22 im Projektverzeichnis `node --test` ausführen. Die Tests brauchen weder ein TikTok-Konto noch Zugangsdaten.

## Geplanter Betrieb

- Creator-Konto als Ziel zum Mitlesen und separates Bot-Konto zum Schreiben.
- Lokale Sprachausgabe, eigene Clips, Soundcheck und Einstellungen im Browser.
- Begrüßungen, Geschenkdank, feste Chatbefehle und optionale freie Antworten.
- Installer für Ubuntu 24.04 auf x86-64 mit Startpasswort und erkannter Dashboard-Adresse.
- Verschlüsselte Sicherung der Einstellungen und Aufnahmen mit Wiederherstellung auf einem frischen Container.

Ein echter LIVE ist erforderlich, um Empfang und Versand mit dem verbundenen TikTok-Konto zu bestätigen.
