using System.IO.Compression;

namespace MinecraftServerManager;

public static class TemplateInstaller
{
    public static void Install(ServerProfile profile, string source)
    {
        var target = Path.GetFullPath(profile.Directory);
        if (System.IO.Directory.Exists(target) && System.IO.Directory.EnumerateFileSystemEntries(target).Any())
            throw new InvalidOperationException("Папка сервера должна быть пустой. Для существующего сервера используйте «Подключить».");
        if (profile.Edition == ServerEdition.Bedrock && !source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Для Bedrock нужен ZIP-архив официального сервера.");
        if (profile.Edition == ServerEdition.Java && !source.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Для Java нужен файл server.jar.");
        System.IO.Directory.CreateDirectory(target);
        if (profile.Edition == ServerEdition.Bedrock)
        {
            ZipFile.ExtractToDirectory(source, target);
            if (!File.Exists(Path.Combine(target, "bedrock_server.exe")))
                throw new InvalidDataException("Архив не содержит bedrock_server.exe в корне.");
        }
        else
        {
            File.Copy(source, Path.Combine(target, "server.jar"));
            File.WriteAllText(Path.Combine(target, "eula.txt"), "eula=false\n");
        }
        PropertiesFile.Set(profile, "server-port", profile.Port.ToString());
    }
}
