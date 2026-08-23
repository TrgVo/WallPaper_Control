param(
    [switch]$Once,
    [switch]$ConfigureScalingOnly
)

$ErrorActionPreference = 'Stop'
$automationRoot = $PSScriptRoot

function Resolve-LivelyEnvironment {
    $configured = @{}
    $environmentPath = Join-Path $automationRoot 'LivelyEnvironment.ini'
    if (Test-Path -LiteralPath $environmentPath) {
        foreach ($line in Get-Content -LiteralPath $environmentPath -ErrorAction SilentlyContinue) {
            if ([string]$line -match '^([^=]+)=(.*)$') { $configured[$matches[1].Trim()] = $matches[2].Trim() }
        }
    }

    $settingsCandidates = @([string]$configured['SettingsPath'])
    $settingsCandidates += (Join-Path $env:LOCALAPPDATA 'Lively Wallpaper\Settings.json')
    $settingsCandidates += (Join-Path $env:LOCALAPPDATA 'Temp\Lively Wallpaper\Settings.json')
    $storePackages = Join-Path $env:LOCALAPPDATA 'Packages'
    if (Test-Path -LiteralPath $storePackages) {
        $settingsCandidates += @(Get-ChildItem -LiteralPath $storePackages -Directory -Filter '*LivelyWallpaper*' -ErrorAction SilentlyContinue | ForEach-Object {
            Join-Path $_.FullName 'LocalCache\Local\Lively Wallpaper\Settings.json'
        })
    }

    $settingsPath = $null
    $wallpaperRoot = $null
    foreach ($candidate in @($settingsCandidates | Where-Object { $_ } | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        try {
            $json = Get-Content -LiteralPath $candidate -Raw | ConvertFrom-Json
            if ([string]$json.WallpaperDir) {
                $settingsPath = $candidate
                $wallpaperRoot = [string]$json.WallpaperDir
                break
            }
        }
        catch { }
    }
    if (-not $settingsPath -or -not $wallpaperRoot) {
        throw 'Lively Wallpaper was not detected. Open Lively once, then restart Wallpaper Control.'
    }

    $isStoreDistribution = $settingsPath.IndexOf((Join-Path $env:LOCALAPPDATA 'Packages'), [StringComparison]::OrdinalIgnoreCase) -eq 0
    $livelyExe = if ($isStoreDistribution) { $null } else { [string]$configured['LivelyExePath'] }
    if (-not $isStoreDistribution -and (-not $livelyExe -or -not (Test-Path -LiteralPath $livelyExe))) {
        $livelyExe = $null
        $runningLively = Get-Process -Name 'Lively' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($runningLively -and $runningLively.Path -and (Test-Path -LiteralPath $runningLively.Path)) {
            $livelyExe = $runningLively.Path
        }
        if (-not $livelyExe) {
            foreach ($candidate in @(
                (Join-Path $env:LOCALAPPDATA 'Programs\Lively Wallpaper\Lively.exe'),
                (Join-Path $env:ProgramFiles 'Lively Wallpaper\Lively.exe'),
                (Join-Path ${env:ProgramFiles(x86)} 'Lively Wallpaper\Lively.exe')
            )) {
                if ($candidate -and (Test-Path -LiteralPath $candidate)) { $livelyExe = $candidate; break }
            }
        }
    }

    $appId = [string]$configured['AppUserModelId']
    if (-not $appId) { $appId = '12030rocksdanister.LivelyWallpaper_97hta09mmv6hy!App' }
    return [pscustomobject]@{
        SettingsPath = $settingsPath
        DataRoot = Split-Path -Parent $settingsPath
        WallpaperRoot = $wallpaperRoot
        LivelyExePath = $livelyExe
        AppUserModelId = $appId
    }
}

$livelyEnvironment = Resolve-LivelyEnvironment
$libraryRoot = Join-Path $livelyEnvironment.WallpaperRoot 'SaveData\wptmp'
$nvidiaControllerPath = Join-Path $automationRoot 'NvidiaAIDVCController.exe'
$nvidiaDynamicVibranceEnabled = $false
$controlSettingsPath = Join-Path $automationRoot 'WallpaperControl.ini'
$disabledMarkerPath = Join-Path $automationRoot 'wallpaper-auto.disabled'
$servicePidPath = Join-Path $automationRoot 'lively-shuffle.pid'
$classifiedVideoGroups = @(
    [pscustomobject]@{ Path = (Join-Path $livelyEnvironment.WallpaperRoot 'NONE RTX DYANMIC VIBRANCE'); Enabled = $false; Intensity = 50; Saturation = 100; Label = 'Off' }
    [pscustomobject]@{ Path = (Join-Path $livelyEnvironment.WallpaperRoot 'RTX DYNAMIC VIBRANCE 50-100'); Enabled = $true; Intensity = 50; Saturation = 100; Label = '50/100' }
    [pscustomobject]@{ Path = (Join-Path $livelyEnvironment.WallpaperRoot 'RTX DYNAMIC VIBRANCE 70-100'); Enabled = $true; Intensity = 70; Saturation = 100; Label = '70/100' }
    [pscustomobject]@{ Path = (Join-Path $livelyEnvironment.WallpaperRoot 'RTX DYNAMIC VIBRANCE 100-100'); Enabled = $true; Intensity = 100; Saturation = 100; Label = '100/100' }
)
$nvidiaFilterOff = [pscustomobject]@{ FilterEnabled = $false; Intensity = 50; Saturation = 100; FilterLabel = 'Off' }
$statePath = Join-Path $automationRoot 'shuffle-state.json'
$logPath = Join-Path $automationRoot 'shuffle.log'
$livelySettingsPath = $livelyEnvironment.SettingsPath
$mpvPropertiesTemplate = Join-Path $livelyEnvironment.DataRoot 'Mpv\LivelyProperties.json'
$wallpaperDataRoot = Join-Path $livelyEnvironment.WallpaperRoot 'SaveData\wpdata'
$appUserModelId = $livelyEnvironment.AppUserModelId
$livelyExePath = $livelyEnvironment.LivelyExePath
$fallbackDurationSeconds = 600
$scanIntervalSeconds = 5
$slotDurationSeconds = 60
$foregroundPollMilliseconds = 250
$fadeHalfDurationMilliseconds = 600
$fadeFramesPerSecond = 30
$dvcFadeThreshold = -30
$dvcVisibleFadeMilliseconds = 180
$dvcDarkFadeMilliseconds = 110
$newVideoPrerollMilliseconds = 250
$mpvIpcRetryCount = 12
$mpvIpcRetryDelayMilliseconds = 250
$fillThresholdWidth = 1920
$fillThresholdHeight = 1200
$sixteenByNineAspect = 16.0 / 9.0
$aspectTolerance = 0.01

if (-not (Test-Path -LiteralPath $automationRoot)) {
    New-Item -ItemType Directory -Path $automationRoot | Out-Null
}

$mutex = New-Object Threading.Mutex($false, 'Local\LivelyWallpaperShuffleBag')
if (-not $mutex.WaitOne(0, $false)) {
    exit 0
}

function Write-Log([string]$message) {
    $line = '{0} {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function Get-WallpaperControlSettings {
    $values = @{}
    $section = ''
    if (Test-Path -LiteralPath $controlSettingsPath) {
        foreach ($rawLine in Get-Content -LiteralPath $controlSettingsPath -ErrorAction SilentlyContinue) {
            $line = ([string]$rawLine).Trim()
            if (-not $line -or $line.StartsWith(';') -or $line.StartsWith('#')) { continue }
            if ($line -match '^\[(.+)\]$') {
                $section = $matches[1]
                continue
            }
            if ($line -match '^([^=]+)=(.*)$') {
                $values[(($section + '.' + $matches[1].Trim()).ToLowerInvariant())] = $matches[2].Trim()
            }
        }
    }

    $mode = [string]$values['color.mode']
    if (-not $mode) { $mode = [string]$values['nvidia.mode'] }
    if ($mode -notin @('Off', 'PerFolder', 'Manual')) { $mode = 'Off' }
    $intensity = 50
    $saturation = 100
    $intensityText = [string]$values['color.intensity']
    if (-not $intensityText) { $intensityText = [string]$values['nvidia.intensity'] }
    [void][int]::TryParse($intensityText, [ref]$intensity)
    $saturationText = [string]$values['color.saturation']
    if (-not $saturationText) { $saturationText = [string]$values['nvidia.saturation'] }
    if ($saturationText) { [void][int]::TryParse($saturationText, [ref]$saturation) }
    return [pscustomobject]@{
        AutoEnabled = [string]$values['wallpaper.autoenabled'] -ne '0'
        NvidiaMode = $mode
        Intensity = [Math]::Max(0, [Math]::Min(100, $intensity))
        Saturation = [Math]::Max(0, [Math]::Min(100, $saturation))
    }
}

function Get-EffectiveNvidiaFilter($video, $controlSettings) {
    switch ($controlSettings.NvidiaMode) {
        'Manual' {
            return [pscustomobject]@{
                FilterEnabled = $true
                Intensity = [int]$controlSettings.Intensity
                Saturation = [int]$controlSettings.Saturation
                FilterLabel = "Manual $($controlSettings.Intensity)/$($controlSettings.Saturation)"
            }
        }
        'PerFolder' { return $video }
        default { return $nvidiaFilterOff }
    }
}

function Get-ClassifiedVideos {
    $knownNames = @{}
    foreach ($group in $classifiedVideoGroups) {
        if (-not (Test-Path -LiteralPath $group.Path)) {
            Write-Log "Classified video folder is missing: $($group.Path)"
            continue
        }
        foreach ($video in Get-ChildItem -LiteralPath $group.Path -Filter '*.mp4' -File | Sort-Object Name) {
            $knownNames[$video.Name.ToLowerInvariant()] = $true
            [pscustomobject]@{
                Name = $video.Name
                BaseName = $video.BaseName
                FullName = $video.FullName
                FilterEnabled = [bool]$group.Enabled
                Intensity = [int]$group.Intensity
                Saturation = [int]$group.Saturation
                FilterLabel = [string]$group.Label
            }
        }
    }

    # Portable installs may not have the user's four classification folders
    # populated yet. Reuse absolute video paths already registered in Lively,
    # without copying the media, and default them to neutral color.
    foreach ($package in Get-ChildItem -LiteralPath $libraryRoot -Directory -ErrorAction SilentlyContinue) {
        $infoPath = Join-Path $package.FullName 'LivelyInfo.json'
        if (-not (Test-Path -LiteralPath $infoPath)) { continue }
        try {
            $info = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json
            $videoPath = [string]$info.FileName
            if (-not $videoPath -or [IO.Path]::GetExtension($videoPath).ToLowerInvariant() -ne '.mp4') { continue }
            if (-not (Test-Path -LiteralPath $videoPath -PathType Leaf)) { continue }
            $videoName = [IO.Path]::GetFileName($videoPath)
            $key = $videoName.ToLowerInvariant()
            if ($knownNames.ContainsKey($key)) { continue }
            $knownNames[$key] = $true
            $file = Get-Item -LiteralPath $videoPath
            [pscustomobject]@{
                Name = $file.Name
                BaseName = $file.BaseName
                FullName = $file.FullName
                FilterEnabled = $false
                Intensity = 50
                Saturation = 100
                FilterLabel = 'Lively existing / Off'
            }
        }
        catch {
            Write-Log "Skipped invalid Lively video metadata: $infoPath"
        }
    }
}

function Get-StablePackageName([string]$fileName) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($fileName.ToLowerInvariant())
        $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
        return 'auto-' + $hash.Substring(0, 12)
    }
    finally {
        $sha.Dispose()
    }
}

