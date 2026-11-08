-- Tern Chinese aliases and per-window settings localization.

local aliases = {
    { "新建标签页", "标签页", "new_tab" },
    { "关闭标签页", "标签页", "close_tab" },
    { "下一个标签页", "标签页", "next_tab" },
    { "上一个标签页", "标签页", "previous_tab" },
    { "重命名标签页", "标签页", "rename_tab" },
    { "标签页向左移", "标签页", "move_tab_left" },
    { "标签页向右移", "标签页", "move_tab_right" },
    { "切换会话", "会话", "switch_session" },
    { "新建会话", "会话", "new_session" },
    { "重命名会话", "会话", "rename_session" },
    { "锁定会话", "会话", "lock_session" },
    { "关闭会话", "会话", "close_session" },
    { "向右分屏", "分屏", "split_right" },
    { "向下分屏", "分屏", "split_down" },
    { "关闭分屏", "分屏", "close_pane" },
    { "最大化区块", "分屏", "zoom" },
    { "均分区块", "分屏", "equalize" },
    { "下一个区块", "分屏", "next_pane" },
    { "上一个区块", "分屏", "previous_pane" },
    { "区块向左移动", "分屏", "move_pane:left" },
    { "区块向右移动", "分屏", "move_pane:right" },
    { "区块向上移动", "分屏", "move_pane:up" },
    { "区块向下移动", "分屏", "move_pane:down" },
    { "新建终端区块", "分屏", "new_shell_block" },
    { "新建智能体区块", "分屏", "new_agent_block" },
    { "新建浏览器区块", "分屏", "new_browser_block" },
    { "新建 Git 区块", "分屏", "new_git_block" },
    { "新建看板", "分屏", "new_board_block" },
    { "新建笔记本", "分屏", "new_notebook" },
    { "新建数据库", "分屏", "new_database" },
    { "复制", "终端", "copy" },
    { "粘贴", "终端", "paste" },
    { "粘贴选区", "终端", "paste_selection" },
    { "查找终端文本", "终端", "find" },
    { "滚动到顶部", "终端", "scroll_top" },
    { "滚动到底部", "终端", "scroll_bottom" },
    { "清空终端", "终端", "clear" },
    { "重启 Shell", "终端", "restart" },
    { "在编辑器打开历史输出", "终端", "open_scrollback" },
    { "打开文件", "文件", "open_file" },
    { "打开文件夹", "文件", "open_folder" },
    { "显示隐藏文件面板", "文件", "files" },
    { "聚焦文件面板", "文件", "focus_files" },
    { "定位当前文件", "文件", "reveal_active_file" },
    { "显示隐藏文件", "文件", "files_toggle_hidden" },
    { "更换主题", "外观", "theme" },
    { "使用系统外观", "外观", "appearance:system" },
    { "切换浅色模式", "外观", "appearance:light" },
    { "切换深色模式", "外观", "appearance:dark" },
    { "增大字体", "外观", "increase_font_size" },
    { "缩小字体", "外观", "decrease_font_size" },
    { "恢复默认字号", "外观", "reset_font_size" },
    { "下一个布局", "外观", "next_layout" },
    { "切换状态栏", "外观", "status_bar" },
    { "设置", "设置", "preferences" },
    { "打开配置文件", "设置", "open_config" },
    { "重新加载配置", "设置", "reload_config" },
    { "重新加载插件", "设置", "reload_plugins" },
    { "帮助文档", "设置", "help" },
    { "更新日志", "设置", "release_notes" },
    { "截屏", "设置", "screenshot" },
}

for i, item in ipairs(aliases) do
    local target = item[3]
    tern.command({
        id = "zh-action-" .. tostring(i),
        title = item[1],
        group = item[2],
        run = function(cx)
            cx:command(target)
        end,
    })
end

-- Verified native-command Chinese aliases (no new keybindings).
require("./extra_commands")

if tern.runtime.os == "windows" then
    require("./auto-bridge")
end
