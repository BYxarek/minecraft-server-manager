using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MinecraftServerManager;

public static class AllowListStore
{
    public static string PathFor(ServerProfile profile) => Path.Combine(profile.Directory, profile.Edition == ServerEdition.Java ? "whitelist.json" : "allowlist.json");

    public static IReadOnlyList<string> Read(ServerProfile profile)
    {
        var path = PathFor(profile);
        if (!File.Exists(path)) return [];
        var array = JsonNode.Parse(File.ReadAllText(path)) as JsonArray ?? throw new InvalidDataException(T("InvalidAllowList"));
        return array.Select(node => node?["name"]?.GetValue<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).ToList();
    }

    public static void ChangeOffline(ServerProfile profile, string name, bool add)
    {
        if (profile.Edition == ServerEdition.Java && add) throw new InvalidOperationException(T("JavaAllowListNeedsServer"));
        var path = PathFor(profile);
        var array = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonArray ?? throw new InvalidDataException(T("InvalidAllowList")) : new JsonArray();
        var matches = array.Where(node => string.Equals(node?["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (add && matches.Count == 0) array.Add(new JsonObject { ["name"] = name, ["ignoresPlayerLimit"] = false });
        if (!add) foreach (var match in matches) array.Remove(match);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, array.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string CommandName(ServerProfile profile, string name)
    {
        name = name.Trim();
        if (profile.Edition == ServerEdition.Java)
        {
            if (!Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$")) throw new InvalidOperationException(T("InvalidPlayerName"));
            return name;
        }
        if (name.Length is < 1 or > 32 || name.Any(char.IsControl) || name.Contains('"') || name.Contains('\\'))
            throw new InvalidOperationException(T("InvalidPlayerName"));
        return "\"" + name + "\"";
    }
}
