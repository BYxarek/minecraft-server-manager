param([string]$IsccPath, [switch]$ReplaceExisting)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile = Join-Path $projectRoot 'src/MinecraftServerManager/MinecraftServerManager.csproj'
$installerScript = Join-Path $projectRoot 'installer/MinecraftServerManager.iss'
$javaReleaseFile = Join-Path $projectRoot 'installer/java-release.json'
$dist = Join-Path $projectRoot 'dist'

[xml]$projectXml = Get-Content -LiteralPath $projectFile
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Project Version must use major.minor.patch.' }
$javaRelease = Get-Content -LiteralPath $javaReleaseFile -Raw | ConvertFrom-Json
if ($javaRelease.url -notmatch '^https://github\.com/adoptium/temurin25-binaries/releases/download/' -or
    $javaRelease.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
    throw 'The pinned Temurin 25 URL or SHA-256 is invalid.'
}

if (-not $IsccPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $IsccPath = $command.Source }
}
if (-not $IsccPath) {
    foreach ($candidate in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $IsccPath = $candidate; break }
    }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Inno Setup 6 ISCC.exe was not found. Install Inno Setup 6 or pass -IsccPath.'
}

$stage = Join-Path $dist "installer-stage-$version"
$installer = Join-Path $dist "MinecraftServerManager-v$version-win-x64-setup.exe"
$compiledName = if ($ReplaceExisting) { "MinecraftServerManager-v$version-win-x64-setup-rebuild" } else { "MinecraftServerManager-v$version-win-x64-setup" }
$compiled = Join-Path $dist "$compiledName.exe"
if (Test-Path -LiteralPath $stage) { throw "Installer stage already exists: $stage" }
if (-not $ReplaceExisting -and (Test-Path -LiteralPath $installer)) { throw "Installer already exists: $installer" }
if (Test-Path -LiteralPath $compiled) { throw "Temporary installer output already exists: $compiled" }

New-Item -ItemType Directory -Path $stage -Force | Out-Null
dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $stage
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed; staged files were kept for inspection.' }
if (-not (Test-Path -LiteralPath (Join-Path $stage 'MinecraftServerManager.exe') -PathType Leaf)) {
    throw 'Published executable was not found; staged files were kept for inspection.'
}

& $IsccPath "/DAppVersion=$version" "/DPublishDir=$stage" "/DJavaDownloadUrl=$($javaRelease.url)" "/DJavaSha256=$($javaRelease.sha256)" "/O$dist" "/F$compiledName" $installerScript
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed; staged files were kept for inspection.' }
if (-not (Test-Path -LiteralPath $compiled -PathType Leaf)) { throw "Installer output was not found: $compiled" }

if ($ReplaceExisting) {
    Copy-Item -LiteralPath $compiled -Destination $installer -Force
    Remove-Item -LiteralPath $compiled -Force
}

Remove-Item -LiteralPath $stage -Recurse -Force
Write-Host "Installer ready: $installer"
