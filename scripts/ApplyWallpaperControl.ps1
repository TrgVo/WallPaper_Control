param(
    [switch]$Silent,
    [ValidateRange(1, 20)][int]$RetryCount = 1,
    [ValidateRange(50, 5000)][int]$RetryDelayMilliseconds = 250
)

$ErrorActionPreference = 'Stop'
$automationRoot = $PSScriptRoot
$settingsPath = Join-Path $automationRoot 'WallpaperControl.ini'
$statePath = Join-Path $automationRoot 'shuffle-state.json'
$servicePidPath = Join-Path $automationRoot 'lively-shuffle.pid'

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
                return [pscustomobject]@{ SettingsPath = $candidate; DataRoot = (Split-Path -Parent $candidate); WallpaperRoot = $root }
            }
        }
        catch { }
    }
    return $null
}

$livelyEnvironment = Resolve-LivelyEnvironment
$wallpaperRoot = if ($livelyEnvironment) { $livelyEnvironment.WallpaperRoot } else { $null }
$livelyDataRoot = if ($livelyEnvironment) { $livelyEnvironment.DataRoot } else { $null }

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
    if ($mode -eq 'PerFolder') { $mode = 'Profiles' }
    if ($mode -notin @('Off', 'Manual', 'Profiles')) { $mode = 'Off' }
    $intensityText = [string]$values['color.intensity']
    if (-not $intensityText) { $intensityText = [string]$values['nvidia.intensity'] }
    $intensity = 50
    [void][int]::TryParse($intensityText, [ref]$intensity)
    $saturationText = [string]$values['color.saturation']
    if (-not $saturationText) { $saturationText = [string]$values['nvidia.saturation'] }
    $saturation = 100
    [void][int]::TryParse($saturationText, [ref]$saturation)
    return [pscustomobject]@{
        Mode = $mode
        Intensity = [Math]::Max(0, [Math]::Min(100, $intensity))
        Saturation = [Math]::Max(0, [Math]::Min(100, $saturation))
    }
}

function Get-MpvBoost([int]$intensity, [int]$saturation) {
    $safeIntensity = [Math]::Max(0, [Math]::Min(100, $intensity))
    $safeSaturation = [Math]::Max(0, [Math]::Min(100, $saturation))
    return [int][Math]::Round($safeIntensity * 0.8 * ($safeSaturation / 100.0))
}

function Sync-NewProfileVideosToCanonicalRoot {
    if (-not $wallpaperRoot) { return }
    $canonicalRoot = Join-Path $wallpaperRoot 'Videos'
    if (-not (Test-Path -LiteralPath $canonicalRoot)) {
        New-Item -ItemType Directory -Path $canonicalRoot | Out-Null
    }
    $profileFolders = @(Get-ChildItem -LiteralPath $wallpaperRoot -Directory -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -in @('NONE RTX DYANMIC VIBRANCE', 'NONE RTX DYNAMIC VIBRANCE') -or
        $_.Name -match '^RTX DYNAMIC VIBRANCE \d{1,3}-\d{1,3}$'
    })
    foreach ($folder in $profileFolders) {
        foreach ($profileVideo in Get-ChildItem -LiteralPath $folder.FullName -Filter '*.mp4' -File -ErrorAction SilentlyContinue) {
            $canonicalPath = Join-Path $canonicalRoot $profileVideo.Name
            if (Test-Path -LiteralPath $canonicalPath -PathType Leaf) { continue }
            try { New-Item -ItemType HardLink -Path $canonicalPath -Target $profileVideo.FullName | Out-Null } catch { }
        }
    }
}

function Get-RunningLivelyVideoName {
    if (-not $livelyDataRoot) { return $null }
    $layoutPath = Join-Path $livelyDataRoot 'WallpaperLayout.json'
    if (-not (Test-Path -LiteralPath $layoutPath)) { return $null }
    try {
        $parsedLayout = Get-Content -LiteralPath $layoutPath -Raw | ConvertFrom-Json
        $layout = @()
        # Windows PowerShell 5.1 can keep a top-level JSON array nested as one
        # item. Flatten it explicitly so each monitor entry is evaluated.
        if ($parsedLayout -is [Array]) {
            foreach ($layoutEntry in $parsedLayout) { $layout += $layoutEntry }
        }
        elseif ($null -ne $parsedLayout) { $layout += $parsedLayout }
        foreach ($entry in $layout) {
            if ([bool]$entry.LivelyScreen.isStale) { continue }
            $infoPath = Join-Path ([string]$entry.LivelyInfoPath) 'LivelyInfo.json'
            if (-not (Test-Path -LiteralPath $infoPath)) { continue }
            $fileName = [string](Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json).FileName
            if ($fileName) { return [IO.Path]::GetFileName($fileName) }
        }
    }
    catch { }
    return $null
}

