using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal sealed class YouTubeChromePlayer
{
    private const string Home = "https://music.youtube.com/";
    private static readonly HttpClient Client = new HttpClient(new HttpClientHandler
        { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
    private readonly string profile;
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 };
    private string playerScript;
    private string tabId;

    internal YouTubeChromePlayer(string profile)
    {
        this.profile = Path.GetFullPath(profile);
    }

    internal async Task<Response> Open()
    {
        var tabs = await FindTabs();
        var tab = MusicTab(tabs);
        if (tab != null)
        {
            using (var timeout = new CancellationTokenSource(5000))
            using (var socket = await Connect(tab, timeout.Token))
            {
                await Call(socket, "Page.bringToFront", new { }, timeout.Token);
            }
            return await Run("status");
        }
        if (tabs != null && tabs.Any(item => IsPage(item, "accounts.google.com") || IsPage(item, "consent.youtube.com") || IsPage(item, "consent.google.com")))
            return new Response { state = "login" };

        Directory.CreateDirectory(profile);
        using (Process.Start(new ProcessStartInfo(ChromePath(),
            "--user-data-dir=\"" + profile + "\" --remote-debugging-port=0 --no-first-run --no-default-browser-check --app=" + Home)
            { UseShellExecute = false })) { }

        Response response = null;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            await Task.Delay(350);
            response = await Run("status");
            if (response.state != "closed" && response.state != "loading" && response.state != "wrong-site") break;
        }
        return response;
    }

    internal async Task<Response> Run(string action, string value = null)
    {
        var tabs = await FindTabs();
        if (tabs == null) return new Response { state = "closed" };
        var tab = MusicTab(tabs);
        if (tab == null)
            return new Response { state = tabs.Any(item => IsPage(item, "accounts.google.com") || IsPage(item, "consent.youtube.com") || IsPage(item, "consent.google.com")) ? "login" : "closed" };

        if (playerScript == null)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "youtube-music-player.mjs");
            playerScript = File.ReadAllText(path).Trim().Substring("export ".Length);
        }
        using (var timeout = new CancellationTokenSource(14000))
        using (var socket = await Connect(tab, timeout.Token))
        {
            var response = await Evaluate(socket, action, value, 1, timeout.Token);
            if (response.state != "start" || action != "queue" || YouTubeMusicControl.VideoId(value) != value) return response;
            var navigation = await Call(socket, "Page.navigate", new { url = Home + "watch?v=" + value }, timeout.Token, 2);
            if (Field(navigation, "errorText") != null) return new Response { state = "error" };
            for (var attempt = 0; attempt < 12; attempt++)
            {
                await Task.Delay(350, timeout.Token);
                var playback = await Evaluate(socket, "play-current", value, 3 + attempt, timeout.Token);
                if (playback.state == "playing") return playback;
                if (playback.state == "autoplay-blocked") break;
            }
            return new Response { state = "starting" };
        }
    }

    private async Task<Response> Evaluate(ClientWebSocket socket, string action, string value, int requestId, CancellationToken token)
    {
        var command = json.Serialize(new { action, value });
        var evaluation = await Call(socket, "Runtime.evaluate", new
        {
            expression = "(" + playerScript + ")(" + command + ")",
            awaitPromise = true,
            returnByValue = true
        }, token, requestId);
        var result = Field(Field(evaluation, "result") as IDictionary<string, object>, "value");
        return result == null ? new Response { state = "error" } : json.ConvertToType<Response>(result);
    }

    private static string ChromePath()
    {
        const string appPath = @"Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using (var key = root.OpenSubKey(appPath))
            {
                var path = key == null ? null : key.GetValue(null) as string;
                if (File.Exists(path)) return path;
            }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
        {
            var path = Path.Combine(Environment.GetFolderPath(folder), @"Google\Chrome\Application\chrome.exe");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Google Chrome wurde nicht gefunden.");
    }

    private async Task<Tab[]> FindTabs()
    {
        try
        {
            var endpoint = File.ReadAllLines(Path.Combine(profile, "DevToolsActivePort"));
            int port;
            if (endpoint.Length < 2 || !int.TryParse(endpoint[0], out port) || port <= 0 || port > 65535 ||
                !endpoint[1].StartsWith("/devtools/browser/", StringComparison.Ordinal)) return null;
            var local = "http://127.0.0.1:" + port;
            var version = json.Deserialize<Dictionary<string, object>>(await Client.GetStringAsync(local + "/json/version"));
            if (!Equals(Field(version, "webSocketDebuggerUrl"), "ws://127.0.0.1:" + port + endpoint[1])) return null;
            var tabs = json.Deserialize<Tab[]>(await Client.GetStringAsync(local + "/json/list"));
            foreach (var tab in tabs)
            {
                Uri uri;
                if (!Uri.TryCreate(tab.webSocketDebuggerUrl, UriKind.Absolute, out uri) || uri.Scheme != "ws" ||
                    uri.Host != "127.0.0.1" || uri.Port != port || uri.AbsolutePath != "/devtools/page/" + tab.id ||
                    uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) tab.webSocketDebuggerUrl = null;
            }
            return tabs;
        }
        catch (IOException) { return null; }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    private Tab MusicTab(Tab[] tabs)
    {
        if (tabs == null) return null;
        var matches = tabs.Where(tab => IsPage(tab, "music.youtube.com") && tab.webSocketDebuggerUrl != null);
        var target = matches.FirstOrDefault(tab => tab.id == tabId) ?? matches.FirstOrDefault();
        tabId = target == null ? null : target.id;
        return target;
    }

    private static bool IsPage(Tab tab, string host)
    {
        Uri uri;
        return tab.type == "page" && Uri.TryCreate(tab.url, UriKind.Absolute, out uri) &&
            uri.Scheme == "https" && uri.Host == host && uri.IsDefaultPort && uri.UserInfo.Length == 0;
    }

    private static async Task<ClientWebSocket> Connect(Tab tab, CancellationToken token)
    {
        var socket = new ClientWebSocket();
        socket.Options.Proxy = null;
        try
        {
            await socket.ConnectAsync(new Uri(tab.webSocketDebuggerUrl), token);
            return socket;
        }
        catch { socket.Dispose(); throw; }
    }

    private async Task<IDictionary<string, object>> Call(ClientWebSocket socket, string method, object parameters, CancellationToken token, int requestId = 1)
    {
        var bytes = Encoding.UTF8.GetBytes(json.Serialize(new { id = requestId, method, @params = parameters }));
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        using (var message = new MemoryStream())
        {
            var buffer = new ArraySegment<byte>(new byte[4096]);
            while (true)
            {
                var read = await socket.ReceiveAsync(buffer, token);
                if (read.MessageType != WebSocketMessageType.Text || message.Length + read.Count > json.MaxJsonLength)
                    throw new InvalidDataException("Ungültige Antwort von Chrome.");
                message.Write(buffer.Array, 0, read.Count);
                if (!read.EndOfMessage) continue;
                var result = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                if (Equals(Field(result, "id"), requestId))
                {
                    if (Field(result, "error") != null) throw new InvalidOperationException("Chrome hat den Befehl abgelehnt.");
                    return Field(result, "result") as IDictionary<string, object>;
                }
                message.SetLength(0);
            }
        }
    }

    private static object Field(IDictionary<string, object> map, string name)
    {
        object value;
        return map != null && map.TryGetValue(name, out value) ? value : null;
    }

    public sealed class Tab
    {
        public string id { get; set; }
        public string type { get; set; }
        public string url { get; set; }
        public string webSocketDebuggerUrl { get; set; }
    }

    internal sealed class Response
    {
        public string state { get; set; }
        public string account { get; set; }
        public Track[] tracks { get; set; }
    }

    internal sealed class Track
    {
        public string id { get; set; }
        public string title { get; set; }
    }
}
