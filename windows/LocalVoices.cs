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
                var audio = File.ReadAllBytes(output);
                AdjustVolume(audio, volume);
                using (var stream = new MemoryStream(audio))
                using (var player = new SoundPlayer(stream))
                {
                    player.Load();
                    token.ThrowIfCancellationRequested();
                    using (token.Register(player.Stop)) player.PlaySync();
                }
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }, token);
    }

    private static void AdjustVolume(byte[] wav, int volume)
    {
        if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            throw new InvalidDataException();
        var pcm = false;
        for (var offset = 12; offset <= wav.Length - 8;)
        {
            var name = Encoding.ASCII.GetString(wav, offset, 4);
            var length = BitConverter.ToInt32(wav, offset + 4);
            var start = offset + 8;
            if (length < 0 || length > wav.Length - start) throw new InvalidDataException();
            if (name == "fmt " && length >= 16)
                pcm = BitConverter.ToInt16(wav, start) == 1 && BitConverter.ToInt16(wav, start + 14) == 16;
            if (name == "data" && pcm && length % 2 == 0)
            {
                for (var position = start; position < start + length; position += 2)
                {
                    var sample = BitConverter.ToInt16(wav, position) * volume / 100;
                    wav[position] = (byte)sample;
                    wav[position + 1] = (byte)(sample >> 8);
                }
                return;
            }
            if (start + length == wav.Length) break;
            offset = start + length + (length % 2);
        }
        throw new InvalidDataException();
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
