using System.IO.Compression;

namespace MinecraftServerManager;

public static class TemplateInstaller
{
    public static string? FindLocalTemplate(ServerEdition edition)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 7 && directory != null; i++, directory = directory.Parent)
        {
            var source = Path.Combine(directory.FullName, "deffolt-minecraft-server");
            if (!System.IO.Directory.Exists(source)) continue;
            if (edition == ServerEdition.Java)
            {
                var jar = Path.Combine(source, "server.jar");
                if (File.Exists(jar)) return jar;
            }
            else
            {
                var zip = System.IO.Directory.GetFiles(source, "bedrock-server-*.zip").FirstOrDefault();
                if (zip != null) return zip;
            }
        }
        return null;
    }

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
