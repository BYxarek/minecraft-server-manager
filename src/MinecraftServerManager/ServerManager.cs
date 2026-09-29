using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MinecraftServerManager;

public enum ServerState { Stopped, Starting, Running, Stopping, Restarting, Failed }

public sealed class ServerManager
{
    private readonly object processGate = new();
    private readonly ConcurrentDictionary<Guid, Process> processes = new();
    private readonly ConcurrentDictionary<Guid, StringBuilder> logs = new();
    private readonly ConcurrentDictionary<Guid, byte> stopping = new();
    private readonly ConcurrentDictionary<Guid, Queue<DateTime>> restartAttempts = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> pendingRestarts = new();
    private readonly ConcurrentDictionary<Guid, ServerState> states = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<string>> onlinePlayers = new();
    private readonly ConcurrentDictionary<Guid, byte> awaitingBedrockNames = new();
    private bool shuttingDown;
    public event Action<Guid>? Changed;

    public ServerState State(ServerProfile profile) => states.GetOrAdd(profile.Id, ServerState.Stopped);
    public IReadOnlyList<string> OnlinePlayers(ServerProfile profile) => onlinePlayers.GetValueOrDefault(profile.Id) ?? [];
    public void RequestPlayers(ServerProfile profile) => Send(profile, "list");

    public bool IsRunning(ServerProfile profile)
    {
        try { return processes.TryGetValue(profile.Id, out var process) && !process.HasExited; }
        catch (InvalidOperationException) { return false; }
    }
    public int? ProcessId(ServerProfile profile)
    {
        try { return processes.TryGetValue(profile.Id, out var process) && !process.HasExited ? process.Id : null; }
        catch (InvalidOperationException) { return null; }
    }
    public double? MemoryMb(ServerProfile profile)
    {
        if (!IsRunning(profile)) return null;
        try { if (!processes.TryGetValue(profile.Id, out var process)) return null; process.Refresh(); return Math.Round(process.WorkingSet64 / 1048576d, 1); }
        catch { return null; }
    }
    public string GetLog(ServerProfile profile)
    {
        if (!logs.TryGetValue(profile.Id, out var buffer)) return T("NoLogYet");
        lock (buffer) return buffer.ToString();
    }
    private void Append(ServerProfile profile, string line)
    {
        ParsePlayers(profile, line);
        var buffer = logs.GetOrAdd(profile.Id, _ => new StringBuilder());
        lock (buffer)
        {
            buffer.AppendLine(line);
            if (buffer.Length > 200_000) buffer.Remove(0, buffer.Length - 150_000);
        }
        Changed?.Invoke(profile.Id);
    }

    private void ParsePlayers(ServerProfile profile, string line)
    {
        // Java includes names on the count line; Bedrock writes them on the next line.
        var java = Regex.Match(line, @"There are \d+ of a max of \d+ players online:\s*(.*)$", RegexOptions.IgnoreCase);
        if (java.Success)
        {
            onlinePlayers[profile.Id] = SplitPlayers(java.Groups[1].Value);
            Changed?.Invoke(profile.Id);
            return;
        }
        var bedrock = Regex.Match(line, @"There are (\d+)/(\d+) players online:?\s*(.*)$", RegexOptions.IgnoreCase);
        if (bedrock.Success)
        {
            if (bedrock.Groups[1].Value == "0")
            {
                awaitingBedrockNames.TryRemove(profile.Id, out _);
                onlinePlayers[profile.Id] = [];
            }
            else if (!string.IsNullOrWhiteSpace(bedrock.Groups[3].Value)) onlinePlayers[profile.Id] = SplitPlayers(bedrock.Groups[3].Value);
            else awaitingBedrockNames[profile.Id] = 0;
            Changed?.Invoke(profile.Id);
            return;
        }
        if (awaitingBedrockNames.TryRemove(profile.Id, out _))
        {
            var names = Regex.Replace(line, @"^\[[^]]+\]\s*", "").Trim();
            onlinePlayers[profile.Id] = SplitPlayers(names);
            Changed?.Invoke(profile.Id);
        }
    }

