using System.Diagnostics;
using System.Text;

namespace KaixinPet.Core.Logging;

public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private const int MaxFiles = 3;

    private static readonly object Gate = new();
    private static string? _filePath;
    private static bool _initialized;

    public static string? FilePath
    {
        get
        {
            lock (Gate)
            {
                return _filePath;
            }
        }
    }

    public static void Initialize(string directory, string prefix = "app")
    {
        lock (Gate)
        {
            Directory.CreateDirectory(directory);
            _filePath = Path.Combine(directory, $"{prefix}-{DateTime.Now:yyyyMMdd}.log");
            _initialized = true;
            RotateIfNeeded(_filePath);
        }

        Info($"log opened; base={AppContext.BaseDirectory}");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception exception) =>
        Write("ERROR", $"{message}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        Debug.WriteLine(line);

        lock (Gate)
        {
            if (!_initialized || _filePath is null)
            {
                return;
            }

            try
            {
                RotateIfNeeded(_filePath);
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 日志失败不能影响主流程。
            }
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytes)
            {
                return;
            }

            for (var i = MaxFiles - 1; i >= 1; i--)
            {
                var older = $"{path}.{i}";
                var newer = i == 1 ? path : $"{path}.{i - 1}";
                if (File.Exists(newer))
                {
                    File.Move(newer, older, overwrite: true);
                }
            }
        }
        catch
        {
            // 忽略轮转失败。
        }
    }
}
