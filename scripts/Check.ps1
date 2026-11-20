param([string]$GameDir = 'D:\steam\steamapps\common\Slay the Spire 2')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $env:DOTNET_CLI_HOME = Join-Path $root '.tools\dotnet-home'
    dotnet restore tests/FrontBack.Checks/FrontBack.Checks.csproj --packages .tools/packages "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw '测试依赖准备失败。' }
    dotnet build tests/FrontBack.Checks/FrontBack.Checks.csproj -c Release --no-restore -p:UseSharedCompilation=false -nodeReuse:false "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
    $runner = Join-Path $root '.tools\runtime9\dotnet.exe'
    if (!(Test-Path -LiteralPath $runner)) { $runner = 'dotnet' }
    & $runner tests/FrontBack.Checks/bin/Release/net9.0/FrontBack.Checks.dll (Join-Path $GameDir 'data_sts2_windows_x86_64')
    if ($LASTEXITCODE -ne 0) { throw '自动检查失败。请确认运行环境为 .NET 9。' }
} finally { Pop-Location }
