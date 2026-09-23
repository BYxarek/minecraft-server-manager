using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinecraftServerManager;

public sealed class ProfileStore
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftServerManager", "servers.json");
    private readonly JsonSerializerOptions options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public List<ServerProfile> Profiles { get; private set; } = [];

    public void Load()
    {
        if (File.Exists(path)) Profiles = JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(path), options) ?? [];
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Profiles, options));
        File.Move(temp, path, true);
    }
}
