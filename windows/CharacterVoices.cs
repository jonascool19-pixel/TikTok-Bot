using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class CharacterVoices
{
    internal static readonly string[] Names = { "Eric Cartman", "Peter Griffin", "Stewie Griffin" };
    private static readonly string[] Models = {
        "weight_3k28fws0v6r1ke3p0w0vw48gm",
        "weight_ymc3szhr46qbfgbyed9fp33cf",
        "weight_z08y522wjtc4y2q3ydzhdz3ay"
    };
    private static readonly HttpClient Client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(12),
        MaxResponseContentBufferSize = 8 * 1024 * 1024
    };

    internal static async Task Speak(int index, string text, int volume, CancellationToken token)
    {
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            var audio = await Synthesize(index, text, timeout.Token).ConfigureAwait(false);
            AdjustVolume(audio, volume);
            await Task.Run(() =>
            {
                using (var stream = new MemoryStream(audio))
                using (var player = new SoundPlayer(stream))
                {
                    player.Load();
                    token.ThrowIfCancellationRequested();
                    using (token.Register(player.Stop)) player.PlaySync();
                }
            }, token).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> Synthesize(int index, string text, CancellationToken token)
    {
        const string api = "https://api.fakeyou.com/tts/";
        var json = new JavaScriptSerializer();
        string job;
        using (var content = new StringContent(json.Serialize(new {
            uuid_idempotency_token = Guid.NewGuid().ToString(),
            tts_model_token = Models[index],
            inference_text = text
        }), Encoding.UTF8, "application/json"))
        using (var response = await Client.PostAsync(api + "inference", content, token).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var result = json.Deserialize<Job>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            job = result == null ? null : result.inference_job_token;
            if (string.IsNullOrEmpty(job) || job.StartsWith("synthetic_", StringComparison.Ordinal))
                throw new InvalidOperationException("FakeYou hat die Anfrage begrenzt oder abgelehnt.");
        }

        while (true)
        {
            await Task.Delay(2000, token).ConfigureAwait(false);
            using (var response = await Client.GetAsync(api + "job/" + Uri.EscapeDataString(job), token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var result = json.Deserialize<Job>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                var state = result == null ? null : result.state;
                if (state == null || state.status == "pending" || state.status == "started") continue;
                var path = state.maybe_public_bucket_wav_audio_path;
                if (state.status != "complete_success" || string.IsNullOrEmpty(path) || !path.StartsWith("/tts_inference_output/", StringComparison.Ordinal))
                    throw new InvalidOperationException("FakeYou hat keine Audiodatei geliefert.");
                using (var audio = await Client.GetAsync("https://storage.googleapis.com/vocodes-public" + path, token).ConfigureAwait(false))
                {
                    audio.EnsureSuccessStatusCode();
                    return await audio.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
        }
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

    private sealed class Job
    {
        public string inference_job_token { get; set; }
        public State state { get; set; }
    }

    private sealed class State
    {
        public string status { get; set; }
        public string maybe_public_bucket_wav_audio_path { get; set; }
    }
}
