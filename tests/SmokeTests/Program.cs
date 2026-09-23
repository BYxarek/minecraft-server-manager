using System.IO.Compression;
using MinecraftServerManager;

var root = Path.Combine(Path.GetTempPath(), "minecraft-manager-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var zip = Path.Combine(root, "bedrock.zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        using var writer = new StreamWriter(archive.CreateEntry("bedrock_server.exe").Open());
        writer.Write("test executable");
    }
    var bedrock = new ServerProfile { Edition = ServerEdition.Bedrock, Directory = Path.Combine(root, "bedrock"), Port = 19133 };
    TemplateInstaller.Install(bedrock, zip);
    Assert(File.Exists(Path.Combine(bedrock.Directory, "bedrock_server.exe")), "Bedrock template was not extracted");
    Assert(PropertiesFile.Read(bedrock).Contains("server-port=19133"), "Bedrock port was not written");

    var jar = Path.Combine(root, "source.jar");
    using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
    {
        using var writer = new StreamWriter(archive.CreateEntry("META-INF/MANIFEST.MF").Open());
        writer.Write("Manifest-Version: 1.0");
    }
    var templates = new TemplateStore(Path.Combine(root, "templates"));
    Assert(!templates.HasAny, "Fresh install unexpectedly has templates");
    templates.Import(ServerEdition.Bedrock, zip);
    Assert(templates.HasAny && templates.GetPath(ServerEdition.Java) == null, "One template should be enough");
    Assert(File.Exists(templates.GetPath(ServerEdition.Bedrock)), "Bedrock template was not copied to app storage");
    templates.Import(ServerEdition.Java, jar);
    Assert(File.Exists(templates.GetPath(ServerEdition.Java)), "Java template was not copied to app storage");
    var reopened = new TemplateStore(Path.Combine(root, "templates"));
    Assert(reopened.Description(ServerEdition.Java).Contains("source.jar"), "Template metadata was not persisted");
    var java = new ServerProfile { Edition = ServerEdition.Java, Directory = Path.Combine(root, "java"), Port = 25566 };
    TemplateInstaller.Install(java, templates.GetPath(ServerEdition.Java)!);
    Assert(File.Exists(Path.Combine(java.Directory, "server.jar")), "Java template was not copied");
    Assert(File.ReadAllText(Path.Combine(java.Directory, "eula.txt")).Contains("eula=false"), "Java EULA must not be accepted automatically");
    PropertiesFile.Set(java, "server-port", "25567");
    Assert(PropertiesFile.Read(java).Contains("server-port=25567"), "Java port was not updated");
    Assert(File.Exists(PropertiesFile.PathFor(java) + ".bak"), "Properties backup missing");

    Directory.CreateDirectory(Path.Combine(java.Directory, "world"));
    File.WriteAllText(Path.Combine(java.Directory, "world", "level.dat"), "world data");
    var backup = new ServerManager().CreateBackup(java);
    Assert(File.Exists(backup), "World backup was not created");
    using (var archive = ZipFile.OpenRead(backup)) Assert(archive.GetEntry("level.dat") != null, "World backup is incomplete");
    Console.WriteLine("Smoke tests passed.");
}
finally { Directory.Delete(root, true); }

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
