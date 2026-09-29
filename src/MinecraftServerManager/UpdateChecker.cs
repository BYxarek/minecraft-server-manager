using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MinecraftServerManager;

public sealed record UpdateResult(Version Current, Version? Latest, string Url, string? Error, bool NoRelease = false)
{
    public bool Available => Latest != null && Latest > Current;
}

public sealed record UpdateCheckOutcome(UpdateResult Result, bool FromCache, DateTime NextAllowedUtc);

public sealed class UpdateChecker
{
    private sealed record CacheEntry(DateTime CheckedAtUtc, DateTime NextAllowedUtc, string? Latest, string Url, string? Error, bool NoRelease);
    private static readonly HttpClient SharedClient = CreateClient();
    private const string ReleasesApi = "https://api.github.com/repos/BYxarek/minecraft-server-manager/releases/latest";
    private const string ReleasesUrl = "https://github.com/BYxarek/minecraft-server-manager/releases";
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(15);
    private readonly HttpClient client;
    private readonly string cachePath;
    private readonly Func<DateTime> utcNow;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CacheEntry? cache;
    private bool cacheLoaded;

    public UpdateChecker(HttpClient? client = null, string? cachePath = null, Func<DateTime>? utcNow = null)
    {
        this.client = client ?? SharedClient;
        this.cachePath = cachePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftServerManager", "update-check.json");
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MinecraftServerManager", "0.1"));
        return client;
    }

    public async Task<UpdateCheckOutcome> Check()
    {
        await gate.WaitAsync();
        try
        {
            if (!cacheLoaded) LoadCache();
            var now = utcNow();
            if (cache is { } saved && now < saved.NextAllowedUtc)
                return new(ToResult(saved), true, saved.NextAllowedUtc);

            var nextAllowed = now + MinimumInterval;
            UpdateResult result;
            try
            {
                using var response = await client.GetAsync(ReleasesApi);
                nextAllowed = Later(nextAllowed, RateLimitEnd(response, now));
                if (response.StatusCode == HttpStatusCode.NotFound)
                    result = new(CurrentVersion(), null, ReleasesUrl, null, true);
                else
                {
                    response.EnsureSuccessStatusCode();
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var tag = json.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
                    var url = json.RootElement.GetProperty("html_url").GetString() ?? ReleasesUrl;
                    var latest = Version.TryParse(tag, out var parsed) ? new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build)) : null;
                    result = new(CurrentVersion(), latest, url, null);
                }
            }
            catch (Exception ex) { result = new(CurrentVersion(), null, ReleasesUrl, ex.Message); }

            cache = new(now, nextAllowed, result.Latest?.ToString(3), result.Url, result.Error, result.NoRelease);
            SaveCache(cache);
            return new(result, false, nextAllowed);
        }
        finally { gate.Release(); }
    }

    private static Version CurrentVersion()
    {
        var version = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 1, 0);
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    private static DateTime Later(DateTime first, DateTime? second) => second is { } value && value > first ? value : first;

    private static DateTime? RateLimitEnd(HttpResponseMessage response, DateTime now)
    {
        if (response.Headers.RetryAfter?.Delta is { } delay) return now + delay;
        if (response.Headers.RetryAfter?.Date is { } date) return date.UtcDateTime;
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0" &&
            response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) && long.TryParse(reset.FirstOrDefault(), out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        return null;
    }

    private UpdateResult ToResult(CacheEntry entry)
    {
        var latest = Version.TryParse(entry.Latest, out var parsed) ? parsed : null;
        return new(CurrentVersion(), latest, entry.Url, entry.Error, entry.NoRelease);
    }

    private void LoadCache()
    {
        cacheLoaded = true;
        try
        {
            if (File.Exists(cachePath)) cache = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(cachePath));
        }
        catch (Exception) { cache = null; }
    }

    private void SaveCache(CacheEntry entry)
    {
        var temporary = cachePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(entry));
            File.Move(temporary, cachePath, true);
        }
        catch (Exception) { /* An unwritable cache must not break update checks. */ }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception) { } }
    }
}
