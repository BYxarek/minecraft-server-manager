using System.IO.Compression;
using System.Text.Json;

namespace MinecraftServerManager;

public sealed class TemplateStore
{
    private readonly string root;
    private readonly string manifestPath;
    private Dictionary<ServerEdition, TemplateRecord> records = new();
    public TemplateStore(string? root = null)
    {
        this.root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftServerManager", "templates");
        manifestPath = Path.Combine(this.root, "templates.json");
        if (File.Exists(manifestPath))
        {
            try { records = JsonSerializer.Deserialize<Dictionary<ServerEdition, TemplateRecord>>(File.ReadAllText(manifestPath)) ?? new(); }
            catch (JsonException) { records = new(); }
        }
    }

    public bool HasAny => GetPath(ServerEdition.Bedrock) != null || GetPath(ServerEdition.Java) != null;
    public string? GetPath(ServerEdition edition)
    {
        var path = Path.Combine(root, edition == ServerEdition.Bedrock ? "bedrock.zip" : "server.jar");
        return File.Exists(path) ? path : null;
    }
    public string Description(ServerEdition edition)
    {
        if (GetPath(edition) == null) return "Файл не добавлен";
        return records.TryGetValue(edition, out var record) ? $"{record.FileName} · {record.ImportedAt:dd.MM.yyyy HH:mm}" : "Файл добавлен";
    }

    public void Import(ServerEdition edition, string source)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("Выбранный файл не найден.", source);
        var extension = edition == ServerEdition.Bedrock ? ".zip" : ".jar";
        if (!source.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Нужен файл {extension}.");
        using (var archive = ZipFile.OpenRead(source))
        {
            var required = edition == ServerEdition.Bedrock ? "bedrock_server.exe" : "META-INF/MANIFEST.MF";
            if (archive.GetEntry(required) == null) throw new InvalidDataException($"Файл не похож на официальный сервер {edition}: нет {required}.");
        }
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, edition == ServerEdition.Bedrock ? "bedrock.zip" : "server.jar");
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            var temporary = target + ".tmp";
            try { File.Copy(source, temporary, true); File.Move(temporary, target, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        records[edition] = new TemplateRecord(Path.GetFileName(source), DateTime.Now);
        var manifestTemp = manifestPath + ".tmp";
        File.WriteAllText(manifestTemp, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(manifestTemp, manifestPath, true);
    }

    private sealed record TemplateRecord(string FileName, DateTime ImportedAt);
}
