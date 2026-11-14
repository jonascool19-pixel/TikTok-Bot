using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

internal sealed class LiveStats : UserControl
{
    private sealed class Activity
    {
        internal string Label;
        internal int Count;
        internal DateTime First;
        internal DateTime Last;
    }

    private readonly Dictionary<string, Activity> viewers = new Dictionary<string, Activity>(StringComparer.OrdinalIgnoreCase);
    private readonly Label summary = new Label { Dock = DockStyle.Top, Height = 62 };
    private readonly ListView ranking = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private string room;
    private string audience;
    private int total;

    internal LiveStats()
    {
        ranking.Columns.Add("Chatkonto", 260);
        ranking.Columns.Add("Kommentare", 100);
        ranking.Columns.Add("Erster Kommentar", 155);
        ranking.Columns.Add("Letzter Kommentar", 155);
        Controls.AddRange(new Control[] { ranking, summary });
        Render();
    }

    internal void SetRoom(string target)
    {
        if (target == null || string.Equals(room, target, StringComparison.OrdinalIgnoreCase)) return;
        room = target;
        audience = null;
        total = 0;
        viewers.Clear();
        Render();
    }

    internal void SetAudience(string value)
    {
        if (audience == value) return;
        audience = value;
        Render();
    }

    internal void Record(string handle, string name)
    {
        total++;
        if (!string.IsNullOrEmpty(handle))
        {
            var now = DateTime.Now;
            Activity entry;
            if (!viewers.TryGetValue(handle, out entry))
            {
                entry = new Activity { Label = name + " (@" + handle + ")", First = now };
                viewers.Add(handle, entry);
            }
            entry.Count++;
            entry.Last = now;
        }
        Render();
    }

    private void Render()
    {
        summary.Text = "Zuschauer laut TikTok: " + (audience ?? "nicht verfügbar") +
            "    Erkannte Chatkonten: " + viewers.Count + "    Kommentare: " + total +
            Environment.NewLine + "Verweildauer, eindeutige Gesamtzuschauer und Geschenke: keine verlässlichen Daten aus der LIVE-Ansicht.";
        ranking.BeginUpdate();
        try
        {
            ranking.Items.Clear();
            foreach (var entry in viewers.Values.OrderByDescending(item => item.Count).ThenBy(item => item.First).Take(100))
                ranking.Items.Add(new ListViewItem(new[] { entry.Label, entry.Count.ToString(), entry.First.ToString("HH:mm:ss"), entry.Last.ToString("HH:mm:ss") }));
        }
        finally { ranking.EndUpdate(); }
    }
}
