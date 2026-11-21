# Builds the F1 plugin and installs it into Stream Deck. Add -OpenDeck to install it into OpenDeck on
# Windows instead (OpenDeck replaces the Elgato app; only one of them can drive the device).
# Add -Publish to also push it to GitHub as a release.
param([string]$Message = 'F1 updated', [switch]$Publish, [switch]$OpenDeck)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if ($OpenDeck) {
    $target = "$env:APPDATA\opendeck\plugins\com.stan.f1.sdPlugin"
    $app = 'opendeck'
    $defaultExe = @("$env:ProgramFiles\OpenDeck\opendeck.exe", "$env:LOCALAPPDATA\OpenDeck\opendeck.exe", "$env:LOCALAPPDATA\Programs\OpenDeck\opendeck.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
} else {
    $target = "$env:APPDATA\Elgato\StreamDeck\Plugins\com.stan.f1.sdPlugin"
    $app = 'StreamDeck'
    $defaultExe = 'C:\Program Files\Elgato\StreamDeck\StreamDeck.exe'
}

dotnet build "$root\F1.csproj" -c Release -o "$root\out" -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Start-Process "$root\out\F1.exe" -ArgumentList '--assets', "`"$root\sdPlugin\images`"" -Wait

$running = Get-Process $app -ErrorAction SilentlyContinue
$exe = if ($running) { $running[0].Path } else { $defaultExe }
# Stream Deck restarts itself quickly after a kill, so keep stopping it until it stays down.
for ($i = 0; $i -lt 10 -and (Get-Process $app -ErrorAction SilentlyContinue); $i++) {
    Get-Process $app -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}
Get-Process F1 -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target*" } | Stop-Process -Force

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force "$target\bin" | Out-Null
Copy-Item "$root\sdPlugin\*" $target -Recurse -Force
Copy-Item "$root\out\*" "$target\bin" -Recurse -Force
# Own logo (optional, this PC only): %APPDATA%\StanF1\f1-logo.svg or .png replaces the plugin, category
# and F1 Mode icons in the installed copy. The repo and the release packages keep the standard icons.
$brand = Start-Process "$root\out\F1.exe" -ArgumentList '--brand', "`"$target\images`"" -Wait -PassThru
if ($brand.ExitCode -ne 0) { Write-Warning 'Own logo could not be applied; standard icons kept.' }

# Started through Explorer so the app never inherits the environment of whatever ran this script:
# a Store app (such as Claude) redirects new %APPDATA% folders into its own private copy, and the app
# would then read settings that the normal one (started at sign-in) never sees.
if ($exe) {
    Start-Process explorer.exe -ArgumentList "`"$exe`""
    Write-Output "$app restarted."
} else {
    Write-Output "Installed in $target; start $app yourself."
}
if ($Publish) { & "$root\..\publish.ps1" -Message $Message }
