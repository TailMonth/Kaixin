using System.Text.Json;
using System.Text.Json.Serialization;

namespace KaixinPet.Core.Settings;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public GeneralSettings General { get; set; } = new();

    public PomodoroSettings Pomodoro { get; set; } = new();

    public AudioSettings Audio { get; set; } = new();

    public BehaviorSettings Behavior { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class GeneralSettings
{
    public string PetName { get; set; } = "开心";

    public string Language { get; set; } = "zh-CN";

    public double Scale { get; set; } = 1.0;

    public bool AlwaysOnTop { get; set; } = true;

    public bool HideOnFullscreen { get; set; } = true;

    public bool LaunchAtLogin { get; set; }

    public bool ReduceMotion { get; set; }

    public string Theme { get; set; } = "system";

    public string LastMonitorId { get; set; } = string.Empty;

    public WindowPosition? LastPosition { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class WindowPosition
{
    public int X { get; set; }

    public int Y { get; set; }
}

public sealed class PomodoroSettings
{
    public int FocusMinutes { get; set; } = 25;

    public int ShortBreakMinutes { get; set; } = 5;

    public int LongBreakMinutes { get; set; } = 15;

    public int RoundsPerLongBreak { get; set; } = 4;

    public bool AutoStartBreaks { get; set; } = true;

    public bool AutoStartFocus { get; set; }

    public bool ShowNotifications { get; set; } = true;

    public bool QuietDuringFocus { get; set; } = true;

    public bool ShowHourglassTime { get; set; } = true;

    public bool ShowHoverControls { get; set; } = true;

    public string Hotkey { get; set; } = "Ctrl+Alt+P";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AudioSettings
{
    public bool Muted { get; set; }

    public double MasterVolume { get; set; } = 0.8;

    public double EffectsVolume { get; set; } = 0.9;

    public double AmbientVolume { get; set; } = 0.25;

    public bool AmbientPurrDuringFocus { get; set; }

    public Dictionary<string, string> CustomSounds { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class BehaviorSettings
{
    public bool SpontaneousPlay { get; set; } = true;

    public int SleepAfterIdleMinutes { get; set; } = 15;

    public int FocusQuietAfterMinutes { get; set; } = 2;

    public int PlaySessionMaxSeconds { get; set; } = 90;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
