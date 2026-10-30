# Tern 简体中文

Tern 的非官方简体中文插件，基于原生 Luau 插件与 CSS 样式。Windows 版本提供设置页面自动切换及命令面板文字汉化。

**兼容环境：Windows · Tern 0.9.8**

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

## 功能

- 翻译设置界面的固定标题、选项名称以及部分说明文字。
- 覆盖常规、智能体、外观、终端、原生视图、数据库、笔记本、Carly、快捷键、Git、插件、会话、主机和高级设置。
- 添加常用操作的中文命令入口，可通过命令面板搜索并执行。
- Windows 启动器通过 UI Automation 事件切换页面样式，并尝试汉化命令面板当前显示的内置命令。

汉化只影响界面显示，不修改 Tern 的命令、快捷键或设置值。

## 安装

1. 安装 [Tern](https://stencil.so/tern)。
2. 下载本仓库源码，将文件放到以下目录（不存在时自行创建）：

   ```text
   %APPDATA%\Tern\plugins\tern-zh-plus\
   ```

   确认 `plugin.toml`、`window.luau`、`TernChineseBridge.cs` 等文件直接位于该目录下，而不是多嵌套了一层文件夹。

3. 在 Tern 中执行 `tern plugin reload`。
4. 双击插件目录中的 **`启动中文 Tern.cmd`** 启动 Tern。

若启动器找不到 `tern.exe`，可以设置环境变量 `TERN_EXE`，或在以下文件中填写其完整路径：

```text
%LOCALAPPDATA%\TernZhPlus\tern-path.txt
```

路径文件只保留在本机，不要放进仓库。

### 使用原来的 Tern 快捷方式

安装后可运行 `设置快捷方式.ps1`，将桌面和开始菜单中指向 `tern.exe` 的快捷方式改为使用中文版启动器。脚本会先备份原快捷方式，不会设置开机自启。

## 使用说明

设置页面汉化依赖 Windows 启动器。关闭 Tern 后，对应的事件监听进程也会退出。

直接运行原版 `tern.exe` 时，插件提供的基础界面文字和中文命令入口仍然可用，但无法自动切换不同设置分类的翻译样式。

## 已知限制

- 仅针对 Tern 0.9.8 进行适配。新版本可能需要调整 CSS 选择器。
- 命令面板保留 Tern 原有的英文命令数据；中文别名支持搜索，内置命令的原位中文显示取决于 Windows UI Automation 事件，部分情况下仍会显示英文。
- 右键菜单、部分弹窗、输入框占位符及动态提示尚未完整汉化。
- macOS 和 Linux 暂不支持 Windows 事件桥接功能。

## 项目结构

- `window.luau`、`extra_commands.luau`：中文命令入口
- `base.css`、`prefs-*.css`：界面文字样式
- `ui-labels.tsv`：内置命令名称的中英文对照
- `TernChineseBridge.cs`：设置分类切换监听
- `TernCommandLabels.cs`：命令面板可见条目的中文样式更新
- `启动中文 Tern.ps1`、`启动中文 Tern.cmd`：Windows 启动器
- `设置快捷方式.ps1`：可选的快捷方式配置工具

## 许可证

[MIT License](LICENSE)

本项目与 Tern、Stencil 或 OMP 官方无隶属关系；仓库不包含其闭源程序。
