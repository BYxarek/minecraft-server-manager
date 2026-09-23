param(
    [int]$Port = 8765,
    [switch]$OpenBrowser
)

$ErrorActionPreference = 'Stop'
$script:PanelVersion = '0.1.0'
$script:Root = Split-Path -Parent $PSScriptRoot
$script:WebRoot = Join-Path $PSScriptRoot 'web'
$script:DataRoot = Join-Path $PSScriptRoot 'data'
$script:BackupRoot = Join-Path $script:Root 'backups'
$script:ExePath = Join-Path $script:Root 'bedrock_server.exe'
$script:PropertiesPath = Join-Path $script:Root 'server.properties'
$script:AllowlistPath = Join-Path $script:Root 'allowlist.json'
$script:PermissionsPath = Join-Path $script:Root 'permissions.json'
$script:ManagedProcess = $null
$script:OutputFile = $null
$script:ErrorFile = $null
$script:CopyTasks = @()
$script:StopRequested = $false
$script:RestartAttempts = [Collections.Generic.List[datetime]]::new()
$script:ConfigPath = Join-Path $script:DataRoot 'config.json'
$script:MetricsPath = Join-Path $script:DataRoot 'metrics.json'
$script:Metrics = [Collections.Generic.List[object]]::new()
$script:LastMetricAt = [datetime]::MinValue
$script:LastCpuAt = $null
$script:LastCpuSeconds = 0.0
$script:UpdateInfo = $null
$script:UpdateCheckedAt = [datetime]::MinValue
$script:Token = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')

[IO.Directory]::CreateDirectory($script:DataRoot) | Out-Null
[IO.Directory]::CreateDirectory($script:BackupRoot) | Out-Null
$script:Config = @{ autoRestart = $true; maxRestartAttempts = 3 }
if (Test-Path -LiteralPath $script:ConfigPath) {
    try {
        $savedConfig = [IO.File]::ReadAllText($script:ConfigPath) | ConvertFrom-Json
        $script:Config.autoRestart = [bool]$savedConfig.autoRestart
        if ([int]$savedConfig.maxRestartAttempts -in 1..10) { $script:Config.maxRestartAttempts = [int]$savedConfig.maxRestartAttempts }
    } catch { Write-Warning 'Не удалось прочитать настройки панели; используются значения по умолчанию.' }
}
if (Test-Path -LiteralPath $script:MetricsPath) {
    try { @([IO.File]::ReadAllText($script:MetricsPath) | ConvertFrom-Json) | ForEach-Object { $script:Metrics.Add($_) } } catch {}
}

function Save-PanelConfig {
    [IO.File]::WriteAllText($script:ConfigPath, ($script:Config | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}

function Get-BedrockProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name = 'bedrock_server.exe'" -ErrorAction SilentlyContinue | Where-Object {
        $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath) -eq [IO.Path]::GetFullPath($script:ExePath))
    })
}

function Get-PropertiesData {
    $raw = [IO.File]::ReadAllText($script:PropertiesPath)
    $values = [ordered]@{}
    foreach ($line in ($raw -split "`r?`n")) {
        if ($line -match '^\s*([^#;=][^=]*)=(.*)$') {
            $values[$matches[1].Trim()] = $matches[2]
        }
    }
    [ordered]@{ values = $values; raw = $raw }
}

