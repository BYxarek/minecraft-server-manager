param([string]$Version)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile = Join-Path $projectRoot 'src/MinecraftServerManager/MinecraftServerManager.csproj'
if (-not $Version) {
    [xml]$projectXml = Get-Content -LiteralPath $projectFile
    $Version = [string]$projectXml.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use major.minor.patch.' }

$templateRoot = Join-Path $projectRoot 'deffolt-minecraft-server'
$bedrock = Get-ChildItem -LiteralPath $templateRoot -Filter 'bedrock-server-*.zip' -File | Select-Object -First 1
$java = Join-Path $templateRoot 'server.jar'
if (-not $bedrock -or -not (Test-Path -LiteralPath $java -PathType Leaf)) { throw 'Bedrock ZIP and Java server.jar are required for this release.' }

$stage = Join-Path $projectRoot "dist/release-$Version"
$archive = Join-Path $projectRoot "dist/MinecraftServerManager-v$Version-win-x64.zip"
if (Test-Path -LiteralPath $stage) { throw "Release folder already exists: $stage" }
if (Test-Path -LiteralPath $archive) { throw "Release archive already exists: $archive" }

New-Item -ItemType Directory -Path $stage -Force | Out-Null
dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $stage
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$defaults = Join-Path $stage 'defaults'
New-Item -ItemType Directory -Path $defaults | Out-Null
Copy-Item -LiteralPath $bedrock.FullName -Destination $defaults
Copy-Item -LiteralPath $java -Destination $defaults

$items = Get-ChildItem -LiteralPath $stage -Force | ForEach-Object FullName
Compress-Archive -LiteralPath $items -DestinationPath $archive -CompressionLevel Optimal
Write-Host "Release ready: $archive"
