param([int]$Port = 8080)
# 在管理员 PowerShell 中执行；仅开放指定程序、端口和本地子网。
$ErrorActionPreference = 'Stop'
if ($Port -lt 1 -or $Port -gt 65535) { throw '端口必须在 1–65535 之间。' }
$exe = Join-Path $PSScriptRoot 'LanShare.exe'
if (!(Test-Path -LiteralPath $exe)) { throw "未找到程序：$exe" }
$ruleName = "LanShare-$Port"
if (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue) { Remove-NetFirewallRule -Name $ruleName }
New-NetFirewallRule -Name $ruleName -DisplayName "LanShare 局域网文件站 ($Port)" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -RemoteAddress LocalSubnet -Program $exe -Profile Private
Write-Host '已允许专用网络中的局域网连接。请确认当前网络配置为专用网络。'
