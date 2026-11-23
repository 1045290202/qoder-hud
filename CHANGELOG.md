# Changelog

本项目的所有重要变更都记录在这里。后续变更先写入 `[Unreleased]`，发布时归档到对应版本。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循语义化版本。

## [Unreleased]

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
