using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Speech.Synthesis;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

internal sealed class LiveComments : UserControl
{
    private readonly ListBox history = new ListBox { Dock = DockStyle.Fill, AccessibleName = "LIVE-Kommentare" };
    private readonly CheckBox read = new CheckBox { Text = "Kommentare vorlesen", AutoSize = true };
    private readonly ComboBox voice = new ThemeComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, AccessibleName = "Stimme" };
    private readonly ComboBox style = new ThemeComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135, AccessibleName = "Stimmeffekt" };
    private readonly NumericUpDown volume = new NumericUpDown { Value = 75, Maximum = 100, Width = 55, AccessibleName = "Lautstärke" };
    private readonly NumericUpDown speed = new NumericUpDown { Value = 100, Minimum = 50, Maximum = 200, Increment = 10, Width = 60, AccessibleName = "Sprechtempo in Prozent" };
    private readonly Button saveVoice = new Button { Text = "Stimme speichern", AutoSize = true };
    private readonly Label savedVoice = new Label { Dock = DockStyle.Bottom, Height = 28, Text = "Stimmänderungen werden automatisch gespeichert." };
    private readonly Timer timer = new Timer { Interval = 2500 };
    private readonly JavaScriptSerializer json = new JavaScriptSerializer();
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 26, Text = "Öffne einen TikTok-LIVE." };
    private SpeechSynthesizer speech;
    private CoreWebView2 browser;
    private Func<string, bool> isAllowedRoom;
    private int generation;
    private bool polling;
    private double? lastPlayback;
    private bool speaking;
    private System.Threading.CancellationTokenSource characterSpeech;
    private int localVoiceCount;
    private readonly Queue<string> pending = new Queue<string>();
    internal event Action<string, string> ViewerComment;
    internal event Action<string, string> ViewerActivity;
    internal event Action<string> AudienceChanged;
    internal bool ChatAvailable { get; private set; }
    internal event Action<bool> ConnectionChanged;
    internal event Action<bool> VoiceSettingsSaved;

    internal void SetConnection(bool connected)
    {
        if (ChatAvailable == connected) return;
        ChatAvailable = connected;
        if (ConnectionChanged != null) ConnectionChanged(connected);
    }

    private const string ReadComments = @"
