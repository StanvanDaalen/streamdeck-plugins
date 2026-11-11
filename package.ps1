# Builds double-click installers in dist\ that also run on PCs without .NET.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = "$root\dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

$plugins = @(
    @{ Project = 'GameVolume'; Id = 'com.stan.gamevolume'; File = 'Audio' },
    @{ Project = 'Launcher'; Id = 'com.stan.launcher'; File = 'Launcher' },
    @{ Project = 'Monitor'; Id = 'com.stan.monitor'; File = 'Monitor' },
    # F1 also runs on Linux under OpenDeck: its package carries a linux-x64 build in bin-linux as well
    # (sdPlugin\manifest.linux.json points OpenDeck there; Stream Deck itself only reads manifest.json).
    @{ Project = 'F1'; Id = 'com.stan.f1'; File = 'F1'; Linux = $true }
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

    if ($p.Linux) {
        $publishLinux = "$dir\publish-linux"
        if (Test-Path $publishLinux) { Remove-Item $publishLinux -Recurse -Force }
        dotnet publish "$dir\$($p.Project).csproj" -c Release -r linux-x64 --self-contained true -o $publishLinux -v q --nologo
        if ($LASTEXITCODE -ne 0) { throw "publish failed: $($p.Project) (linux-x64)" }
        # OpenDeck makes the program executable itself before starting it (a zip made here has no Unix modes).
        New-Item -ItemType Directory -Force "$stage\bin-linux" | Out-Null
        Copy-Item "$publishLinux\*" "$stage\bin-linux" -Recurse -Force
    }

    # tar writes forward slashes in the archive, which Stream Deck needs.
    tar -a -c -f "$dist\$($p.File).zip" -C "$dist\stage" "$($p.Id).sdPlugin"
    if ($LASTEXITCODE -ne 0) { throw "zip failed: $($p.File)" }
    Move-Item "$dist\$($p.File).zip" "$dist\$($p.File).streamDeckPlugin"
}
Get-ChildItem $dist -Filter *.streamDeckPlugin | ForEach-Object { '{0}  {1:N0} MB' -f $_.Name, ($_.Length / 1MB) }
