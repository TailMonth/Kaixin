using System.Text.Json;
using KaixinPet.Core.Logging;

namespace KaixinPet.Audio;

public sealed record SoundEventDefinition(
    string Id,
    string Label,
    string File,
    bool Loop,
    double Volume,
    double CooldownSeconds);

public sealed class SoundCatalog
{
    private readonly Dictionary<string, SoundEventDefinition> _byId;

    private SoundCatalog(IReadOnlyList<SoundEventDefinition> events)
    {
        Events = events;
        _byId = events.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<SoundEventDefinition> Events { get; }

    public SoundEventDefinition? Find(string id) =>
        _byId.TryGetValue(id, out var definition) ? definition : null;

    public static SoundCatalog Load(string soundsRoot)
    {
        var path = Path.Combine(soundsRoot, "sounds.json");
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var document = JsonSerializer.Deserialize<SoundCatalogFile>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (document?.Events is { Count: > 0 } events)
                {
                    var normalized = events
                        .Where(item => !string.IsNullOrWhiteSpace(item.Id) &&
                                       !string.IsNullOrWhiteSpace(item.File))
                        .Select(item => new SoundEventDefinition(
                            item.Id.Trim(),
                            string.IsNullOrWhiteSpace(item.Label) ? item.Id : item.Label.Trim(),
                            item.File.Trim(),
                            item.Loop,
                            Math.Clamp(item.Volume <= 0 ? 1.0 : item.Volume, 0, 1),
                            Math.Max(0, item.CooldownSeconds)))
                        .ToArray();
                    if (normalized.Length > 0)
                    {
                        Log.Info($"audio catalog loaded: {normalized.Length} events, {path}");
                        return new SoundCatalog(normalized);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            Log.Warn($"sounds.json load failed; using built-in catalog: {exception.Message}");
        }

        return new SoundCatalog(CreateFallback());
    }

    private static IReadOnlyList<SoundEventDefinition> CreateFallback() =>
    [
        new("interaction.pet", "摸猫", "default/interaction.pet.wav", false, 0.8, 1.5),
        new("interaction.pickup", "拎起", "default/interaction.pickup.wav", false, 0.7, 1.0),
        new("interaction.drop", "放下落地", "default/interaction.drop.wav", false, 0.6, 1.0),
        new("interaction.play_start", "开始玩球", "default/interaction.play_start.wav", false, 0.8, 3.0),
        new("interaction.ball_bat", "拍球", "default/interaction.ball_bat.wav", false, 0.55, 0.4),
        new("interaction.wake", "被叫醒", "default/interaction.wake.wav", false, 0.6, 2.0),
        new("ambient.purr", "呼噜环境音", "default/ambient.purr.wav", true, 0.25, 0),
        new("timer.focus_start", "专注开始", "default/timer.focus_start.wav", false, 0.8, 3.0),
        new("timer.focus_complete", "专注完成", "default/timer.focus_complete.wav", false, 0.9, 5.0),
        new("timer.break_start", "休息开始", "default/timer.break_start.wav", false, 0.8, 3.0),
        new("timer.break_complete", "休息结束", "default/timer.break_complete.wav", false, 0.85, 5.0),
        new("timer.pause", "暂停", "default/timer.pause.wav", false, 0.6, 3.0),
        new("timer.skip", "跳过 / 重置", "default/timer.skip.wav", false, 0.5, 3.0)
    ];

    private sealed class SoundCatalogFile
    {
        public List<SoundEventRecord>? Events { get; set; }
    }

    private sealed class SoundEventRecord
    {
        public string Id { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public string File { get; set; } = string.Empty;

        public bool Loop { get; set; }

        public double Volume { get; set; } = 1.0;

        public double CooldownSeconds { get; set; }
    }
}
