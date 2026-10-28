using System.IO;
using System.Text.Json;
using KaixinPet.Core.Logging;

namespace KaixinPet.Pet.Assets;

public sealed class CharacterManifest
{
    public string Character { get; set; } = "kaixin";
    public int MasterScale { get; set; } = 2;
    public CanvasSize Canvas { get; set; } = new();
    public AnchorPoint Anchor { get; set; } = new();
    public Dictionary<string, AtlasDef> Atlases { get; set; } = new();
    public Dictionary<string, ClipDef> Clips { get; set; } = new();
}

public sealed class CanvasSize
{
    public int Width { get; set; } = 384;
    public int Height { get; set; } = 384;
}

public sealed class AnchorPoint
{
    public int X { get; set; } = 192;
    public int Y { get; set; } = 360;
}

public sealed class AtlasDef
{
    public string File { get; set; } = string.Empty;
}

public sealed class ClipDef
{
    public string Atlas { get; set; } = string.Empty;
    public double Fps { get; set; } = 10;
    public bool Loop { get; set; } = true;
    public bool Overlay { get; set; }
    public int[] Frames { get; set; } = Array.Empty<int>();
}

public sealed class PropsManifest
{
    public HourglassDef Hourglass { get; set; } = new();
    public string ShadowCat { get; set; } = "shadow_cat.png";
    public string ShadowHourglass { get; set; } = "shadow_hourglass.png";
}

public sealed class HourglassDef
{
    public string Atlas { get; set; } = "hourglass.png";
    public string SandTop { get; set; } = "hourglass_sand_top.png";
    public string SandBottom { get; set; } = "hourglass_sand_bottom.png";
}

public sealed class AssetCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public required CharacterManifest Character { get; init; }
    public required PropsManifest Props { get; init; }
    public required SpriteAtlas IdleAtlas { get; init; }
    public required SpriteAtlas EyesAtlas { get; init; }
    public required SpriteImage Hourglass { get; init; }
    public required SpriteImage SandTop { get; init; }
    public required SpriteImage SandBottom { get; init; }
    public required SpriteImage ShadowCat { get; init; }
    public required SpriteImage ShadowHourglass { get; init; }

    public static AssetCatalog Load(string assetsRoot)
    {
        var characterDir = Path.Combine(assetsRoot, "characters", "kaixin");
        var propsDir = Path.Combine(assetsRoot, "props");

        var manifestPath = Path.Combine(characterDir, "character.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("缺少角色清单", manifestPath);
        }

        var character = JsonSerializer.Deserialize<CharacterManifest>(
            File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException($"角色清单为空：{manifestPath}");

        var canvas = character.Canvas;
        var idle = CreateAtlas(characterDir, character, "idle", canvas);
        var eyes = CreateAtlas(characterDir, character, "eyes", canvas);

        var propsPath = Path.Combine(propsDir, "props.json");
        if (!File.Exists(propsPath))
        {
            throw new FileNotFoundException("缺少道具清单", propsPath);
        }

        var props = JsonSerializer.Deserialize<PropsManifest>(
            File.ReadAllText(propsPath), JsonOptions)
            ?? throw new InvalidDataException($"道具清单为空：{propsPath}");

        var catalog = new AssetCatalog
        {
            Character = character,
            Props = props,
            IdleAtlas = idle,
            EyesAtlas = eyes,
            Hourglass = new SpriteImage(Path.Combine(propsDir, props.Hourglass.Atlas)),
            SandTop = new SpriteImage(Path.Combine(propsDir, props.Hourglass.SandTop)),
            SandBottom = new SpriteImage(Path.Combine(propsDir, props.Hourglass.SandBottom)),
            ShadowCat = new SpriteImage(Path.Combine(propsDir, props.ShadowCat)),
            ShadowHourglass = new SpriteImage(Path.Combine(propsDir, props.ShadowHourglass))
        };

        Log.Info(
            $"assets loaded: character={character.Character}, masterScale={character.MasterScale}, " +
            $"idle={idle.FrameCount}f, root={assetsRoot}");
        return catalog;
    }

    private static SpriteAtlas CreateAtlas(
        string characterDir, CharacterManifest manifest, string name, CanvasSize canvas)
    {
        if (!manifest.Atlases.TryGetValue(name, out var def))
        {
            throw new InvalidDataException($"角色清单缺少图集定义：{name}");
        }

        return new SpriteAtlas(Path.Combine(characterDir, def.File), canvas.Width, canvas.Height);
    }
}
