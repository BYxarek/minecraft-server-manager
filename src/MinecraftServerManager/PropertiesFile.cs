using System.Text;

namespace MinecraftServerManager;

public static class PropertiesFile
{
    public static string PathFor(ServerProfile profile) => Path.Combine(profile.Directory, "server.properties");
    public static string Read(ServerProfile profile) => File.Exists(PathFor(profile)) ? File.ReadAllText(PathFor(profile)) : "";
    public static void Write(ServerProfile profile, string content)
    {
        var path = PathFor(profile);
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
    public static void Set(ServerProfile profile, string key, string value)
    {
        var lines = Read(profile).Replace("\r\n", "\n").Split('\n').ToList();
        var index = lines.FindIndex(x => x.TrimStart().StartsWith(key + "=", StringComparison.Ordinal));
        if (index < 0) lines.Add(key + "=" + value);
        else lines[index] = key + "=" + value;
        Write(profile, string.Join("\n", lines).TrimEnd('\n') + "\n");
    }
}