function Get-InstalledVideoMap {
    $map = @{}
    Get-ChildItem -LiteralPath $libraryRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $infoPath = Join-Path $_.FullName 'LivelyInfo.json'
        if (-not (Test-Path -LiteralPath $infoPath)) { return }
        try {
            $info = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json
            if ([IO.Path]::GetExtension([string]$info.FileName).ToLowerInvariant() -ne '.mp4') { return }
            $name = [IO.Path]::GetFileName([string]$info.FileName)
            $map[$name.ToLowerInvariant()] = $_.FullName
        }
        catch {
            Write-Log "Skipped invalid metadata: $infoPath"
        }
    }
    return $map
}

function Import-NewVideos {
    $installed = Get-InstalledVideoMap
    foreach ($video in Get-ClassifiedVideos) {
        $key = $video.Name.ToLowerInvariant()
        if ($installed.ContainsKey($key)) { continue }

        $packageName = Get-StablePackageName $video.Name
        $packagePath = Join-Path $libraryRoot $packageName
        $hardLinkPath = Join-Path $packagePath $video.Name
        $infoPath = Join-Path $packagePath 'LivelyInfo.json'

        if (-not (Test-Path -LiteralPath $packagePath)) {
            New-Item -ItemType Directory -Path $packagePath | Out-Null
        }
        if (-not (Test-Path -LiteralPath $hardLinkPath)) {
            New-Item -ItemType HardLink -Path $hardLinkPath -Target $video.FullName | Out-Null
        }

        $info = [ordered]@{
            AppVersion = '1.0.0.0'
            Title = $video.BaseName
            Thumbnail = $null
            Preview = $null
            Desc = $null
            Author = $null
            License = $null
            Contact = ''
            Type = 7
            FileName = $video.Name
            Arguments = ''
            IsAbsolutePath = $false
            Id = $null
            Tags = $null
            Version = 0
        }
        $json = $info | ConvertTo-Json -Depth 10
        [IO.File]::WriteAllText($infoPath, $json, (New-Object Text.UTF8Encoding($false)))
        $installed[$key] = $packagePath
        Write-Log "Imported with hard link: $($video.Name)"
    }
    return $installed
}

