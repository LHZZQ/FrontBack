param([string]$GameDir = 'D:\steam\steamapps\common\Slay the Spire 2')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $env:DOTNET_CLI_HOME = Join-Path $root '.tools\dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    dotnet restore src/FrontBack/FrontBack.csproj --packages .tools/packages "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw '依赖准备失败。' }
    dotnet build src/FrontBack/FrontBack.csproj -c Release --no-restore -p:UseSharedCompilation=false -nodeReuse:false "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
} finally { Pop-Location }
