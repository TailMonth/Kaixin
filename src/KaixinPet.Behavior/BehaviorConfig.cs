using System.Text.Json;
using KaixinPet.Core.Logging;

namespace KaixinPet.Behavior;

public sealed class BehaviorConfig
{
    public IdleBehaviorConfig Idle { get; set; } = new();

    public SleepBehaviorConfig Sleep { get; set; } = new();

    public ReadingBehaviorConfig Reading { get; set; } = new();

    public static BehaviorConfig Load(string assetsRoot)
    {
        var path = Path.Combine(assetsRoot, "behaviors.json");
        try
        {
            if (File.Exists(path))
            {
                var config = JsonSerializer.Deserialize<BehaviorConfig>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config is not null)
                {
                    config.Normalize();
                    Log.Info($"behaviors loaded: {path}");
                    return config;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            Log.Warn($"behaviors.json load failed; using defaults: {exception.Message}");
        }

        var fallback = new BehaviorConfig();
        fallback.Normalize();
        return fallback;
    }

    private void Normalize()
    {
        Idle ??= new IdleBehaviorConfig();
        Sleep ??= new SleepBehaviorConfig();
        Reading ??= new ReadingBehaviorConfig();
        Idle.Actions ??= new List<IdleActionConfig>();

        if (Idle.Actions.Count == 0)
        {
            Idle.Actions = new IdleBehaviorConfig().Actions;
        }

        Idle.BlinkMinSeconds = Math.Clamp(Idle.BlinkMinSeconds, 1, 60);
        Idle.BlinkMaxSeconds = Math.Max(Idle.BlinkMinSeconds, Math.Clamp(Idle.BlinkMaxSeconds, 1, 120));
        Idle.MicroMinSeconds = Math.Clamp(Idle.MicroMinSeconds, 3, 300);
        Idle.MicroMaxSeconds = Math.Max(Idle.MicroMinSeconds, Math.Clamp(Idle.MicroMaxSeconds, 3, 600));

        Sleep.TwitchMinSeconds = Math.Clamp(Sleep.TwitchMinSeconds, 2, 120);
        Sleep.TwitchMaxSeconds = Math.Max(Sleep.TwitchMinSeconds, Math.Clamp(Sleep.TwitchMaxSeconds, 2, 240));

        Reading.PageMinSeconds = Math.Clamp(Reading.PageMinSeconds, 2, 120);
        Reading.PageMaxSeconds = Math.Max(
            Reading.PageMinSeconds,
            Math.Clamp(Reading.PageMaxSeconds, 2, 240));
        Reading.MicroMinSeconds = Math.Clamp(Reading.MicroMinSeconds, 3, 300);
        Reading.MicroMaxSeconds = Math.Max(
            Reading.MicroMinSeconds,
            Math.Clamp(Reading.MicroMaxSeconds, 3, 600));
    }
}

public sealed class IdleBehaviorConfig
{
    public double BlinkMinSeconds { get; set; } = 3;

    public double BlinkMaxSeconds { get; set; } = 9;

    public double MicroMinSeconds { get; set; } = 20;

    public double MicroMaxSeconds { get; set; } = 45;

    public List<IdleActionConfig> Actions { get; set; } =
    [
        new() { Clip = "idle.look_around", Weight = 3, CooldownSeconds = 45 },
        new() { Clip = "idle.groom", Weight = 2, CooldownSeconds = 90 },
        new() { Clip = "idle.stretch", Weight = 1, CooldownSeconds = 180 },
        new() { Clip = "idle.tail_sway", Weight = 2, CooldownSeconds = 30 },
        new() { Clip = "idle.ear_twitch", Weight = 2, CooldownSeconds = 60 }
    ];
}

public sealed class IdleActionConfig
{
    public string Clip { get; set; } = string.Empty;

    public double Weight { get; set; } = 1;

    public double CooldownSeconds { get; set; } = 60;
}

public sealed class SleepBehaviorConfig
{
    public double TwitchMinSeconds { get; set; } = 8;

    public double TwitchMaxSeconds { get; set; } = 18;
}

public sealed class ReadingBehaviorConfig
{
    public double PageMinSeconds { get; set; } = 6;

    public double PageMaxSeconds { get; set; } = 12;

    public double MicroMinSeconds { get; set; } = 12;

    public double MicroMaxSeconds { get; set; } = 24;
}