    private static IReadOnlyList<string> SplitPlayers(string names) => names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public void Start(ServerProfile profile)
    {
        lock (processGate)
        {
            if (shuttingDown) throw new InvalidOperationException(T("AppClosing"));
            if (processes.ContainsKey(profile.Id) || State(profile) == ServerState.Starting) throw new InvalidOperationException(T("AlreadyRunning"));
            CancelPendingRestart(profile);
            states[profile.Id] = ServerState.Starting;
        }
        Changed?.Invoke(profile.Id);
        try { StartProcess(profile); }
        catch { states[profile.Id] = ServerState.Failed; Changed?.Invoke(profile.Id); throw; }
    }

    private void StartProcess(ServerProfile profile)
    {
        var executable = profile.Edition == ServerEdition.Bedrock ? Path.Combine(profile.Directory, "bedrock_server.exe") : JavaRuntime.Resolve(profile.JavaPath);
        if (profile.Edition == ServerEdition.Bedrock && !File.Exists(executable)) throw new FileNotFoundException(T("BedrockExecutableNotFound"), executable);
        if (profile.Edition == ServerEdition.Java && !File.Exists(Path.Combine(profile.Directory, "server.jar"))) throw new FileNotFoundException(T("JavaJarNotFound"));
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
                throw new InvalidOperationException(T("AcceptEula"));
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
            int code;
            try { code = process.ExitCode; } catch (InvalidOperationException) { code = -1; }
            bool shouldRestart;
            lock (processGate)
            {
                if (processes.TryGetValue(profile.Id, out var current) && ReferenceEquals(current, process)) processes.TryRemove(profile.Id, out _);
                else { process.Dispose(); return; }
                var expected = stopping.TryRemove(profile.Id, out _);
                shouldRestart = profile.AutoRestart && !expected && !shuttingDown;
                states[profile.Id] = shouldRestart ? ServerState.Restarting : expected || code == 0 ? ServerState.Stopped : ServerState.Failed;
                onlinePlayers.TryRemove(profile.Id, out _);
                awaitingBedrockNames.TryRemove(profile.Id, out _);
            }
            Append(profile, F("ServerExited", code));
            Changed?.Invoke(profile.Id);
            process.Dispose();
            if (shouldRestart) _ = RestartAfterCrash(profile);
        };
        try
        {
            lock (processGate)
            {
                if (shuttingDown) throw new InvalidOperationException(T("AppClosing"));
                if (!process.Start()) throw new InvalidOperationException(T("StartFailed"));
                processes[profile.Id] = process;
                states[profile.Id] = ServerState.Running;
                Append(profile, F("StartedPid", process.Id));
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
        }
        catch
        {
            lock (processGate)
            {
                if (processes.TryGetValue(profile.Id, out var current) && ReferenceEquals(current, process)) processes.TryRemove(profile.Id, out _);
            }
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.Dispose();
            throw;
        }
        Changed?.Invoke(profile.Id);
    }

