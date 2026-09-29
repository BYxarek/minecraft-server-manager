param(
    [Parameter(Mandatory = $true)][ValidateSet('Detect', 'Install')][string]$Mode,
    [string]$StatusFile,
    [string]$ArchivePath,
    [string]$ManagedRoot,
    [string]$SettingsPath,
    [string]$ExpectedSha256
)

$ErrorActionPreference = 'Stop'

function Get-JavaVersion([string]$Executable) {
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { return $null }
    try {
        $start = [System.Diagnostics.ProcessStartInfo]::new($Executable)
        $start.Arguments = '-version'
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardError = $true
        $start.RedirectStandardOutput = $true
        $process = [System.Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(5000)) { $process.Kill($true); return $null }
            $output = $process.StandardError.ReadToEnd() + $process.StandardOutput.ReadToEnd()
            if ($output -match 'version\s+"([^"]+)"') { return $Matches[1] }
            if ($output -match '(?:openjdk|java)\s+(\d+(?:\.\d+)+)') { return $Matches[1] }
        }
        finally { $process.Dispose() }
    }
    catch { return $null }
    return $null
}

if ($Mode -eq 'Detect') {
    if (-not $StatusFile) { throw 'StatusFile is required.' }
    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($ManagedRoot) { $candidates.Add((Join-Path $ManagedRoot 'bin/java.exe')) }
    if ($env:JAVA_HOME) { $candidates.Add((Join-Path $env:JAVA_HOME 'bin/java.exe')) }
    Get-Command java.exe -All -ErrorAction SilentlyContinue | ForEach-Object { $candidates.Add($_.Source) }
    $foundPath = ''
    $foundVersion = ''
    foreach ($candidate in $candidates | Select-Object -Unique) {
        $version = Get-JavaVersion $candidate
        if ($version) { $foundPath = $candidate; $foundVersion = $version; break }
    }
    $statusDirectory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($StatusFile))
    [System.IO.Directory]::CreateDirectory($statusDirectory) | Out-Null
    [System.IO.File]::WriteAllText($StatusFile, "[Java]`r`nVersion=$foundVersion`r`nPath=$foundPath`r`n", [System.Text.Encoding]::Unicode)
    exit 0
}

if (-not $ArchivePath -or -not $ManagedRoot -or -not $SettingsPath -or -not $ExpectedSha256) { throw 'Install parameters are incomplete.' }
$managed = [System.IO.Path]::GetFullPath($ManagedRoot).TrimEnd('\')
$localData = [System.IO.Path]::GetFullPath([Environment]::GetFolderPath('LocalApplicationData')).TrimEnd('\')
if (-not $managed.StartsWith($localData + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Java target must be inside LocalApplicationData.' }
if ($managed -ne [System.IO.Path]::Combine($localData, 'MinecraftServerManager', 'Java25')) { throw 'Unexpected managed Java target.' }
if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) { throw 'Java archive was not downloaded.' }
if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $ExpectedSha256.ToUpperInvariant()) { throw 'Java archive checksum mismatch.' }

$parent = [System.IO.Path]::GetDirectoryName($managed)
[System.IO.Directory]::CreateDirectory($parent) | Out-Null
$stage = Join-Path $parent ('.java25-stage-' + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $parent ('.java25-backup-' + [Guid]::NewGuid().ToString('N'))
$oldUserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$oldJavaHome = [Environment]::GetEnvironmentVariable('JAVA_HOME', 'User')
$movedOld = $false
$installedNew = $false
try {
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $stage -Force
    $java = Get-ChildItem -LiteralPath $stage -Recurse -File -Filter java.exe | Where-Object { $_.Directory.Name -eq 'bin' } | Select-Object -First 1
    if (-not $java) { throw 'Java archive does not contain bin/java.exe.' }
    $version = Get-JavaVersion $java.FullName
    if ($version -notmatch '^25(?:\.|$)') { throw "Expected Java 25, found $version." }
    $sourceRoot = $java.Directory.Parent.FullName
    if (-not $sourceRoot.StartsWith($stage + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Java archive layout is unsafe.' }
    if (Test-Path -LiteralPath $managed) { Move-Item -LiteralPath $managed -Destination $backup; $movedOld = $true }
    Move-Item -LiteralPath $sourceRoot -Destination $managed
    $installedNew = $true
    $javaPath = Join-Path $managed 'bin/java.exe'
    $binPath = Join-Path $managed 'bin'
    $pathParts = @($oldUserPath -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ine $binPath.TrimEnd('\') })
    [Environment]::SetEnvironmentVariable('Path', ((@($binPath) + $pathParts) -join ';').TrimEnd(';'), 'User')
    [Environment]::SetEnvironmentVariable('JAVA_HOME', $managed, 'User')
    $settingsDirectory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($SettingsPath))
    [System.IO.Directory]::CreateDirectory($settingsDirectory) | Out-Null
    $temporarySettings = $SettingsPath + '.tmp'
    [System.IO.File]::WriteAllText($temporarySettings, (@{ path = $javaPath } | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Copy($temporarySettings, $SettingsPath, $true)
    [System.IO.File]::Delete($temporarySettings)
    if ($movedOld) { Remove-Item -LiteralPath $backup -Recurse -Force }
}
catch {
    [Environment]::SetEnvironmentVariable('Path', $oldUserPath, 'User')
    [Environment]::SetEnvironmentVariable('JAVA_HOME', $oldJavaHome, 'User')
    if ($installedNew -and (Test-Path -LiteralPath $managed)) { Remove-Item -LiteralPath $managed -Recurse -Force }
    if ($movedOld -and (Test-Path -LiteralPath $backup)) { Move-Item -LiteralPath $backup -Destination $managed }
    throw
}
finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
