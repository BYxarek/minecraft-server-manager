using System.Text;

namespace MinecraftServerManager;

public static class PropertiesFile
{
    public static string PathFor(ServerProfile profile) => Path.Combine(profile.Directory, "server.properties");
    public static string Read(ServerProfile profile) => File.Exists(PathFor(profile)) ? File.ReadAllText(PathFor(profile)) : "";
    public static string Get(ServerProfile profile, string key, string fallback) => GetValue(Read(profile), key, fallback);
    public static Dictionary<string, string> Entries(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            var separator = trimmed.IndexOf('=');
            if (!trimmed.StartsWith('#') && separator > 0)
                result[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }
        return result;
    }
    public static string GetValue(string content, string key, string fallback)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#')) continue;
            var separator = trimmed.IndexOf('=');
            if (separator >= 0 && string.Equals(trimmed[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return trimmed[(separator + 1)..].Trim();
        }
        return fallback;
    }
    public static void Write(ServerProfile profile, string content)
    {
        var path = PathFor(profile);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Set(ServerProfile profile, string key, string value)
    {
        Write(profile, SetValue(Read(profile), key, value));
    }
    public static string SetValue(string content, string key, string value)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        var index = lines.FindIndex(x =>
        {
            var trimmed = x.Trim();
            var separator = trimmed.IndexOf('=');
            return !trimmed.StartsWith('#') && separator >= 0 && string.Equals(trimmed[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase);
        });
        if (index < 0) lines.Add(key + "=" + value);
        else lines[index] = key + "=" + value;
        return string.Join("\n", lines).TrimEnd('\n') + "\n";
    }
}
