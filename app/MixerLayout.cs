using System.Diagnostics;

namespace VoiceMeeterAEC;

internal sealed record MixerLayout(
    string Key,
    string DisplayName,
    string ProcessName,
    int HardwareStrips,
    int BusCount,
    string[] StripNames,
    int[] StripStarts)
{
    public int StripCount => StripNames.Length;
    public int DefaultPlaybackStrip => HardwareStrips + 1;

    public static readonly MixerLayout Banana = new(
        "banana", "Banana", "voicemeeterpro", 3, 3,
        ["IN1", "IN2", "IN3", "VAIO", "AUX"],
        [1, 3, 5, 7, 15]);

    public static readonly MixerLayout Potato = new(
        "potato", "Potato", "voicemeeter8", 5, 5,
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
        try { return Process.GetProcessesByName(layout.ProcessName).Length > 0; }
        catch { return false; }
    }
}
