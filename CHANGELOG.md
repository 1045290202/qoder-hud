# Changelog

本项目的所有重要变更都记录在这里。后续变更先写入 `[Unreleased]`，发布时归档到对应版本。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循语义化版本。

## [Unreleased]

## [1.0.3] - 2026-11-25

### Changed

- 安装器默认创建或更新开机自启快捷方式，不再需要 `-Autostart`；可用 `-Autostart:$false` 关闭，或 `-SkipStartup` 保留当前状态。

## [1.0.2] - 2026-11-25

### Changed

- “前往 Qoder”仅在 Desktop 任务下执行；JetBrains 与 CLI 暂不支持自动跳转。
- 找不到目标或来源不支持时，改用 Windows 托盘气球提示，不再弹出阻塞操作的模态窗口。

### Fixed

- 双击、右键菜单和详情按钮现在使用当前任务来源，不再统一聚焦 Qoder。

## [1.0.1] - 2026-11-24

### Fixed

- 手动停止任务后 HUD 一直显示运行中。现在会读取 Qoder 数据库或 JetBrains `idea.log` 的中断状态，自动切换为“已停止”。

### Changed

- 已停止任务使用灰色卡片和独立状态文案，顶部统计同步区分已完成与已停止。

## [1.0.0] - 2026-11-23

首个公开发布版本。

### Added

- Windows 副屏/小屏任务仪表盘，WPF 保留模式 + GPU 合成。
- 识别 Qoder Desktop、JetBrains 插件和 qodercli 来源。
- 任务标题查询：Qoder sqlite 与 JetBrains idea.log，失败时回退到 prompt 或目录名。
- 运行 / 等待 / 完成三态任务卡、任务详情页、200ms 缓出滚动。
- 单击详情、双击切换 Qoder、右键菜单、置顶开关、托盘控制。
- 自适应工作区大小，支持显示器、位置和缩放配置。
- `--screenshot` / `--screenshot-detail` 演示截图模式，生成不含本机任务的主界面和任务详情预览。
- PowerShell 安装/卸载器、hooks 合并、自启和旧路径迁移。
