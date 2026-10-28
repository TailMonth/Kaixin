namespace KaixinPet.Core;

public static class AppPaths
{
    public static bool IsPortable { get; } =
        File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));

    public static string DataRoot { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KaixinPet");

    public static string LogsDirectory => Path.Combine(DataRoot, "logs");
    public static string SoundsDirectory => Path.Combine(DataRoot, "sounds");
    public static string CacheDirectory => Path.Combine(DataRoot, "cache");
    public static string CustomSoundsDirectory => Path.Combine(SoundsDirectory, "custom");
    public static string SettingsPath => Path.Combine(DataRoot, "settings.json");

    public static string AssetsRoot { get; } = ResolveAssetsRoot();

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(SoundsDirectory);
        Directory.CreateDirectory(CustomSoundsDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }

    private static string ResolveAssetsRoot()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "assets");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "assets");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return direct;
    }
}
