# 隐私说明

Qoder HUD 是本机工具，不包含网络请求、遥测或账号登录。

## 数据流

```text
Qoder hooks
  -> QoderHudHook.exe (读取 stdin JSON)
  -> 本机命名管道 QoderHud
  -> QoderHud.exe (WPF 界面)
```

管道只在当前 Windows 用户会话内使用，不经过网络。

## Hook 采集的内容

`QoderHudHook.exe` 从 Qoder 传入的 JSON 中读取：

- 会话 ID、事件类型、模型、工作目录；
- 工具名和工具参数摘要；
- prompt、最后一条助手消息（只用于界面显示和标题降级）；
- 父进程链；
- 白名单环境变量。

环境变量白名单：

```text
QODER_HOOK_SOURCE, QODER_CLIENT_TYPE, QODER_BUSINESS_PRODUCT,
QODER_SESSION_TYPE, QODER_CLIENT_VERSION, QODER_HOOK_VERSION,
QODER_PRODUCT_ID, QODER_IDE, JETBRAINS_IDE, TERMINAL_EMULATOR,
IDEA_*, TERM_PROGRAM
```

包含 TOKEN / SECRET / PASSWORD / AUTH / KEY / COOKIE / CREDENTIAL 的变量会被显式排除。

## 本地保存的内容

- 配置：`%APPDATA%\QoderHud\config.json`
- 置顶状态：`%APPDATA%\QoderHud\topmost.txt`
- 日志：`%LOCALAPPDATA%\QoderHud\hud.log`
- Qoder 设置备份：`%USERPROFILE%\.qoder\settings.json.before-hud*`

日志可能包含任务标题、会话 ID 缩写、来源、工作目录和工具摘要。日志超过 512KB 会重建。

## 标题查询

标题查询只读取本机：

- Qoder Desktop：`%APPDATA%\com.qoder.app*\main.sqlite`
- JetBrains：`%LOCALAPPDATA%\JetBrains\*\log\idea.log`

不会上传数据库或日志。

## 不采集/不发送

- 不上传任何数据；
- 不读取浏览器、剪贴板、键盘输入；
- 不读取 Qoder 登录凭据；
- 不包含广告、遥测或自动更新网络请求。

## 安全提示

- 发布包是未签名的可执行文件，Windows SmartScreen 可能提示；请从可信的 Release 下载并校验 SHA256。
- Hook 在 Qoder 用户权限下运行；安装器只修改 `settings.json` 中的 HUD 条目，并保留备份。
- 如果需要彻底清理，运行 `uninstall.cmd -RemoveData`，然后手动删除 `%USERPROFILE%\.qoder\hud`（旧版本目录）。

## 报告问题

在公开 issue 中请使用占位符替换用户名、内网路径、会话 ID 和任何敏感内容。
