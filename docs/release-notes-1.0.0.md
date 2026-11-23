# Qoder HUD 1.0.0

首个公开发布版本。

## 亮点

- Windows 副屏/小屏任务仪表盘。
- 识别 Qoder Desktop、JetBrains 插件和 qodercli 来源。
- 本地标题查询：Qoder sqlite 与 JetBrains idea.log，失败自动回退。
- 单击详情、双击切换 Qoder、右键菜单、置顶开关、托盘控制。
- 动画滚动列表，运行/等待/完成三种状态。
- WPF 保留模式 + GPU 合成；静态画面不整帧重绘。
- 自适应工作区、缩放和显示器选择。
- PowerShell 安装/卸载器，无需 Node.js，无需管理员权限。

## 安装

1. 解压 `QoderHud-1.0.0.zip`。
2. 运行 `install.cmd`；需要开机自启时运行 `install.cmd -Autostart`。
3. 重启 Qoder 或新开一个会话加载 hooks。

默认安装到 `%LOCALAPPDATA%\QoderHud`。详细说明见 `docs/install.md`。

## 系统要求

- Windows 10 / 11；
- .NET Framework 4.8（Windows 自带）；
- Qoder Desktop、Qoder JetBrains 插件或 qodercli（用于事件）。

## 已知限制

- 界面为中文；
- 混合 DPI 缩放环境建议统一缩放比例；
- 滚轮需要 Windows“滚动非活动窗口”设置；
- 可执行文件未签名，可能触发 SmartScreen；
- 标题依赖 Qoder/JetBrains 本地数据布局，可用配置覆盖。

## 许可证

MIT。见 `LICENSE`。
