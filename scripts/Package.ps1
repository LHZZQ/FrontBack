$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content -LiteralPath (Join-Path $root 'FrontBack.json') -Raw | ConvertFrom-Json).version
$package = Join-Path $root "dist\FrontBack-v$version"
$content = Join-Path $package 'FrontBack'
New-Item -ItemType Directory -Force $content | Out-Null
$files = @{
    'FrontBack.dll' = 'src\FrontBack\bin\Release\net9.0\FrontBack.dll'
    'FrontBack.json' = 'FrontBack.json'
    'FrontBack.config.json' = 'FrontBack.config.json'
}
foreach ($name in $files.Keys) {
    Copy-Item -LiteralPath (Join-Path $root $files[$name]) -Destination (Join-Path $content $name) -Force
}
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $package 'README.zh-CN.md') -Force
$zip = Join-Path $root "dist\FrontBack-v$version.zip"
# 禁止把游戏 DLL、检查工具或旧文件带入发布包。
$unexpected = Get-ChildItem -LiteralPath $content -Force | Where-Object { $_.PSIsContainer -or !$files.ContainsKey($_.Name) }
if ($unexpected) { throw '打包目录包含预期之外的文件，请检查。' }
# 对外安装包保留 FrontBack 目录层级。
Compress-Archive -LiteralPath $content,(Join-Path $package 'README.zh-CN.md') -DestinationPath $zip -Force
Write-Output "已打包：$zip"
