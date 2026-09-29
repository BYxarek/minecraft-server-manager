using System.Text.Json;

namespace MinecraftServerManager;

public static class JavaRuntime
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MinecraftServerManager", "java.json");

    public static string DefaultPath()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (document.RootElement.TryGetProperty("path", out var property))
                {
                    var path = property.GetString();
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
                }
            }
        }
        catch (Exception) { }
        return "java";
    }

    public static string Resolve(string configuredPath) =>
        string.Equals(configuredPath, "java", StringComparison.OrdinalIgnoreCase) ? DefaultPath() : configuredPath;
}
