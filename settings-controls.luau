-- Keep the native controls and accessible names; draw their current labels as SVG.
local function scope(page)
    local label = page:sub(1, 1):upper() .. page:sub(2)
    return 'main#panel > .tn-stage > .pf.sheet > .pf-nav:has(> .pf-groups > .pf-group > .pf-items > .nav-row[aria-label="' .. label .. '"][aria-selected="true"]) + .pf-scroll > '
end
local function root(page) return scope(page) .. '.pf-doc > ' end
local labels = {}
local images = {}
local cachedKey = nil
local cachedCss = ""

local function fields(line)
    local result = {}
    for field in (line .. "\t"):gmatch("([^\t]*)\t") do table.insert(result, field) end
    return result
end

local function controls(path)
    local result = {}
    local ok, text = pcall(tern.fs.read, path, 1024 * 1024)
    if not ok or not text then return result end
    for line in text:gmatch("[^\r\n]+") do
        local row = fields(line)
        if #row == 8 then
            result[row[1]] = result[row[1]] or {}
            table.insert(result[row[1]], {
                row = row[2] .. "\t" .. row[3], item = tonumber(row[4]), selector = row[5], icon = row[6], source = row[7], mode = row[8],
            })
        end
    end
    return result
end

for line in tern.fs.read(tern.plugin.dir .. "/settings-labels.tsv", 1024 * 1024):gmatch("[^\r\n]+") do
    local row = fields(line)
    if #row == 2 then labels[row[1]] = row[2] end
end

-- Tern moves rows between builds; each compat folder carries its own table.
local shared = controls(tern.plugin.dir .. "/settings-controls.tsv")
local byVersion = { ["tern 0.9.23 (35c63ab)"] = shared, ["tern 0.9.30 (fe54a4c)"] = shared }
local function pagesFor(version)
    local pages = byVersion[version]
    if pages then return pages end
    local build = version and version:match("tern (%d+%.%d+%.%d+)")
    pages = build and controls(tern.plugin.dir .. "/compat/" .. build .. "/settings-controls.tsv") or nil
    if not pages or not next(pages) then pages = shared end
    if version then byVersion[version] = pages end
    return pages
end

local function xml(value)
    return value:gsub("&", "&amp;"):gsub("<", "&lt;"):gsub(">", "&gt;"):gsub('"', "&quot;")
end

local function labelImage(text, dark, icon)
    local key = text .. (dark and ":dark:" or ":light:") .. icon
    if images[key] then return images[key] end
    local width = 0
    for _, codepoint in utf8.codes(text) do width += codepoint < 128 and 7.5 or 14 end
    width = math.ceil(width + 4)
    if icon ~= "-" then width += 24 end
    local x = width / 2 + (icon == "left" and 10 or icon == "right" and -10 or 0)
    local svg = '<svg xmlns="http://www.w3.org/2000/svg" width="' .. width * 3 .. '" height="84" viewBox="0 0 ' .. width .. ' 28"><text x="' .. x .. '" y="14" text-anchor="middle" dominant-baseline="central" font-family="Microsoft YaHei, sans-serif" font-size="13" fill="' .. (dark and "#e5e5e5" or "#353535") .. '">' .. xml(text) .. '</text></svg>'
    local result = { url = 'url("data:image/svg+xml;base64,' .. tern.base64.encode(svg) .. '")', width = width }
    images[key] = result
    return result
end

local values = {
    ["sqlite.location"] = {remember = "记住上次位置", always = "始终使用指定文件夹", session = "本次启动期间记住位置"},
    ["sqlite.wrap"] = {never = "不换行", word = "按单词换行", character = "按字符换行", whitespace = "按空白符换行"},
    ["sqlite.quotes"] = {double = "双引号，标准 SQL", grave = "反引号，MySQL 风格", brackets = "方括号，SQL Server 风格", square = "方括号，SQL Server 风格"},
}

