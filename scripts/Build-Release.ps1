param([string]$Version)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile = Join-Path $projectRoot 'src/MinecraftServerManager/MinecraftServerManager.csproj'
if (-not $Version) {
    [xml]$projectXml = Get-Content -LiteralPath $projectFile
    $Version = [string]$projectXml.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use major.minor.patch.' }

$stage = Join-Path $projectRoot "dist/release-$Version"
$archive = Join-Path $projectRoot "dist/MinecraftServerManager-v$Version-win-x64.zip"
if (Test-Path -LiteralPath $stage) { throw "Release folder already exists: $stage" }
if (Test-Path -LiteralPath $archive) { throw "Release archive already exists: $archive" }

New-Item -ItemType Directory -Path $stage -Force | Out-Null
dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $stage
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$items = Get-ChildItem -LiteralPath $stage -Force | ForEach-Object FullName
Compress-Archive -LiteralPath $items -DestinationPath $archive -CompressionLevel Optimal
Write-Host "Release ready: $archive"