    private async Task RestartAfterCrash(ServerProfile profile)
    {
        var attempts = restartAttempts.GetOrAdd(profile.Id, _ => new Queue<DateTime>());
        lock (attempts)
        {
            while (attempts.Count > 0 && DateTime.UtcNow - attempts.Peek() > TimeSpan.FromMinutes(5)) attempts.Dequeue();
            if (attempts.Count >= 3) { states[profile.Id] = ServerState.Failed; Append(profile, T("RestartLimit")); return; }
            attempts.Enqueue(DateTime.UtcNow);
        }
        using var cancellation = new CancellationTokenSource();
        lock (processGate)
        {
            if (shuttingDown || states.GetValueOrDefault(profile.Id) != ServerState.Restarting) return;
            pendingRestarts[profile.Id] = cancellation;
        }
        try
        {
            await Task.Delay(3000, cancellation.Token);
            lock (processGate)
            {
                if (cancellation.IsCancellationRequested || shuttingDown || !profile.AutoRestart || IsRunning(profile) ||
                    !pendingRestarts.TryGetValue(profile.Id, out var current) || !ReferenceEquals(current, cancellation)) return;
                pendingRestarts.TryRemove(profile.Id, out _);
                Start(profile);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { states[profile.Id] = ServerState.Failed; Append(profile, F("RestartFailed", ex.Message)); }
        finally { pendingRestarts.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(profile.Id, cancellation)); }
    }

    public void CancelPendingRestart(ServerProfile profile)
    {
        lock (processGate)
        {
            if (pendingRestarts.TryRemove(profile.Id, out var cancellation)) cancellation.Cancel();
            if (!IsRunning(profile)) states[profile.Id] = ServerState.Stopped;
        }
        Changed?.Invoke(profile.Id);
    }

    public void Send(ServerProfile profile, string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        lock (processGate)
        {
            if (!processes.TryGetValue(profile.Id, out var process) || process.HasExited) throw new InvalidOperationException(T("NotStartedHere"));
            process.StandardInput.WriteLine(command);
            process.StandardInput.Flush();
        }
        Append(profile, "> " + command);
    }

    public async Task Stop(ServerProfile profile, bool force = false)
    {
        Process process;
        lock (processGate)
        {
            CancelPendingRestart(profile);
            if (!processes.TryGetValue(profile.Id, out process!) || !IsRunning(profile)) return;
            stopping[profile.Id] = 0;
            states[profile.Id] = ServerState.Stopping;
        }
        Changed?.Invoke(profile.Id);
        if (!force)
        {
            try { Send(profile, "stop"); }
            catch (InvalidOperationException) { return; }
            catch (IOException) { force = true; }
            if (!force)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await process.WaitForExitAsync(timeout.Token); return; }
                catch (ObjectDisposedException) { return; }
                catch (OperationCanceledException) { Append(profile, T("StopTimeout")); }
            }
        }
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { return; }
        try { await process.WaitForExitAsync(); } catch (ObjectDisposedException) { }
    }

    public async Task StopAll()
    {
        List<ServerProfile> profiles;
        lock (processGate)
        {
            shuttingDown = true;
            foreach (var id in pendingRestarts.Keys.ToList())
                if (pendingRestarts.TryRemove(id, out var cancellation)) cancellation.Cancel();
            profiles = activeProfiles.Where(p => processes.ContainsKey(p.Id)).ToList();
        }
        await Task.WhenAll(profiles.Select(p => Stop(p)));
    }
    public void ResumeAfterFailedShutdown()
    {
        lock (processGate) shuttingDown = false;
    }
    private IReadOnlyList<ServerProfile> activeProfiles = [];
    public void SetProfiles(IReadOnlyList<ServerProfile> profiles) => activeProfiles = profiles;

    public string CreateBackup(ServerProfile profile)
    {
        if (IsRunning(profile)) throw new InvalidOperationException(T("StopBeforeBackup"));
        var source = WorldDirectory(profile);
        if (!System.IO.Directory.Exists(source)) throw new DirectoryNotFoundException(T("WorldNotCreated"));
        var backups = Path.Combine(profile.Directory, "backups");
        System.IO.Directory.CreateDirectory(backups);
        var destination = Path.Combine(backups, $"{profile.Edition}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.zip");
        try { ZipFile.CreateFromDirectory(source, destination, CompressionLevel.Optimal, false); }
        catch { if (File.Exists(destination)) File.Delete(destination); throw; }
        return destination;
    }

    public void RestoreBackup(ServerProfile profile, string backupPath)
    {
        if (IsRunning(profile)) throw new InvalidOperationException(T("StopBeforeWorldRestore"));
        var backups = Path.GetFullPath(Path.Combine(profile.Directory, "backups"));
        var fullBackup = Path.GetFullPath(backupPath);
        if (!string.Equals(Path.GetDirectoryName(fullBackup), backups, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullBackup))
            throw new FileNotFoundException(T("BackupNotFound"), backupPath);
        var destination = WorldDirectory(profile);
        var staged = destination + ".restore-" + Guid.NewGuid().ToString("N");
        var previous = destination + ".previous-" + Guid.NewGuid().ToString("N");
        try
        {
            ZipFile.ExtractToDirectory(fullBackup, staged);
            if (Directory.Exists(destination)) Directory.Move(destination, previous);
            try { Directory.Move(staged, destination); }
            catch
            {
                if (Directory.Exists(previous)) Directory.Move(previous, destination);
                throw;
            }
            if (Directory.Exists(previous)) Directory.Delete(previous, true);
        }
        finally { if (Directory.Exists(staged)) Directory.Delete(staged, true); }
    }

    private static string WorldDirectory(ServerProfile profile)
    {
        if (profile.Edition == ServerEdition.Bedrock) return Path.Combine(profile.Directory, "worlds");
        var name = PropertiesFile.Get(profile, "level-name", "world");
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new InvalidDataException(T("InvalidWorldName"));
        return Path.Combine(profile.Directory, name);
    }
}
