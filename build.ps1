param()

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$buildRoot = Join-Path $repoRoot '.build'
$payloadRoot = Join-Path $buildRoot 'payload'
$payloadAutomation = Join-Path $payloadRoot 'Automation'
$payloadAssets = Join-Path $payloadAutomation 'Assets'
$distRoot = Join-Path $repoRoot 'dist'

if (Test-Path -LiteralPath $buildRoot) {
    $resolvedBuild = (Resolve-Path -LiteralPath $buildRoot).Path
    $expectedBuild = [IO.Path]::GetFullPath((Join-Path $repoRoot '.build'))
    if (-not [string]::Equals($resolvedBuild, $expectedBuild, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected build directory: $resolvedBuild"
    }
    Remove-Item -LiteralPath $resolvedBuild -Recurse -Force
}

New-Item -ItemType Directory -Path $payloadAssets -Force | Out-Null
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) { throw 'Could not find .NET Framework csc.exe.' }

$gpp = (Get-Command g++.exe -ErrorAction SilentlyContinue).Source
if (-not $gpp) { throw 'Could not find g++.exe. Install MinGW-w64 and add it to PATH.' }

$icon = Join-Path $repoRoot 'assets\WallpaperControl.ico'
$appOutput = Join-Path $payloadRoot 'WallpaperControl.exe'
$launcherOutput = Join-Path $payloadAutomation 'LivelyShuffleLauncher.exe'
$setupOutput = Join-Path $distRoot 'WallpaperControlSetup.exe'
$setupBuildOutput = Join-Path $buildRoot 'WallpaperControlSetup.exe'

& $csc /nologo /target:winexe /optimize+ `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    /reference:System.Core.dll /reference:Microsoft.CSharp.dll `
    "/win32icon:$icon" "/out:$appOutput" `
    (Join-Path $repoRoot 'src\WallpaperControlApp.cs')
if ($LASTEXITCODE -ne 0) { throw "WallpaperControl compile failed: $LASTEXITCODE" }

& $gpp -std=c++17 -O2 -municode -mwindows -static -static-libgcc -static-libstdc++ `
    (Join-Path $repoRoot 'src\LivelyShuffleLauncher.cpp') -o $launcherOutput
if ($LASTEXITCODE -ne 0) { throw "Launcher compile failed: $LASTEXITCODE" }

Copy-Item -LiteralPath `
    (Join-Path $repoRoot 'scripts\LivelyShuffle.ps1'), `
    (Join-Path $repoRoot 'scripts\ApplyWallpaperControl.ps1') `
    -Destination $payloadAutomation -Force
Copy-Item -LiteralPath `
    (Join-Path $repoRoot 'assets\WallpaperControl.png'), `
    (Join-Path $repoRoot 'assets\WallpaperControl.ico') `
    -Destination $payloadAssets -Force

$resources = @(
    "/resource:$payloadRoot\WallpaperControl.exe,Payload.WallpaperControl.exe",
    "/resource:$payloadAutomation\LivelyShuffle.ps1,Payload.Automation.LivelyShuffle.ps1",
    "/resource:$payloadAutomation\ApplyWallpaperControl.ps1,Payload.Automation.ApplyWallpaperControl.ps1",
    "/resource:$payloadAutomation\LivelyShuffleLauncher.exe,Payload.Automation.LivelyShuffleLauncher.exe",
    "/resource:$payloadAssets\WallpaperControl.png,Payload.Assets.WallpaperControl.png",
    "/resource:$payloadAssets\WallpaperControl.ico,Payload.Assets.WallpaperControl.ico"
)

& $csc /nologo /target:winexe /optimize+ `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    /reference:System.Core.dll /reference:System.Web.Extensions.dll `
    "/win32icon:$icon" "/out:$setupBuildOutput" `
    @resources (Join-Path $repoRoot 'src\WallpaperControlSetup.cs')
if ($LASTEXITCODE -ne 0) { throw "Setup compile failed: $LASTEXITCODE" }

try {
    Copy-Item -LiteralPath $setupBuildOutput -Destination $setupOutput -Force
}
catch {
    # Some managed/sandboxed Windows environments lock an existing executable
    # in dist after inspection. The build itself is still valid in .build.
    Write-Warning "Could not replace dist artifact: $($_.Exception.Message)"
    $setupOutput = $setupBuildOutput
}

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $setupOutput
Write-Host "Built: $setupOutput"
Write-Host "SHA256: $($hash.Hash)"
