# Builds the Monitor plugin, installs it into Stream Deck and publishes the update to GitHub.
param([string]$Message = 'Monitor bijgewerkt')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$target = "$env:APPDATA\Elgato\StreamDeck\Plugins\com.stan.monitor.sdPlugin"

dotnet build "$root\Monitor.csproj" -c Release -o "$root\out" -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Start-Process "$root\out\Monitor.exe" -ArgumentList '--assets', "`"$root\sdPlugin\images`"" -Wait

$running = Get-Process StreamDeck -ErrorAction SilentlyContinue
$exe = if ($running) { $running[0].Path } else { 'C:\Program Files\Elgato\StreamDeck\StreamDeck.exe' }
if ($running) { $running | Stop-Process -Force; Start-Sleep -Seconds 3 }

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force "$target\bin" | Out-Null
Copy-Item "$root\sdPlugin\*" $target -Recurse -Force
Copy-Item "$root\out\*" "$target\bin" -Recurse -Force

# Started through Explorer so Stream Deck never inherits the environment of whatever ran this script:
# a Store app (such as Claude) redirects new %APPDATA% folders into its own private copy, and Stream
# Deck would then read settings that the normal Stream Deck (started at sign-in) never sees.
Start-Process explorer.exe -ArgumentList "`"$exe`""
Write-Output 'Stream Deck restarted.'
& "$root\..\publish.ps1" -Message $Message