function Shuffle-Names([string[]]$names) {
    if ($names.Count -le 1) { return @($names) }
    return @($names | Sort-Object { Get-Random })
}

function Load-State {
    if (Test-Path -LiteralPath $statePath) {
        try { return Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json }
        catch { Write-Log 'State was invalid; starting a new shuffle cycle.' }
    }
    return [pscustomobject]@{ Cycle = 0; Current = $null; Order = @(); Index = 0 }
}

function Save-State($state) {
    $json = $state | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($statePath, $json, (New-Object Text.UTF8Encoding($false)))
}

function Get-NextVideo($state, [string[]]$availableNames) {
    # Upgrade state written by older versions of this script.
    if ($null -eq $state.PSObject.Properties['Order']) {
        $state | Add-Member -NotePropertyName Order -NotePropertyValue @()
        $state | Add-Member -NotePropertyName Index -NotePropertyValue 0
    }

    $availableLookup = @{}
    foreach ($name in $availableNames) { $availableLookup[$name.ToLowerInvariant()] = $name }
    $order = @($state.Order | Where-Object { $availableLookup.ContainsKey(([string]$_).ToLowerInvariant()) })
    $index = [Math]::Min([int]$state.Index, $order.Count)

    # Insert newly added videos only into the unplayed part of the prepared order.
    $inOrder = @{}
    foreach ($name in $order) { $inOrder[([string]$name).ToLowerInvariant()] = $true }
    foreach ($name in (Shuffle-Names @($availableNames | Where-Object { -not $inOrder.ContainsKey($_.ToLowerInvariant()) }))) {
        $insertAt = if ($order.Count -le $index) { $order.Count } else { Get-Random -Minimum $index -Maximum ($order.Count + 1) }
        $before = @($order | Select-Object -First $insertAt)
        $after = @($order | Select-Object -Skip $insertAt)
        $order = @($before + $name + $after)
    }

    if ($order.Count -eq 0 -or $index -ge $order.Count) {
        $nextCycle = @(Shuffle-Names $availableNames)
        if ($nextCycle.Count -gt 1 -and $state.Current -and $nextCycle[0] -eq $state.Current) {
            $swap = $nextCycle[0]
            $nextCycle[0] = $nextCycle[1]
            $nextCycle[1] = $swap
        }
        $state.Cycle = [int]$state.Cycle + 1
        $order = $nextCycle
        $index = 0
    }

    $next = [string]$order[$index]
    $state.Order = @($order)
    $state.Index = $index + 1
    $state.Current = $next
    return [pscustomobject]@{ State = $state; Name = $next }
}

