using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace VoiceMeeterAEC;

internal sealed record MixerLayout(
    string Key,
    string DisplayName,
    string[] ProcessNames,
    int HardwareStrips,
    int BusCount,
    string[] StripNames,
    int[] StripStarts)
{
    public int StripCount => StripNames.Length;
    public int DefaultPlaybackStrip => HardwareStrips + 1;

    // VoiceMeeter ships both 32-bit and 64-bit executables; either may be running.
    public static readonly MixerLayout Banana = new(
        "banana", "Banana", ["voicemeeterpro", "voicemeeterpro_x64"], 3, 3,
        ["IN1", "IN2", "IN3", "VAIO", "AUX"],
        [1, 3, 5, 7, 15]);

    public static readonly MixerLayout Potato = new(
        "potato", "Potato", ["voicemeeter8", "voicemeeter8x64"], 5, 5,
        ["IN1", "IN2", "IN3", "IN4", "IN5", "VAIO", "AUX", "VAIO3"],
        [1, 3, 5, 7, 9, 11, 19, 27]);

    public static MixerLayout Resolve(string preference) => preference switch
    {
        "banana" => Banana,
        "potato" => Potato,
        _ => DetectRunning() ?? Potato
    };

    public static MixerLayout? DetectRunning()
    {
        if (IsRunning(Potato)) return Potato;
        if (IsRunning(Banana)) return Banana;
        return null;
    }

    public static bool IsRunning(MixerLayout layout)
    {
        foreach (var name in layout.ProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                var running = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
                if (running) return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>VoiceMeeter's Remote API DLL, found through its uninstall entry, else the default folder.</summary>
    public static string RemoteDllPath()
    {
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VB", "Voicemeeter", "VoicemeeterRemote64.dll");
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}");
            var uninstall = (key?.GetValue("UninstallString") as string)?.Trim();
            if (string.IsNullOrEmpty(uninstall)) return fallback;
            if (uninstall.StartsWith('"')) uninstall = uninstall[1..].Split('"')[0];
            var directory = Path.GetDirectoryName(uninstall);
            if (string.IsNullOrEmpty(directory)) return fallback;
            var candidate = Path.Combine(directory, "VoicemeeterRemote64.dll");
            return File.Exists(candidate) ? candidate : fallback;
        }
        catch { return fallback; }
    }
}
