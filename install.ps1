[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'QoderHud'),
    [string]$SettingsPath = (Join-Path $env:USERPROFILE '.qoder\settings.json'),
    [switch]$NoHooks,
    [switch]$NoStart,
    [switch]$Autostart = $true,
    [switch]$SkipStartup,
    [switch]$Uninstall,
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$AppName = 'Qoder HUD'
$SourceRoot = $PSScriptRoot
$AppDataDir = Join-Path $env:APPDATA 'QoderHud'
$LocalDataDir = Join-Path $env:LOCALAPPDATA 'QoderHud'
$ConfigPath = Join-Path $AppDataDir 'config.json'
$TopMostPath = Join-Path $AppDataDir 'topmost.txt'
$LogPath = Join-Path $LocalDataDir 'hud.log'
$LegacyTopMost = Join-Path $env:USERPROFILE '.qoder\hud\topmost.txt'
$StartupDir = [Environment]::GetFolderPath('Startup')
$ShortcutPath = Join-Path $StartupDir 'Qoder Hud.lnk'
$Events = @('UserPromptSubmit','Stop','SessionEnd','PermissionRequest','PermissionDenied','Notification','PreToolUse','PostToolUse','PostToolUseFailure')
$AsyncEvents = @('PreToolUse','PostToolUse','PostToolUseFailure')

function Info([string]$Message) { Write-Host $Message }
function Fail([string]$Message) { Write-Host ('错误: ' + $Message) -ForegroundColor Red; exit 1 }

function Stop-HudProcesses {
    $procs = Get-Process -Name 'QoderHud','QoderHudHook' -ErrorAction SilentlyContinue
    if ($procs) {
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }
}

