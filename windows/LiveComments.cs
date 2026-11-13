using System;
using System.Collections.Generic;
using System.Drawing;
using System.Speech.Synthesis;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

internal sealed class LiveComments : UserControl
{
    private readonly ListBox history = new ListBox { Dock = DockStyle.Fill, AccessibleName = "LIVE-Kommentare" };
    private readonly CheckBox read = new CheckBox { Text = "Kommentare vorlesen", AutoSize = true };
    private readonly ComboBox voice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, AccessibleName = "Stimme" };
    private readonly ComboBox style = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135, AccessibleName = "Stimmeffekt" };
    private readonly NumericUpDown volume = new NumericUpDown { Value = 75, Maximum = 100, Width = 55, AccessibleName = "Lautstärke" };
    private readonly Timer timer = new Timer { Interval = 2500 };
    private readonly JavaScriptSerializer json = new JavaScriptSerializer();
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 26, Text = "Öffne einen TikTok-LIVE." };
    private SpeechSynthesizer speech;
    private CoreWebView2 browser;
    private int generation;
    private bool polling;
    private bool speaking;
    private readonly Queue<string> pending = new Queue<string>();
    internal event Action<string> CommentReceived;

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
        const entry = { Name: name.textContent.trim().slice(0,80), Text: text.textContent.trim().slice(0,300) };
        if (!entry.Name || !entry.Text) continue;
        const key = JSON.stringify(entry);
        if (previous && previous.root === root && seen.get(row) !== key) comments.push(entry);
        seen.set(row, key);
    }
    return comments;
})()";

    internal LiveComments()
    {
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        var test = new Button { Text = "Stimme testen", AutoSize = true };
        style.Items.AddRange(new object[] { "Original", "Cartoon · hoch", "Cartoon · tief" });
        style.SelectedIndex = 0;
        toolbar.Controls.AddRange(new Control[] { read, voice, style, new Label { Text = "Lautstärke %", AutoSize = true }, volume, test });
        Controls.AddRange(new Control[] { history, status, toolbar });
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
        read.Enabled = test.Enabled = voice.Enabled = voice.Items.Count > 0;
    }

    internal void Attach(CoreWebView2 core)
    {
        browser = core;
        browser.NavigationStarting += delegate
        {
            generation++;
            history.Items.Clear();
            StopSpeech();
        };
        timer.Start();
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
        return initial != null && initial.Count == 0 && added != null && added.Count == 1 &&
            added[0].Name == "Zuschauer" && added[0].Text == "Wie heißt das Spiel?" &&
            repeated != null && repeated.Count == 0;
    }

    private async void Poll(object sender, EventArgs args)
    {
        if (polling || browser == null) return;
        Uri source;
        if (!Uri.TryCreate(browser.Source, UriKind.Absolute, out source) || source.Scheme != "https" ||
            !source.IsDefaultPort || source.Host != "www.tiktok.com" ||
            !System.Text.RegularExpressions.Regex.IsMatch(source.AbsolutePath, @"\A/@[a-zA-Z0-9_.]{1,64}/live/?\z"))
        {
            status.Text = "Öffne einen TikTok-LIVE.";
            return;
        }

        polling = true;
        var current = generation;
        try
        {
            var result = await browser.ExecuteScriptAsync(ReadComments);
            if (IsDisposed || current != generation) return;
            var comments = json.Deserialize<List<Comment>>(result);
            status.Text = comments == null ? "Warte auf den LIVE-Chat …" : "LIVE-Chat wird mitgelesen.";
            if (comments == null) return;
            foreach (var comment in comments)
            {
                var line = comment.Name + ": " + comment.Text;
                if (history.Items.Count == 200) history.Items.RemoveAt(0);
                history.Items.Add(line);
                history.TopIndex = history.Items.Count - 1;
                if (read.Checked) Speak(line);
                var onComment = CommentReceived;
                if (onComment != null) onComment(comment.Text);
            }
        }
        catch (InvalidOperationException) { if (!IsDisposed) status.Text = "LIVE-Chat vorübergehend nicht verfügbar."; }
        catch (ArgumentException) { if (!IsDisposed) status.Text = "LIVE-Chat konnte nicht gelesen werden."; }
        finally { polling = false; }
    }

    private void Speak(string text)
    {
        if (speech == null || voice.SelectedItem == null) return;
        if (pending.Count == 5) pending.Dequeue();
        pending.Enqueue(text);
        if (!speaking) SpeechCompleted();
    }

    private void SpeechCompleted()
    {
        speaking = false;
        if (pending.Count == 0 || IsDisposed) return;
        try
        {
            speech.SelectVoice((string)voice.SelectedItem);
            speech.Volume = (int)volume.Value;
            var text = pending.Dequeue();
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
        catch (InvalidOperationException) { status.Text = "Sprachausgabe nicht verfügbar."; }
        catch (ArgumentException) { status.Text = "Die ausgewählte Stimme ist nicht verfügbar."; }
    }

    private void StopSpeech()
    {
        pending.Clear();
        if (speaking) speech.SpeakAsyncCancelAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            if (speech != null) speech.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class Comment
    {
        public string Name { get; set; }
        public string Text { get; set; }
    }
}
