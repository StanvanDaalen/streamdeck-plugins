# Builds the F1 plugin and installs it into Stream Deck. Add -Publish to also push it to GitHub as a release.
param([string]$Message = 'F1 bijgewerkt', [switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$target = "$env:APPDATA\Elgato\StreamDeck\Plugins\com.stan.f1.sdPlugin"

dotnet build "$root\F1.csproj" -c Release -o "$root\out" -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Start-Process "$root\out\F1.exe" -ArgumentList '--assets', "`"$root\sdPlugin\images`"" -Wait

$running = Get-Process StreamDeck -ErrorAction SilentlyContinue
$exe = if ($running) { $running[0].Path } else { 'C:\Program Files\Elgato\StreamDeck\StreamDeck.exe' }
# Stream Deck restarts itself quickly after a kill, so keep stopping it until it stays down.
for ($i = 0; $i -lt 10 -and (Get-Process StreamDeck -ErrorAction SilentlyContinue); $i++) {
    Get-Process StreamDeck -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}
Get-Process F1 -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target*" } | Stop-Process -Force

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force "$target\bin" | Out-Null
Copy-Item "$root\sdPlugin\*" $target -Recurse -Force
Copy-Item "$root\out\*" "$target\bin" -Recurse -Force

# Started through Explorer so Stream Deck never inherits the environment of whatever ran this script:
# a Store app (such as Claude) redirects new %APPDATA% folders into its own private copy, and Stream
# Deck would then read settings that the normal Stream Deck (started at sign-in) never sees.
Start-Process explorer.exe -ArgumentList "`"$exe`""
Write-Output 'Stream Deck restarted.'
if ($Publish) { & "$root\..\publish.ps1" -Message $Message }
