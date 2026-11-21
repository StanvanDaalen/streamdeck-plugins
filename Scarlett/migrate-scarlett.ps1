# Moves Scarlett keys and dials that were part of Volume & Launcher (com.stan.gamevolume.output/speakers/headphones)
# to the separate Scarlett Switch plugin, with their settings. Run only while Stream Deck is closed: install.ps1 does that.
# Safe to run again: without old Scarlett actions it does nothing.
$ErrorActionPreference = 'Stop'
$deck = "$env:APPDATA\Elgato\StreamDeck"
$pattern = '"UUID"\s*:\s*"com\.stan\.gamevolume\.(output|speakers|headphones)"'

$files = @()
foreach ($dir in 'ProfilesV3', 'ProfilesV2') {
    if (Test-Path "$deck\$dir") {
        $files += Get-ChildItem "$deck\$dir" -Recurse -Filter manifest.json | Where-Object { (Get-Content $_.FullName -Raw) -match $pattern }
    }
}
if ($files.Count -eq 0) { return }

# A copy of every profile file that is changed, in case anything looks wrong afterwards.
$backup = "$env:APPDATA\ScarlettSwitch\profile-backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
New-Item -ItemType Directory -Force $backup | Out-Null
$moved = 0
$plugin = '{"Name":"Scarlett Switch","UUID":"com.stan.scarlett","Version":"1.0.0.0"}'
$names = @{ output = 'switch'; speakers = 'speakers'; headphones = 'headphones' }
foreach ($f in $files) {
    $relative = $f.FullName.Substring($deck.Length + 1)
    $copy = Join-Path $backup $relative
    New-Item -ItemType Directory -Force (Split-Path $copy) | Out-Null
    Copy-Item $f.FullName $copy

    $text = [IO.File]::ReadAllText($f.FullName)
    # Per action: its "Plugin" object comes before its "UUID", with no other action ("ActionID") in between.
    $action = '("Plugin"\s*:\s*)\{[^{}]*"com\.stan\.gamevolume"[^{}]*\}((?:(?!"ActionID").)*?)"UUID"(\s*):(\s*)"com\.stan\.gamevolume\.(output|speakers|headphones)"'
    $text = [regex]::Replace($text, $action, {
        param($m)
        $script:moved++
        $m.Groups[1].Value + $plugin + $m.Groups[2].Value + '"UUID"' + $m.Groups[3].Value + ':' + $m.Groups[4].Value + '"com.stan.scarlett.' + $names[$m.Groups[5].Value] + '"'
    }, 'Singleline')
    [IO.File]::WriteAllText($f.FullName, $text, (New-Object Text.UTF8Encoding $false))
}
Write-Output "Scarlett actions moved to Scarlett Switch: $moved (backup in $backup)"
