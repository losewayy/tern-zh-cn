local rowPattern = "section:nth%-of%-type%((%d+)%)>div:nth%-of%-type%(1%)>div:nth%-of%-type%((%d+)%)"

local function selector(page, source)
    if page ~= "appearance" and page ~= "advanced" and page ~= "keyboard" then return source end
    local removed = false
    local result = source:gsub(rowPattern, function(section, row)
        local s, r = tonumber(section), tonumber(row)
        if (page == "appearance" and s == 6 and r >= 8)
            or (page == "advanced" and s == 1 and r >= 5)
            or (page == "keyboard" and s == 5 and r >= 32) then
            r += 1
        elseif page == "keyboard" and s == 8 and r == 61 then
            removed = true
        elseif page == "keyboard" and s == 8 and r >= 62 and r <= 98 then
            r -= 1
        elseif page == "keyboard" and s == 10 and r >= 30 then
            if r <= 31 then removed = true else r -= 2 end
        end
        return "section:nth-of-type(" .. section .. ")>div:nth-of-type(1)>div:nth-of-type(" .. r .. ")"
    end)
    if not removed then return result end
    return nil
end

local function styles(page, source, remap)
    return (source:gsub("([^\n{]+)({[^\n]*)", function(rule, body)
        local updated = remap(page, rule)
        return updated and updated .. body or ""
    end))
end

return { selector = selector, styles = styles }
