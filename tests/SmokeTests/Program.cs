using System.IO.Compression;
using System.Net;
using System.Net.Http;
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
    var bedrock = new ServerProfile { Edition = ServerEdition.Bedrock, Directory = Path.Combine(root, "new-parent", "bedrock"), Port = 19133 };
    TemplateInstaller.Install(bedrock, zip);
    Assert(File.Exists(Path.Combine(bedrock.Directory, "bedrock_server.exe")), "Bedrock template was not extracted");
    Assert(PropertiesFile.Read(bedrock).Contains("server-port=19133"), "Bedrock port was not written");
    var edited = PropertiesFile.SetValue("# keep this comment\nmotd=old\n", "motd", "new");
    Assert(edited.Contains("# keep this comment") && edited.Contains("motd=new"), "Simple property edits must preserve other lines");

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
    AllowListStore.ChangeOffline(bedrock, "Player One", true);
    Assert(AllowListStore.Read(bedrock).SequenceEqual(["Player One"]), "Bedrock allow list add failed");
    AllowListStore.ChangeOffline(bedrock, "Player One", false);
    Assert(AllowListStore.Read(bedrock).Count == 0 && File.Exists(AllowListStore.PathFor(bedrock) + ".bak"), "Bedrock allow list removal or backup failed");
    File.WriteAllText(AllowListStore.PathFor(java), "[{\"uuid\":\"123\",\"name\":\"Steve\"}]");
    AllowListStore.ChangeOffline(java, "Steve", false);
    Assert(AllowListStore.Read(java).Count == 0, "Java allow list removal failed");
    Assert(AllowListStore.CommandName(bedrock, "Player One") == "\"Player One\"", "Bedrock player names with spaces must be quoted");

    var deletedProfile = new ServerProfile { Directory = Path.Combine(root, "delete-server"), Edition = ServerEdition.Bedrock };
    var nestedProfile = new ServerProfile { Directory = Path.Combine(deletedProfile.Directory, "other-server"), Edition = ServerEdition.Java };
    Directory.CreateDirectory(Path.Combine(deletedProfile.Directory, "worlds", "test-world"));
    File.WriteAllText(Path.Combine(deletedProfile.Directory, "worlds", "test-world", "level.dat"), "data");
    Directory.CreateDirectory(Path.Combine(deletedProfile.Directory, "backups"));
    File.WriteAllText(Path.Combine(deletedProfile.Directory, "backups", "world.zip"), "backup");
    AssertThrows(() => ServerFolderDeletion.Validate(deletedProfile, [deletedProfile, nestedProfile]), "Overlapping server folders must block deletion");
    ServerFolderDeletion.Delete(deletedProfile, [deletedProfile]);
    Assert(!Directory.Exists(deletedProfile.Directory), "Deleting a server must remove its world directory");
    AssertThrows(() => ServerFolderDeletion.Validate(new ServerProfile { Directory = Path.GetPathRoot(root)! }, []), "Drive roots must block deletion");
    AssertThrows(() => ServerFolderDeletion.Validate(new ServerProfile { Directory = AppContext.BaseDirectory }, []), "Application folder must block deletion");

    Directory.CreateDirectory(Path.Combine(java.Directory, "world"));
    File.WriteAllText(Path.Combine(java.Directory, "world", "level.dat"), "world data");
    var backup = new ServerManager().CreateBackup(java);
    Assert(File.Exists(backup), "World backup was not created");
    using (var archive = ZipFile.OpenRead(backup)) Assert(archive.GetEntry("level.dat") != null, "World backup is incomplete");

    var now = DateTime.UtcNow;
    var cachePath = Path.Combine(root, "update-check.json");
    using var handler = new FakeUpdateHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"tag_name\":\"v99.0.0\",\"html_url\":\"https://github.com/example/release\"}")
    });
    using var http = new HttpClient(handler);
    var checker = new UpdateChecker(http, cachePath, () => now);
    var checks = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => checker.Check()));
    Assert(handler.Calls == 1 && checks.Count(x => x.FromCache) == 11, "Concurrent update checks must share one GitHub request");
    Assert((await new UpdateChecker(http, cachePath, () => now).Check()).FromCache && handler.Calls == 1, "Update cooldown must persist across launches");
    now = now.AddMinutes(16);
    Assert(!(await checker.Check()).FromCache && handler.Calls == 2, "Update check must resume after cooldown");

    var limitedNow = DateTime.UtcNow;
    using var limitedHandler = new FakeUpdateHandler(() =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", new DateTimeOffset(limitedNow.AddHours(2)).ToUnixTimeSeconds().ToString());
        return response;
    });
    using var limitedHttp = new HttpClient(limitedHandler);
    var limited = new UpdateChecker(limitedHttp, Path.Combine(root, "limited-update.json"), () => limitedNow);
    await limited.Check();
    limitedNow = limitedNow.AddMinutes(20);
    Assert((await limited.Check()).FromCache && limitedHandler.Calls == 1, "GitHub rate-limit reset must extend the cooldown");
    Console.WriteLine("Smoke tests passed.");
}
finally { Directory.Delete(root, true); }

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void AssertThrows(Action action, string message)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new Exception(message);
}

sealed class FakeUpdateHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        await Task.Delay(25, cancellationToken);
        return respond();
    }
}
