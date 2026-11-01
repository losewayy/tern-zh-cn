# Tern 简体中文（本地版）0.5.0（适配 Tern 0.9.9）

## 使用方法

双击桌面的「Tern 中文版」启动。启动器会在 Tern 窗口存活期间监听设置页的标签切换，收到事件时通过 Tern 官方 `ctl css` 接口换上对应页面的汉化样式，不做定时检查，也不读取聊天、终端内容或密钥。

已覆盖：命令面板，以及全部 14 个设置分类（常规、智能体、外观、终端、原生视图、数据库、笔记本、Carly、键盘、Git、插件、会话、主机、高级）的标题、说明文字与主要控件。

Tern 原程序、OMP 和设置值均未修改。

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
