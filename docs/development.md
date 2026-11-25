# 开发与发布

## 环境

- Windows 10/11；
- .NET Framework 4.8（Windows 自带 csc/WPF）；
- Node.js 仅用于开发期的 `install.mjs` shim，不是运行时或发布安装的依赖；
- GitHub CLI（`gh`）可选，用于发布 Release。

## 构建

```text
build.cmd
```

产物：

```text
bin\QoderHud.exe
bin\QoderHudHook.exe
```

`build.cmd` 使用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 和同目录的 WPF 引用，不依赖 Visual Studio 或 NuGet。

## 打包

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

`package.ps1` 会：

1. 执行 `build.cmd`；
2. 把可执行文件、安装脚本、README、LICENSE、示例配置和 `docs` 复制到 `release\QoderHud`；
3. 生成 `release\QoderHud-<VERSION>.zip`。

`release/` 和 `bin/` 已加入 `.gitignore`，发布包作为 GitHub Release 附件，不提交进仓库。

## 源码结构

| 文件 | 作用 |
| --- | --- |
| `src/Program.cs` | 入口、单实例互斥、`--screenshot` 参数、全局异常、日志、版本号 |
| `src/HudWindow.cs` | WPF 视觉树、任务状态、详情、滚动、托盘、hooks 管道、演示截图渲染 |
| `src/Model.cs` | `HudTask`、`FeedItem`、状态枚举 |
| `src/Config.cs` | `config.json` 读取、默认值和 Clamp |
| `src/AppPaths.cs` | 配置/日志/置顶状态路径 |
| `src/TitleSource.cs` | Qoder sqlite、JetBrains idea.log 标题查询 |
| `src/JsonProbe.cs` | 轻量 JSON 字段解析 |
| `src/Hook.cs` | stdin JSON、父进程/环境变量识别来源、命名管道客户端 |
| `src/Native.cs` | Win32 P/Invoke |
| `install.ps1` / `install.cmd` | 安装、hooks 合并、自启、迁移 |
| `uninstall.cmd` | 卸载入口 |
| `install.mjs` | 开发用 Node shim，转调 `install.ps1` |

## 架构

1. Qoder 触发 hook，`QoderHudHook.exe` 从 stdin 读取 JSON。
2. Hook 通过父进程链和白名单环境变量判断来源，并注入 `_hud_origin` 等字段。
3. Hook 把 JSON 写入本机命名管道 `QoderHud`。
4. `HudWindow` 的管道线程读取 JSON，在 UI Dispatcher 上更新任务。
5. `TitleSource` 在线程池查询本地标题，缺少时回退。
6. WPF 视觉树只构建一次；脉冲/滚动使用合成线程动画，1 秒定时器只更新变化的文本。

## 开发期安装

```text
node install.mjs
```

等价于调用 `install.ps1`，默认安装到 `%LOCALAPPDATA%\QoderHud`。支持的兼容参数：

- `--autostart` -> `-Autostart`
- `--remove` -> `-Uninstall`
- 环境变量 `QODER_HUD_SETTINGS` -> `-SettingsPath`
- 环境变量 `QODER_HUD_NO_START` -> `-NoStart`

## hooks 合并测试

不要用真实 `settings.json` 做破坏性测试。创建一个临时 settings：

```json
{
  "theme": "dark",
  "hooks": {
    "UserPromptSubmit": [
      { "hooks": [ { "type": "command", "command": "C:/demo/other-hook.exe" } ] },
      { "hooks": [ { "type": "command", "command": "C:/old/QoderHudHook.exe" } ] }
    ],
    "Stop": [
      { "matcher": "*", "hooks": [ { "type": "command", "command": "C:/demo/other-hook.exe" } ] }
    ]
  }
}
```

安装到临时目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 `
  -InstallDir "$env:TEMP\QoderHudTest" `
  -SettingsPath "$env:TEMP\QoderHudTest\settings.json" `
  -NoStart -SkipStartup
```

