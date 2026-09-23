using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Collections.Concurrent;

namespace MinecraftServerManager;

public sealed class ServerManager
{
    private readonly ConcurrentDictionary<Guid, Process> processes = new();
    private readonly ConcurrentDictionary<Guid, StringBuilder> logs = new();
    private readonly ConcurrentDictionary<Guid, byte> stopping = new();
    private readonly ConcurrentDictionary<Guid, Queue<DateTime>> restartAttempts = new();
    public event Action<Guid>? Changed;

    public bool IsRunning(ServerProfile profile) => processes.TryGetValue(profile.Id, out var p) && !p.HasExited;
    public int? ProcessId(ServerProfile profile) => IsRunning(profile) ? processes[profile.Id].Id : null;
    public double? MemoryMb(ServerProfile profile)
    {
        if (!IsRunning(profile)) return null;
        try { processes[profile.Id].Refresh(); return Math.Round(processes[profile.Id].WorkingSet64 / 1048576d, 1); }
        catch { return null; }
    }
    public string GetLog(ServerProfile profile) => logs.TryGetValue(profile.Id, out var buffer) ? buffer.ToString() : "Сервер ещё не запускался в приложении.";
    private void Append(ServerProfile profile, string line)
    {
        var buffer = logs.GetOrAdd(profile.Id, _ => new StringBuilder());
        lock (buffer)
        {
            buffer.AppendLine(line);
            if (buffer.Length > 200_000) buffer.Remove(0, buffer.Length - 150_000);
        }
        Changed?.Invoke(profile.Id);
    }

    public void Start(ServerProfile profile)
    {
        if (IsRunning(profile)) throw new InvalidOperationException("Сервер уже работает.");
        var executable = profile.Edition == ServerEdition.Bedrock ? Path.Combine(profile.Directory, "bedrock_server.exe") : profile.JavaPath;
        if (profile.Edition == ServerEdition.Bedrock && !File.Exists(executable)) throw new FileNotFoundException("Не найден bedrock_server.exe.", executable);
        if (profile.Edition == ServerEdition.Java && !File.Exists(Path.Combine(profile.Directory, "server.jar"))) throw new FileNotFoundException("Не найден server.jar.");
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = profile.Directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (profile.Edition == ServerEdition.Java)
        {
            if (!File.Exists(Path.Combine(profile.Directory, "eula.txt")) || !File.ReadAllText(Path.Combine(profile.Directory, "eula.txt")).Contains("eula=true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Для Java-сервера прочитайте EULA Minecraft и установите eula=true в eula.txt.");
            info.ArgumentList.Add($"-Xmx{profile.MemoryMb}M");
            info.ArgumentList.Add("-jar");
            info.ArgumentList.Add("server.jar");
            info.ArgumentList.Add("nogui");
        }
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Append(profile, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(profile, e.Data); };
        process.Exited += (_, _) =>
        {
            var code = process.ExitCode;
            processes.TryRemove(profile.Id, out _);
            Append(profile, $"[Приложение] Сервер завершился. Код: {code}.");
            var shouldRestart = profile.AutoRestart && !stopping.TryRemove(profile.Id, out _);
            Changed?.Invoke(profile.Id);
            process.Dispose();
            if (shouldRestart) _ = RestartAfterCrash(profile);
        };
        stopping.TryRemove(profile.Id, out _);
        if (!process.Start()) throw new InvalidOperationException("Не удалось запустить сервер.");
        processes[profile.Id] = process;
        Append(profile, $"[Приложение] Запущен PID {process.Id}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Changed?.Invoke(profile.Id);
    }

    private async Task RestartAfterCrash(ServerProfile profile)
    {
        var attempts = restartAttempts.GetOrAdd(profile.Id, _ => new Queue<DateTime>());
        lock (attempts)
        {
            while (attempts.Count > 0 && DateTime.UtcNow - attempts.Peek() > TimeSpan.FromMinutes(5)) attempts.Dequeue();
            if (attempts.Count >= 3) { Append(profile, "[Приложение] Автозапуск остановлен после трёх сбоев за 5 минут."); return; }
            attempts.Enqueue(DateTime.UtcNow);
        }
        await Task.Delay(3000);
        if (!profile.AutoRestart || IsRunning(profile)) return;
        try { Start(profile); } catch (Exception ex) { Append(profile, "[Приложение] Автозапуск не удался: " + ex.Message); }
    }

    public void Send(ServerProfile profile, string command)
    {
        if (!IsRunning(profile)) throw new InvalidOperationException("Сервер не запущен из приложения.");
        if (string.IsNullOrWhiteSpace(command)) return;
        processes[profile.Id].StandardInput.WriteLine(command);
        processes[profile.Id].StandardInput.Flush();
        Append(profile, "> " + command);
    }

    public async Task Stop(ServerProfile profile, bool force = false)
    {
        if (!IsRunning(profile)) return;
        var process = processes[profile.Id];
        stopping[profile.Id] = 0;
        if (!force)
        {
            Send(profile, "stop");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); return; }
            catch (OperationCanceledException) { Append(profile, "[Приложение] Время ожидания остановки истекло."); }
        }
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    public async Task StopAll()
    {
        var running = processes.Values.Where(p => !p.HasExited).Select(p => p.Id).ToHashSet();
        var profiles = activeProfiles.Where(p => running.Contains(ProcessId(p) ?? -1)).ToList();
        await Task.WhenAll(profiles.Select(p => Stop(p)));
    }
    private IReadOnlyList<ServerProfile> activeProfiles = [];
    public void SetProfiles(IReadOnlyList<ServerProfile> profiles) => activeProfiles = profiles;

    public string CreateBackup(ServerProfile profile)
    {
        if (IsRunning(profile)) throw new InvalidOperationException("Остановите сервер перед созданием копии мира.");
        var worlds = profile.Edition == ServerEdition.Bedrock ? "worlds" : "world";
        var source = Path.Combine(profile.Directory, worlds);
        if (!System.IO.Directory.Exists(source)) throw new DirectoryNotFoundException("Мир ещё не создан.");
        var backups = Path.Combine(profile.Directory, "backups");
        System.IO.Directory.CreateDirectory(backups);
        var destination = Path.Combine(backups, $"{profile.Edition}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        ZipFile.CreateFromDirectory(source, destination, CompressionLevel.Optimal, false);
        return destination;
    }
}
