using System.Text.Json;
using KaixinPet.Core.Logging;

namespace KaixinPet.Behavior;

public sealed class BehaviorConfig
{
    public IdleBehaviorConfig Idle { get; set; } = new();

    public SleepBehaviorConfig Sleep { get; set; } = new();

    public PlayBehaviorConfig Play { get; set; } = new();

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
        Play ??= new PlayBehaviorConfig();
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

        Play.MinSeconds = Math.Clamp(Play.MinSeconds, 5, 300);
        Play.MaxSeconds = Math.Max(Play.MinSeconds, Math.Clamp(Play.MaxSeconds, 5, 300));
        Play.SpontaneousMinSeconds = Math.Clamp(Play.SpontaneousMinSeconds, 30, 1800);
        Play.SpontaneousMaxSeconds = Math.Max(
            Play.SpontaneousMinSeconds,
            Math.Clamp(Play.SpontaneousMaxSeconds, 30, 3600));
        Play.SpontaneousChance = Math.Clamp(Play.SpontaneousChance, 0, 1);
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

public sealed class PlayBehaviorConfig
{
    public int MinSeconds { get; set; } = 45;

    public int MaxSeconds { get; set; } = 90;

    public double SpontaneousMinSeconds { get; set; } = 180;

    public double SpontaneousMaxSeconds { get; set; } = 360;

    public double SpontaneousChance { get; set; } = 0.25;
}