local function addLabel(rules, page, definition, text)
    if not text then return end
    local selector = root(page) .. definition.selector
    local light = labelImage(text, false, definition.icon)
    local dark = labelImage(text, true, definition.icon)
    table.insert(rules, selector .. '{font-size:0!important;position:relative;min-width:' .. (light.width + 24) .. 'px;min-height:28px;background-image:light-dark(' .. light.url .. ',' .. dark.url .. ');background-size:' .. light.width .. 'px 28px;background-position:center;background-repeat:no-repeat;}')
    if definition.icon ~= "-" then
        table.insert(rules, selector .. ' > .ico{position:absolute;' .. (definition.icon == "right" and 'right:10px;' or 'left:10px;') .. 'top:50%;transform:translateY(-50%);}')
    end
end

local fixedCache = {}
local layout0930 = require("./layout-0930")
local function fixed(page, version)
    local key = page .. ":" .. (version or "")
    if fixedCache[key] then return fixedCache[key] end
    local newLayout = version == "tern 0.9.30 (fe54a4c)"
    local rules = {}
    for _, definition in ipairs(pagesFor(version)[page] or {}) do
        if definition.mode == "fixed" then
            if newLayout then
                local changed = table.clone(definition)
                changed.selector = layout0930.selector(page, definition.selector)
                if changed.selector then addLabel(rules, page, changed, labels[definition.source]) end
            else
                addLabel(rules, page, definition, labels[definition.source])
            end
        end
    end
    if newLayout and page == "advanced" then
        addLabel(rules, page, {
            selector = "section:nth-of-type(1)>div:nth-of-type(1)>div:nth-of-type(5)>div:nth-of-type(2)>div:nth-of-type(1)>button:nth-of-type(1)",
            icon = "-",
        }, "跟随显示器")
    end
    local css = table.concat(rules, "\n")
    fixedCache[key] = css
    return css
end

local function live(page, snapshot, cx, version)
    snapshot = snapshot or ""
    local lines = {}
    for line in snapshot:gmatch("[^\r\n]+") do table.insert(lines, line) end
    local rows = {}
    if lines[1] == page then
        for index = 2, #lines do
            local row = fields(lines[index])
            if #row >= 3 then rows[row[1] .. "\t" .. row[2]] = row end
        end
    end
    local settingValues = {}
    for name in pairs(values) do settingValues[name] = cx.settings:get(name) end
    local key = version .. "\n" .. page .. "\n" .. snapshot .. tern.json.encode(settingValues)
    if cachedKey == key then return cachedCss end
    local rules = {}
    for target, definitions in pairs(pagesFor(version)) do
        for _, definition in ipairs(definitions) do
            if values[definition.mode] then
                addLabel(rules, target, definition, values[definition.mode][settingValues[definition.mode]])
            elseif definition.mode == "live" and target == page then
                local row = rows[definition.row]
                local source = row and row[definition.item + 2]
                addLabel(rules, target, definition, source and labels[source])
            end
        end
    end
    local hints = {
        ["agent-4"] = "section:nth-of-type(4) > .pf-rows > .pf-row:first-child .h",
        ["agent-5"] = "section:nth-of-type(5) > .pf-rows > .pf-row:first-child .h",
        ["agent-6"] = "section:nth-of-type(6) > .pf-rows > .pf-row:first-child .h",
        ["git-try"] = "section:nth-of-type(9) > .pf-rows > .pf-row:nth-of-type(2) .h",
        ["git-empty"] = "section:nth-of-type(13) > .pf-rows > .pf-status > span",
        ["update-status"] = "section:nth-of-type(8) .tn-up-say",
        ["omp-status"] = "section:nth-of-type(9) .tn-up-say",
        ["ovnfs-status"] = "section:nth-of-type(10) .tn-up-say",
        ["abkit-status"] = "section:nth-of-type(11) .tn-up-say",
    }
    for name, relative in pairs(hints) do
        local hint = rows["@hint\t" .. name]
        if hint then
            local selector = root(page) .. relative
            local wrapping = name:match("%-status$") and 'nowrap' or 'normal'
            table.insert(rules, selector .. '{font-size:0!important;white-space:' .. wrapping .. ';}')
            table.insert(rules, selector .. '::before{content:' .. tern.json.encode(hint[3]) .. ';font-size:12px;white-space:' .. wrapping .. ';display:inline-block;}')
        end
    end
    cachedKey = key
    cachedCss = table.concat(rules, "\n")
    return cachedCss
end

return { scope = scope, fixed = fixed, live = live }
