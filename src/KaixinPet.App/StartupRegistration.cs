using System.IO;
using System.Reflection;
using System.Security;
using Microsoft.Win32;

namespace KaixinPet.App;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KaixinPet";

    public static bool TryApply(bool enabled, out string error)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
                            Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                error = "无法打开当前用户的启动项";
                return false;
            }

            if (enabled)
            {
                var command = BuildCommand();
                if (command is null)
                {
                    error = "无法确定程序路径";
                    return false;
                }

                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            error = $"写入启动项失败：{exception.Message}";
            return false;
        }
    }

    private static string? BuildCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        var fileName = Path.GetFileNameWithoutExtension(processPath);
        if (fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assembly = Assembly.GetEntryAssembly()?.Location;
            return string.IsNullOrWhiteSpace(assembly)
                ? null
                : $"\"{processPath}\" \"{assembly}\" --startup";
        }

        return $"\"{processPath}\" --startup";
    }
}
