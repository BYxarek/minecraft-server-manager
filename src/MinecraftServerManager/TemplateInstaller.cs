using System.IO.Compression;

namespace MinecraftServerManager;

public static class TemplateInstaller
{
    public static void Install(ServerProfile profile, string source)
    {
        var target = Path.GetFullPath(profile.Directory);
        if (System.IO.Directory.Exists(target) && System.IO.Directory.EnumerateFileSystemEntries(target).Any())
            throw new InvalidOperationException(T("EmptyFolderRequired"));
        if (profile.Edition == ServerEdition.Bedrock && !source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(T("BedrockZipRequired"));
        if (profile.Edition == ServerEdition.Java && !source.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(T("JavaJarRequired"));
        var staging = target + ".install-" + Guid.NewGuid().ToString("N");
        var originalExists = System.IO.Directory.Exists(target);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        System.IO.Directory.CreateDirectory(staging);
        try
        {
            if (profile.Edition == ServerEdition.Bedrock)
            {
                ZipFile.ExtractToDirectory(source, staging);
                if (!File.Exists(Path.Combine(staging, "bedrock_server.exe")))
                    throw new InvalidDataException(T("BedrockExecutableNotInArchive"));
            }
            else
            {
                File.Copy(source, Path.Combine(staging, "server.jar"));
                File.WriteAllText(Path.Combine(staging, "eula.txt"), "eula=false\n");
            }
            var stagingProfile = new ServerProfile { Directory = staging, Edition = profile.Edition, Port = profile.Port };
            PropertiesFile.Set(stagingProfile, "server-port", profile.Port.ToString());
            if (originalExists) System.IO.Directory.Delete(target);
            System.IO.Directory.Move(staging, target);
        }
        finally { if (System.IO.Directory.Exists(staging)) System.IO.Directory.Delete(staging, true); }
    }
}
