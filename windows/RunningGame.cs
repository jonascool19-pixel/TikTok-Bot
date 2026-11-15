using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal static class RunningGame
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    internal static string Detect()
    {
        var epic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Epic\EpicGamesLauncher\Data\Manifests");
        try
        {
            if (Directory.Exists(root))
                foreach (var file in Directory.EnumerateFiles(root, "*.item"))
                    ReadEpicManifest(epic, file);
        }
        catch (IOException error) { ErrorLog.Write("Spielerkennung", "Epic-Bibliothek konnte nicht gelesen werden.", error); }
        catch (UnauthorizedAccessException error) { ErrorLog.Write("Spielerkennung", "Epic-Bibliothek konnte nicht gelesen werden.", error); }

        uint foreground;
        GetWindowThreadProcessId(GetForegroundWindow(), out foreground);

        var runningGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steam = DetectSteam();
        var detected = DetectForeground(foreground, epic, steam);
        if (detected.Length > 0) return detected;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    var module = process.MainModule;
                    string title;
                    if (!epic.TryGetValue(module.FileName, out title) && string.IsNullOrWhiteSpace(steam))
                        title = TitleFromExecutable(module.FileName, module.FileVersionInfo.ProductName,
                            module.FileVersionInfo.FileDescription, process.MainWindowTitle);
                    if (!string.IsNullOrWhiteSpace(title)) runningGames.Add(title);
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                catch (NotSupportedException) { }
            }
        }
        if (!string.IsNullOrWhiteSpace(steam)) runningGames.Add(steam);
        if (runningGames.Count == 1)
            foreach (var title in runningGames) return title;
        return "";
    }

    private static void ReadEpicManifest(Dictionary<string, string> games, string file)
    {
        try
        {
            var item = new JavaScriptSerializer().Deserialize<EpicItem>(File.ReadAllText(file));
            if (item == null || item.bIsIncompleteInstall || string.IsNullOrWhiteSpace(item.DisplayName) ||
                string.IsNullOrWhiteSpace(item.InstallLocation) || string.IsNullOrWhiteSpace(item.LaunchExecutable)) return;
            var path = Path.GetFullPath(Path.Combine(item.InstallLocation, item.LaunchExecutable));
            games[path] = item.DisplayName.Trim();
        }
        catch (IOException error) { ReportEpic(error); }
        catch (UnauthorizedAccessException error) { ReportEpic(error); }
        catch (ArgumentException error) { ReportEpic(error); }
        catch (InvalidOperationException error) { ReportEpic(error); }
        catch (NotSupportedException error) { ReportEpic(error); }
    }

    private static void ReportEpic(Exception error)
    {
        ErrorLog.Write("Spielerkennung", "Ein Epic-Spieleintrag konnte nicht gelesen werden.", error);
    }

    private static string DetectForeground(uint id, Dictionary<string, string> epic, string steam)
    {
        if (id == 0 || id > int.MaxValue) return "";
        try
        {
            using (var process = Process.GetProcessById((int)id))
            {
                var module = process.MainModule;
                string known;
                if (epic.TryGetValue(module.FileName, out known)) return known;
                if (steam.Length > 0 && module.FileName.IndexOf(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase) >= 0) return steam;
                return TitleFromExecutable(module.FileName, module.FileVersionInfo.ProductName,
                    module.FileVersionInfo.FileDescription, process.MainWindowTitle);
            }
        }
        catch (ArgumentException) { return ""; }
        catch (InvalidOperationException) { return ""; }
        catch (System.ComponentModel.Win32Exception) { return ""; }
        catch (NotSupportedException) { return ""; }
    }

    internal static string TitleFromExecutable(string path, string product, string description, string window)
    {
        if (!Regex.IsMatch(path, @"(?i)[\\/](?:steamapps[\\/]common|Epic Games|GOG Games|XboxGames|WindowsApps|Ubisoft Game Launcher[\\/]games|EA Games|Origin Games|Battle\.net Games|Riot Games|Rockstar Games|Amazon Games|Games)[\\/]")) return "";
        var exe = Path.GetFileNameWithoutExtension(path);
        if (Regex.IsMatch(exe, @"(?i)launcher|crash|anticheat|uninstall|setup|webhelper|updater|bootstrap|^gamingservices|^gamelaunchhelper|^EADesktop|^UbisoftConnect|^Battle\.net$|^GalaxyClient")) return "";
        foreach (var title in new[] { product, description, window })
        {
            if (string.IsNullOrWhiteSpace(title) || Regex.IsMatch(title, @"(?i)launcher|crash reporter|anti.cheat|unreal engine|unity player")) continue;
            return Regex.Replace(title.Trim(), @"\s+", " ");
        }
        return "";
    }
    private static string DetectSteam()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;
        if (string.IsNullOrWhiteSpace(path)) return "";
        var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "RunningAppID", 0);
        var id = value is int ? (int)value : 0;
        if (id <= 0) return "";
        try
        {
            var primary = Path.Combine(path, "steamapps");
            var libraries = Path.Combine(primary, "libraryfolders.vdf");
            var folders = new List<string> { primary };
            if (File.Exists(libraries))
                foreach (Match library in Regex.Matches(File.ReadAllText(libraries), @"(?m)^\s*""path""\s+""([^""]+)"""))
                    folders.Add(Path.Combine(library.Groups[1].Value.Replace(@"\\", @"\"), "steamapps"));
            foreach (var folder in folders)
            {
                var manifest = Path.Combine(folder, "appmanifest_" + id + ".acf");
                if (!File.Exists(manifest)) continue;
                var title = ParseSteamTitle(File.ReadAllText(manifest));
                if (title.Length > 0) return title;
            }
        }
        catch (IOException error) { ErrorLog.Write("Spielerkennung", "Steam-Bibliothek konnte nicht gelesen werden.", error); }
        catch (UnauthorizedAccessException error) { ErrorLog.Write("Spielerkennung", "Steam-Bibliothek konnte nicht gelesen werden.", error); }
        return "";
    }

    internal static string ParseSteamTitle(string manifest)
    {
        var name = Regex.Match(manifest, @"(?m)^\s*""name""\s+""([^""]+)""");
        return name.Success ? name.Groups[1].Value.Trim() : "";
    }

    private sealed class EpicItem
    {
        public string DisplayName { get; set; }
        public string InstallLocation { get; set; }
        public string LaunchExecutable { get; set; }
        public bool bIsIncompleteInstall { get; set; }
    }
}
