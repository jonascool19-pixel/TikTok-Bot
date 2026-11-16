using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class YouTubeBrowserBridge
{
    private const string Origin = "chrome-extension://gjladlonlpinppagaeolghiohhhacalm/";
    private const int MaxMessage = 32768;

    internal static bool IsNativeHost(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], Origin, StringComparison.Ordinal);
    }

    private static string PipeName(SecurityIdentifier user) { return "TikTokBot.YouTubeMusic." + user.Value; }

    internal static async Task<BrowserResponse> Send(string action, string value = null)
    {
        try
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var acl = new PipeSecurity();
                acl.SetAccessRuleProtection(true, false);
                acl.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
                using (var pipe = new NamedPipeServerStream(PipeName(identity.User), PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, acl))
                {
                    var connect = Task.Factory.FromAsync(pipe.BeginWaitForConnection, pipe.EndWaitForConnection, null);
                    if (await Task.WhenAny(connect, Task.Delay(6000)) != connect) return null;
                    await connect;
                    var json = new JavaScriptSerializer { MaxJsonLength = MaxMessage };
                    var request = json.Serialize(new { action, value });
                    var response = Task.Run(() =>
                    {
                        Write(pipe, request);
                        return Read(pipe);
                    });
                    if (await Task.WhenAny(response, Task.Delay(15000)) != response) return null;
                    var payload = await response;
                    return payload == null ? null : json.Deserialize<BrowserResponse>(payload);
                }
            }
        }
        catch (Exception error)
        {
            ErrorLog.Write("YouTube Music", "Chrome-Verbindung konnte nicht verarbeitet werden.", error);
            return null;
        }
    }

    internal static int RunNativeHost()
    {
        try
        {
            using (var identity = WindowsIdentity.GetCurrent())
            using (var input = Console.OpenStandardInput())
            using (var output = Console.OpenStandardOutput())
            {
                var json = new JavaScriptSerializer { MaxJsonLength = MaxMessage };
                while (true)
                {
                    var line = Read(input);
                    if (line == null) return 0;
                    var ready = json.Deserialize<Ready>(line);
                    if (ready == null || !ready.ready) return 2;
                    using (var pipe = new NamedPipeClientStream(".", PipeName(identity.User), PipeDirection.InOut))
                    {
                        try { pipe.Connect(3000); }
                        catch (TimeoutException) { Write(output, "{\"idle\":true}"); continue; }
                        string request;
                        try { request = Read(pipe); }
                        catch (IOException) { Write(output, "{\"idle\":true}"); continue; }
                        if (request == null) { Write(output, "{\"idle\":true}"); continue; }
                        Write(output, request);
                        var reply = Read(input);
                        if (reply == null) return 0;
                        try { Write(pipe, reply); }
                        catch (IOException) { }
                    }
                }
            }
        }
        catch (Exception) { return 2; }
    }

    private static string Read(Stream stream)
    {
        var first = stream.ReadByte();
        if (first < 0) return null;
        var lengthBytes = new byte[4];
        lengthBytes[0] = (byte)first;
        ReadExactly(stream, lengthBytes, 1);
        var length = BitConverter.ToInt32(lengthBytes, 0);
        if (length <= 0 || length > MaxMessage) throw new InvalidDataException();
        var body = new byte[length];
        ReadExactly(stream, body, 0);
        return new UTF8Encoding(false, true).GetString(body);
    }

    private static void ReadExactly(Stream stream, byte[] data, int start)
    {
        while (start < data.Length)
        {
            var count = stream.Read(data, start, data.Length - start);
            if (count == 0) throw new EndOfStreamException();
            start += count;
        }
    }

    private static void Write(Stream stream, string message)
    {
        var body = Encoding.UTF8.GetBytes(message);
        if (body.Length == 0 || body.Length > MaxMessage) throw new InvalidDataException();
        var prefix = BitConverter.GetBytes(body.Length);
        stream.Write(prefix, 0, prefix.Length);
        stream.Write(body, 0, body.Length);
        stream.Flush();
    }

    private sealed class Ready { public bool ready { get; set; } }
    internal sealed class BrowserResponse
    {
        public string state { get; set; }
        public string account { get; set; }
        public BrowserTrack[] tracks { get; set; }
    }
    internal sealed class BrowserTrack
    {
        public string id { get; set; }
        public string title { get; set; }
    }
}
