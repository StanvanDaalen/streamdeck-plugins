# Builds sdPlugin\F1.streamDeckProfile: the Stream Deck + profile that ships with the plugin.
# Keys (column,row) and dials are laid out below; run install.ps1 afterwards to put it on the Stream Deck.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = (Get-Content "$root\sdPlugin\manifest.json" -Raw | ConvertFrom-Json).Version

$keys = [ordered]@{
    '0,0' = 'stop';  '1,0' = 'flags'; '2,0' = 'live';  '3,0' = 'sync'
    '0,1' = 'race';  '1,1' = 'quali'; '2,1' = 'pause'; '3,1' = 'test'
}
$dials = [ordered]@{ '0,0' = 'flags'; '1,0' = 'zone'; '2,0' = 'volume'; '3,0' = 'latency' }

function Action($name) {
    [ordered]@{
        ActionID    = [guid]::NewGuid().ToString()
        LinkedTitle = $true
        Name        = $name
        Plugin      = [ordered]@{ Name = 'F1'; UUID = 'com.stan.f1'; Version = $version }
        Resources   = $null
        Settings    = @{}
        State       = 0
        States      = @([ordered]@{ ShowTitle = $false; Title = '' })
        UUID        = "com.stan.f1.$name"
    }
}

$work = Join-Path $env:TEMP 'f1-profile'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
$profileId = [guid]::NewGuid().ToString().ToUpper()
$pageId = [guid]::NewGuid().ToString().ToUpper()
$pdir = "$work\Profiles\$profileId.sdProfile"
New-Item -ItemType Directory -Force "$pdir\Profiles\$pageId\Images", "$pdir\Images" | Out-Null

$keypad = [ordered]@{}; foreach ($k in $keys.Keys) { $keypad[$k] = Action $keys[$k] }
$encoder = [ordered]@{}; foreach ($k in $dials.Keys) { $encoder[$k] = Action $dials[$k] }
$page = [ordered]@{
    Controllers = @(
        [ordered]@{ Actions = $keypad; Type = 'Keypad' },
        [ordered]@{ Actions = $encoder; Type = 'Encoder' }
    )
    Icon = ''; Name = ''
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText("$pdir\Profiles\$pageId\manifest.json", ($page | ConvertTo-Json -Depth 10 -Compress), $utf8)

$manifest = [ordered]@{
    Device  = [ordered]@{ Model = '20GBD9901'; UUID = '' }
    Name    = 'F1'
    Pages   = [ordered]@{ Current = $pageId.ToLower(); Default = $pageId.ToLower(); Pages = @($pageId.ToLower()) }
    Version = '3.0'
}
[IO.File]::WriteAllText("$pdir\manifest.json", ($manifest | ConvertTo-Json -Depth 10 -Compress), $utf8)

$package = [ordered]@{
    AppVersion = '7.3.0.22513'; DeviceModel = '20GBD9901'; DeviceSettings = $null; FormatVersion = 1
    OSType = 'Windows'; OSVersion = '10.0.26200'; RequiredPlugins = @('com.stan.f1')
}
[IO.File]::WriteAllText("$work\package.json", ($package | ConvertTo-Json -Depth 10 -Compress), $utf8)

# tar writes forward slashes in the archive, which Stream Deck needs.
$out = "$root\sdPlugin\F1.streamDeckProfile"
if (Test-Path $out) { Remove-Item $out -Force }
tar -a -c -f "$work.zip" -C $work package.json Profiles
if ($LASTEXITCODE -ne 0) { throw 'zip failed' }
Move-Item "$work.zip" $out -Force
Write-Output "Profiel gemaakt: $out"
