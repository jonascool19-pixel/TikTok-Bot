using System;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media;

internal sealed class MusicQueue : UserControl
{
    private readonly MediaPlayer player = new MediaPlayer();
    private readonly ListBox queue = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Musikwarteschlange" };
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 28, Text = "Keine Wiedergabe" };
    private bool playing;

    internal MusicQueue()
    {
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        AddButton(tools, "Dateien hinzufügen", AddFiles);
        AddButton(tools, "Abspielen", delegate { if (!playing) Next(); });
        AddButton(tools, "Überspringen", Next);
        AddButton(tools, "Stopp", Stop);
        AddButton(tools, "Auswahl entfernen", delegate
        {
            if (queue.SelectedIndex >= 0) queue.Items.RemoveAt(queue.SelectedIndex);
        });
        var volume = new NumericUpDown { Maximum = 100, Value = 35, Width = 55, AccessibleName = "Musiklautstärke" };
        tools.Controls.Add(new Label { Text = "Lautstärke %", AutoSize = true });
        tools.Controls.Add(volume);
        player.Volume = (double)volume.Value / 100;
        volume.ValueChanged += delegate { player.Volume = (double)volume.Value / 100; };
        player.MediaEnded += delegate { if (playing) Next(); };
        player.MediaFailed += delegate
        {
            if (!playing) return;
            Stop();
            status.Text = "Datei konnte nicht abgespielt werden. Wähle Abspielen für den nächsten Titel.";
        };
        queue.DisplayMember = "Name";
        Controls.AddRange(new Control[] { queue, status, tools });
    }

    private static void AddButton(Control tools, string caption, Action action)
    {
        var button = new Button { Text = caption, AutoSize = true };
        button.Click += delegate { action(); };
        tools.Controls.Add(button);
    }

    private void AddFiles()
    {
        using (var picker = new OpenFileDialog { Multiselect = true, Filter = "Audio|*.mp3;*.wav;*.wma;*.m4a;*.aac" })
        {
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            foreach (var file in picker.FileNames) queue.Items.Add(new FileInfo(file));
        }
    }

    private void Next()
    {
        Stop();
        if (queue.Items.Count == 0) return;
        var track = (FileInfo)queue.Items[0];
        queue.Items.RemoveAt(0);
        if (!track.Exists)
        {
            status.Text = "Datei nicht gefunden: " + track.Name;
            return;
        }
        player.Open(new Uri(track.FullName));
        player.Play();
        playing = true;
        status.Text = "Läuft: " + track.Name;
    }

    private void Stop()
    {
        playing = false;
        player.Close();
        status.Text = "Keine Wiedergabe";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) player.Close();
        base.Dispose(disposing);
    }
}