function Get-VideoDurationSeconds([string]$path) {
    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.Namespace((Split-Path -LiteralPath $path))
        $item = $folder.ParseName((Split-Path -Leaf $path))
        $length = $folder.GetDetailsOf($item, 27)
        $duration = [TimeSpan]::Zero
        if ([TimeSpan]::TryParse($length, [ref]$duration) -and $duration.TotalSeconds -ge 1) {
            return [int][Math]::Ceiling($duration.TotalSeconds)
        }
    }
    catch {
        Write-Log "Could not read duration for $path"
    }
    return $fallbackDurationSeconds
}

function Get-ScreenSize {
    try {
        $settings = Get-Content -LiteralPath $livelySettingsPath -Raw | ConvertFrom-Json
        $bounds = [string]$settings.SelectedDisplay.Bounds
        $numbers = @([regex]::Matches($bounds, '-?\d+') | ForEach-Object { [int]$_.Value })
        if ($numbers.Count -ge 4 -and $numbers[2] -gt 0 -and $numbers[3] -gt 0) {
            return [pscustomobject]@{ Width = $numbers[2]; Height = $numbers[3] }
        }
    }
    catch {
        Write-Log 'Could not read display size; using 1920x1200.'
    }
    return [pscustomobject]@{ Width = 1920; Height = 1200 }
}

function Get-VideoSize([string]$path) {
    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.Namespace((Split-Path -LiteralPath $path))
        $item = $folder.ParseName((Split-Path -Leaf $path))
        $heightText = $folder.GetDetailsOf($item, 329)
        $widthText = $folder.GetDetailsOf($item, 331)
        $heightMatch = [regex]::Match($heightText, '\d+')
        $widthMatch = [regex]::Match($widthText, '\d+')
        if ($heightMatch.Success -and $widthMatch.Success) {
            return [pscustomobject]@{ Width = [int]$widthMatch.Value; Height = [int]$heightMatch.Value }
        }
    }
    catch {
        Write-Log "Could not read video size for $path"
    }
    return $null
}

function Test-IsHighResolution16By9($video) {
    if (-not $video -or $video.Height -le 0) { return $false }
    if ($video.Width -le $fillThresholdWidth -or $video.Height -le $fillThresholdHeight) { return $false }

    $aspect = $video.Width / $video.Height
    $relativeError = [Math]::Abs($aspect - $sixteenByNineAspect) / $sixteenByNineAspect
    return $relativeError -le $aspectTolerance
}

function Set-SmartScaling([string]$packagePath, [string]$videoPath) {
    $screen = Get-ScreenSize
    $video = Get-VideoSize $videoPath
    # High-resolution 16:9 sources fill the 16:10 display without stretching.
    # Every other aspect/resolution preserves the complete source frame.
    $scalerValue = if (Test-IsHighResolution16By9 $video) {
        3
    }
    elseif ($video -and $video.Width -lt $screen.Width -and $video.Height -lt $screen.Height) {
        0
    }
    else {
        2
    }
    $packageName = Split-Path -Leaf $packagePath
    $propertyFolder = Join-Path (Join-Path $wallpaperDataRoot $packageName) '1'
    $propertyPath = Join-Path $propertyFolder 'LivelyProperties.json'
    if (-not (Test-Path -LiteralPath $propertyFolder)) {
        New-Item -ItemType Directory -Path $propertyFolder -Force | Out-Null
    }
    if (Test-Path -LiteralPath $propertyPath) {
        $properties = Get-Content -LiteralPath $propertyPath -Raw | ConvertFrom-Json
    }
    else {
        $properties = Get-Content -LiteralPath $mpvPropertiesTemplate -Raw | ConvertFrom-Json
    }
    $properties.scaler.value = $scalerValue
    $json = $properties | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($propertyPath, $json, (New-Object Text.UTF8Encoding($false)))
    $mode = if ($scalerValue -eq 0) { 'Native size with letterbox' } elseif ($scalerValue -eq 3) { 'Uniform Fill (high-resolution 16:9)' } else { 'Uniform letterbox' }
    if ($video) {
        Write-Log "Smart scaling: $($video.Width)x$($video.Height) on $($screen.Width)x$($screen.Height) => $mode"
    }
    return $scalerValue
}

