param([string]$Dotnet = 'dotnet', [string]$Python = 'python', [string[]]$Runtimes = @('win-x64', 'linux-x64', 'linux-arm64', 'linux-arm'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
foreach ($rid in $Runtimes) {
    if ($rid -notin @('win-x64','linux-x64','linux-arm64','linux-arm')) { throw "不支持的运行时：$rid" }
    $destination = Join-Path $artifacts $rid
    & $Dotnet publish (Join-Path $projectRoot 'LanShare.csproj') -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $destination
    if ($LASTEXITCODE -ne 0) { throw "发布失败：$rid" }
    # Only ship program files, never saved passwords or uploaded data.
    & $Python (Join-Path $PSScriptRoot 'package.py') --rid $rid
    if ($LASTEXITCODE -ne 0) { throw "打包失败：$rid" }
}
Write-Host "发布完成：$artifacts"
