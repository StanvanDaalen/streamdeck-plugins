# Builds double-click installers in dist\ that also run on PCs without .NET.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = "$root\dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

$plugins = @(
    @{ Project = 'GameVolume'; Id = 'com.stan.gamevolume'; File = 'Audio' },
    @{ Project = 'Launcher'; Id = 'com.stan.launcher'; File = 'Launcher' },
    @{ Project = 'Monitor'; Id = 'com.stan.monitor'; File = 'Monitor' }
)

foreach ($p in $plugins) {
    $dir = "$root\$($p.Project)"
    $publish = "$dir\publish"
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet publish "$dir\$($p.Project).csproj" -c Release -r win-x64 --self-contained true -o $publish -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $($p.Project)" }

    $stage = "$dist\stage\$($p.Id).sdPlugin"
    New-Item -ItemType Directory -Force "$stage\bin" | Out-Null
    Copy-Item "$dir\sdPlugin\*" $stage -Recurse -Force
    Copy-Item "$publish\*" "$stage\bin" -Recurse -Force

    # tar writes forward slashes in the archive, which Stream Deck needs.
    tar -a -c -f "$dist\$($p.File).zip" -C "$dist\stage" "$($p.Id).sdPlugin"
    if ($LASTEXITCODE -ne 0) { throw "zip failed: $($p.File)" }
    Move-Item "$dist\$($p.File).zip" "$dist\$($p.File).streamDeckPlugin"
}
Get-ChildItem $dist -Filter *.streamDeckPlugin | ForEach-Object { '{0}  {1:N0} MB' -f $_.Name, ($_.Length / 1MB) }