验证：

- 其他 hooks、`matcher`、`async` 保留；
- 旧 `QoderHudHook.exe` 条目被替换；
- 9 个事件都存在。

卸载并再次验证：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall `
  -InstallDir "$env:TEMP\QoderHudTest" `
  -SettingsPath "$env:TEMP\QoderHudTest\settings.json" -SkipStartup
```

## 配置布局测试

1. 修改 `%APPDATA%\QoderHud\config.json` 的 `scale` 为 `0.8`，重启 HUD，日志应出现 `fit=0.8`，窗口约为 768×394。
2. 设置 `display: primary`、`position: top-right`，重启，日志应出现 `place \\.\DISPLAY1 bounds=...`。
3. 恢复 `display: auto`、`scale: 1`，确认最终回到预期屏幕。

## 渲染检查

启动日志包含：

```text
renderTier=2
```

表示 WPF 正在使用硬件渲染层级。进程应加载 `wpfgfx_v0400.dll`、`d3d9.dll`、`DWMAPI.dll`。如果 HUD 顶部出现异常透明行，确认不透明窗口和 DWM 圆角/边框属性仍然生效。

## 界面截图

README 预览图由内置演示数据生成：

```text
bin\QoderHud.exe --screenshot docs\images\hud.png
bin\QoderHud.exe --screenshot-detail docs\images\hud-detail.png
```

已安装版本可以直接调用：

```text
%LOCALAPPDATA%\QoderHud\QoderHud.exe --screenshot docs\images\hud.png
%LOCALAPPDATA%\QoderHud\QoderHud.exe --screenshot-detail docs\images\hud-detail.png
```

- `--screenshot` 输出主界面；`--screenshot-detail` 打开演示任务详情后输出详情页。
- 路径参数可省略，默认在当前目录生成 `hud-preview.png` 或 `hud-detail-preview.png`。
- 输出为 2 倍图，尺寸 1920×984，PNG。
- 截图模式使用固定的演示任务和静态帧，不读取配置、hooks、会话或标题数据库，也不会连接正在运行的 HUD。
- 渲染完成后进程直接退出，不显示窗口、托盘或菜单位置。

## 发布流程

1. 更新版本号和变更记录：
   - `VERSION`
   - `src/Program.cs` 中的 `AssemblyVersion` / `AssemblyFileVersion` / `Program.Version`
   - `README.md` 的版本
   - `CHANGELOG.md`：把 `[Unreleased]` 的条目归档到 `[<version>] - YYYY-MM-DD`，并保留新的空 `[Unreleased]` 段
2. 运行 `package.ps1`。
3. 校验 ZIP SHA256，并写入 GitHub Release 正文。
4. 初始化/提交 Git：

```text
git init -b main
git add .
git commit -m "Qoder HUD 1.0.2"
git remote add origin https://github.com/<owner>/<repo>.git
git push -u origin main
```

5. 创建 Release。Release 正文从 `CHANGELOG.md` 对应版本段落复制：

```text
gh release create v1.0.2 release/QoderHud-1.0.2.zip --title "Qoder HUD 1.0.2"
```

发布前检查：

- `settings.json` 备份存在；
- 9 个 hooks 指向新路径；
- 临时 settings 的合并/卸载测试通过；
- 顶部像素不透明；
- `renderTier=2`；
- 安装路径无空格或短路径可用；
- 发布 ZIP 包含 `docs`；
- 许可证和版权署名确认。

## 已知限制

- 仅支持 Windows 10/11。
- 界面文案为中文。
- 混合 DPI 缩放环境建议统一缩放比例。
- HUD 不抢焦点，因此滚轮依赖 Windows“滚动非活动窗口”设置。
- 可执行文件未签名，首次运行可能触发 SmartScreen。
- 标题查询依赖 Qoder/JetBrains 的本地数据布局；异常布局需要配置覆盖。
