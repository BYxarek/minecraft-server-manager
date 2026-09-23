using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MinecraftServerManager;

public sealed record UpdateResult(Version Current, Version? Latest, string Url, string? Error)
{
    public bool Available => Latest != null && Latest > Current;
}

public static class UpdateChecker
{
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string ReleasesApi = "https://api.github.com/repos/BYxarek/minecraft-server-manager/releases/latest";
    static UpdateChecker() => client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MinecraftServerManager", "0.1"));
    public static async Task<UpdateResult> Check()
    {
        var assemblyVersion = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 1, 0);
        var current = new Version(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(0, assemblyVersion.Build));
        try
        {
            using var response = await client.GetAsync(ReleasesApi);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new(current, null, "https://github.com/BYxarek/minecraft-server-manager/releases", null);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tag = json.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            var url = json.RootElement.GetProperty("html_url").GetString() ?? "https://github.com/BYxarek/minecraft-server-manager/releases";
            var latest = Version.TryParse(tag, out var parsed) ? new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build)) : null;
            return new(current, latest, url, null);
        }
        catch (Exception ex) { return new(current, null, "https://github.com/BYxarek/minecraft-server-manager/releases", ex.Message); }
    }
}