function Write-TextSafely([string]$Path, [string]$Text) {
    if ($Text.Length -gt 1MB -or $Text.Contains([char]0)) { throw 'Недопустимое содержимое файла.' }
    $backup = "$Path.panel.bak"
    if (Test-Path -LiteralPath $Path) { Copy-Item -LiteralPath $Path -Destination $backup -Force }
    $temp = "$Path.panel.tmp"
    [IO.File]::WriteAllText($temp, $Text, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temp -Destination $Path -Force
}

function Read-JsonArray([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $text = [IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($text)) { return }
    $value = $text | ConvertFrom-Json
    if ($null -ne $value) { @($value) }
}

function Test-Properties([string]$Raw) {
    $parsed = [ordered]@{}
    foreach ($line in ($Raw -split "`r?`n")) {
        if ($line -match '^\s*([^#;=][^=]*)=(.*)$') { $parsed[$matches[1].Trim()] = $matches[2].Trim() }
    }
    foreach ($key in @('server-port', 'server-portv6')) {
        if ($parsed.Contains($key)) {
            $number = 0
            if ((-not [int]::TryParse($parsed[$key], [ref]$number)) -or $number -lt 1 -or $number -gt 65535) { throw "$key должен быть числом от 1 до 65535." }
        }
    }
    foreach ($key in @('max-players', 'view-distance', 'tick-distance', 'player-idle-timeout', 'max-threads')) {
        if ($parsed.Contains($key) -and $parsed[$key] -notmatch '^\d+$') { throw "$key должен быть целым неотрицательным числом." }
    }
    if ($parsed.Contains('view-distance') -and [int]$parsed['view-distance'] -lt 5) { throw 'view-distance должен быть не меньше 5.' }
    if ($parsed.Contains('tick-distance') -and ([int]$parsed['tick-distance'] -lt 4 -or [int]$parsed['tick-distance'] -gt 12)) { throw 'tick-distance должен быть от 4 до 12.' }
    if ($parsed.Contains('max-players') -and [int]$parsed['max-players'] -lt 1) { throw 'max-players должен быть больше 0.' }
}

function Get-ManagedRunning {
    $script:ManagedProcess -and -not $script:ManagedProcess.HasExited
}

function Start-BedrockServer {
    if ((Get-BedrockProcesses).Count -gt 0) { throw 'Сервер уже запущен. Остановите текущую консоль командой stop, затем запустите его из панели.' }

    $logPath = Join-Path $script:DataRoot 'console.log'
    $errorPath = Join-Path $script:DataRoot 'error.log'
    [IO.File]::AppendAllText($logPath, "`r`n===== Запуск $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') =====`r`n")
    $script:OutputFile = [IO.File]::Open($logPath, 'Append', 'Write', 'ReadWrite')
    $script:ErrorFile = [IO.File]::Open($errorPath, 'Append', 'Write', 'ReadWrite')

    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $script:ExePath
    $info.WorkingDirectory = $script:Root
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true

    $script:ManagedProcess = [Diagnostics.Process]::new()
    $script:ManagedProcess.StartInfo = $info
    if (-not $script:ManagedProcess.Start()) { throw 'Не удалось запустить сервер.' }
    $script:StopRequested = $false
    $script:CopyTasks = @(
        $script:ManagedProcess.StandardOutput.BaseStream.CopyToAsync($script:OutputFile),
        $script:ManagedProcess.StandardError.BaseStream.CopyToAsync($script:ErrorFile)
    )
}

function Send-Command([string]$Command) {
    if (-not (Get-ManagedRunning)) { throw 'Консоль доступна только для сервера, запущенного из панели.' }
    if ([string]::IsNullOrWhiteSpace($Command) -or $Command.Length -gt 500 -or $Command.Contains("`n") -or $Command.Contains("`r")) { throw 'Команда пуста или содержит недопустимые символы.' }
    $script:ManagedProcess.StandardInput.WriteLine($Command.TrimStart('/'))
    $script:ManagedProcess.StandardInput.Flush()
}

function Stop-BedrockServer([bool]$Force = $false) {
    if (-not (Get-ManagedRunning)) { throw 'Этот процесс не запущен из панели.' }
    $script:StopRequested = $true
    if ($Force) {
        $script:ManagedProcess.Kill($true)
        $script:ManagedProcess.WaitForExit(5000) | Out-Null
    } else {
        Send-Command 'stop'
        if (-not $script:ManagedProcess.WaitForExit(20000)) { throw 'Сервер не завершился за 20 секунд. Проверьте консоль или используйте аварийную остановку.' }
    }
    foreach ($file in @($script:OutputFile, $script:ErrorFile)) { if ($file) { $file.Dispose() } }
    $script:OutputFile = $null
    $script:ErrorFile = $null
}

function Watch-BedrockServer {
    if (-not $script:ManagedProcess -or -not $script:ManagedProcess.HasExited -or $script:StopRequested -or -not $script:Config.autoRestart) { return }
    foreach ($file in @($script:OutputFile, $script:ErrorFile)) { if ($file) { try { $file.Dispose() } catch {} } }
    $script:OutputFile = $null
    $script:ErrorFile = $null
    $now = Get-Date
    @($script:RestartAttempts | Where-Object { ($now - $_).TotalMinutes -ge 5 }) | ForEach-Object { $script:RestartAttempts.Remove($_) | Out-Null }
    if ($script:RestartAttempts.Count -ge $script:Config.maxRestartAttempts) {
        $script:StopRequested = $true
        [IO.File]::AppendAllText((Join-Path $script:DataRoot 'console.log'), "`r`n[ПАНЕЛЬ] Автоперезапуск остановлен: слишком много сбоев за 5 минут.`r`n")
        return
    }
    $script:RestartAttempts.Add($now)
    $script:ManagedProcess = $null
    [IO.File]::AppendAllText((Join-Path $script:DataRoot 'console.log'), "`r`n[ПАНЕЛЬ] Сервер неожиданно завершился, выполняется автоперезапуск.`r`n")
    try { Start-BedrockServer } catch { [IO.File]::AppendAllText((Join-Path $script:DataRoot 'error.log'), "`r`n[ПАНЕЛЬ] Ошибка автоперезапуска: $($_.Exception.Message)`r`n") }
}

function Add-Metric {
    $now = Get-Date
    if (($now - $script:LastMetricAt).TotalSeconds -lt 30) { return }
    $status = Get-Status
    $cpuPercent = 0.0
    if ($status.running) {
        $process = Get-Process -Id $status.pid -ErrorAction SilentlyContinue
        if ($process -and $script:LastCpuAt) {
            $elapsed = ($now - $script:LastCpuAt).TotalSeconds
            if ($elapsed -gt 0) { $cpuPercent = [math]::Round((($process.CPU - $script:LastCpuSeconds) / $elapsed / [Environment]::ProcessorCount) * 100, 1) }
        }
        if ($process) { $script:LastCpuSeconds = $process.CPU }
    }
    $script:LastCpuAt = $now
    $script:LastMetricAt = $now
    $script:Metrics.Add([ordered]@{ time = $now.ToString('o'); memoryMb = $status.memoryMb; cpuPercent = [math]::Max(0, $cpuPercent); running = $status.running })
    while ($script:Metrics.Count -gt 720) { $script:Metrics.RemoveAt(0) }
    [IO.File]::WriteAllText($script:MetricsPath, ($script:Metrics | ConvertTo-Json -Depth 4 -Compress), [Text.UTF8Encoding]::new($false))
}

function Get-UpdateInfo {
    if ($script:UpdateInfo -and ((Get-Date) - $script:UpdateCheckedAt).TotalHours -lt 1) { return $script:UpdateInfo }
    $installed = (Get-Item -LiteralPath $script:ExePath).VersionInfo.ProductVersion
    try {
        $data = Invoke-RestMethod -Uri 'https://net-secondary.web.minecraft-services.net/api/v1.0/download/links' -TimeoutSec 15
        $url = @($data.result.links | Where-Object downloadType -eq 'serverBedrockWindows')[0].downloadUrl
        $latest = if ($url -match 'bedrock-server-([0-9.]+)\.zip') { $matches[1] } else { throw 'Версия не найдена в официальном ответе.' }
        $script:UpdateInfo = [ordered]@{ installed = $installed; latest = $latest; updateAvailable = [version]$latest -gt [version]$installed; downloadPage = 'https://www.minecraft.net/en-us/download/server/bedrock'; checked = (Get-Date).ToString('o'); error = $null }
    } catch {
        $script:UpdateInfo = [ordered]@{ installed = $installed; latest = $null; updateAvailable = $false; downloadPage = 'https://www.minecraft.net/en-us/download/server/bedrock'; checked = (Get-Date).ToString('o'); error = $_.Exception.Message }
    }
    $script:UpdateCheckedAt = Get-Date
    $script:UpdateInfo
}

function Get-Status {
    $items = Get-BedrockProcesses
    $process = if ($items.Count) { Get-Process -Id $items[0].ProcessId -ErrorAction SilentlyContinue } else { $null }
    $managed = Get-ManagedRunning
    $properties = (Get-PropertiesData).values
    $version = (Get-Item -LiteralPath $script:ExePath).VersionInfo.ProductVersion
    [ordered]@{
        running = [bool]$process
        managed = [bool]$managed
        pid = if ($process) { $process.Id } else { $null }
        version = $version
        clientVersion = ($version -replace '^1\.', '' -replace '\.1$', '')
        uptimeSeconds = if ($process) { [math]::Floor(((Get-Date) - $process.StartTime).TotalSeconds) } else { 0 }
        memoryMb = if ($process) { [math]::Round($process.WorkingSet64 / 1MB, 1) } else { 0 }
        cpuSeconds = if ($process) { [math]::Round($process.CPU, 1) } else { 0 }
        serverName = $properties['server-name']
        levelName = $properties['level-name']
        maxPlayers = $properties['max-players']
        port = $properties['server-port']
        transport = $properties['transport']
        panelVersion = $script:PanelVersion
        autoRestart = [bool]$script:Config.autoRestart
    }
}

function Get-Worlds {
    $active = (Get-PropertiesData).values['level-name']
    $worldRoot = Join-Path $script:Root 'worlds'
    if (-not (Test-Path -LiteralPath $worldRoot)) { return @() }
    @(Get-ChildItem -LiteralPath $worldRoot -Directory | ForEach-Object {
        $size = (Get-ChildItem -LiteralPath $_.FullName -File -Recurse -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
        [ordered]@{ name = $_.Name; active = $_.Name -eq $active; sizeMb = [math]::Round($size / 1MB, 1); modified = $_.LastWriteTime.ToString('s') }
    })
}

function Set-ActiveWorld([string]$Name) {
    if ((Get-BedrockProcesses).Count -gt 0) { throw 'Сначала остановите сервер.' }
    $worldPath = Join-Path (Join-Path $script:Root 'worlds') $Name
    if ([IO.Path]::GetFileName($Name) -ne $Name -or -not (Test-Path -LiteralPath $worldPath -PathType Container)) { throw 'Мир не найден.' }
    $raw = [IO.File]::ReadAllText($script:PropertiesPath)
    if ($raw -match '(?m)^level-name=.*$') { $raw = [regex]::Replace($raw, '(?m)^level-name=.*$', "level-name=$Name") } else { $raw += "`r`nlevel-name=$Name`r`n" }
    Write-TextSafely $script:PropertiesPath $raw
}

function New-WorldBackup {
    $status = Get-Status
    if ($status.running -and -not $status.managed) { throw 'Для безопасной копии остановите внешний сервер или запустите его из панели.' }
    $world = $status.levelName
    $source = Join-Path (Join-Path $script:Root 'worlds') $world
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'Активный мир не найден.' }
    $held = $false
    try {
        if ($status.managed) {
            Send-Command 'save hold'
            $held = $true
            Start-Sleep -Milliseconds 1500 # ponytail: фиксированная пауза; разбирать save query стоит при больших мирах.
        }
        $safeName = $world -replace '[^\p{L}\p{N}._-]', '_'
        $target = Join-Path $script:BackupRoot ("{0}_{1}.zip" -f $safeName, (Get-Date -Format 'yyyyMMdd_HHmmss_fff'))
        Compress-Archive -Path (Join-Path $source '*') -DestinationPath $target -CompressionLevel Fastest
        [ordered]@{ name = [IO.Path]::GetFileName($target) }
    } finally {
        if ($held -and (Get-ManagedRunning)) { Send-Command 'save resume' }
    }
}

function Get-Backups {
    @(Get-ChildItem -LiteralPath $script:BackupRoot -Filter '*.zip' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | ForEach-Object {
        [ordered]@{ name = $_.Name; sizeMb = [math]::Round($_.Length / 1MB, 1); created = $_.LastWriteTime.ToString('s') }
    })
}

function Assert-SimpleName([string]$Name, [string]$Label = 'Имя') {
    if ([string]::IsNullOrWhiteSpace($Name) -or [IO.Path]::GetFileName($Name) -ne $Name -or $Name.Contains('..')) { throw "$Label недопустимо." }
}

function Expand-ZipBytes([byte[]]$Bytes, [string]$Prefix) {
    if (-not $Bytes -or $Bytes.Length -lt 4) { throw 'Архив пуст или повреждён.' }
    $folder = Join-Path $script:DataRoot ("$Prefix-" + [guid]::NewGuid().ToString('N'))
    $zip = "$folder.zip"
    [IO.File]::WriteAllBytes($zip, $Bytes)
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    try { Expand-Archive -LiteralPath $zip -DestinationPath $folder -Force } finally { [IO.File]::Delete($zip) }
    $folder
}

function Import-World([byte[]]$Bytes, [string]$FileName) {
    if ((Get-BedrockProcesses).Count -gt 0) { throw 'Для импорта мира сначала остановите сервер.' }
    $FileName = [Uri]::UnescapeDataString($FileName)
    Assert-SimpleName $FileName 'Имя файла'
    if ([IO.Path]::GetExtension($FileName) -notin @('.zip', '.mcworld')) { throw 'Поддерживаются файлы .mcworld и .zip.' }
    $temp = Expand-ZipBytes $Bytes 'world-import'
    try {
        $level = Get-ChildItem -LiteralPath $temp -Filter 'level.dat' -File -Recurse | Select-Object -First 1
        if (-not $level) { throw 'В архиве не найден level.dat — это не мир Bedrock.' }
        $source = $level.Directory.FullName
        $baseName = [IO.Path]::GetFileNameWithoutExtension($FileName) -replace '[^\p{L}\p{N} ._-]', '_'
        if ([string]::IsNullOrWhiteSpace($baseName)) { $baseName = 'Импортированный мир' }
        $destination = Join-Path (Join-Path $script:Root 'worlds') $baseName
        $suffix = 2
        while (Test-Path -LiteralPath $destination) { $destination = Join-Path (Join-Path $script:Root 'worlds') "$baseName $suffix"; $suffix++ }
        [IO.Directory]::Move($source, $destination)
        [ordered]@{ name = [IO.Path]::GetFileName($destination) }
    } finally {
        if (Test-Path -LiteralPath $temp) { [IO.Directory]::Delete($temp, $true) }
    }
}

function Export-World([string]$Name) {
    Assert-SimpleName $Name 'Имя мира'
    $active = (Get-PropertiesData).values['level-name']
    if ($Name -eq $active) { return New-WorldBackup }
    $source = Join-Path (Join-Path $script:Root 'worlds') $Name
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'Мир не найден.' }
    $safeName = $Name -replace '[^\p{L}\p{N}._-]', '_'
    $target = Join-Path $script:BackupRoot ("{0}_{1}.zip" -f $safeName, (Get-Date -Format 'yyyyMMdd_HHmmss_fff'))
    Compress-Archive -Path (Join-Path $source '*') -DestinationPath $target -CompressionLevel Fastest
    [ordered]@{ name = [IO.Path]::GetFileName($target) }
}

function Restore-WorldBackup([string]$BackupName) {
    Assert-SimpleName $BackupName 'Имя резервной копии'
    $backup = Join-Path $script:BackupRoot $BackupName
    if ([IO.Path]::GetExtension($BackupName) -ne '.zip' -or -not (Test-Path -LiteralPath $backup -PathType Leaf)) { throw 'Резервная копия не найдена.' }
    $status = Get-Status
    if ($status.running -and -not $status.managed) { throw 'Внешний сервер нужно остановить вручную.' }
    $restart = $status.managed
    if ($restart) { Stop-BedrockServer }
    $world = (Get-PropertiesData).values['level-name']
    $worldPath = Join-Path (Join-Path $script:Root 'worlds') $world
    New-WorldBackup | Out-Null
    $temp = Join-Path $script:DataRoot ('restore-' + [guid]::NewGuid().ToString('N'))
    $old = Join-Path $script:DataRoot ('previous-world-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($temp) | Out-Null
    try {
        Expand-Archive -LiteralPath $backup -DestinationPath $temp -Force
        if (-not (Test-Path -LiteralPath (Join-Path $temp 'level.dat') -PathType Leaf)) { throw 'В копии нет level.dat.' }
        [IO.Directory]::Move($worldPath, $old)
        try { [IO.Directory]::Move($temp, $worldPath) } catch { [IO.Directory]::Move($old, $worldPath); throw }
        [IO.Directory]::Delete($old, $true)
    } finally {
        if (Test-Path -LiteralPath $temp) { [IO.Directory]::Delete($temp, $true) }
        if ($restart) { Start-BedrockServer }
    }
}

function Get-EnabledPackIds([string]$Kind) {
    $world = (Get-PropertiesData).values['level-name']
    $fileName = if ($Kind -eq 'resource') { 'world_resource_packs.json' } else { 'world_behavior_packs.json' }
    $file = Join-Path (Join-Path (Join-Path $script:Root 'worlds') $world) $fileName
    $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    if (Test-Path -LiteralPath $file) { try { @([IO.File]::ReadAllText($file) | ConvertFrom-Json) | ForEach-Object { [void]$set.Add([string]$_.pack_id) } } catch {} }
    return ,$set
}

function Get-Packs {
    $result = @()
    foreach ($kind in @('resource', 'behavior')) {
        $folderName = if ($kind -eq 'resource') { 'resource_packs' } else { 'behavior_packs' }
        $root = Join-Path $script:Root $folderName
        $enabled = Get-EnabledPackIds $kind
        foreach ($folder in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
            if ($folder.Name -match '^(vanilla|chemistry|editor|experimental_|server_)') { continue }
            $manifestPath = Join-Path $folder.FullName 'manifest.json'
            if (-not (Test-Path -LiteralPath $manifestPath)) { continue }
            try {
                $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
                $version = @($manifest.header.version) -join '.'
                $result += [ordered]@{ folder = $folder.Name; kind = $kind; name = [string]$manifest.header.name; description = [string]$manifest.header.description; id = [string]$manifest.header.uuid; version = $version; enabled = $enabled.Contains([string]$manifest.header.uuid) }
            } catch {}
        }
    }
    $result
}

function Import-Pack([byte[]]$Bytes, [string]$FileName) {
    $FileName = [Uri]::UnescapeDataString($FileName)
    Assert-SimpleName $FileName 'Имя файла'
    if ([IO.Path]::GetExtension($FileName) -notin @('.zip', '.mcpack')) { throw 'Поддерживаются файлы .mcpack и .zip.' }
    $temp = Expand-ZipBytes $Bytes 'pack-import'
    try {
        $manifestFile = Get-ChildItem -LiteralPath $temp -Filter 'manifest.json' -File -Recurse | Select-Object -First 1
        if (-not $manifestFile) { throw 'В архиве не найден manifest.json.' }
        $manifest = [IO.File]::ReadAllText($manifestFile.FullName) | ConvertFrom-Json
        $types = @($manifest.modules | ForEach-Object type)
        $kind = if ('resources' -in $types) { 'resource' } else { 'behavior' }
        $folderName = if ($kind -eq 'resource') { 'resource_packs' } else { 'behavior_packs' }
        $packRoot = Join-Path $script:Root $folderName
        $baseName = [IO.Path]::GetFileNameWithoutExtension($FileName) -replace '[^\p{L}\p{N}._-]', '_'
        $destination = Join-Path $packRoot $baseName
        $suffix = 2
        while (Test-Path -LiteralPath $destination) { $destination = Join-Path $packRoot "$baseName-$suffix"; $suffix++ }
        [IO.Directory]::Move($manifestFile.Directory.FullName, $destination)
        [ordered]@{ name = [string]$manifest.header.name; kind = $kind }
    } finally {
        if (Test-Path -LiteralPath $temp) { [IO.Directory]::Delete($temp, $true) }
    }
}

function Set-PackEnabled([string]$Kind, [string]$Id, [bool]$Enabled) {
    if ((Get-BedrockProcesses).Count -gt 0) { throw 'Для изменения наборов сначала остановите сервер.' }
    if ($Kind -notin @('resource', 'behavior') -or $Id -notmatch '^[0-9a-fA-F-]{36}$') { throw 'Некорректный набор.' }
    $pack = @(Get-Packs | Where-Object { $_.kind -eq $Kind -and $_.id -eq $Id })[0]
    if (-not $pack) { throw 'Набор не найден.' }
    $world = (Get-PropertiesData).values['level-name']
    $fileName = if ($Kind -eq 'resource') { 'world_resource_packs.json' } else { 'world_behavior_packs.json' }
    $file = Join-Path (Join-Path (Join-Path $script:Root 'worlds') $world) $fileName
    $entries = if (Test-Path -LiteralPath $file) { @([IO.File]::ReadAllText($file) | ConvertFrom-Json) } else { @() }
    $entries = @($entries | Where-Object pack_id -ne $Id)
    if ($Enabled) { $entries += [ordered]@{ pack_id = $Id; version = @($pack.version -split '\.' | ForEach-Object { [int]$_ }) } }
    Write-TextSafely $file (ConvertTo-Json -InputObject @($entries) -Depth 5)
}

function Remove-Pack([string]$Kind, [string]$Folder) {
    if ((Get-BedrockProcesses).Count -gt 0) { throw 'Для удаления набора сначала остановите сервер.' }
    Assert-SimpleName $Folder 'Имя папки'
    if ($Kind -notin @('resource', 'behavior')) { throw 'Некорректный тип набора.' }
    $folderName = if ($Kind -eq 'resource') { 'resource_packs' } else { 'behavior_packs' }
    $root = Join-Path $script:Root $folderName
    $source = Join-Path $root $Folder
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'Набор не найден.' }
    $trash = Join-Path $script:DataRoot 'removed-packs'
    [IO.Directory]::CreateDirectory($trash) | Out-Null
    [IO.Directory]::Move($source, (Join-Path $trash ("$Folder-" + (Get-Date -Format 'yyyyMMddHHmmss'))))
}

function Get-OnlinePlayers {
    if (-not (Get-ManagedRunning)) { return @() }
    Send-Command 'list'
    Start-Sleep -Milliseconds 350
    $log = Get-LogTail
    $matches = [regex]::Matches($log, '(?m)There are \d+/\d+ players online:\s*(?:\r?\n)?([^\r\n]*)')
    if (-not $matches.Count) { return @() }
    $names = $matches[$matches.Count - 1].Groups[1].Value.Trim()
    if (-not $names) { return @() }
    @($names -split ',\s*' | Where-Object { $_ })
}

function Invoke-PlayerAction([string]$Action, [string]$Player) {
    if ($Action -notin @('kick', 'op', 'deop')) { throw 'Недопустимое действие.' }
    if ($Player -notmatch '^[\p{L}\p{N}_ ]{1,32}$') { throw 'Некорректное имя игрока.' }
    Send-Command "$Action `"$Player`""
}

function Get-LogTail {
    foreach ($file in @($script:OutputFile, $script:ErrorFile)) {
        if ($file) { try { $file.Flush() } catch {} }
    }
    $lines = @()
    foreach ($name in @('console.log', 'error.log')) {
        $path = Join-Path $script:DataRoot $name
        if (Test-Path -LiteralPath $path) { $lines += Get-Content -LiteralPath $path -Tail 300 -ErrorAction SilentlyContinue }
    }
    ($lines -join "`n")
}

function Read-Request($Stream) {
    $bytes = [Collections.Generic.List[byte]]::new()
    $state = 0
    while ($bytes.Count -lt 65536) {
        $value = $Stream.ReadByte()
        if ($value -lt 0) { break }
        $bytes.Add([byte]$value)
        if (($state -eq 0 -or $state -eq 2) -and $value -eq 13) { $state++ }
        elseif (($state -eq 1 -or $state -eq 3) -and $value -eq 10) { $state++ }
        else { $state = 0 }
        if ($state -eq 4) { break }
    }
    $headerText = [Text.Encoding]::ASCII.GetString($bytes.ToArray())
    $lines = $headerText -split "`r`n"
    $requestLine = $lines[0] -split ' '
    $headers = @{}
    foreach ($line in $lines[1..($lines.Count - 1)]) {
        $colon = $line.IndexOf(':')
        if ($colon -gt 0) { $headers[$line.Substring(0, $colon).Trim().ToLowerInvariant()] = $line.Substring($colon + 1).Trim() }
    }
    $length = if ($headers.ContainsKey('content-length')) { [int]$headers['content-length'] } else { 0 }
    if ($length -gt 512MB) { throw 'Файл больше 512 МБ.' }
    $bodyBytes = [byte[]]::new($length)
    $offset = 0
    while ($offset -lt $length) {
        $read = $Stream.Read($bodyBytes, $offset, $length - $offset)
        if ($read -le 0) { break }
        $offset += $read
    }
    [ordered]@{ method = $requestLine[0]; path = ($requestLine[1] -split '\?')[0]; headers = $headers; body = [Text.Encoding]::UTF8.GetString($bodyBytes, 0, $offset); bodyBytes = $bodyBytes }
}

function Send-Response($Stream, [int]$Status, [string]$ContentType, [byte[]]$Body, [hashtable]$ExtraHeaders = @{}) {
    $reason = if ($Status -eq 200) { 'OK' } elseif ($Status -eq 204) { 'No Content' } elseif ($Status -eq 400) { 'Bad Request' } elseif ($Status -eq 403) { 'Forbidden' } elseif ($Status -eq 404) { 'Not Found' } else { 'Internal Server Error' }
    $extra = ($ExtraHeaders.GetEnumerator() | ForEach-Object { "$($_.Key): $($_.Value)`r`n" }) -join ''
    $header = "HTTP/1.1 $Status $reason`r`nContent-Type: $ContentType`r`nContent-Length: $($Body.Length)`r`nCache-Control: no-store`r`nX-Content-Type-Options: nosniff`r`nContent-Security-Policy: default-src 'self'; style-src 'self'; script-src 'self'`r`n${extra}Connection: close`r`n`r`n"
    $headerBytes = [Text.Encoding]::ASCII.GetBytes($header)
    $Stream.Write($headerBytes, 0, $headerBytes.Length)
    if ($Body.Length) { $Stream.Write($Body, 0, $Body.Length) }
}

function Send-Json($Stream, $Value, [int]$Status = 200) {
    $json = if ($null -eq $Value) { 'null' } else { ConvertTo-Json -InputObject $Value -Depth 12 -Compress }
    Send-Response $Stream $Status 'application/json; charset=utf-8' ([Text.Encoding]::UTF8.GetBytes($json))
}

function Handle-Api($Request, $Stream) {
    if ($Request.method -ne 'GET' -and $Request.headers['x-panel-token'] -ne $script:Token) { Send-Json $Stream @{ error = 'Доступ запрещён.' } 403; return }
    $body = if ($Request.body -and $Request.headers['content-type'] -like 'application/json*') { $Request.body | ConvertFrom-Json } else { $null }
    if ($Request.method -eq 'GET' -and $Request.path.StartsWith('/api/backups/download/')) {
        $name = [Uri]::UnescapeDataString($Request.path.Substring('/api/backups/download/'.Length))
        Assert-SimpleName $name 'Имя файла'
        $file = Join-Path $script:BackupRoot $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Файл не найден.' }
        Send-Response $Stream 200 'application/zip' ([IO.File]::ReadAllBytes($file)) @{ 'Content-Disposition' = "attachment; filename*=UTF-8''$([Uri]::EscapeDataString($name))" }
        return
    }
    switch ("$($Request.method) $($Request.path)") {
        'GET /api/status' { Send-Json $Stream (Get-Status) }
        'GET /api/update' { Send-Json $Stream (Get-UpdateInfo) }
        'GET /api/metrics' { Send-Json $Stream @{ metrics = @($script:Metrics) } }
        'GET /api/panel/config' { Send-Json $Stream $script:Config }
        'POST /api/panel/config' {
            $script:Config.autoRestart = [bool]$body.autoRestart
            if ([int]$body.maxRestartAttempts -notin 1..10) { throw 'Число попыток должно быть от 1 до 10.' }
            $script:Config.maxRestartAttempts = [int]$body.maxRestartAttempts
            Save-PanelConfig
            Send-Json $Stream @{ ok = $true }
        }
        'GET /api/properties' { Send-Json $Stream (Get-PropertiesData) }
        'POST /api/properties' { Test-Properties $body.raw; Write-TextSafely $script:PropertiesPath $body.raw; Send-Json $Stream @{ ok = $true } }
        'GET /api/allowlist' { Send-Json $Stream @{ players = @(Read-JsonArray $script:AllowlistPath) } }
        'POST /api/allowlist' {
            $result = @($body.players | ForEach-Object {
                if ([string]::IsNullOrWhiteSpace($_.name) -or $_.name.Length -gt 64) { throw 'Некорректное имя игрока.' }
                $entry = [ordered]@{ ignoresPlayerLimit = [bool]$_.ignoresPlayerLimit; name = [string]$_.name }
                if ($_.xuid) { $entry.xuid = [string]$_.xuid }
                $entry
            })
            Write-TextSafely $script:AllowlistPath (ConvertTo-Json -InputObject @($result) -Depth 5)
            if (Get-ManagedRunning) { Send-Command 'allowlist reload' }
            Send-Json $Stream @{ ok = $true }
        }
        'GET /api/permissions' { Send-Json $Stream @{ players = @(Read-JsonArray $script:PermissionsPath) } }
        'POST /api/permissions' {
            $result = @($body.players | ForEach-Object {
                if ($_.xuid -notmatch '^\d+$' -or $_.permission -notin @('visitor', 'member', 'operator')) { throw 'Проверьте XUID и уровень доступа.' }
                [ordered]@{ permission = [string]$_.permission; xuid = [string]$_.xuid }
            })
            Write-TextSafely $script:PermissionsPath (ConvertTo-Json -InputObject @($result) -Depth 5)
            Send-Json $Stream @{ ok = $true }
        }
        'GET /api/worlds' { Send-Json $Stream @{ worlds = @(Get-Worlds) } }
        'POST /api/world/select' { Set-ActiveWorld $body.name; Send-Json $Stream @{ ok = $true } }
        'POST /api/worlds/import' { Send-Json $Stream (Import-World $Request.bodyBytes $Request.headers['x-filename']) }
        'POST /api/worlds/export' { Send-Json $Stream (Export-World $body.name) }
        'GET /api/backups' { Send-Json $Stream @{ backups = @(Get-Backups) } }
        'POST /api/backups' { Send-Json $Stream (New-WorldBackup) }
        'POST /api/backups/restore' { Restore-WorldBackup $body.name; Send-Json $Stream @{ ok = $true } }
        'GET /api/players/online' { Send-Json $Stream @{ players = @(Get-OnlinePlayers) } }
        'POST /api/players/action' { Invoke-PlayerAction $body.action $body.player; Send-Json $Stream @{ ok = $true } }
        'GET /api/packs' { Send-Json $Stream @{ packs = @(Get-Packs) } }
        'POST /api/packs/import' { Send-Json $Stream (Import-Pack $Request.bodyBytes $Request.headers['x-filename']) }
        'POST /api/packs/toggle' { Set-PackEnabled $body.kind $body.id ([bool]$body.enabled); Send-Json $Stream @{ ok = $true } }
        'POST /api/packs/remove' { Remove-Pack $body.kind $body.folder; Send-Json $Stream @{ ok = $true } }
        'GET /api/logs' { Send-Json $Stream @{ text = Get-LogTail } }
        'POST /api/server/start' { $script:RestartAttempts.Clear(); Start-BedrockServer; Send-Json $Stream @{ ok = $true } }
        'POST /api/server/stop' { Stop-BedrockServer; Send-Json $Stream @{ ok = $true } }
        'POST /api/server/force-stop' { Stop-BedrockServer $true; Send-Json $Stream @{ ok = $true } }
        'POST /api/server/restart' { Stop-BedrockServer; Start-BedrockServer; Send-Json $Stream @{ ok = $true } }
        'POST /api/command' { Send-Command ([string]$body.command); Send-Json $Stream @{ ok = $true } }
        default { Send-Json $Stream @{ error = 'Маршрут не найден.' } 404 }
    }
}

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
$listener.Start()
$url = "http://127.0.0.1:$Port/"
Write-Host "Панель Bedrock запущена: $url" -ForegroundColor Green
Write-Host 'Для завершения панели нажмите Ctrl+C. Управляемый сервер будет безопасно остановлен.' -ForegroundColor DarkGray
if ($OpenBrowser) { Start-Process $url }

try {
    while ($true) {
        Watch-BedrockServer
        Add-Metric
        if (-not $listener.Pending()) { Start-Sleep -Milliseconds 100; continue }
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $request = Read-Request $stream
            try {
                if ($request.path.StartsWith('/api/')) {
                    Handle-Api $request $stream
                } else {
                    $relative = if ($request.path -eq '/') { 'index.html' } else { $request.path.TrimStart('/') }
                    $file = [IO.Path]::GetFullPath((Join-Path $script:WebRoot $relative))
                    if (-not $file.StartsWith([IO.Path]::GetFullPath($script:WebRoot), [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $file -PathType Leaf)) {
                        Send-Response $stream 404 'text/plain; charset=utf-8' ([Text.Encoding]::UTF8.GetBytes('Не найдено'))
                    } else {
                        $content = [IO.File]::ReadAllBytes($file)
                        if ($relative -eq 'index.html') {
                            $html = [Text.Encoding]::UTF8.GetString($content).Replace('__PANEL_TOKEN__', $script:Token)
                            $content = [Text.Encoding]::UTF8.GetBytes($html)
                        }
                        $type = switch ([IO.Path]::GetExtension($file)) { '.html' { 'text/html; charset=utf-8' } '.css' { 'text/css; charset=utf-8' } '.js' { 'application/javascript; charset=utf-8' } '.svg' { 'image/svg+xml' } default { 'application/octet-stream' } }
                        Send-Response $stream 200 $type $content
                    }
                }
            } catch {
                Send-Json $stream @{ error = $_.Exception.Message } 400
            }
        } catch { Write-Warning $_.Exception.Message }
        finally { $client.Dispose() }
    }
} finally {
    if (Get-ManagedRunning) {
        Write-Host 'Останавливаю управляемый сервер…' -ForegroundColor Yellow
        try { Stop-BedrockServer } catch { Write-Warning $_.Exception.Message }
    }
    $listener.Stop()
}