function Get-SourceFile([string]$Name) {
    $candidates = @(
        (Join-Path $SourceRoot $Name),
        (Join-Path $SourceRoot ('bin\' + $Name))
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    Fail ('找不到 ' + $Name + '。请从发布包运行安装器，或先执行 build.cmd。')
}

function Get-HookCommand([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -match '\s') {
        try {
            $fso = New-Object -ComObject Scripting.FileSystemObject
            $short = $fso.GetFile($full).ShortPath
            if ($short -and ($short -notmatch '\s')) {
                $full = $short
            } else {
                Write-Warning ('安装路径包含空格，WebStorm 的 hook 可能无法执行: ' + $full)
            }
        } catch {
            Write-Warning ('无法获取短路径，WebStorm 的 hook 可能无法执行: ' + $full)
        }
    }
    return $full.Replace('\', '/')
}

function Read-Settings([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { Fail ('找不到 Qoder 配置: ' + $Path) }
    try {
        $text = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
        return ($text | ConvertFrom-Json)
    } catch {
        Fail ('settings.json 不是合法 JSON: ' + $_.Exception.Message)
    }
}

function Update-Hooks {
    param(
        [string]$Path,
        [string]$Command,
        [switch]$Remove
    )
    $settings = Read-Settings $Path
    if (-not $settings.PSObject.Properties['hooks']) {
        if ($Remove) { return }
        $settings | Add-Member -MemberType NoteProperty -Name hooks -Value (New-Object psobject)
    }
    $hooks = $settings.hooks
    if ($null -eq $hooks) {
        if ($Remove) { return }
        $settings.hooks = New-Object psobject
        $hooks = $settings.hooks
    }

    $sync = $null
    $async = $null
    if (-not $Remove) {
        $sync = [pscustomobject]@{ type = 'command'; command = $Command }
        $async = [pscustomobject]@{ type = 'command'; command = $Command; async = $true }
    }

    foreach ($event in $Events) {
        $existing = @()
        $prop = $hooks.PSObject.Properties[$event]
        if ($prop) { $existing = @($prop.Value) }

        $kept = New-Object System.Collections.ArrayList
        foreach ($entry in $existing) {
            $entryHooks = @()
            if ($entry -and $entry.PSObject.Properties['hooks']) { $entryHooks = @($entry.hooks) }
            $keepHooks = @()
            foreach ($hook in $entryHooks) {
                $cmd = $null
                if ($hook -and $hook.PSObject.Properties['command']) { $cmd = [string]$hook.command }
                if ($cmd -and $cmd -match 'QoderHudHook\.exe') { continue }
                $keepHooks += $hook
            }
            if ($keepHooks.Count -gt 0) {
                $copy = [ordered]@{}
                foreach ($p in $entry.PSObject.Properties) {
                    if ($p.Name -ne 'hooks') { $copy[$p.Name] = $p.Value }
                }
                $copy['hooks'] = @($keepHooks)
                [void]$kept.Add([pscustomobject]$copy)
            } elseif ($entryHooks.Count -eq 0 -and $entry) {
                [void]$kept.Add($entry)
            }
        }

        if (-not $Remove) {
            if ($AsyncEvents -contains $event) {
                [void]$kept.Add([pscustomobject]@{ hooks = @($async) })
            } else {
                [void]$kept.Add([pscustomobject]@{ hooks = @($sync) })
            }
        }

        if ($kept.Count -gt 0) {
            $hooks | Add-Member -Force -MemberType NoteProperty -Name $event -Value @($kept)
        } elseif ($prop) {
            $hooks.PSObject.Properties.Remove($event)
        }
    }

    if ($Remove -and @($hooks.PSObject.Properties).Count -eq 0) {
        $settings.PSObject.Properties.Remove('hooks')
    }

    $json = $settings | ConvertTo-Json -Depth 30
    try { $null = $json | ConvertFrom-Json } catch {
        Fail '生成的 settings.json 不是合法 JSON，已中止（原文件未改动）。'
    }

    $baseBackup = $Path + '.before-hud'
    if (-not (Test-Path -LiteralPath $baseBackup)) {
        Copy-Item -LiteralPath $Path -Destination $baseBackup -Force
    }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    Copy-Item -LiteralPath $Path -Destination ($Path + '.before-hud-' + $stamp) -Force
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
}

function Ensure-Config {
    New-Item -ItemType Directory -Force -Path $AppDataDir | Out-Null
    New-Item -ItemType Directory -Force -Path $LocalDataDir | Out-Null
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        $example = Join-Path $SourceRoot 'config.example.json'
        if (Test-Path -LiteralPath $example) {
            Copy-Item -LiteralPath $example -Destination $ConfigPath -Force
        } else {
            $template = "{`r`n  `"display`": `"auto`",`r`n  `"position`": `"auto`",`r`n  `"scale`": 1.0,`r`n  `"topmost`": true,`r`n  `"qoderDataDir`": `"`",`r`n  `"jetbrainsLogDir`": `"`"`r`n}`r`n"
            [IO.File]::WriteAllText($ConfigPath, $template, (New-Object System.Text.UTF8Encoding($false)))
        }
        Info ('已创建配置: ' + $ConfigPath)
    }
    if ((Test-Path -LiteralPath $LegacyTopMost) -and -not (Test-Path -LiteralPath $TopMostPath)) {
        Copy-Item -LiteralPath $LegacyTopMost -Destination $TopMostPath -Force
    }
}

function Set-StartupShortcut([string]$Target) {
    if ($SkipStartup) { return }
    if ($Autostart) {
        try {
            $ws = New-Object -ComObject WScript.Shell
            $sc = $ws.CreateShortcut($ShortcutPath)
            $sc.TargetPath = $Target
            $sc.WorkingDirectory = $InstallDir
            $sc.Description = $AppName
            $sc.Save()
            Info ('已设置开机自启: ' + $ShortcutPath)
        } catch {
            Write-Warning ('设置开机自启失败: ' + $_.Exception.Message)
        }
        return
    }
    if (Test-Path -LiteralPath $ShortcutPath) {
        try {
            $ws = New-Object -ComObject WScript.Shell
            $sc = $ws.CreateShortcut($ShortcutPath)
            if ([string]$sc.TargetPath -match 'QoderHud\.exe') {
                Remove-Item -LiteralPath $ShortcutPath -Force
                Info '已移除开机自启快捷方式（默认启用自启；如需关闭请加 -Autostart:$false）'
            }
        } catch {
        }
    }
}

if ($Uninstall) {
    Stop-HudProcesses
    if (Test-Path -LiteralPath $SettingsPath) {
        try {
            Update-Hooks -Path $SettingsPath -Remove
            Info '已移除 Qoder hooks。'
        } catch {
            Write-Warning ('移除 hooks 失败: ' + $_.Exception.Message)
        }
    }
    if (-not $SkipStartup -and (Test-Path -LiteralPath $ShortcutPath)) {
        Remove-Item -LiteralPath $ShortcutPath -Force
        Info '已移除开机自启快捷方式。'
    }
    $targetHud = Join-Path $InstallDir 'QoderHud.exe'
    $targetHook = Join-Path $InstallDir 'QoderHudHook.exe'
    if (Test-Path -LiteralPath $targetHud) { Remove-Item -LiteralPath $targetHud -Force }
    if (Test-Path -LiteralPath $targetHook) { Remove-Item -LiteralPath $targetHook -Force }
    if ((Test-Path -LiteralPath $InstallDir) -and -not (Get-ChildItem -LiteralPath $InstallDir -Force)) {
        Remove-Item -LiteralPath $InstallDir -Force
    }
    if ($RemoveData) {
        if (Test-Path -LiteralPath $AppDataDir) { Remove-Item -LiteralPath $AppDataDir -Recurse -Force }
        if (Test-Path -LiteralPath $LocalDataDir) { Remove-Item -LiteralPath $LocalDataDir -Recurse -Force }
        Info '已删除配置、置顶状态和日志。'
    }
    Info ('卸载完成。旧的 ~/.qoder/hud 目录如存在可手动删除。')
    return
}

$srcHud = Get-SourceFile 'QoderHud.exe'
$srcHook = Get-SourceFile 'QoderHudHook.exe'
$targetHud = Join-Path $InstallDir 'QoderHud.exe'
$targetHook = Join-Path $InstallDir 'QoderHudHook.exe'

Stop-HudProcesses
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
if ([IO.Path]::GetFullPath($srcHud) -ne [IO.Path]::GetFullPath($targetHud)) {
    Copy-Item -LiteralPath $srcHud -Destination $targetHud -Force
}
if ([IO.Path]::GetFullPath($srcHook) -ne [IO.Path]::GetFullPath($targetHook)) {
    Copy-Item -LiteralPath $srcHook -Destination $targetHook -Force
}
Ensure-Config

if (-not $NoHooks) {
    if (Test-Path -LiteralPath $SettingsPath) {
        $command = Get-HookCommand $targetHook
        Update-Hooks -Path $SettingsPath -Command $command
        Info ('已写入 Qoder hooks: ' + $SettingsPath)
        Info '如果 Qoder 正在运行，请重启它或新开一个会话以加载新 hooks。'
    } else {
        Write-Warning ('未找到 Qoder 配置 ' + $SettingsPath + '，HUD 将以独立模式安装（不写 hooks）。')
    }
}

Set-StartupShortcut $targetHud

if (-not $NoStart) {
    Start-Process -FilePath $targetHud -WorkingDirectory $InstallDir | Out-Null
    Info '已启动 Qoder HUD。'
}

Info ''
Info ('安装目录: ' + $InstallDir)
Info ('配置文件: ' + $ConfigPath)
Info ('日志文件: ' + $LogPath)
Info ('卸载: 运行 uninstall.cmd，或 install.cmd -Uninstall')