function Get-RunningMpvVideoName {
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
            $requestId = Get-Random -Minimum 100000 -Maximum 999999
            $message = @{ command = @('get_property', 'path'); request_id = $requestId } | ConvertTo-Json -Compress
            $writer.WriteLine($message)
            do {
                $responseText = $reader.ReadLine()
                if (-not $responseText) { break }
                $response = $responseText | ConvertFrom-Json
            } while ([int]$response.request_id -ne $requestId)

            if ($responseText -and $response.error -eq 'success' -and [string]$response.data) {
                $videoName = [IO.Path]::GetFileName([string]$response.data)
                if ($videoName -and $wallpaperRoot) {
                    $canonicalVideo = Join-Path (Join-Path $wallpaperRoot 'Videos') $videoName
                    $isKnownProfile = @(Get-ChildItem -LiteralPath $wallpaperRoot -Directory -ErrorAction SilentlyContinue | Where-Object {
                        ($_.Name -in @('NONE RTX DYANMIC VIBRANCE', 'NONE RTX DYNAMIC VIBRANCE') -or
                         $_.Name -match '^RTX DYNAMIC VIBRANCE \d{1,3}-\d{1,3}$') -and
                        (Test-Path -LiteralPath (Join-Path $_.FullName $videoName) -PathType Leaf)
                    }).Count -gt 0
                    if ((Test-Path -LiteralPath $canonicalVideo -PathType Leaf) -or $isKnownProfile) { return $videoName }
                }
            }
        }
        catch { }
        finally {
            if ($reader) { $reader.Dispose() }
            if ($writer) { $writer.Dispose() }
            if ($client) { $client.Dispose() }
        }
    }
    return $null
}

function Get-CurrentVideoProfile {
    $mpvVideoName = Get-RunningMpvVideoName
    $shuffleVideoName = $null
    if (Test-Path -LiteralPath $statePath) {
        try { $shuffleVideoName = [string](Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).Current } catch { }
    }

    $shuffleRunning = $false
    if (Test-Path -LiteralPath $servicePidPath) {
        try {
            $servicePid = [int](Get-Content -LiteralPath $servicePidPath -Raw).Trim()
            $shuffleRunning = $null -ne (Get-Process -Id $servicePid -ErrorAction SilentlyContinue)
        }
        catch { }
    }
    # Seamless loadfile keeps Lively's original package in WallpaperLayout.json.
    # While Auto Wallpaper is active, shuffle-state is the authoritative video.
    $name = if ($mpvVideoName) { $mpvVideoName } elseif ($shuffleRunning -and $shuffleVideoName) { $shuffleVideoName } else { Get-RunningLivelyVideoName }
    if (-not $name) { $name = $shuffleVideoName }
    if (-not $name) { return [pscustomobject]@{ Enabled = 0; Intensity = 0; Saturation = 100; Boost = 0 } }

    if (-not $wallpaperRoot) { return [pscustomobject]@{ Enabled = 0; Intensity = 0; Saturation = 100; Boost = 0 } }
    $groups = @(
        [pscustomobject]@{ Path = (Join-Path $wallpaperRoot 'NONE RTX DYANMIC VIBRANCE'); Enabled = 0; Intensity = 0; Saturation = 100; Boost = 0 }
        [pscustomobject]@{ Path = (Join-Path $wallpaperRoot 'NONE RTX DYNAMIC VIBRANCE'); Enabled = 0; Intensity = 0; Saturation = 100; Boost = 0 }
    )
    foreach ($directory in Get-ChildItem -LiteralPath $wallpaperRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name) {
        if ($directory.Name -notmatch '^RTX DYNAMIC VIBRANCE (\d{1,3})-(\d{1,3})$') { continue }
        $intensity = [Math]::Max(0, [Math]::Min(100, [int]$matches[1]))
        $saturation = [Math]::Max(0, [Math]::Min(100, [int]$matches[2]))
        $groups += [pscustomobject]@{ Path = $directory.FullName; Enabled = 1; Intensity = $intensity; Saturation = $saturation; Boost = (Get-MpvBoost $intensity $saturation) }
    }
    foreach ($group in $groups) {
        if (Test-Path -LiteralPath (Join-Path $group.Path $name)) {
            return [pscustomobject]@{ Enabled = $group.Enabled; Intensity = $group.Intensity; Saturation = $group.Saturation; Boost = $group.Boost }
        }
    }
    return [pscustomobject]@{ Enabled = 0; Intensity = 0; Saturation = 100; Boost = 0 }
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
            $requestId = Get-Random -Minimum 100000 -Maximum 999999
            $message = @{ command = @('set_property', 'saturation', $boost); request_id = $requestId } | ConvertTo-Json -Compress
            $writer.WriteLine($message)
            do {
                $responseText = $reader.ReadLine()
                if (-not $responseText) { break }
                $response = $responseText | ConvertFrom-Json
            } while ([int]$response.request_id -ne $requestId)
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

Sync-NewProfileVideosToCanonicalRoot
$settings = Read-Settings
$filter = switch ($settings.Mode) {
    'Manual' { [pscustomobject]@{ Enabled = 1; Intensity = $settings.Intensity; Saturation = $settings.Saturation; Boost = (Get-MpvBoost $settings.Intensity $settings.Saturation) }; break }
    'Profiles' { Get-CurrentVideoProfile; break }
    default { [pscustomobject]@{ Enabled = 0; Intensity = 0; Saturation = 100; Boost = 0 } }
}

$boost = Get-MpvSaturationBoost $filter
$updatedPipes = 0
for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
    $updatedPipes = [Math]::Max($updatedPipes, (Set-MpvWallpaperColor $boost))
    if ($attempt -lt $RetryCount) { Start-Sleep -Milliseconds $RetryDelayMilliseconds }
}

if (-not $Silent) {
    "Mode=$($settings.Mode) Intensity=$($filter.Intensity) Saturation=$($filter.Saturation) MpvBoost=+$boost Nvidia=Unchanged Pipes=$updatedPipes Attempts=$RetryCount"
}
