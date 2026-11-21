# Builds the PlayStation 5 for OBS plugin and installs it into Stream Deck on this PC.
# Use -Publish to also put the update on GitHub through publish.ps1.
param([string]$Message = 'PlayStation 5 for OBS bijgewerkt', [switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$target = "$env:APPDATA\Elgato\StreamDeck\Plugins\com.stan.ps5.sdPlugin"

dotnet build "$root\PS5.csproj" -c Release -o "$root\out" -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Start-Process "$root\out\PS5.exe" -ArgumentList '--assets', "`"$root\sdPlugin\images`"" -Wait

$running = Get-Process StreamDeck -ErrorAction SilentlyContinue
$exe = if ($running) { $running[0].Path } else { 'C:\Program Files\Elgato\StreamDeck\StreamDeck.exe' }
if ($running) { $running | Stop-Process -Force; Start-Sleep -Seconds 3 }
Get-Process PS5 -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target\*" } | Stop-Process -Force

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force "$target\bin" | Out-Null
Copy-Item "$root\sdPlugin\*" $target -Recurse -Force
Copy-Item "$root\out\*" "$target\bin" -Recurse -Force

# Started through Explorer so Stream Deck never inherits the environment of whatever ran this script
# (a Store app such as Claude would otherwise redirect %APPDATA% for it).
Start-Process explorer.exe -ArgumentList "`"$exe`""
Write-Output 'Stream Deck restarted.'
if ($Publish) { & "$root\..\publish.ps1" -Message $Message }
