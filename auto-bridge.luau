local pageFiles = {
    General = "general", Agents = "agents", Appearance = "appearance",
    Terminal = "terminal", Lenses = "lenses", Databases = "databases",
    Notebooks = "notebooks", Carly = "carly", Keyboard = "keyboard",
    Git = "git", Plugins = "plugins", Sessions = "sessions", Hosts = "hosts",
    Advanced = "advanced",
}

local token = string.format("%08x%08x", math.random(0, 2147483647), math.random(0, 2147483647))
local channel = tern.plugin.data .. "/" .. token
local running = true
local lastPage = nil
local lastPalette = nil
local lastMenu = nil
local informed = false
local hadVersion = false
local missingVersion = 0
local waitingForWindow = 0
local identifying = true
local cache = {}
local scope = "main#panel > .tn-stage > .pf.sheet > .pf-scroll > "

-- Invisible characters let the helper identify this window without moving or focusing it.
local marker = utf8.char(0x2063)
for digit in token:gmatch(".") do
    local value = tonumber(digit, 16)
    for shift = 3, 0, -1 do
        marker ..= utf8.char(math.floor(value / (2 ^ shift)) % 2 == 1 and 0x200c or 0x200b)
    end
end
marker ..= utf8.char(0x2063)

tern.chrome.window_title(function(win)
    if identifying then return (win.space or "Tern") .. marker end
    return nil
end)

local function finishIdentifying()
    if identifying then
        identifying = false
        tern.chrome.refresh()
    end
end

local function readSmall(path, limit)
    local ok, value = pcall(tern.fs.read, path, limit)
    return ok and value or nil
end

local function pageStyles(page, version)
    local name = pageFiles[page]
    if not name then return "" end
    local key = version .. ":" .. name
    if cache[key] then return cache[key] end
    local directory = tern.plugin.dir
    if version == "tern 0.9.9 (9c1c09f)" and (name == "general" or name == "keyboard" or name == "advanced") then
        directory ..= "/compat/0.9.9"
    end
    local source = readSmall(directory .. "/prefs-" .. name .. ".css", 1024 * 1024)
    if not source then return "" end
    local lines = {}
    for line in (source .. "\n"):gmatch("([^\n]*)\n") do
        if not line:match("^%s*main:nth%-of%-type%(%s*1%s*%)>div:nth%-of%-type%(%s*1%s*%)>div:nth%-of%-type%(%s*7%s*%)") then
            line = line:gsub("^(%s*)%.pf%-doc", "%1" .. scope .. ".pf-doc")
            line = line:gsub("^(%s*)div%.pf%-doc", "%1" .. scope .. "div.pf-doc")
            table.insert(lines, line)
        end
    end
    local result = table.concat(lines, "\n")
    cache[key] = result
    return result
end

local function poll(cx)
    if not running then return end
    tern.timer(250, poll)
    local version = readSmall(channel .. ".version", 256)
    local page = readSmall(channel .. ".page", 128)
    if version then
        finishIdentifying()
        hadVersion = true
        missingVersion = 0
    elseif not hadVersion then
        waitingForWindow += 1
        if waitingForWindow == 12 then
            -- Another plugin may own the window title; use tab titles as a fallback.
            tern.chrome.tab_title(function(tab)
                if not identifying then return nil end
                local title = tab.name
                if not title or title == "" then title = tab.title end
                if not title or title == "" then title = tab.program end
                return (title or "Tern") .. marker
            end)
            tern.chrome.refresh()
        end
    elseif hadVersion then
        missingVersion += 1
        if missingVersion >= 8 then
            running = false
            tern.css("settings", "")
            tern.css("palette", "")
            tern.css("menu", "")
            cx:toast("error", "Tern 汉化小程序已退出", "重新加载插件或重新打开 Tern 可重试。")
            return
        end
    end
    if version and page then
        local supported = version == "tern 0.9.12 (e498b49)" or version == "tern 0.9.9 (9c1c09f)"
        local key = supported and (version .. ":" .. page) or ""
        if key ~= lastPage then
            tern.css("settings", supported and pageStyles(page, version) or "")
            lastPage = key
        end
        if not supported and not informed then
            informed = true
            cx:toast("info", "Tern 版本未通过设置页汉化校验", "为避免错译，设置页暂时保留英文。")
        end
    end
    local palette = readSmall(channel .. ".palette", 1024 * 1024)
    if palette and palette ~= lastPalette then
        tern.css("palette", palette)
        lastPalette = palette
    end
    local menu = readSmall(channel .. ".menu", 1024 * 1024)
    if menu and menu ~= lastMenu then
        tern.css("menu", menu)
        lastMenu = menu
    end
end

tern.timer(100, function(cx)
    tern.timer(250, poll)
    local windows = tern.getenv("SYSTEMROOT")
    if not windows then
        running = false
        finishIdentifying()
        cx:toast("error", "Tern 汉化小程序未能启动", "无法找到 Windows 系统目录。")
        return
    end
    local powershell = windows .. "/System32/WindowsPowerShell/v1.0/powershell.exe"
    local ok, err = pcall(tern.process.run, {
        powershell, "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
        "-File", tern.plugin.dir .. "/auto-bridge.ps1", channel,
    }, function(result, callbackCx)
        if result.status ~= 0 then
            running = false
            finishIdentifying()
            tern.css("settings", "")
            tern.css("palette", "")
            tern.css("menu", "")
            tern.log.warn("Chinese helper could not start", result.status, result.stderr)
            callbackCx:toast("error", "Tern 汉化小程序未能启动", "请检查插件安装和 Windows 环境。")
        end
    end)
    if not ok then
        running = false
        finishIdentifying()
        tern.log.warn("Chinese helper could not start", err)
        cx:toast("error", "Tern 汉化小程序未能启动", "请检查插件安装和 Windows 环境。")
    end
end)
