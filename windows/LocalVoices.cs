using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class LocalVoices
{
    internal static readonly string[] Names = { "Kerstin", "Thorsten · neutral", "Thorsten · amüsiert", "Thorsten · überrascht", "Thorsten · flüsternd", "Thorsten · müde" };
    private static readonly int[] Speakers = { 0, 4, 0, 6, 7, 5 };

    internal static Task Speak(int index, string text, int volume, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var piper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper");
            var model = index == 0 ? "de_DE-kerstin-low" : "de_DE-thorsten_emotional-medium";
            var output = Path.Combine(Path.GetTempPath(), "TikTokBot-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                token.ThrowIfCancellationRequested();
                using (var process = Process.Start(new ProcessStartInfo(Path.Combine(piper, "piper.exe"),
                    "--model \"" + Path.Combine(piper, model + ".onnx") + "\" --speaker " + Speakers[index] +
                    " --volume " + (volume / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " --output_file \"" + output + "\"")
                {
                    WorkingDirectory = piper, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
                }))
                {
                    using (token.Register(() => Stop(process)))
                    {
                        // Piper reads UTF-8 from stdin regardless of the Windows console code page.
                        using (var input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
                            input.WriteLine(text.Replace('\r', ' ').Replace('\n', ' '));
                        if (!process.WaitForExit(15000))
                        {
                            Stop(process);
                            throw new TimeoutException();
                        }
                        token.ThrowIfCancellationRequested();
                        if (process.ExitCode != 0 || !File.Exists(output)) throw new InvalidDataException();
                    }
                }
                using (var player = new SoundPlayer(output))
                {
                    player.Load();
                    token.ThrowIfCancellationRequested();
                    using (token.Register(player.Stop)) player.PlaySync();
                }
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }, token);
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
