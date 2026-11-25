# 安装与卸载

## 快速安装

1. 解压发布包。
2. 双击 `install.cmd`。安装器默认创建或更新开机自启快捷方式，并立即启动 HUD。
3. 如果 Qoder 已经在运行，重启 Qoder 或新开一个会话，让它加载新的 hooks。

不需要开机自启时运行：

```text
install.cmd -Autostart:$false
```

只想跳过自启配置、保留当前启动项状态时运行：

```text
install.cmd -SkipStartup
```

安装器不需要管理员权限，默认使用 Windows 自带的 PowerShell。

## 默认路径

| 内容 | 路径 |
| --- | --- |
| 程序 | `%LOCALAPPDATA%\QoderHud` |
| 配置 | `%APPDATA%\QoderHud\config.json` |
| 置顶状态 | `%APPDATA%\QoderHud\topmost.txt` |
| 日志 | `%LOCALAPPDATA%\QoderHud\hud.log` |
| Qoder hooks | `%USERPROFILE%\.qoder\settings.json` |
| 开机自启 | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Qoder Hud.lnk` |

## 安装器参数

```text
install.cmd [-Autostart] [-NoHooks] [-NoStart] [-SkipStartup]
             [-InstallDir <目录>] [-SettingsPath <settings.json>]
```

- `-Autostart`：默认启用，创建/更新开机自启快捷方式；如果旧快捷方式指向旧路径，会一并更新。传 `-Autostart:$false` 可移除已有快捷方式。
- `-NoHooks`：只安装 HUD，不修改 Qoder 设置。
- `-NoStart`：安装后不启动 HUD。
- `-SkipStartup`：不触碰开机自启快捷方式，主要用于测试。
- `-InstallDir`：自定义程序安装目录。
- `-SettingsPath`：自定义 Qoder 配置路径。

也可以直接调用 PowerShell：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

## Hooks 行为

安装器会：

1. 读取 `%USERPROFILE%\.qoder\settings.json`。
2. 备份为 `.before-hud`（首次）和 `.before-hud-<时间戳>`（每次安装）。
3. 移除所有包含 `QoderHudHook.exe` 的旧条目。
4. 写入 9 个事件：

| 事件 | 模式 |
| --- | --- |
| `UserPromptSubmit` | 同步 |
| `Stop` | 同步 |
| `SessionEnd` | 同步 |
| `PermissionRequest` | 同步 |
| `PermissionDenied` | 同步 |
| `Notification` | 同步 |
| `PreToolUse` | async |
| `PostToolUse` | async |
| `PostToolUseFailure` | async |

其他 hooks、`matcher` 和同一事件里的其他命令都会保留。安装器还会把旧版 `.qoder\hud\topmost.txt` 迁移到新的状态目录。

如果安装路径包含空格，安装器会尝试使用 8.3 短路径，避免 WebStorm hook runner 的引号兼容问题；如果短路径不可用会给出警告，此时建议换到不含空格的目录。

## 旧版本迁移

旧版本可能安装在 `%USERPROFILE%\.qoder\hud`。新安装器不会删除该目录：

- 旧 hooks 会被替换为新路径。
- 旧的 `topmost.txt` 会自动迁移。
- 旧的 `hud.log` 不会迁移，新的日志在 `%LOCALAPPDATA%\QoderHud\hud.log`。
- 重启 Qoder 后确认事件正常，再手动删除旧目录即可。

## 卸载

```text
uninstall.cmd
```

卸载器会：

- 停止 `QoderHud` / `QoderHudHook` 进程。
- 从 Qoder 设置中移除本 HUD 的 hooks，保留其他 hooks。
- 删除开机自启快捷方式。
- 删除安装目录中的程序文件；目录为空时删除目录。

需要同时删除配置、置顶状态和日志：

```text
uninstall.cmd -RemoveData
```

自定义安装目录或 Qoder 配置路径时，把对应参数一起传给 `uninstall.cmd`。

## 常见安装问题

**PowerShell 被执行策略拦截**

使用 `install.cmd`，它已经带 `-ExecutionPolicy Bypass`。不要直接双击 `.ps1`。

**提示找不到 Qoder 配置**

默认期望 `%USERPROFILE%\.qoder\settings.json`。Qoder 未安装或使用自定义路径时：

```text
install.cmd -NoHooks
install.cmd -SettingsPath "D:\path\to\settings.json"
```

**安装后 HUD 能显示但没有任务**

通常是 Qoder 还在使用旧的 hooks 缓存。重启 Qoder 或新开会话；也可以查看日志和 `settings.json` 中是否存在 9 个 `QoderHudHook.exe` 条目。

**旧 HUD 还在运行**

安装器会按进程名停止 `QoderHud`。如果旧版本来自不同的可执行文件或被计划任务启动，请先手动退出旧版本再安装。
