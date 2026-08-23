param([switch]$Silent)

$ErrorActionPreference = 'Stop'
$automationRoot = $PSScriptRoot
$settingsPath = Join-Path $automationRoot 'WallpaperControl.ini'
$statePath = Join-Path $automationRoot 'shuffle-state.json'

function Resolve-LivelyEnvironment {
    $configuredSettings = $null
    $environmentPath = Join-Path $automationRoot 'LivelyEnvironment.ini'
    if (Test-Path -LiteralPath $environmentPath) {
        foreach ($line in Get-Content -LiteralPath $environmentPath -ErrorAction SilentlyContinue) {
            if ([string]$line -match '^SettingsPath=(.+)$') { $configuredSettings = $matches[1].Trim() }
        }
    }

    $candidates = @($configuredSettings)
    $candidates += (Join-Path $env:LOCALAPPDATA 'Lively Wallpaper\Settings.json')
    $candidates += (Join-Path $env:LOCALAPPDATA 'Temp\Lively Wallpaper\Settings.json')
    $storePackages = Join-Path $env:LOCALAPPDATA 'Packages'
    if (Test-Path -LiteralPath $storePackages) {
        $candidates += @(Get-ChildItem -LiteralPath $storePackages -Directory -Filter '*LivelyWallpaper*' -ErrorAction SilentlyContinue | ForEach-Object {
            Join-Path $_.FullName 'LocalCache\Local\Lively Wallpaper\Settings.json'
        })
    }

    foreach ($candidate in @($candidates | Where-Object { $_ } | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        try {
            $json = Get-Content -LiteralPath $candidate -Raw | ConvertFrom-Json
            $root = [string]$json.WallpaperDir
            if ($root) {
                return [pscustomobject]@{ SettingsPath = $candidate; WallpaperRoot = $root }
            }
        }
        catch { }
    }
    return $null
}

$livelyEnvironment = Resolve-LivelyEnvironment
$wallpaperRoot = if ($livelyEnvironment) { $livelyEnvironment.WallpaperRoot } else { $null }

function Read-Settings {
    $values = @{}
    $section = ''
    if (Test-Path -LiteralPath $settingsPath) {
        foreach ($rawLine in Get-Content -LiteralPath $settingsPath) {
            $line = ([string]$rawLine).Trim()
            if (-not $line -or $line.StartsWith(';') -or $line.StartsWith('#')) { continue }
            if ($line -match '^\[(.+)\]$') { $section = $matches[1]; continue }
            if ($line -match '^([^=]+)=(.*)$') {
                $values[(($section + '.' + $matches[1].Trim()).ToLowerInvariant())] = $matches[2].Trim()
            }
        }
    }

    # [Color] is the safe MPV-only replacement. Fall back to the old section so
    # an existing installation migrates without losing the selected mode.
    $mode = [string]$values['color.mode']
    if (-not $mode) { $mode = [string]$values['nvidia.mode'] }
    if ($mode -notin @('Off', 'PerFolder', 'Manual')) { $mode = 'Off' }
    $intensityText = [string]$values['color.intensity']
    if (-not $intensityText) { $intensityText = [string]$values['nvidia.intensity'] }
    $intensity = 50
    [void][int]::TryParse($intensityText, [ref]$intensity)
    $folderBoost50 = 35
    $folderBoost70 = 55
    $folderBoost100 = 80
    [void][int]::TryParse([string]$values['foldertuning.boost50'], [ref]$folderBoost50)
    [void][int]::TryParse([string]$values['foldertuning.boost70'], [ref]$folderBoost70)
    [void][int]::TryParse([string]$values['foldertuning.boost100'], [ref]$folderBoost100)
    return [pscustomobject]@{
        Mode = $mode
        Intensity = [Math]::Max(0, [Math]::Min(100, $intensity))
        Saturation = 100
        FolderBoost50 = [Math]::Max(0, [Math]::Min(100, $folderBoost50))
        FolderBoost70 = [Math]::Max(0, [Math]::Min(100, $folderBoost70))
        FolderBoost100 = [Math]::Max(0, [Math]::Min(100, $folderBoost100))
    }
}

function Get-PerFolderFilter {
    $name = $null
    if (Test-Path -LiteralPath $statePath) {
        try { $name = [string](Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).Current } catch { }
    }
    if (-not $name) { return [pscustomobject]@{ Enabled = 0; Intensity = 50; Saturation = 100; Boost = 0 } }

    if (-not $wallpaperRoot) { return [pscustomobject]@{ Enabled = 0; Intensity = 50; Saturation = 100; Boost = 0 } }
    $groups = @(
        [pscustomobject]@{ Path = (Join-Path $wallpaperRoot 'NONE RTX DYANMIC VIBRANCE'); Enabled = 0; Intensity = 50; Boost = 0 }
        [pscustomobject]@{ Path = (Join-Path $wallpaperRoot 'RTX DYNAMIC VIBRANCE 50-100'); Enabled = 1; Intensity = 50; Boost = $settings.FolderBoost50 }
        [pscustomobject]@{ Path = (Join-Path $wallpaperRoot 'RTX DYNAMIC VIBRANCE 70-100'); Enabled = 1; Intensity = 70; Boost = $settings.FolderBoost70 }
        [pscustomobject]@{ Path = (Join-Path $wallpaperRoot 'RTX DYNAMIC VIBRANCE 100-100'); Enabled = 1; Intensity = 100; Boost = $settings.FolderBoost100 }
    )
    foreach ($group in $groups) {
        if (Test-Path -LiteralPath (Join-Path $group.Path $name)) {
            return [pscustomobject]@{ Enabled = $group.Enabled; Intensity = $group.Intensity; Saturation = 100; Boost = $group.Boost }
        }
    }
    return [pscustomobject]@{ Enabled = 0; Intensity = 50; Saturation = 100; Boost = 0 }
}

function Get-MpvSaturationBoost($filter) {
    if (-not $filter.Enabled) { return 0 }
    return [Math]::Max(0, [Math]::Min(100, [int]$filter.Boost))
}

function Set-MpvWallpaperColor([int]$boost) {
    $successCount = 0
    foreach ($pipe in Get-ChildItem -LiteralPath '\\.\pipe\' -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^mpvsocket' }) {
        $client = $null
        $writer = $null
        $reader = $null
        try {
            $client = New-Object IO.Pipes.NamedPipeClientStream('.', $pipe.Name, ([IO.Pipes.PipeDirection]::InOut), ([IO.Pipes.PipeOptions]::None))
            $client.Connect(350)
            $writer = New-Object IO.StreamWriter($client, (New-Object Text.UTF8Encoding($false)), 1024, $true)
            $reader = New-Object IO.StreamReader($client, (New-Object Text.UTF8Encoding($false)), $false, 1024, $true)
            $writer.AutoFlush = $true
            $writer.WriteLine('{"command":["set_property","saturation",' + $boost + ']}')
            $responseText = $reader.ReadLine()
            if ($responseText -and (($responseText | ConvertFrom-Json).error -eq 'success')) { $successCount++ }
        }
        catch { }
        finally {
            if ($reader) { $reader.Dispose() }
            if ($writer) { $writer.Dispose() }
            if ($client) { $client.Dispose() }
        }
    }
    return $successCount
}

$settings = Read-Settings
$filter = switch ($settings.Mode) {
    'Manual' { [pscustomobject]@{ Enabled = 1; Intensity = $settings.Intensity; Saturation = 100; Boost = [int][Math]::Round($settings.Intensity * 0.8) }; break }
    'PerFolder' { Get-PerFolderFilter; break }
    default { [pscustomobject]@{ Enabled = 0; Intensity = 50; Saturation = 100 } }
}

$boost = Get-MpvSaturationBoost $filter
$updatedPipes = Set-MpvWallpaperColor $boost

if (-not $Silent) {
    "Mode=$($settings.Mode) Intensity=$($filter.Intensity) Saturation=100 MpvBoost=+$boost Nvidia=Unchanged Pipes=$updatedPipes"
}