if (-not ('ApplicationActivationManager' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;

public enum ActivateOptions { None = 0 }

[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager {
    IntPtr ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, ActivateOptions options, out uint processId);
    IntPtr ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    IntPtr ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
class ApplicationActivationManager { }

public static class PackagedAppLauncher {
    public static uint Activate(string appUserModelId, string arguments) {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        uint processId;
        manager.ActivateApplication(appUserModelId, arguments, ActivateOptions.None, out processId);
        return processId;
    }
}
'@
}

if (-not ('ForegroundWindowInspector' -as [type])) {
    Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class ForegroundWindowInspector {
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    public static string ReadClassName(IntPtr window) {
        var className = new StringBuilder(256);
        GetClassName(window, className, className.Capacity);
        return className.ToString();
    }
}
'@
}

function Get-ForegroundWindowContext {
    $window = [ForegroundWindowInspector]::GetForegroundWindow()
    if ($window -eq [IntPtr]::Zero) {
        return [pscustomobject]@{ ProcessId = 0; ProcessName = 'None'; ClassName = 'None' }
    }

    [uint32]$processId = 0
    [void][ForegroundWindowInspector]::GetWindowThreadProcessId($window, [ref]$processId)
    $processName = 'Unknown'
    if ($processId -gt 0) {
        try { $processName = (Get-Process -Id $processId -ErrorAction Stop).ProcessName }
        catch { }
    }
    return [pscustomobject]@{
        ProcessId = $processId
        ProcessName = $processName
        ClassName = [ForegroundWindowInspector]::ReadClassName($window)
    }
}

function Test-IsDesktopForeground($context) {
    # Only the actual Windows desktop/taskbar is allowed. Explorer folder
    # windows, games, browsers and every other app keep the NVIDIA effect off.
    return @('Progman', 'WorkerW', 'Shell_TrayWnd', 'Shell_SecondaryTrayWnd') -contains [string]$context.ClassName
}

function Invoke-LivelyArguments([string]$arguments) {
    if ($livelyExePath -and (Test-Path -LiteralPath $livelyExePath)) {
        $process = Start-Process -FilePath $livelyExePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if ($process) { [void]$process.WaitForExit(10000) }
        return
    }
    [void][PackagedAppLauncher]::Activate($appUserModelId, $arguments)
}

function Set-LivelyWallpaper([string]$packagePath) {
    Invoke-LivelyArguments ('setwp --file "' + $packagePath + '"')
}

function Set-RunningBrightness([int]$value) {
    Invoke-LivelyArguments ('setprop --property "brightness=' + $value + '"')
}

function Set-NvidiaDynamicVibrance($filter, [int]$processRetryCount = 1) {
    if (-not (Test-Path -LiteralPath $nvidiaControllerPath)) {
        Write-Log "NVIDIA controller is missing: $nvidiaControllerPath"
        return $false
    }

    $livelyMpvProcesses = @()
    for ($attempt = 1; $attempt -le [Math]::Max(1, $processRetryCount); $attempt++) {
        $livelyMpvProcesses = @(Get-Process -Name 'mpv' -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -and $_.Path.IndexOf('LivelyWallpaper', [StringComparison]::OrdinalIgnoreCase) -ge 0
        })
        if ($livelyMpvProcesses.Count -gt 0) { break }
        if ($attempt -lt $processRetryCount) { Start-Sleep -Milliseconds 250 }
    }

    if ($livelyMpvProcesses.Count -eq 0) {
        Write-Log 'No Lively MPV process was found for NVIDIA RTX Dynamic Vibrance.'
        return $false
    }

    $successCount = 0
    foreach ($process in $livelyMpvProcesses) {
        # RTX Dynamic Vibrance is a display-output effect even when NVIDIA
        # exposes a process profile. Never enable it from this service.
        $controllerOutput = & $nvidiaControllerPath --set-persistent $process.Id 0 50 100 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0) {
            $successCount++
        }
        else {
            Write-Log "NVIDIA RTX Dynamic Vibrance failed for MPV $($process.Id): $($controllerOutput.Trim())"
        }
    }

    if ($successCount -eq 0) { return $false }
    Write-Log "NVIDIA RTX Dynamic Vibrance is locked Off for $successCount MPV process(es)."
    return $true
}

$script:dvcSuppressedByForeground = $false

function Update-ForegroundDvcSafety($activeFilter) {
    if (-not $nvidiaDynamicVibranceEnabled) { return $true }
    $context = Get-ForegroundWindowContext
    if (Test-IsDesktopForeground $context) {
        if ($script:dvcSuppressedByForeground -and $activeFilter) {
            [void](Set-NvidiaDynamicVibrance $activeFilter)
            Write-Log "Desktop returned to foreground; wallpaper RTX Dynamic Vibrance restored ($($activeFilter.FilterLabel))."
            $script:dvcSuppressedByForeground = $false
        }
        return $true
    }

    if (-not $script:dvcSuppressedByForeground) {
        if ($nvidiaDynamicVibranceEnabled) {
            [void](Set-NvidiaDynamicVibrance $nvidiaFilterOff)
        }
        Write-Log "Foreground app detected ($($context.ProcessName), $($context.ClassName)); wallpaper RTX Dynamic Vibrance disabled and shuffle paused."
        $script:dvcSuppressedByForeground = $true
    }
    return $false
}

function Wait-ForDesktopForeground($activeFilter) {
    while (-not (Update-ForegroundDvcSafety $activeFilter)) {
        Start-Sleep -Milliseconds $foregroundPollMilliseconds
    }
}

function Open-MpvIpcConnections {
    $connections = @()
    foreach ($pipe in Get-ChildItem -LiteralPath '\\.\pipe\' -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^mpvsocket' }) {
        $client = $null
        $writer = $null
        $reader = $null
        try {
            $client = New-Object IO.Pipes.NamedPipeClientStream('.', $pipe.Name, ([IO.Pipes.PipeDirection]::InOut), ([IO.Pipes.PipeOptions]::None))
            $client.Connect(300)
            $writer = New-Object IO.StreamWriter($client, (New-Object Text.UTF8Encoding($false)), 1024, $true)
            $reader = New-Object IO.StreamReader($client, (New-Object Text.UTF8Encoding($false)), $false, 1024, $true)
            $writer.AutoFlush = $true
            $connections += [pscustomobject]@{ Client = $client; Writer = $writer; Reader = $reader; Name = $pipe.Name }
        }
        catch {
            if ($reader) { $reader.Dispose() }
            if ($writer) { $writer.Dispose() }
            if ($client) { $client.Dispose() }
        }
    }
    return $connections
}

