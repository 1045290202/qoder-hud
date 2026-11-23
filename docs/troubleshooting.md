# 故障排查

## 先看日志

日志默认在：

```text
%LOCALAPPDATA%\QoderHud\hud.log
```

托盘菜单“打开日志”可以直接打开。日志包含启动、位置、hooks 来源、任务状态、标题查询和布局结果，不包含完整对话内容。

## HUD 一直空闲

1. 重启 Qoder 或新开一个会话，hooks 才会重新加载。
2. 检查 `%USERPROFILE%\.qoder\settings.json` 是否存在 9 个包含 `QoderHudHook.exe` 的条目。
3. 确认 QoderHud 进程正在运行。
4. 查看日志是否有 “new task” 或 “state” 记录。

如果 HUD 能独立启动但收不到事件，通常是 hooks 路径不对或 Qoder 缓存了旧 hooks。重新运行 `install.cmd`，然后重启 Qoder。

## 有任务但没有标题

标题来自本地数据：

- Qoder Desktop：`%APPDATA%\com.qoder.app*\main.sqlite`
- JetBrains：`%LOCALAPPDATA%\JetBrains\*\log\idea.log`

如果 Qoder 的数据目录或 JetBrains 日志目录不标准，在配置中设置：

```json
{
  "qoderDataDir": "D:\\QoderData",
  "jetbrainsLogDir": "D:\\JetBrains"
}
```

标题查询失败时会回退到 prompt、会话名或目录名，这是正常的降级行为。

## 滚轮不能滚动

HUD 不抢焦点，滚轮依赖 Windows 的“滚动非活动窗口”：

- 设置 → 蓝牙和其他设备 → 鼠标 → 打开“当我悬停在非活动窗口上时滚动它们”。
- 或把注册表 `HKCU\Control Panel\Desktop\MouseWheelRouting` 设为 `2`，然后重新登录。

如果仍无效，先用托盘“显示/隐藏”确认 HUD 没有被其他窗口覆盖。

## HUD 不在预期的屏幕上

检查配置：

```json
{
  "display": "auto",
  "position": "auto",
  "scale": 1.0
}
```

- `auto` 选最小的非主显示器。
- 想在主屏显示时用 `primary`。
- 想指定副屏时用设备名后缀，如 `DISPLAY2`。
- 在托盘“重新加载配置”后应用当前位置/比例。

日志中的 `place \\.\DISPLAYx bounds=...` 会显示最终结果。找不到指定设备时会回退到 `auto`。

## HUD 太大或太小

调低/调高 `scale`（0.5–2.0），或使用 `scale: auto` 的默认逻辑：工作区不足时自动缩小。混合 DPI 环境建议把 HUD 屏与主屏设为相同缩放比例，或重启 HUD 让它重新计算。

## 置顶按钮状态不对

置顶状态保存在 `%APPDATA%\QoderHud\topmost.txt`。要恢复配置中的 `topmost` 初始值：

1. 退出 HUD。
2. 删除 `topmost.txt`。
3. 重启 HUD。

## 安装/卸载被 PowerShell 拦截

使用 `install.cmd` / `uninstall.cmd`，它们已经带 `-ExecutionPolicy Bypass`。不要直接双击 `.ps1`。

## 安装路径包含空格

WebStorm 的 hook runner 对带引号的 Windows 路径兼容性较差。安装器会尝试 8.3 短路径；如果失败会警告。建议用不含空格的 `-InstallDir`。

## 旧版本残留

旧版本可能在 `%USERPROFILE%\.qoder\hud`。新安装器不会删除它。重启 Qoder 确认新 hooks 正常后，可以手动删除该目录。旧的 `hud.log` 和 `topmost.txt` 不会自动迁移日志内容。

## 卸载后仍有 hooks

卸载器只移除包含 `QoderHudHook.exe` 的条目，并保留其他 hooks。如果仍有残留：

1. 检查 `%USERPROFILE%\.qoder\settings.json` 中是否还有 HUD 条目。
2. 使用 `-SettingsPath` 指向实际使用的 Qoder 配置。
3. 如果 Qoder 正在运行，重启后再检查。

## 联系支持

报告问题时请附上：

- Windows 版本；
- Qoder 版本和来源（Desktop / JetBrains / CLI）；
- `hud.log` 中相关时间段；
- 复现步骤和截图。

不要在公开 issue 中粘贴 token、私有路径或完整对话内容。
