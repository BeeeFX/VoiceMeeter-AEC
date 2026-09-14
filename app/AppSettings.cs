using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceMeeterAEC;

public sealed class AppSettings
{
    public int Schema { get; set; } = 2;
    public string Language { get; set; } = "en";
    public string Theme { get; set; } = "dark";
    public int MicrophoneStrip { get; set; } = 1;
    public string MicrophoneSide { get; set; } = "left";
    public List<int> ReferenceStrips { get; set; } = [6];
    [JsonIgnore]
    public int ReferenceStrip
    {
        get => ReferenceStrips.FirstOrDefault(6);
        set => ReferenceStrips = [value];
    }
    public int SpeakerBus { get; set; } = 2;
    public List<int> AutoStrips { get; set; } = [6];
    public bool WatchAllStrips { get; set; }
    public string StartMode { get; set; } = "auto";
    public string Suppression { get; set; } = "gentle";
    public int HoldMs { get; set; }
    public int DelayMs { get; set; }
    public bool StartWithWindows { get; set; }
    public bool CheckForUpdatesAutomatically { get; set; } = true;
    public DateTime LastUpdateCheckUtc { get; set; }
    [JsonIgnore] public bool MigratedFromLegacy { get; private set; }

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceMeeterAEC");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string LogPath => Path.Combine(DataDirectory, "engine.log");
    private static string LegacyStartupPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "VoiceMeeter AEC.lnk");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            return Parse(File.ReadAllText(SettingsPath), File.Exists(LegacyStartupPath));
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(string executablePath)
    {
        Validate();
        Directory.CreateDirectory(DataDirectory);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, true);

        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (StartWithWindows)
            key.SetValue("VoiceMeeter AEC", $"\"{executablePath}\" --startup");
        else
            key.DeleteValue("VoiceMeeter AEC", false);
        if (File.Exists(LegacyStartupPath)) File.Delete(LegacyStartupPath);
        MigratedFromLegacy = false;
    }

    internal static AppSettings Parse(string json, bool legacyStartupExists)
    {
        using var document = JsonDocument.Parse(json);
        if (ReadInt(document.RootElement, "schema", 2) == 1)
            return FromLegacy(document.RootElement, legacyStartupExists);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
        if (!document.RootElement.TryGetProperty("ReferenceStrips", out _))
            settings.ReferenceStrip = ReadInt(document.RootElement, "ReferenceStrip", 6);
        settings.Validate();
        return settings;
    }

    private static AppSettings FromLegacy(JsonElement root, bool legacyStartupExists)
    {
        var micChannel = ReadInt(root, "mic", 1);
        var referenceChannel = ReadInt(root, "refL", 11);
        var strips = ReadString(root, "strips", "6")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
            .Where(value => value is >= 1 and <= 8)
            .ToList();
        var mode = ReadInt(root, "mode", 2);
        var settings = new AppSettings
        {
            Language = ReadString(root, "language", "en"),
            MicrophoneStrip = Math.Clamp((micChannel + 1) / 2, 1, 5),
            MicrophoneSide = micChannel % 2 == 0 ? "right" : "left",
            ReferenceStrip = ChannelToStrip(referenceChannel),
            SpeakerBus = ReadInt(root, "bus", 2),
            AutoStrips = strips,
            WatchAllStrips = ReadInt(root, "autoScope", 0) == 1,
            StartMode = mode switch { 0 => "bypass", 1 => "aec", 3 => "mute", _ => "auto" },
            Suppression = ReadString(root, "suppression", "gentle"),
            HoldMs = ReadInt(root, "hold", 0),
            DelayMs = ReadInt(root, "delay", 0),
            StartWithWindows = legacyStartupExists,
            MigratedFromLegacy = true
        };
        settings.Validate();
        return settings;
    }

    private static int ChannelToStrip(int channel) => channel switch
    {
        >= 1 and <= 10 => (channel + 1) / 2,
        >= 11 and <= 18 => 6,
        >= 19 and <= 26 => 7,
        >= 27 and <= 34 => 8,
        _ => 6
    };

    private static int ReadInt(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : fallback;
    }

    private static string ReadString(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    private void Validate()
    {
        if (Language is not ("en" or "fr")) Language = "en";
        if (Theme is not ("dark" or "light")) Theme = "dark";
        MicrophoneStrip = Math.Clamp(MicrophoneStrip, 1, 5);
        if (MicrophoneSide is not ("left" or "right")) MicrophoneSide = "left";
        ReferenceStrips = (ReferenceStrips ?? []).Where(value => value is >= 1 and <= 8 && value != MicrophoneStrip).Distinct().Order().ToList();
        if (ReferenceStrips.Count == 0) ReferenceStrips.Add(MicrophoneStrip == 6 ? 7 : 6);
        SpeakerBus = Math.Clamp(SpeakerBus, 1, 5);
        AutoStrips = (AutoStrips ?? []).Where(value => value is >= 1 and <= 8).Distinct().Order().ToList();
        if (AutoStrips.Count == 0) AutoStrips.AddRange(ReferenceStrips);
        if (StartMode is not ("bypass" or "aec" or "auto" or "mute")) StartMode = "auto";
        if (Suppression is not ("gentle" or "balanced" or "strong")) Suppression = "gentle";
        HoldMs = Math.Clamp(HoldMs, 0, 250);
        DelayMs = Math.Clamp(DelayMs, 0, 500);
        if (LastUpdateCheckUtc > DateTime.UtcNow.AddDays(1)) LastUpdateCheckUtc = default;
    }
}