function Close-MpvIpcConnections($connections) {
    foreach ($connection in @($connections)) {
        try { $connection.Reader.Dispose() } catch { }
        try { $connection.Writer.Dispose() } catch { }
        try { $connection.Client.Dispose() } catch { }
    }
}

function Set-MpvBrightness($connections, [int]$value) {
    $message = '{"command":["set_property","brightness",' + $value + ']}'
    foreach ($connection in @($connections)) {
        try {
            $connection.Writer.WriteLine($message)
            [void]$connection.Reader.ReadLine()
        }
        catch { }
    }
}

function Send-MpvCommand($connections, [object[]]$command) {
    $message = @{ command = $command } | ConvertTo-Json -Compress -Depth 10
    foreach ($connection in @($connections)) {
        $connection.Writer.WriteLine($message)
        $responseText = $connection.Reader.ReadLine()
        if ($responseText) {
            $response = $responseText | ConvertFrom-Json
            if ($response.error -and $response.error -ne 'success') {
                throw "MPV IPC command failed: $($response.error)"
            }
        }
    }
}

function Get-MpvSaturationBoost($filter) {
    if (-not $filter -or -not $filter.FilterEnabled) { return 0 }
    # Preserve the user's classified Intensity 50/70/100. MPV's neutral point
    # is 0, so these map to a comfortable local-only boost of +18/+25/+35.
    return [int][Math]::Round(([Math]::Max(0, [Math]::Min(100, [int]$filter.Intensity))) * 0.35)
}

function Set-MpvWallpaperColor($connections, $filter, [bool]$writeLog = $true) {
    $boost = Get-MpvSaturationBoost $filter
    Send-MpvCommand $connections @('set_property', 'saturation', $boost)
    if ($writeLog) {
        Write-Log "MPV wallpaper-only color boost: Intensity $($filter.Intensity)/100, Saturation baseline 100, MPV +$boost. NVIDIA remains Off."
    }
}

function Set-RunningMpvWallpaperColor($filter) {
    $connections = @(Open-MpvIpcConnections)
    if ($connections.Count -eq 0) { return $false }
    try {
        Set-MpvWallpaperColor $connections $filter $true
        return $true
    }
    finally {
        Close-MpvIpcConnections $connections
    }
}

function Invoke-MpvFadeOnConnections($connections, [int]$from, [int]$to, [int]$durationMilliseconds = $fadeHalfDurationMilliseconds) {
    $frameCount = [Math]::Max(2, [int][Math]::Round($durationMilliseconds * $fadeFramesPerSecond / 1000))
    $clock = [Diagnostics.Stopwatch]::StartNew()
    for ($frame = 0; $frame -le $frameCount; $frame++) {
        $t = $frame / $frameCount
        $eased = $t * $t * (3 - 2 * $t)
        $value = [int][Math]::Round($from + (($to - $from) * $eased))
        Set-MpvBrightness $connections $value
        $targetTime = [int][Math]::Round(($frame + 1) * $durationMilliseconds / $frameCount)
        $remaining = $targetTime - $clock.ElapsedMilliseconds
        if ($remaining -gt 0) { Start-Sleep -Milliseconds $remaining }
    }
    Set-MpvBrightness $connections $to
}

function Set-MpvScalingProperties($connections, [string]$videoPath, [int]$scalerValue, [bool]$writeModeLog) {
    # Reset every scaling-related property on every video. This prevents the
    # previous video's Fill/letterbox mode leaking into the newly loaded file.
    Send-MpvCommand $connections @('set_property', 'lavfi-complex', '')
    Send-MpvCommand $connections @('set_property', 'video-aspect-override', 'no')
    Send-MpvCommand $connections @('set_property', 'keepaspect', 'yes')
    # Lively owns a fixed desktop-sized window. Prevent MPV from resizing it when
    # the next file has a different resolution/aspect, which also avoids forcing
    # driver-level filters to attach to a newly configured output surface.
    Send-MpvCommand $connections @('set_property', 'keepaspect-window', 'no')
    Send-MpvCommand $connections @('set_property', 'auto-window-resize', 'no')
    Send-MpvCommand $connections @('set_property', 'video-zoom', 0)

    if ($scalerValue -eq 3) {
        Send-MpvCommand $connections @('set_property', 'video-unscaled', 'no')
        Send-MpvCommand $connections @('set_property', 'panscan', 1)
        if ($writeModeLog) {
            Write-Log 'High-resolution 16:9 Uniform Fill: full screen, native aspect, side crop, no stretch.'
        }
        return
    }

    Send-MpvCommand $connections @('set_property', 'panscan', 0)
    if ($scalerValue -eq 0) {
        Send-MpvCommand $connections @('set_property', 'video-unscaled', 'downscale-big')
        if ($writeModeLog) {
            Write-Log 'Native-size letterbox: no upscale, no crop, no stretch.'
        }
        return
    }

    Send-MpvCommand $connections @('set_property', 'video-unscaled', 'no')
    if ($writeModeLog) {
        $screen = Get-ScreenSize
        $video = Get-VideoSize $videoPath
        $originalGap = 0.0
        if ($video) {
            $videoAspect = $video.Width / $video.Height
            $screenAspect = $screen.Width / $screen.Height
            if ($videoAspect -gt $screenAspect) {
                $fittedHeight = $screen.Width / $videoAspect
                $originalGap = $screen.Height - $fittedHeight
            }
            else {
                $fittedWidth = $screen.Height * $videoAspect
                $originalGap = $screen.Width - $fittedWidth
            }
        }
        Write-Log ('Uniform letterbox: {0:N0}px total gap, zoom 0, no crop, no stretch.' -f $originalGap)
    }
}

