-- Offsets from the 0.9.30 layout.
local function selector(page, source)
    if page ~= "appearance" and page ~= "keyboard" then return source end
    local removed = false
    local result = source:gsub("section:nth%-of%-type%((%d+)%)>div:nth%-of%-type%(1%)>div:nth%-of%-type%((%d+)%)", function(section, row)
        local s, r = tonumber(section), tonumber(row)
        if page == "appearance" then
            if (s == 4 and r >= 2) or (s == 6 and r >= 3) then r += 1 end
        elseif s == 5 and r >= 18 then
            r += 3
        elseif s == 6 and r >= 40 then
            r += 1
        elseif s == 8 then
            local previousRow = r
            if previousRow >= 17 then r += 1 end
            if previousRow >= 60 then r += 1 end
            if previousRow >= 138 then r += 2 end
        elseif s == 10 then
            if r >= 25 and r <= 29 then removed = true end
            if r >= 30 then r -= 5 end
        end
        return "section:nth-of-type(" .. section .. ")>div:nth-of-type(1)>div:nth-of-type(" .. r .. ")"
    end)
    if not removed then return result end
    return nil
end

return { selector = selector }
