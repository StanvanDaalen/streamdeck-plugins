# Moves keys of the old, separate Launcher plugin (com.stan.launcher) to the launcher keys of Volume & Launcher,
# with their settings, and removes the old plugin. Run only while Stream Deck is closed: install.ps1 does that.
# Safe to run again: without old keys or an old plugin it does nothing.
$ErrorActionPreference = 'Stop'
$deck = "$env:APPDATA\Elgato\StreamDeck"
$old = "$deck\Plugins\com.stan.launcher.sdPlugin"

$files = @()
foreach ($dir in 'ProfilesV3', 'ProfilesV2') {
    if (Test-Path "$deck\$dir") {
        $files += Get-ChildItem "$deck\$dir" -Recurse -Filter manifest.json | Where-Object { (Get-Content $_.FullName -Raw) -match 'com\.stan\.launcher' }
    }
}

if ($files.Count -gt 0) {
    # A copy of every profile file that is changed, in case anything looks wrong afterwards.
    $backup = "$env:APPDATA\StanGameVolume\profile-backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
    New-Item -ItemType Directory -Force $backup | Out-Null
    $moved = 0
    foreach ($f in $files) {
        $relative = $f.FullName.Substring($deck.Length + 1)
        $copy = Join-Path $backup $relative
        New-Item -ItemType Directory -Force (Split-Path $copy) | Out-Null
        Copy-Item $f.FullName $copy

        $text = [IO.File]::ReadAllText($f.FullName)
        $moved += ([regex]::Matches($text, '"UUID"\s*:\s*"com\.stan\.launcher\.(game|fixedgame|app)"')).Count
        # Fixed Game Key was folded into Game Key long ago; its gameId setting carries over as is.
        $text = [regex]::Replace($text, '"UUID"(\s*):(\s*)"com\.stan\.launcher\.(game|fixedgame)"', '"UUID"$1:$2"com.stan.gamevolume.gamekey"')
        $text = [regex]::Replace($text, '"UUID"(\s*):(\s*)"com\.stan\.launcher\.app"', '"UUID"$1:$2"com.stan.gamevolume.appkey"')
        $text = [regex]::Replace($text, '"Plugin"(\s*):(\s*)\{[^{}]*"com\.stan\.launcher"[^{}]*\}',
            '"Plugin"$1:$2{"Name":"Volume & Launcher","UUID":"com.stan.gamevolume","Version":"3.0.0.0"}')
        [IO.File]::WriteAllText($f.FullName, $text, (New-Object Text.UTF8Encoding $false))
    }
    Write-Output "Launcher keys moved to Volume & Launcher: $moved (backup in $backup)"
}

if (Test-Path $old) {
    Remove-Item $old -Recurse -Force
    Write-Output 'Old Launcher plugin removed.'
}