function Invoke-SeamlessMpvTransition([string]$videoPath, [int]$scalerValue, $filter) {
    $connections = @()
    for ($attempt = 1; $attempt -le $mpvIpcRetryCount; $attempt++) {
        $connections = @(Open-MpvIpcConnections)
        if ($connections.Count -gt 0) { break }
        if ($attempt -lt $mpvIpcRetryCount) {
            Start-Sleep -Milliseconds $mpvIpcRetryDelayMilliseconds
        }
    }
    if (@($connections).Count -eq 0) { return $false }
    try {
        # Lock the existing output window before loadfile starts reconfiguring the decoder.
        Send-MpvCommand $connections @('set_property', 'keepaspect-window', 'no')
        Send-MpvCommand $connections @('set_property', 'auto-window-resize', 'no')
        # Keep the darkest interval short to avoid a lingering OLED-like ghost
        # impression while still preserving a soft scene transition.
        Invoke-MpvFadeOnConnections $connections 0 $dvcFadeThreshold $dvcVisibleFadeMilliseconds
        Invoke-MpvFadeOnConnections $connections $dvcFadeThreshold -100 $dvcDarkFadeMilliseconds
        # Reuse the current MPV window instead of letting Lively destroy and recreate it.
        Send-MpvCommand $connections @('loadfile', $videoPath, 'replace')
        Send-MpvCommand $connections @('set_property', 'loop-file', 'inf')
        Set-MpvScalingProperties $connections $videoPath $scalerValue $false
        # The process and window already exist; only allow time for the first decoded frame.
        Start-Sleep -Milliseconds $newVideoPrerollMilliseconds
        # Reapply after the first frame/file initialization in case MPV reset a
        # file-scoped property while processing loadfile.
        Set-MpvScalingProperties $connections $videoPath $scalerValue $true
        Set-MpvWallpaperColor $connections $filter $true
        Invoke-MpvFadeOnConnections $connections -100 $dvcFadeThreshold $dvcDarkFadeMilliseconds
        Invoke-MpvFadeOnConnections $connections $dvcFadeThreshold 0 $dvcVisibleFadeMilliseconds
        Write-Log "Seamless MPV load completed using $(@($connections).Count) persistent IPC connection(s)."
        return $true
    }
    finally {
        try { Set-MpvBrightness $connections 0 } catch { }
        Close-MpvIpcConnections $connections
    }
}

function Invoke-SmoothMpvFade([int]$from, [int]$to, [int]$durationMilliseconds = $fadeHalfDurationMilliseconds) {
    $connections = Open-MpvIpcConnections
    if (@($connections).Count -eq 0) {
        Write-Log 'MPV IPC unavailable; using a single brightness change as fallback.'
        Set-RunningBrightness $to
        Start-Sleep -Milliseconds $durationMilliseconds
        return
    }

    try {
        Invoke-MpvFadeOnConnections $connections $from $to $durationMilliseconds
        Write-Log "Smooth MPV fade $from to $to using $(@($connections).Count) IPC connection(s)."
    }
    finally {
        Close-MpvIpcConnections $connections
    }
}

function Set-PackageBrightness([string]$packagePath, [int]$value) {
    $packageName = Split-Path -Leaf $packagePath
    $propertyPath = Join-Path (Join-Path (Join-Path $wallpaperDataRoot $packageName) '1') 'LivelyProperties.json'
    if (-not (Test-Path -LiteralPath $propertyPath)) { return }
    $properties = Get-Content -LiteralPath $propertyPath -Raw | ConvertFrom-Json
    $properties.brightness.value = $value
    $json = $properties | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($propertyPath, $json, (New-Object Text.UTF8Encoding($false)))
}

function Invoke-FadeOut {
    Invoke-SmoothMpvFade 0 $dvcFadeThreshold $dvcVisibleFadeMilliseconds
    Invoke-SmoothMpvFade $dvcFadeThreshold -100 $dvcDarkFadeMilliseconds
}

function Invoke-FadeIn($filter) {
    [void](Set-RunningMpvWallpaperColor $filter)
    Invoke-SmoothMpvFade -100 $dvcFadeThreshold $dvcDarkFadeMilliseconds
    Invoke-SmoothMpvFade $dvcFadeThreshold 0 $dvcVisibleFadeMilliseconds
}

