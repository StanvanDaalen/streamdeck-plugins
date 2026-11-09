# Builds the Launcher plugin, installs it into Stream Deck and publishes the update to GitHub.
param([string]$Message = 'Launcher bijgewerkt')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$target = "$env:APPDATA\Elgato\StreamDeck\Plugins\com.stan.launcher.sdPlugin"

dotnet build "$root\Launcher.csproj" -c Release -o "$root\out" -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Start-Process "$root\out\Launcher.exe" -ArgumentList '--assets', "`"$root\sdPlugin\images`"" -Wait

$running = Get-Process StreamDeck -ErrorAction SilentlyContinue
$exe = if ($running) { $running[0].Path } else { 'C:\Program Files\Elgato\StreamDeck\StreamDeck.exe' }
if ($running) { $running | Stop-Process -Force; Start-Sleep -Seconds 3 }

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force "$target\bin" | Out-Null
Copy-Item "$root\sdPlugin\*" $target -Recurse -Force
Copy-Item "$root\out\*" "$target\bin" -Recurse -Force

Start-Process $exe
Write-Output 'Stream Deck restarted.'
& "$root\..\publish.ps1" -Message $Message
