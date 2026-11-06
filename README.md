# Tern 简体中文（本地版）0.5.1（适配 Tern 0.9.12）

## 截图

| 常规设置 | 外观设置 |
| :---: | :---: |
| ![常规设置](assets/screenshots/settings-general-zh.png) | ![外观设置](assets/screenshots/settings-appearance-zh.png) |
| 原生视图 | 快捷键 |
| ![原生视图](assets/screenshots/settings-lenses-zh.png) | ![快捷键](assets/screenshots/settings-keyboard-zh.png) |
| 智能体 | 插件 |
| ![智能体](assets/screenshots/settings-agents-zh.png) | ![插件](assets/screenshots/settings-plugins-zh.png) |

命令面板：

![命令面板中文显示](assets/screenshots/command-palette-zh.png)

OMP 原生设置页（需安装 [omp-di18n 汉化扩展](https://github.com/losewayy/omp-di18n)，部分说明仍为英文）：

| 模型设置 | 文件设置 |
| :---: | :---: |
| ![OMP 模型设置](assets/screenshots/omp-settings-model-zh.png) | ![OMP 文件设置](assets/screenshots/omp-settings-files-zh.png) |

## 使用方法

双击桌面的「Tern 中文版」启动。启动器会在 Tern 窗口存活期间监听设置页的标签切换，收到事件时通过 Tern 官方 `ctl css` 接口换上对应页面的汉化样式，不做定时检查，也不读取聊天、终端内容或密钥。

已覆盖：命令面板，以及全部 14 个设置分类（常规、智能体、外观、终端、原生视图、数据库、笔记本、Carly、键盘、Git、插件、会话、主机、高级）的标题、说明文字与主要控件。

Tern 原程序、OMP 和设置值均未修改。

## Tern 更新后的兼容性检查

设置页按元素位置翻译，Tern 增删选项可能造成错译。中文启动器识别到非 0.9.12 的版本时，会通过 `tools/check_tern_settings.py` 在独立的无界面 Tern 中对照 14 个设置页的标签和说明；该检查需要 PATH 中有 Python。检查失败、无法启动或检测到内容变化时，会提示并停止加载设置页专用翻译，保留原文；基础界面样式和中文命令仍会加载，仍需单独核验。

手动检查：`python tools/check_tern_settings.py --exe "C:\Users\<用户名>\AppData\Local\Programs\Tern\tern.com"`。基准记录只包含测试窗口里的设置标签与说明，不包含用户会话或配置值。

0.9.12 已对照 0.9.9 的全部设置分类，修正高级页面新增项目造成的错位、键盘页面新增命令造成的错位，以及常规页面的名称变化。页面翻译覆盖仍随 Tern 变化；内层元素与动态选项不在自动检查的全部覆盖范围内。

## 更新 Tern 之后中文消失

Tern 自动更新后会自己重启窗口，这个过程不经过中文启动器，所以桥接进程不会启动，界面会变回英文。**重新双击桌面快捷方式即可恢复**，插件和样式都没有丢。

如果双击后仍然是英文，看 `%TEMP%\tern-zh-launch.log` 里的报错，通常是找不到 `tern.exe`：把它的完整路径写进 `%LOCALAPPDATA%\TernZhPlus\tern-path.txt` 即可。

## 文件

- `base.css`：公共界面标签翻译
- `prefs-*.css`：各设置分类的汉化样式（仅在对应页面加载）
- `window.luau`：中文命令注册，无定时器
- `TernChineseBridge.cs`：Windows UI Automation 事件监听，负责切换页面样式
- `TernCommandLabels.cs`：命令面板标签替换
- `ui-labels.tsv`：命令面板英文到中文的对照表
- `启动中文 Tern.ps1`：启动窗口并在其存活期间监听标签事件
- `启动中文 Tern.cmd`：命令行入口
- `设置快捷方式.ps1`：创建或恢复桌面快捷方式

## 日志与还原

事件监听日志在 `%TEMP%\tern-zh-bridge-<pid>.log`，启动错误在 `%TEMP%\tern-zh-launch.log`。

卸载时关闭 Tern 中文版，删除 `tern-zh-plus` 插件目录及桌面快捷方式即可。

## 已知限制

翻译靠 CSS 隐藏英文原文、再用 `::before` 显示中文，因此依赖 Tern 的类名和页面结构。Tern 改版后个别页面可能失效，需要重新对照新版界面调整选择器。
