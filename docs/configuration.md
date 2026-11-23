# 配置说明

配置文件默认位于：

```text
%APPDATA%\QoderHud\config.json
```

应用首次运行或安装器执行时会创建该文件；升级不会覆盖已有配置。托盘菜单可以“打开配置”和“重新加载配置”。

## 完整字段

```json
{
  "display": "auto",
  "position": "auto",
  "scale": 1.0,
  "topmost": true,
  "qoderDataDir": "",
  "jetbrainsLogDir": ""
}
```

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `display` | string | `auto` | `auto`：优先最小的非主显示器，没有副屏时使用主屏；`primary`：主屏；其他值按设备名匹配。 |
| `position` | string | `auto` | `auto`、`center`、`top-left`、`top-right`、`bottom-left`、`bottom-right`。 |
| `scale` | number | `1.0` | 0.5–2.0。工作区小于设计尺寸时会自动缩小，保证完整显示。 |
| `topmost` | bool | `true` | 初始置顶状态；存在 `topmost.txt` 时以保存的置顶按钮状态为准。 |
| `qoderDataDir` | string | 空 | 可选。Qoder 的 `main.sqlite` 所在目录、包含 `com.qoder.app*` 的目录，或 sqlite 文件本身。 |
| `jetbrainsLogDir` | string | 空 | 可选。JetBrains 根目录、包含产品目录的目录，或单个 `idea.log`。 |

字段值非法时会回退到默认值，并在日志中记录布局结果。

## display

匹配顺序：

1. `auto`：在所有非主显示器中选面积最小的；没有非主屏时选主屏。
2. `primary`：始终使用主屏。
3. 其他值：与 Windows 设备名做不区分大小写的完整匹配或后缀匹配。例如 `DISPLAY2` 可以匹配 `\\.\DISPLAY2`。

找不到指定设备时回退到 `auto`，只记录一次警告。

## position

- `auto`：非主屏居中；如果最终落在主屏（没有副屏），放在右上角。
- `center`：工作区居中。
- `top-left` / `top-right` / `bottom-left` / `bottom-right`：距离工作区边缘 20 DIP。

## scale 与 DPI

`scale` 是最大显示比例：

```text
实际比例 = min(scale, 工作区宽 / 960, 工作区高 / 492)
```

- 5 寸 960×492 工作区、`scale=1.0`：窗口正好铺满工作区。
- 同一屏幕、`scale=0.8`：窗口 768×394 居中，四周露出桌面。
- 工作区小于 960×492 时，即使 `scale=1.0` 也会自动缩小。
- 尺寸按 Windows 系统 DPI 换算成 DIP；多显示器混合缩放时，建议把 HUD 所在屏和主屏设为相同缩放比例，或重启 HUD 后让它按新 DPI 重新计算。

## topmost

优先级：

1. `%APPDATA%\QoderHud\topmost.txt`（置顶按钮保存的状态）。
2. 配置中的 `topmost`（首次运行或状态文件不存在时使用）。

要让配置里的 `topmost` 重新生效，先退出 HUD，删除 `topmost.txt`，再启动。

“重新加载配置”会应用显示位置、比例、标题数据目录，但不会覆盖当前的置顶状态。

## 标题数据目录

默认查询：

- Qoder Desktop：`%APPDATA%\com.qoder.app*\main.sqlite`
- JetBrains：`%LOCALAPPDATA%\JetBrains\*\log\idea.log`

路径不标准时设置覆盖：

```json
{
  "qoderDataDir": "D:\\QoderData",
  "jetbrainsLogDir": "D:\\JetBrains"
}
```

`qoderDataDir` 可以是：

- 包含 `main.sqlite` 的目录；
- 包含 `com.qoder.app*` 子目录的目录；
- `main.sqlite` 文件本身。

`jetbrainsLogDir` 可以是：

- JetBrains 根目录（下面有各个产品目录）；
- 包含产品目录的目录；
- 单个 `idea.log` 文件。

查询失败时 HUD 会回退到 prompt、会话名或目录名，不影响运行。

## 示例：5 寸副屏

```json
{
  "display": "DISPLAY2",
  "position": "center",
  "scale": 1.0,
  "topmost": false
}
```

## 示例：主屏右上角

```json
{
  "display": "primary",
  "position": "top-right",
  "scale": 1.0,
  "topmost": true
}
```

## 示例：缩小到 85%

```json
{
  "display": "auto",
  "position": "auto",
  "scale": 0.85,
  "topmost": false
}
```