try {
    $controlSettings = Get-WallpaperControlSettings
    if (((Test-Path -LiteralPath $disabledMarkerPath) -or -not $controlSettings.AutoEnabled) -and -not $ConfigureScalingOnly) {
        Write-Log 'Shuffle start ignored because Wallpaper Control is switched off.'
        return
    }
    if ($ConfigureScalingOnly) {
        Write-Log 'Portable setup requested one-time Lively library configuration.'
        $installed = Import-NewVideos
        foreach ($video in Get-ClassifiedVideos) {
            $packagePath = $installed[$video.Name.ToLowerInvariant()]
            Set-SmartScaling $packagePath $video.FullName
        }
        Write-Log 'Smart scaling configured for the complete library.'
        return
    }
    [IO.File]::WriteAllText($servicePidPath, [string]$PID, (New-Object Text.UTF8Encoding($false)))
    Write-Log 'Shuffle service started.'
    $script:nvidiaDynamicVibranceEnabled = $false
    [void](Set-NvidiaDynamicVibrance $nvidiaFilterOff)
    Write-Log 'NVIDIA RTX Dynamic Vibrance is locked Off; MPV wallpaper-only color is active.'
    $activeVideoFilter = $null
    $lastNvidiaSignature = ''
    :serviceLoop while ($true) {
        if (Test-Path -LiteralPath $disabledMarkerPath) {
            Write-Log 'Wallpaper Control switched Auto Wallpaper off; stopping shuffle service.'
            break
        }
        $controlSettings = Get-WallpaperControlSettings
        if (-not $controlSettings.AutoEnabled) { break }
        $script:nvidiaDynamicVibranceEnabled = $false
        Wait-ForDesktopForeground $activeVideoFilter
        $installed = Import-NewVideos
        $classifiedVideos = @(Get-ClassifiedVideos)
        $videoByName = @{}
        foreach ($video in $classifiedVideos) {
            $videoByName[$video.Name.ToLowerInvariant()] = $video
        }
        $available = @($classifiedVideos | Select-Object -ExpandProperty Name)
        if ($available.Count -eq 0) {
            Write-Log 'No videos found; retrying later.'
            if ($Once) { break }
            Start-Sleep -Seconds 30
            continue
        }

        $state = Load-State
        $selection = Get-NextVideo $state $available
        $state = $selection.State

        $name = $selection.Name
        $video = $videoByName[$name.ToLowerInvariant()]
        $effectiveFilter = Get-EffectiveNvidiaFilter $video $controlSettings
        $lastNvidiaSignature = "$($controlSettings.NvidiaMode)|$($effectiveFilter.FilterEnabled)|$($effectiveFilter.Intensity)|$($effectiveFilter.Saturation)"
        $packagePath = $installed[$name.ToLowerInvariant()]
        $videoPath = $video.FullName
        $slotClock = [Diagnostics.Stopwatch]::StartNew()
        $scalerValue = Set-SmartScaling $packagePath $videoPath
        $seamless = Invoke-SeamlessMpvTransition $videoPath $scalerValue $effectiveFilter
        if (-not $seamless) {
            Write-Log 'Persistent MPV IPC unavailable; using safe Lively replacement fallback.'
            Invoke-FadeOut
            Set-PackageBrightness $packagePath -100
            try {
                Set-LivelyWallpaper $packagePath
                Start-Sleep -Milliseconds 900
                if ($nvidiaDynamicVibranceEnabled) {
                    [void](Set-NvidiaDynamicVibrance $nvidiaFilterOff 6)
                }
                Set-RunningBrightness -100
                Start-Sleep -Milliseconds 150
                Invoke-FadeIn $effectiveFilter
            }
            finally {
                try { Set-RunningBrightness 0 } catch { }
                Set-PackageBrightness $packagePath 0
            }
        }
        $activeVideoFilter = $effectiveFilter
        $script:dvcSuppressedByForeground = $false
        Save-State $state
        $remainingInCycle = @($state.Order).Count - [int]$state.Index
        Write-Log "Cycle $($state.Cycle): slot $($state.Index)/$(@($state.Order).Count), $name; $remainingInCycle remain."

        if ($Once) { break }
        $remainingSlotMilliseconds = [Math]::Max(0, ($slotDurationSeconds * 1000) - $slotClock.ElapsedMilliseconds)
        $activeElapsedMilliseconds = 0
        $scanElapsedMilliseconds = 0
        while ($activeElapsedMilliseconds -lt $remainingSlotMilliseconds) {
            if (Test-Path -LiteralPath $disabledMarkerPath) { break serviceLoop }
            $latestControlSettings = Get-WallpaperControlSettings
            if (-not $latestControlSettings.AutoEnabled) { break serviceLoop }
            $latestFilter = Get-EffectiveNvidiaFilter $video $latestControlSettings
            $latestSignature = "$($latestControlSettings.NvidiaMode)|$($latestFilter.FilterEnabled)|$($latestFilter.Intensity)|$($latestFilter.Saturation)"
            if ($latestSignature -ne $lastNvidiaSignature) {
                $script:nvidiaDynamicVibranceEnabled = $false
                $activeVideoFilter = $latestFilter
                [void](Set-NvidiaDynamicVibrance $nvidiaFilterOff)
                $script:dvcSuppressedByForeground = $false
                [void](Set-RunningMpvWallpaperColor $activeVideoFilter)
                Write-Log "Wallpaper Control changed safe MPV color mode to $($latestControlSettings.NvidiaMode). NVIDIA remains Off."
                $lastNvidiaSignature = $latestSignature
            }
            $desktopIsForeground = Update-ForegroundDvcSafety $activeVideoFilter
            $sleepForMilliseconds = [int][Math]::Min($foregroundPollMilliseconds, ($remainingSlotMilliseconds - $activeElapsedMilliseconds))
            Start-Sleep -Milliseconds $sleepForMilliseconds
            if ($desktopIsForeground) {
                $activeElapsedMilliseconds += $sleepForMilliseconds
            }
            $scanElapsedMilliseconds += $sleepForMilliseconds
            if ($scanElapsedMilliseconds -ge ($scanIntervalSeconds * 1000)) {
                [void](Import-NewVideos)
                $scanElapsedMilliseconds = 0
            }
        }
    }
}
catch {
    Write-Log ('Fatal error: ' + $_.Exception.Message)
    throw
}
finally {
    try { [void](Set-NvidiaDynamicVibrance $nvidiaFilterOff) } catch { }
    try { if (Test-Path -LiteralPath $servicePidPath) { Remove-Item -LiteralPath $servicePidPath -Force } } catch { }
    Write-Log 'Shuffle service stopped.'
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
