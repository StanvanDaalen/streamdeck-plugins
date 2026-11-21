# Builds Volume & Launcher, installs it into Stream Deck and publishes the update to GitHub.
param([string]$Message = 'Volume & Launcher updated', [switch]$NoPublish)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$target = "$env:APPDATA\Elgato\StreamDeck\Plugins\com.stan.gamevolume.sdPlugin"

dotnet build "$root\VolumeLauncher.csproj" -c Release -o "$root\out" -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Start-Process "$root\out\VolumeLauncher.exe" -ArgumentList '--assets', "`"$root\sdPlugin\images`"" -Wait

$running = Get-Process StreamDeck -ErrorAction SilentlyContinue
$exe = if ($running) { $running[0].Path } else { 'C:\Program Files\Elgato\StreamDeck\StreamDeck.exe' }
if ($running) { $running | Stop-Process -Force; Start-Sleep -Seconds 3 }

# Keys of the old, separate Launcher plugin move over to this plugin, and the old plugin goes.
& "$root\migrate-launcher.ps1"

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force "$target\bin" | Out-Null
Copy-Item "$root\sdPlugin\*" $target -Recurse -Force
Copy-Item "$root\out\*" "$target\bin" -Recurse -Force

# Started through Explorer so Stream Deck never inherits the environment of whatever ran this script:
# a Store app (such as Claude) redirects new %APPDATA% folders into its own private copy, and Stream
# Deck would then read settings that the normal Stream Deck (started at sign-in) never sees.
Start-Process explorer.exe -ArgumentList "`"$exe`""
Write-Output 'Stream Deck restarted.'
if (-not $NoPublish) { & "$root\..\publish.ps1" -Message $Message }