(() => {
    const root = document.querySelector('[data-e2e=""live-chat-container""], [data-e2e=""chat-room""]');
    if (!root) return null;
    const previous = window.__ttbChatRows;
    const seen = previous && previous.root === root ? previous.seen : new WeakMap();
    window.__ttbChatRows = {root, seen};
    const comments = [];
    for (const row of Array.from(root.querySelectorAll('[data-e2e=""chat-message""]')).slice(-100)) {
        const name = row.querySelector('[data-e2e=""message-owner-name""]');
        const text = row.querySelector('.break-words.align-middle');
        if (!name || !text) continue;
        const link = row.querySelector('a[href^=""/@""]');
        const handle = link && link.getAttribute('href').match(/^\/@([\w.]+)(?:[/?#]|$)/);
        const entry = { Name: name.textContent.trim().slice(0,80), Text: text.textContent.trim().slice(0,300), Handle: handle ? handle[1] : '' };
        if (!entry.Name || !entry.Text) continue;
        const key = JSON.stringify(entry);
        if (previous && previous.root === root && seen.get(row) !== key) comments.push(entry);
        seen.set(row, key);
    }
    return comments;
})()";

    private readonly CheckBox readGifts = new CheckBox { Text = "Geschenke ansagen und danken", AutoSize = true };
    private const string ReadGifts = @"
(() => {
    const root = document.querySelector('[data-e2e=""live-chat-container""], [data-e2e=""chat-room""]');
    if (!root) return [];
    const previous = window.__ttbGiftRows;
    if (previous && previous.root === root) return previous.pending.splice(0);
    if (previous) previous.observer.disconnect();
    const seen = new WeakMap();
    const pending = [];
    const rowSelector = '[data-e2e=""gift-message""], [data-e2e=""live-gift-message""], [data-e2e=""gift-chat-message""], [data-e2e=""live-chat-gift""], [data-e2e=""gift-notice""], [data-e2e=""live-gift-notice""], [data-message-type=""gift""]';
    const giftSelector = '[data-e2e=""gift-name""], [data-e2e=""gift-title""], [data-e2e=""live-gift-name""]';
    const iconSelector = 'img[data-e2e=""gift-icon""][alt], [data-e2e=""gift-image""] img[alt], img[data-e2e=""gift-image""][alt]';
    const textRowSelector = '[data-e2e=""chat-message""], [data-e2e=""chat-notice""], [role=""listitem""]';
    const markerSelector = rowSelector + ', ' + giftSelector + ', ' + iconSelector + ', ' + textRowSelector;
    function scan(node, fresh) {
        if (node.nodeType !== 1) return;
        const rows = new Set();
        const ancestor = node.closest(rowSelector + ', ' + textRowSelector);
        if (ancestor) rows.add(ancestor);
        const markers = Array.from(node.querySelectorAll(markerSelector));
        if (node.matches(markerSelector)) markers.unshift(node);
        for (const marker of markers) {
            const row = marker.closest(rowSelector) || marker.closest(textRowSelector);
            if (row) rows.add(row);
        }
        for (const row of rows) {
            const sender = row.querySelector('[data-e2e=""gift-sender-name""], [data-e2e=""message-owner-name""], a[href^=""/@""]');
            const gift = row.querySelector(giftSelector);
            const icon = row.querySelector(iconSelector);
            const name = ((sender && sender.textContent.trim()) || row.getAttribute('data-sender-name') || '').slice(0,80);
            const text = name && row.textContent.trim();
            const notice = text && text.startsWith(name) && text.slice(name.length).trim();
            const label = notice && notice.match(/^(?:sent\s+(?:(?:a|an)\s+)?(?:a gift\s*[:–-]\s*)?|hat\s+(?:ein Geschenk\s*[:–-]\s*)?)(.{1,80}?)(?:\s+geschickt|\s+gesendet)?(?:\s*[×x]\s*(\d+))?\s*[.!]?$/i);
            const plainIcon = row.querySelector('img[alt]:not([alt=""""])');
            const messageBody = row.querySelector('.break-words.align-middle');
            const structured = row.matches(rowSelector) || gift || icon;
            if (!structured && (!root.contains(row) || messageBody || !label || !plainIcon || (/^hat\s/i.test(notice) && !/\b(geschickt|gesendet|Geschenk)\b/i.test(notice)))) continue;
            const title = ((gift && gift.textContent.trim()) || (icon && icon.alt.trim()) ||
                (label && label[1]) ||
                row.getAttribute('data-gift-name') || '').slice(0,80);
            if (!name || !title) continue;
            const countText = row.getAttribute('data-gift-count') ||
                row.querySelector('[data-e2e=""gift-count""]')?.textContent || (label && label[2]) || '';
            const count = Math.min(9999, Number(countText.match(/\d+/)?.[0]) || 1);
            const key = JSON.stringify([name,title,count]);
            if (fresh && seen.get(row) !== key && pending.length < 100) pending.push({Name:name, Title:title, Count:count});
            seen.set(row,key);
        }
    }
    scan(root,false);
    // Gift banners can be siblings of the chat, and may disappear between polls.
    document.querySelectorAll(rowSelector).forEach(row => scan(row,false));
    const observer = new MutationObserver(records => {
        for (const record of records) {
            const target = record.target.nodeType === 1 ? record.target : record.target.parentElement;
            const row = target && target.closest(rowSelector + ', ' + textRowSelector);
            if (row) scan(row, true);
            if (record.type === 'childList')
                for (const node of record.addedNodes) scan(node,true);
        }
    });
    observer.observe(document.body, {subtree:true, childList:true, characterData:true, attributes:true,
        attributeFilter:['data-e2e','data-message-type','data-sender-name','data-gift-name','data-gift-count','alt']});
    window.__ttbGiftRows = {root, observer, pending};
    return [];
})()";

    internal LiveComments()
    {
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        var test = new Button { Text = "Stimme testen", AutoSize = true };
        var testGift = new Button { Text = "Geschenkansage testen", AutoSize = true };
        testGift.Click += delegate { Speak(GiftAnnouncement("Alex", "Rose")); };
        style.Items.AddRange(new object[] { "Original", "Cartoon · hoch", "Cartoon · tief" });
        style.SelectedIndex = 0;
        toolbar.Controls.AddRange(new Control[] { read, voice, style, new Label { Text = "Lautstärke %", AutoSize = true }, volume,
            new Label { Text = "Tempo %", AutoSize = true }, speed, test, saveVoice, readGifts, testGift });
        Controls.AddRange(new Control[] { history, status, savedVoice, toolbar });
        timer.Tick += Poll;
        read.CheckedChanged += delegate { if (!read.Checked) StopSpeech(); };
        test.Click += delegate { Speak("Das ist ein Test der Sprachausgabe."); };

        try
        {
            speech = new SpeechSynthesizer();
            foreach (var installed in speech.GetInstalledVoices())
                if (installed.Enabled) voice.Items.Add(installed.VoiceInfo.Name);
            if (voice.Items.Count > 0) voice.SelectedIndex = 0;
            speech.SpeakCompleted += delegate
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)SpeechCompleted);
            };
        }
        catch (InvalidOperationException) { }
        localVoiceCount = voice.Items.Count;
        foreach (var name in LocalVoices.Names) voice.Items.Add(name + " · Deutsch (lokal)");
        if (voice.SelectedIndex < 0) voice.SelectedIndex = 0;
        voice.SelectedIndexChanged += delegate
        {
            if (voice.SelectedIndex < localVoiceCount) return;
            StopSpeech();
        };
    }

    internal void Attach(CoreWebView2 core, Func<string, bool> allowRoom)
    {
        browser = core;
        isAllowedRoom = allowRoom;
        browser.NavigationStarting += delegate
        {
            SetConnection(false);
            lastPlayback = null;
            generation++;
            history.Items.Clear();
            StopSpeech();
        };
        timer.Start();
    }

    public sealed class VoicePreferences
    {
        public string Voice { get; set; }
        public string Effect { get; set; }
        public decimal Volume { get; set; }
        public decimal Speed { get; set; }
        public bool ReadComments { get; set; }
        public bool ReadGifts { get; set; }
    }

    internal void LoadVoicePreferences(string directory)
    {
        var path = Path.Combine(directory, "voice-settings.json");
        try
        {
            if (File.Exists(path))
            {
                var saved = json.Deserialize<VoicePreferences>(File.ReadAllText(path));
                if (saved != null)
                {
                    if (voice.Items.Contains(saved.Voice ?? "")) voice.SelectedItem = saved.Voice;
                    if (style.Items.Contains(saved.Effect ?? "")) style.SelectedItem = saved.Effect;
                    if (saved.Volume >= volume.Minimum && saved.Volume <= volume.Maximum) volume.Value = saved.Volume;
                    if (saved.Speed >= speed.Minimum && saved.Speed <= speed.Maximum) speed.Value = saved.Speed;
                    read.Checked = saved.ReadComments;
                    readGifts.Checked = saved.ReadGifts;
                    savedVoice.Text = "Stimmeinstellungen geladen.";
                }
            }
        }
        catch (IOException error) { ErrorLog.Write("Audio", "Stimmeinstellungen konnten nicht geladen werden.", error); }
        catch (UnauthorizedAccessException error) { ErrorLog.Write("Audio", "Stimmeinstellungen konnten nicht geladen werden.", error); }
        catch (ArgumentException error) { ErrorLog.Write("Audio", "Stimmeinstellungen sind ungültig.", error); }
        catch (InvalidOperationException error) { ErrorLog.Write("Audio", "Stimmeinstellungen sind ungültig.", error); }

        EventHandler save = delegate
        {
            var preferences = new VoicePreferences
            {
                Voice = voice.SelectedItem as string, Effect = style.SelectedItem as string,
                Volume = volume.Value, Speed = speed.Value, ReadComments = read.Checked, ReadGifts = readGifts.Checked
            };
            try
            {
                Directory.CreateDirectory(directory);
                var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(staging, json.Serialize(preferences));
                if (File.Exists(path)) File.Replace(staging, path, null);
                else File.Move(staging, path);
                savedVoice.Text = "Stimmeinstellungen gespeichert.";
                if (VoiceSettingsSaved != null) VoiceSettingsSaved(true);
            }
            catch (IOException error) { ReportVoiceSaveError(error); }
            catch (UnauthorizedAccessException error) { ReportVoiceSaveError(error); }
        };
        voice.SelectedIndexChanged += save;
        style.SelectedIndexChanged += save;
        volume.ValueChanged += save;
        speed.ValueChanged += save;
        read.CheckedChanged += save;
        readGifts.CheckedChanged += save;
        saveVoice.Click += save;
    }

    private void ReportVoiceSaveError(Exception error)
    {
        savedVoice.Text = "Stimmeinstellungen konnten nicht gespeichert werden.";
        ErrorLog.Write("Audio", savedVoice.Text, error);
        if (VoiceSettingsSaved != null) VoiceSettingsSaved(false);
    }

    internal static bool VerifyVoicePreferences(string directory)
    {
        using (var first = new LiveComments())
        using (var second = new LiveComments())
        {
            first.LoadVoicePreferences(directory);
            first.voice.SelectedIndex = first.voice.Items.Count - 2;
            first.style.SelectedIndex = 2;
            first.volume.Value = 42;
            first.speed.Value = 130;
            first.read.Checked = true;
            first.readGifts.Checked = true;
            first.saveVoice.PerformClick();
            second.LoadVoicePreferences(directory);
            return Equals(first.voice.SelectedItem, second.voice.SelectedItem) &&
                second.style.SelectedIndex == 2 && second.volume.Value == 42 && second.speed.Value == 130 && second.read.Checked && second.readGifts.Checked;
        }
    }

    internal static async System.Threading.Tasks.Task<bool> VerifyReader(CoreWebView2 core)
    {
        var serializer = new JavaScriptSerializer();
        var initial = serializer.Deserialize<List<Comment>>(await core.ExecuteScriptAsync(ReadComments));
        await core.ExecuteScriptAsync("const row = document.querySelector('[data-e2e=chat-message]').cloneNode(true);" +
            "row.querySelector('.break-words').textContent = 'Wie heißt das Spiel?';" +
            "document.querySelector('[data-e2e=chat-room]').appendChild(row);");
        var added = serializer.Deserialize<List<Comment>>(await core.ExecuteScriptAsync(ReadComments));
        var repeated = serializer.Deserialize<List<Comment>>(await core.ExecuteScriptAsync(ReadComments));
        await core.ExecuteScriptAsync(ReadGifts);
        await core.ExecuteScriptAsync("document.querySelector('[data-e2e=chat-room]').insertAdjacentHTML('beforeend', " +
            "\"<div data-e2e='gift-message'><b data-e2e='gift-sender-name'>Alex</b><span data-e2e='gift-name'>Rose</span></div>\");");
        var gifts = serializer.Deserialize<List<Gift>>(await core.ExecuteScriptAsync(ReadGifts));
        var repeatedGifts = serializer.Deserialize<List<Gift>>(await core.ExecuteScriptAsync(ReadGifts));
        await core.ExecuteScriptAsync("document.querySelector('[data-e2e=chat-room]').insertAdjacentHTML('beforeend', " +
            "\"<div data-e2e='live-chat-gift'><a href='/@sam'>Sam</a><img data-e2e='gift-icon' alt='Stern'></div>\");");
        await core.ExecuteScriptAsync("document.body.insertAdjacentHTML('beforeend', " +
            "\"<div data-e2e='live-gift-notice' data-sender-name='Kim' data-gift-name='Rose' data-gift-count='3'></div>\");" +
            "queueMicrotask(() => document.querySelector('[data-e2e=live-gift-notice]').remove());");
        var iconGifts = serializer.Deserialize<List<Gift>>(await core.ExecuteScriptAsync(ReadGifts));
        return initial != null && initial.Count == 0 && added != null && added.Count == 1 &&
            added[0].Name == "Zuschauer" && added[0].Text == "Wie heißt das Spiel?" &&
            repeated != null && repeated.Count == 0 && gifts.Count == 1 && repeatedGifts.Count == 0 &&
            iconGifts.Count == 2 && iconGifts[0].Name == "Sam" && iconGifts[0].Title == "Stern" &&
            iconGifts[1].Name == "Kim" && iconGifts[1].Count == 3 &&
            GiftAnnouncement(gifts[0].Name, gifts[0].Title) == "Alex hat ein Geschenk geschickt: Rose. Danke, Alex, für Rose!";
    }

    private async void Poll(object sender, EventArgs args)
    {
        if (polling || browser == null) return;
        if (!isAllowedRoom(browser.Source))
        {
            SetConnection(false);
            lastPlayback = null;
            status.Text = "Bot wartet auf das gespeicherte LIVE-Konto.";
            return;
        }

        polling = true;
        var current = generation;
        try
        {
            var playbackResult = await browser.ExecuteScriptAsync(@"(() => {
                const room = document.querySelector('[data-e2e=""live-player""], [data-e2e=""live-player-container""]');
                const video = room ? (room.matches('video') ? room : room.querySelector('video')) :
                    Array.from(document.querySelectorAll('video')).find(item => item.duration === Infinity);
                if (!video || video.paused || video.ended || video.readyState < 2 || !video.videoWidth ||
                    document.querySelector('[data-e2e=""live-ended""], [data-e2e=""live-offline""]')) return null;
                return video.currentTime;
            })()");
            if (IsDisposed || current != generation || !isAllowedRoom(browser.Source)) return;
            var playback = json.Deserialize<double?>(playbackResult);
            var progressing = IsPlaybackAdvancing(lastPlayback, playback);
            lastPlayback = playback;
            var giftResult = await browser.ExecuteScriptAsync(ReadGifts);
            if (IsDisposed || current != generation || !isAllowedRoom(browser.Source)) return;
            foreach (var gift in json.Deserialize<List<Gift>>(giftResult))
            {
                var announcement = GiftAnnouncement(gift.Name, gift.Title, gift.Count);
                if (history.Items.Count == 200) history.Items.RemoveAt(0);
                history.Items.Add(announcement);
                history.TopIndex = history.Items.Count - 1;
                if (readGifts.Checked) Speak(announcement, true);
            }
            if (!progressing)
            {
                SetConnection(false);
                if (AudienceChanged != null) AudienceChanged(null);
                status.Text = "Warte auf ein laufendes LIVE-Video.";
                return;
            }
            var result = await browser.ExecuteScriptAsync(ReadComments);
            var audienceResult = await browser.ExecuteScriptAsync("document.querySelector('[data-e2e=live-viewer-count], [data-e2e=live-view-count]')?.textContent.trim().slice(0, 32) ?? null");
            if (IsDisposed || current != generation || !isAllowedRoom(browser.Source)) return;
            var audience = json.Deserialize<string>(audienceResult);
            if (AudienceChanged != null) AudienceChanged(string.IsNullOrWhiteSpace(audience) ? null : audience);
            var comments = json.Deserialize<List<Comment>>(result);
            SetConnection(comments != null);
            status.Text = comments == null ? "Warte auf den LIVE-Chat …" : "LIVE-Chat wird mitgelesen.";
            if (comments == null) return;
            foreach (var comment in comments)
            {
                var line = comment.Name + ": " + comment.Text;
                if (history.Items.Count == 200) history.Items.RemoveAt(0);
                history.Items.Add(line);
                history.TopIndex = history.Items.Count - 1;
                if (read.Checked) Speak(line);
                var onViewer = ViewerComment;
                if (ViewerActivity != null) ViewerActivity(comment.Handle, comment.Name);
                if (onViewer != null) onViewer(comment.Handle, comment.Text);
            }
        }
        catch (InvalidOperationException error) { ReportPollError(current, "LIVE-Chat vorübergehend nicht verfügbar.", error); }
        catch (ArgumentException error) { ReportPollError(current, "LIVE-Chat konnte nicht gelesen werden.", error); }
        finally { polling = false; }
    }

    private void ReportPollError(int current, string message, Exception error)
    {
        if (IsDisposed || current != generation) return;
        SetConnection(false);
        lastPlayback = null;
        ReportError("LIVE-Chat", message, error);
    }

    internal static bool IsPlaybackAdvancing(double? previous, double? current)
    {
        return previous.HasValue && current.HasValue && current.Value > previous.Value;
    }

    internal static string GiftAnnouncement(string name, string title, int count = 1)
    {
        return name + " hat ein Geschenk geschickt: " + (count > 1 ? count + " mal " : "") + title + ". " + ChatReplies.GiftThanks(name, title, Math.Max(1, count));
    }

    private sealed class Gift
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public int Count { get; set; }
    }

    private void Speak(string text, bool gift = false)
    {
        if (voice.SelectedItem == null) return;
        if (gift)
        {
            if (pendingGifts.Count >= 20) { ErrorLog.Write("Geschenke", "Die Sprachausgabe für Geschenke ist ausgelastet."); return; }
            pendingGifts.Enqueue(text);
        }
        else
        {
            if (pending.Count == 5) pending.Dequeue();
            pending.Enqueue(text);
        }
        if (!speaking) SpeechCompleted();
    }

    private readonly Queue<string> pendingGifts = new Queue<string>();

    private async void SpeechCompleted()
    {
        speaking = false;
        if ((pendingGifts.Count == 0 && pending.Count == 0) || IsDisposed) return;
        if (voice.SelectedIndex >= localVoiceCount)
        {
            var text = pendingGifts.Count > 0 ? pendingGifts.Dequeue() : pending.Dequeue();
            var cancellation = new System.Threading.CancellationTokenSource();
            characterSpeech = cancellation;
            speaking = true;
            try { await LocalVoices.Speak(voice.SelectedIndex - localVoiceCount, text, (int)volume.Value, (int)speed.Value, cancellation.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!IsDisposed && !cancellation.IsCancellationRequested) ReportError("Audio", "Lokale Stimme nicht verfügbar.", error);
            }
            finally
            {
                characterSpeech = null;
                cancellation.Dispose();
                if (!IsDisposed) SpeechCompleted();
            }
            return;
        }
        try
        {
            speech.SelectVoice((string)voice.SelectedItem);
            speech.Volume = (int)volume.Value;
            speech.Rate = (int)Math.Round(10 * Math.Log((double)speed.Value / 100, 2));
            var text = pendingGifts.Count > 0 ? pendingGifts.Dequeue() : pending.Dequeue();
            if (style.SelectedIndex == 0) speech.SpeakAsync(text);
            else
            {
                var pitch = style.SelectedIndex == 1 ? "high" : "low";
                var rate = style.SelectedIndex == 1 ? "fast" : "slow";
                speech.SpeakSsmlAsync("<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='" +
                    speech.Voice.Culture.Name + "'><prosody pitch='" + pitch + "' rate='" + rate + "'>" +
                    System.Security.SecurityElement.Escape(text) + "</prosody></speak>");
            }
            speaking = true;
        }
        catch (InvalidOperationException error) { ReportError("Audio", "Sprachausgabe nicht verfügbar.", error); }
        catch (ArgumentException error) { ReportError("Audio", "Die ausgewählte Stimme ist nicht verfügbar.", error); }
    }

    private void ReportError(string area, string message, Exception error)
    {
        status.Text = message;
        ErrorLog.Write(area, message, error);
    }

    private void StopSpeech()
    {
        pending.Clear();
        pendingGifts.Clear();
        if (characterSpeech != null) characterSpeech.Cancel();
        else if (speaking && speech != null) speech.SpeakAsyncCancelAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            if (characterSpeech != null) characterSpeech.Cancel();
            if (speech != null) speech.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class Comment
    {
        public string Name { get; set; }
        public string Text { get; set; }
        public string Handle { get; set; }
    }
}
