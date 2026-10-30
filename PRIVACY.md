# 隐私与安全

这个仓库仅发布 Luau、CSS、C#、PowerShell 插件源码及经过检查和裁剪的界面演示截图。不包含 Tern 安装包、OMP、聊天记录、个人配置或 Windows 快捷方式。

- UI Automation 监听器处理 Tern 设置分类选择事件，以及命令面板搜索值和列表选中事件。
- 汉化样式通过本机 `tern ctl css` 接口切换，命令行工具本身不会访问外部翻译服务。
- 监听器不注册为 Windows 服务或开机启动项，随 Tern 窗口退出。
- 本机 tern.exe 路径应保存在 %LOCALAPPDATA%\TernZhPlus\tern-path.txt，千万不要提交到 Git。
- 调试日志位于本地 %TEMP%，其中可能包含 PID、时间和错误路径；请不要上传日志。
- 请不要提交 settings.json、.env、密钥、终端会话、未经检查的屏幕截图或任何 *.lnk 文件。只有 assets/screenshots/ 里经过检查和裁剪的界面示例允许提交，发布前应检查图片是否包含私人路径或账号信息。
- Git 提交记录可能公开作者姓名和邮箱。正式发布前请使用你愿意公开的昵称及邮箱（例如 GitHub 提供的 noreply 邮箱），并检查仓库历史记录。

项目不提供遥测或远程上传功能。请仅从可信来源获取并审查脚本后运行。
