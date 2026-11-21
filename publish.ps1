# Commits any plugin changes, pushes them and publishes new installers as a GitHub release.
# Every install.ps1 calls this, so an installed update also reaches GitHub. Does nothing without changes.
# The release notes come from "Unreleased" in CHANGELOG.md (New / Improved / Fixed); that part then
# moves under the new version number. Without changelog lines, -Message is used as the notes.
param([string]$Message = 'Plugins updated')

# Git writes harmless notices to stderr; failures are caught through exit codes below.
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
if (-not (Test-Path $gh)) { $gh = (Get-Command gh -ErrorAction SilentlyContinue).Source }
if (-not $gh) { Write-Warning 'GitHub CLI not found, nothing published.'; exit 0 }

$unreleasedHeading = '## Unreleased'
$emptyUnreleased = "$unreleasedHeading`n`n### New`n`n### Improved`n`n### Fixed`n"

# Takes the unreleased changelog lines, puts them under the new version and returns them as release notes.
function Move-Changelog([string]$path, [string]$version) {
    if (-not (Test-Path $path)) { return $null }
    $text = [IO.File]::ReadAllText($path) -replace "`r`n", "`n"
    $start = $text.IndexOf($unreleasedHeading)
    if ($start -lt 0) { return $null }
    $bodyStart = $start + $unreleasedHeading.Length
    $next = $text.IndexOf("`n## ", $bodyStart)
    if ($next -lt 0) { $next = $text.Length }
    $body = $text.Substring($bodyStart, $next - $bodyStart).Trim()
    if ($body -notmatch '(?m)^- ') { return $null }

    # Groups without lines are left out of the release.
    $body = [regex]::Replace($body, '(?m)^### [^\n]+\n+(?=### |\z)', '').Trim()
    $date = Get-Date -Format 'yyyy-MM-dd'
    $released = "$emptyUnreleased`n## $version ($date)`n`n$body`n"
    $text = $text.Substring(0, $start) + $released + $text.Substring($next).TrimStart("`n").Insert(0, "`n")
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))
    return $body
}

Push-Location $root
try {
    git -c core.safecrlf=false add -A
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { Write-Output 'No changes to publish.'; exit 0 }

    $latest = & $gh release view --json tagName --jq .tagName
    $parts = ($latest -replace '^v', '') -split '\.'
    $version = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
    $tag = "v$version"

    $notes = Move-Changelog "$root\CHANGELOG.md" $version
    if (-not $notes) { $notes = $Message }
    git -c core.safecrlf=false add -A

    $msgFile = Join-Path $env:TEMP 'streamdeck-commit.txt'
    [IO.File]::WriteAllText($msgFile, "$Message`n`nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`n")
    git commit -q -F $msgFile
    if ($LASTEXITCODE -ne 0) { throw 'commit failed' }
    git push -q
    if ($LASTEXITCODE -ne 0) { throw 'push failed' }

    powershell -NoProfile -ExecutionPolicy Bypass -File "$root\package.ps1"
    if ($LASTEXITCODE -ne 0) { throw 'building packages failed' }

    $notesFile = Join-Path $env:TEMP 'streamdeck-release-notes.md'
    [IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding $false))
    $assets = Get-ChildItem "$root\dist" -Filter *.streamDeckPlugin | ForEach-Object FullName
    & $gh release create $tag @assets --title "Stream Deck plugins $version" --notes-file $notesFile --latest
    if ($LASTEXITCODE -ne 0) { throw 'release failed' }
    Write-Output "Published: $tag"
}
catch { Write-Warning "Publishing failed: $_" }
finally { Pop-Location }
