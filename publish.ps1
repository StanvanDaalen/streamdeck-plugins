# Commits any plugin changes, pushes them and publishes new installers as a GitHub release.
# Every install.ps1 calls this, so an installed update also reaches GitHub. Does nothing without changes.
param([string]$Message = 'Plugins bijgewerkt')

# Git writes harmless notices to stderr; failures are caught through exit codes below.
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
if (-not (Test-Path $gh)) { $gh = (Get-Command gh -ErrorAction SilentlyContinue).Source }
if (-not $gh) { Write-Warning 'GitHub CLI niet gevonden, niets gepubliceerd.'; exit 0 }

Push-Location $root
try {
    git -c core.safecrlf=false add -A
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { Write-Output 'Geen wijzigingen om te publiceren.'; exit 0 }

    $msgFile = Join-Path $env:TEMP 'streamdeck-commit.txt'
    [IO.File]::WriteAllText($msgFile, "$Message`n`nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`n")
    git commit -q -F $msgFile
    if ($LASTEXITCODE -ne 0) { throw 'commit mislukt' }
    git push -q
    if ($LASTEXITCODE -ne 0) { throw 'push mislukt' }

    powershell -NoProfile -ExecutionPolicy Bypass -File "$root\package.ps1"
    if ($LASTEXITCODE -ne 0) { throw 'pakketten bouwen mislukt' }

    $latest = & $gh release view --json tagName --jq .tagName
    $parts = ($latest -replace '^v', '') -split '\.'
    $tag = "v$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
    $assets = Get-ChildItem "$root\dist" -Filter *.streamDeckPlugin | ForEach-Object FullName
    & $gh release create $tag @assets --title "Stream Deck-plugins $($tag.TrimStart('v'))" --notes $Message --latest
    if ($LASTEXITCODE -ne 0) { throw 'release mislukt' }
    Write-Output "Gepubliceerd: $tag"
}
catch { Write-Warning "Publiceren mislukt: $_" }
finally { Pop-Location }
