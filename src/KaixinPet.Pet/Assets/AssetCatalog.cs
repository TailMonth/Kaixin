using System.IO;
using System.Text.Json;
using KaixinPet.Core.Logging;

namespace KaixinPet.Pet.Assets;

public sealed class CharacterManifest
{
    public string Character { get; set; } = "kaixin";
    public int MasterScale { get; set; } = 1;
    public CanvasSize Canvas { get; set; } = new();
    public AnchorPoint Anchor { get; set; } = new();
    public Dictionary<string, AtlasDef> Atlases { get; set; } = new();
    public Dictionary<string, ClipDef> Clips { get; set; } = new();
}

public sealed class CanvasSize
{
    public int Width { get; set; } = 192;
    public int Height { get; set; } = 192;
}

public sealed class AnchorPoint
{
    public int X { get; set; } = 96;
    public int Y { get; set; } = 180;
}

public sealed class AtlasDef
{
    public string File { get; set; } = string.Empty;
    public int CellWidth { get; set; }
    public int CellHeight { get; set; }
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
    public int MasterScale { get; set; } = 2;
    public HourglassDef Hourglass { get; set; } = new();
    public YarnDef Yarn { get; set; } = new();
    public string ShadowCat { get; set; } = "shadow_cat.png";
    public string ShadowSleep { get; set; } = "shadow_sleep.png";
    public string ShadowHourglass { get; set; } = "shadow_hourglass.png";
}

public sealed class HourglassDef
{
    public string Atlas { get; set; } = "hourglass.png";
    public string SandTop { get; set; } = "hourglass_sand_top.png";
    public string SandBottom { get; set; } = "hourglass_sand_bottom.png";
}

public sealed class YarnDef
{
    public string Atlas { get; set; } = "yarn.png";
    public int CellSize { get; set; } = 64;
    public int MasterScale { get; set; } = 2;
    public int Frames { get; set; } = 8;
}

public sealed class AssetCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public required CharacterManifest Character { get; init; }
    public required PropsManifest Props { get; init; }
    public required Dictionary<string, SpriteAtlas> Atlases { get; init; }
    public required SpriteAtlas Yarn { get; init; }
    public required SpriteImage Hourglass { get; init; }
    public required SpriteImage SandTop { get; init; }
    public required SpriteImage SandBottom { get; init; }
    public required SpriteImage ShadowCat { get; init; }
    public required SpriteImage ShadowSleep { get; init; }
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

        var atlases = new Dictionary<string, SpriteAtlas>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, definition) in character.Atlases)
        {
            var cellWidth = definition.CellWidth > 0 ? definition.CellWidth : character.Canvas.Width;
            var cellHeight = definition.CellHeight > 0 ? definition.CellHeight : character.Canvas.Height;
            atlases[name] = new SpriteAtlas(
                Path.Combine(characterDir, definition.File), cellWidth, cellHeight);
        }

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
            Atlases = atlases,
            Yarn = new SpriteAtlas(
                Path.Combine(propsDir, props.Yarn.Atlas),
                props.Yarn.CellSize,
                props.Yarn.CellSize),
            Hourglass = new SpriteImage(Path.Combine(propsDir, props.Hourglass.Atlas)),
            SandTop = new SpriteImage(Path.Combine(propsDir, props.Hourglass.SandTop)),
            SandBottom = new SpriteImage(Path.Combine(propsDir, props.Hourglass.SandBottom)),
            ShadowCat = new SpriteImage(Path.Combine(propsDir, props.ShadowCat)),
            ShadowSleep = new SpriteImage(Path.Combine(propsDir, props.ShadowSleep)),
            ShadowHourglass = new SpriteImage(Path.Combine(propsDir, props.ShadowHourglass))
        };

        Log.Info(
            $"assets loaded: character={character.Character}, masterScale={character.MasterScale}, " +
            $"atlases={atlases.Count}, clips={character.Clips.Count}, root={assetsRoot}");
        return catalog;
    }
}
