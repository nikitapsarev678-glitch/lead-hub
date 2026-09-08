using System.IO;

namespace LeadHub.App.Services;

/// <summary>Пути данных приложения: %LOCALAPPDATA%\LeadHub, либо папка exe в портативном режиме.</summary>
public static class AppPaths
{
    private static bool? _portable;

    public static bool Portable =>
        _portable ??= File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.marker"));

    public static string DataDir
    {
        get
        {
            var dir = Portable
                ? AppContext.BaseDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeadHub");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string Database => Path.Combine(DataDir, "leadhub.db");
    public static string KeyFile => Path.Combine(DataDir, "key.bin");
    public static string SingBoxConfig => Path.Combine(DataDir, "singbox-config.json");
    public static string ExportDir => Path.Combine(DataDir, "export");

    /// <summary>Поиск sing-box.exe: настройка → рядом с программой → PATH.</summary>
    public static string? FindSingBox(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return configuredPath;
        var local = Path.Combine(AppContext.BaseDirectory, "tools", "sing-box", "sing-box.exe");
        if (File.Exists(local)) return local;
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "sing-box.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* некорректная запись PATH */ }
        }
        return null;
    }
}
