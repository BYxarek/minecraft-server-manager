using System.Text.Json.Serialization;

namespace MinecraftServerManager;

public enum ServerEdition { Bedrock, Java }

public sealed class ServerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Minecraft Server";
    public ServerEdition Edition { get; set; }
    public string Directory { get; set; } = "";
    public int Port { get; set; }
    public int MemoryMb { get; set; } = 2048;
    public string JavaPath { get; set; } = "java";
    public bool AutoRestart { get; set; }
    [JsonIgnore] public string Endpoint => $"127.0.0.1:{Port}";
    public override string ToString() => $"{Name}  ·  {Edition}  ·  {Port}";
}
