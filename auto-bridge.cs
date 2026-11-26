using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

public static class TernAutoBridge {
    public static int Main(string[] args) {
        string channel = Environment.GetEnvironmentVariable("TERN_ZH_CHANNEL");
        string pluginDir = Environment.GetEnvironmentVariable("TERN_ZH_PLUGIN");
        int parentId;
        long parentStart;
        if (String.IsNullOrEmpty(channel) || String.IsNullOrEmpty(pluginDir) || !Int32.TryParse(Environment.GetEnvironmentVariable("TERN_ZH_PARENT"), out parentId) || !Int64.TryParse(Environment.GetEnvironmentVariable("TERN_ZH_PARENT_START"), out parentStart)) return 2;
        try { Run(channel, pluginDir, parentId, parentStart); return 0; }
        catch (Exception) { return 1; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInfo { public IntPtr A, B, C, D, Pid, Parent; }
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int info, out ProcessBasicInfo result, int size, out int returned);
    private delegate bool WindowCallback(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly HashSet<string> ControlLabels = new HashSet<string>(StringComparer.Ordinal);
    private static readonly Regex VersionLabel = new Regex(@"^Tern \d+\.\d+\.\d+ \([0-9a-f]+\)$");
    private static readonly HashSet<string> ControlRows = new HashSet<string>(StringComparer.Ordinal);
    private static AutomationElement SettingsContent;
    private static readonly Dictionary<string, string> SettingsHints = new Dictionary<string, string>(StringComparer.Ordinal) {
        { "General\tNot checked yet", "update-status\t尚未检查更新" },
        { "General\tomp isn't installed", "omp-status\t尚未安装 omp" },
        { "General\tovnfs isn't installed", "ovnfs-status\t尚未安装 ovnfs" },
        { "General\tABKit isn't installed", "abkit-status\t尚未安装 ABKit" },
        { "General\tAn update is on its way · downloading in a few minutes", "update-status\t更新即将到来，几分钟后开始下载。" },
        { "Git\tNo repository: focus a git block or a terminal in a checkout to set up its git-flow, LFS and sparse checkout.", "git-empty\t尚未选中仓库。请聚焦 Git 区块，或工作目录位于仓库中的终端，再设置 git-flow、LFS 和稀疏检出。" },
    };
    private static readonly Dictionary<string, string> SettingsMenuNames = new Dictionary<string, string>(StringComparer.Ordinal) {
        { "Remember last location", "记住上次位置" },
        { "Always use this location", "始终使用指定文件夹" },
        { "Remember last location for this session", "本次启动期间记住位置" },
    };
    private static readonly Dictionary<string, string> MenuNames = new Dictionary<string, string>(StringComparer.Ordinal) {
        { "Paste", "粘贴" }, { "Select all", "全选" }, { "Split right", "向右分屏" }, { "Split down", "向下分屏" },
        { "Move pane to tab…", "将区块移至标签页…" }, { "Park pane", "暂存区块" },
        { "Move pane to window…", "将区块移至窗口…" },
        { "Find in scrollback", "查找历史输出" }, { "Open scrollback in editor", "在编辑器中打开历史输出" },
        { "Clear screen and scrollback", "清空屏幕及历史输出" }, { "Restart shell", "重启 Shell" },
        { "Close block", "关闭区块" }, { "Picture in picture", "画中画" },
        { "Zoom block", "最大化区块" }, { "Restore pane", "还原区块" },
    };

    public static int ParentId() {
        ProcessBasicInfo info; int returned;
        if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0, out info, Marshal.SizeOf(typeof(ProcessBasicInfo)), out returned) != 0) throw new InvalidOperationException("Cannot find Tern window process");
        return info.Parent.ToInt32();
    }

    private static string WindowMarker(string channel) {
        string token = Path.GetFileName(channel);
        var result = new StringBuilder("\u2063");
        foreach (char digit in token) {
            int value = Convert.ToInt32(digit.ToString(), 16);
            for (int bit = 3; bit >= 0; bit--) result.Append((value & (1 << bit)) != 0 ? '\u200c' : '\u200b');
        }
        return result.Append('\u2063').ToString();
    }

    private static IntPtr FindWindow(int parentId, string marker, bool checkTabs) {
        var candidates = new List<IntPtr>();
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, data) => {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid != parentId) return true;
            var name = new StringBuilder(128);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() != "StencilWindow") return true;
            var title = new StringBuilder(1024);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.ToString().EndsWith(marker, StringComparison.Ordinal)) { found = hwnd; return false; }
            candidates.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        if (found != IntPtr.Zero || !checkTabs) return found;
        foreach (IntPtr hwnd in candidates) {
            try {
                var root = AutomationElement.FromHandle(hwnd);
                var tabs = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
                foreach (AutomationElement tab in tabs) {
                    if (tab.Current.Name.EndsWith(marker, StringComparison.Ordinal)) return hwnd;
                }
            } catch (ElementNotAvailableException) {} catch (InvalidOperationException) {} catch (COMException) {}
        }
        return IntPtr.Zero;
    }

    private static bool Modern(string version) {
        return version == "tern 0.9.35 (ccfae03)" || version == "tern 0.9.30 (fe54a4c)" || version == "tern 0.9.23 (35c63ab)";
    }

    private static string Version(Process parent) {
        string cli = Path.Combine(Path.GetDirectoryName(parent.MainModule.FileName), "tern.com");
        if (!File.Exists(cli)) return "";
        var start = new ProcessStartInfo(cli, "--version");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        using (var process = Process.Start(start)) {
            if (!process.WaitForExit(3000)) { try { process.Kill(); } catch {} return ""; }
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : "";
        }
    }

    private static void CleanOldHelpers(string channel) {
        try {
            string current = Process.GetCurrentProcess().MainModule.FileName;
            foreach (string file in Directory.GetFiles(Path.GetDirectoryName(channel), "bridge-*.exe")) {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.Length != 23 || !name.Substring(7).Trim("0123456789abcdef".ToCharArray()).Equals("", StringComparison.Ordinal)) continue;
                if (String.Equals(file, current, StringComparison.OrdinalIgnoreCase)) continue;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(1)) continue;
                try { File.Delete(file); } catch (IOException) {} catch (UnauthorizedAccessException) {}
            }
        } catch (IOException) {} catch (UnauthorizedAccessException) {}
    }

    private static string SettingsPage(AutomationElement root, bool modern) {
        SettingsContent = null;
        var fields = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        foreach (AutomationElement field in fields) {
            try {
                var parent = TreeWalker.ControlViewWalker.GetParent(field);
                if (field.Current.Name != "Search settings" || parent == null || parent.Current.Name != "Settings") continue;
                string query = field.GetCurrentPropertyValue(ValuePattern.ValueProperty) as string;
                if (!String.IsNullOrWhiteSpace(query)) return "";
            } catch (ElementNotAvailableException) {} catch (InvalidOperationException) {}
        }
        var tabs = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
        string page = "";
        AutomationElement settings = null;
        foreach (AutomationElement tab in tabs) {
            try {
                var parent = TreeWalker.ControlViewWalker.GetParent(tab);
                if (parent == null || parent.Current.Name != "Pages") continue;
                var group = TreeWalker.ControlViewWalker.GetParent(parent);
                if (group == null || group.Current.Name != "Settings") continue;
                object selected = tab.GetCurrentPropertyValue(SelectionItemPattern.IsSelectedProperty);
                if (selected is bool && (bool)selected) {
                    if (page != "") return "";
                    page = tab.Current.Name;
                    settings = group;
                }
            } catch (ElementNotAvailableException) {} catch (InvalidOperationException) {}
        }
        if (modern && page != "") {
            // The new navigation and content rebuild separately during a page switch.
            var content = TreeWalker.ControlViewWalker.GetNextSibling(settings);
            if (content == null || content.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, page)) == null) return "";
            SettingsContent = content;
        }
        return page;
    }

    private static string SettingsControls(string page) {
        var result = new StringBuilder(page.ToLowerInvariant()).Append('\n');
        if (SettingsContent == null || page == "") return result.ToString();
        foreach (var hint in SettingsHints) {
            string prefix = page + "\t";
            if (!hint.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string source = hint.Key.Substring(prefix.Length);
            if (SettingsContent.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, source)) != null) result.Append("@hint\t").Append(hint.Value).Append('\n');
        }
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = SettingsContent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group));
        foreach (AutomationElement group in groups) {
            var texts = group.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
            if (texts.Count < 2) continue;
            var label = texts[0];
            string name = label.Current.Name;
            // The update row names the running build; the control table names it once.
            if (VersionLabel.IsMatch(name)) name = "Tern <version>";
            if (page == "Agents" && name == "Command line tool") {
                string help = texts[1].Current.Name;
                if (help.StartsWith("Found at ", StringComparison.Ordinal)) {
                    string location = help.Substring(9);
                    string file = Path.GetFileName(location.Split(new[] { " · " }, StringSplitOptions.None)[0]);
                    int section = file == "claude" || file == "claude.exe" ? 4 : file == "codex" || file == "codex.exe" ? 5 : file == "copilot" || file == "copilot.exe" ? 6 : 0;
                    if (section != 0) result.Append("@hint\tagent-").Append(section).Append('\t').Append("已找到：").Append(location.Replace(" · adapter installs on first use", "，首次使用时会自动安装适配器。")).Append('\n');
                }
            }
            if (page == "Git" && name == "Try it" && texts[1].Current.Name == "Focus a git block or a terminal in a checkout on this machine to try it.") {
                result.Append("@hint\tgit-try\t请聚焦本机 Git 区块，或工作目录位于仓库中的终端，再试用此设置。\n");
            }
            if (!ControlRows.Contains(page.ToLowerInvariant() + "\t" + name)) continue;
            int occurrence;
            counts.TryGetValue(name, out occurrence);
            counts[name] = ++occurrence;
            if (name == "Managed by Tern") {
                string help = texts[1].Current.Name;
                occurrence = help.Contains("ovnfs") ? 2 : help.Contains("ABKit") ? 3 : 1;
            }
            result.Append(name).Append('\t').Append(occurrence);
            var buttons = group.FindAll(TreeScope.Descendants, new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton)));
            foreach (AutomationElement button in buttons) {
                string caption = button.Current.Name;
                result.Append('\t').Append(ControlLabels.Contains(caption) ? caption : "");
            }
            result.Append('\n');
        }
        return result.ToString();
    }

    private static bool InsidePalette(AutomationElement element) {
        for (int i = 0; i < 12 && element != null; i++) {
            try {
                if (element.Current.Name == "Command palette" && element.Current.ControlType == ControlType.Window) return true;
                element = TreeWalker.ControlViewWalker.GetParent(element);
            } catch (ElementNotAvailableException) { return false; }
        }
        return false;
    }

    private static string Lookup(string visible) {
        if (String.IsNullOrEmpty(visible)) return null;
        string translation;
        if (Names.TryGetValue(visible, out translation)) return translation;
        string result = null;
        int longest = -1;
        foreach (var item in Names) {
            string english = item.Key;
            if (english.Length <= longest || visible.Length <= english.Length || !visible.StartsWith(english, StringComparison.Ordinal)) continue;
            string suffix = visible.Substring(english.Length);
            if (english.EndsWith("…", StringComparison.Ordinal) || suffix.StartsWith("Current", StringComparison.Ordinal) || suffix.StartsWith("Ctrl", StringComparison.Ordinal) || suffix.StartsWith("Shift", StringComparison.Ordinal) || suffix.StartsWith("Alt", StringComparison.Ordinal) || suffix.StartsWith("On", StringComparison.Ordinal) || suffix.StartsWith("Off", StringComparison.Ordinal) || suffix.StartsWith(" ", StringComparison.Ordinal)) {
                result = item.Value;
                longest = english.Length;
            }
        }
        return result;
    }

    private static string Escape(string value) {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
    }

    private static void Publish(string path, string text) {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text, Utf8);
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    private static string PaletteStyles(AutomationElement root) {
        var lists = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
        var css = new StringBuilder();
        foreach (AutomationElement list in lists) {
            if (!InsidePalette(list)) continue;
            int index = 0;
            for (var child = TreeWalker.ControlViewWalker.GetFirstChild(list); child != null;) {
                AutomationElement next = null;
                try {
                    next = TreeWalker.ControlViewWalker.GetNextSibling(child);
                    index++;
                    if (child.Current.ControlType == ControlType.ListItem) {
                        string text = Lookup(child.Current.Name);
                        if (!String.IsNullOrEmpty(text)) {
                            string selector = "#pal .ck-list > .tn-pl-item:nth-of-type(" + index + ") .ck-label";
                            css.Append(selector).Append("{font-size:0!important;white-space:nowrap!important;}\n");
                            css.Append(selector).Append("::before{content:\"").Append(Escape(text)).Append("\";font-size:14px;display:inline-block;white-space:nowrap;}\n");
                        }
                    }
                } catch (ElementNotAvailableException) {}
                child = next;
            }
            break;
        }
        return css.ToString();
    }

    private static string MenuStyles(AutomationElement root) {
        var translations = SettingsContent == null ? MenuNames : SettingsMenuNames;
        var menus = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu));
        foreach (AutomationElement menu in menus) {
            var entries = new List<string>();
            for (var child = TreeWalker.ControlViewWalker.GetFirstChild(menu); child != null; child = TreeWalker.ControlViewWalker.GetNextSibling(child)) {
                if (child.Current.ControlType != ControlType.MenuItem) continue;
                string visible = child.Current.Name;
                string match = null;
                foreach (string english in translations.Keys) {
                    if ((visible == english || visible.StartsWith(english + " ", StringComparison.Ordinal)) && (match == null || english.Length > match.Length)) match = english;
                }
                if (match == null) return "";
                entries.Add(match);
            }
            if (SettingsContent == null) {
                if (entries.Count < 10 || entries[0] != "Paste" || entries[entries.Count - 1] != "Close block") continue;
                if (!entries.Contains("Split right") || !entries.Contains("Split down") || !entries.Contains("Find in scrollback") || !entries.Contains("Restart shell")) continue;
            }
            var css = new StringBuilder();
            for (int i = 0; i < entries.Count; i++) {
                string selector = "body > div.pop > button.mi:nth-of-type(" + (i + 1) + "):nth-last-of-type(" + (entries.Count - i) + ")";
                css.Append(selector).Append("{font-size:0!important;}\n");
                css.Append(selector).Append("::before{content:\"").Append(Escape(translations[entries[i]])).Append("\";font-size:13px;display:inline-block;order:1;}\n");
                css.Append(selector).Append(" > .ico{order:0;}\n");
                css.Append(selector).Append(" > .sub{font-size:11px!important;order:2;margin-left:auto;}\n");
            }
            return css.ToString();
        }
        return "";
    }

    public static void Run(string channel, string pluginDir, int parentId, long parentStart) {
        CleanOldHelpers(channel);
        foreach (string line in File.ReadAllLines(Path.Combine(pluginDir, "ui-labels.tsv"), Encoding.UTF8)) {
            int split = line.IndexOf('\t');
            if (split > 0 && split < line.Length - 1) Names[line.Substring(0, split)] = line.Substring(split + 1);
        }
        foreach (string line in File.ReadAllLines(Path.Combine(pluginDir, "settings-labels.tsv"), Encoding.UTF8)) {
            int split = line.IndexOf('\t');
            if (split > 0) ControlLabels.Add(line.Substring(0, split));
        }
        foreach (string line in File.ReadAllLines(Path.Combine(pluginDir, "settings-controls.tsv"), Encoding.UTF8)) {
            string[] fields = line.Split('\t');
            if (fields.Length == 8 && fields[7] == "live") ControlRows.Add(fields[0] + "\t" + fields[1]);
        }
        using (var parent = Process.GetProcessById(parentId)) {
            if (parent.StartTime.ToUniversalTime().Ticks != parentStart) throw new InvalidOperationException("Tern window process changed");
            parent.EnableRaisingEvents = true;
            parent.Exited += delegate { Wake.Set(); };
            string marker = WindowMarker(channel);
            IntPtr hwnd = IntPtr.Zero;
            for (int i = 0; i < 85 && hwnd == IntPtr.Zero && !parent.HasExited; i++) {
                hwnd = FindWindow(parentId, marker, i >= 25);
                if (hwnd == IntPtr.Zero) Thread.Sleep(100);
            }
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Tern window not found");
            string windowId = parent.Id + "-" + hwnd.ToInt64();
            bool newStop;
            using (var stop = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TernZhStop-" + windowId, out newStop))
            using (var mutex = new Mutex(false, @"Local\TernZhHelper-" + windowId)) {
                if (!newStop) stop.Set();
                bool acquired;
                try { acquired = mutex.WaitOne(5000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new InvalidOperationException("Previous Tern helper did not stop");
                try {
                    var root = AutomationElement.FromHandle(hwnd);
                    AutomationEventHandler selection = (sender, args) => Wake.Set();
                    StructureChangedEventHandler structure = (sender, args) => Wake.Set();
                    AutomationPropertyChangedEventHandler value = (sender, args) => Wake.Set();
                    string lastPage = null, lastPalette = null, lastMenu = null, lastControls = null;
                    try {
                        Automation.AddAutomationEventHandler(SelectionItemPattern.ElementSelectedEvent, root, TreeScope.Descendants, selection);
                        Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree, structure);
                        Automation.AddAutomationPropertyChangedEventHandler(root, TreeScope.Descendants, value, ValuePattern.ValueProperty);
                        string version = Version(parent);
                        Publish(channel + ".version", version);
                        while (!parent.HasExited && IsWindow(hwnd)) {
                            try {
                                string page = SettingsPage(root, Modern(version));
                                if (page != lastPage) { Publish(channel + ".page", page); lastPage = page; }
                                string controls = SettingsControls(page);
                                if (controls != lastControls) { Publish(channel + ".controls", controls); lastControls = controls; }
                                string palette = PaletteStyles(root);
                                if (palette != lastPalette) { Publish(channel + ".palette", palette); lastPalette = palette; }
                                string menu = MenuStyles(root);
                                if (menu != lastMenu) { Publish(channel + ".menu", menu); lastMenu = menu; }
                            } catch (ElementNotAvailableException) {} catch (InvalidOperationException) {} catch (IOException) {}
                            if (WaitHandle.WaitAny(new WaitHandle[] { Wake, stop }, 750) == 1) break;
                        }
                    } finally {
                        try { Automation.RemoveAutomationEventHandler(SelectionItemPattern.ElementSelectedEvent, root, selection); } catch (Exception) {}
                        try { Automation.RemoveStructureChangedEventHandler(root, structure); } catch (Exception) {}
                        try { Automation.RemoveAutomationPropertyChangedEventHandler(root, value); } catch (Exception) {}
                        foreach (string suffix in new[] { ".page", ".palette", ".menu", ".controls", ".version" }) {
                            try { File.Delete(channel + suffix); File.Delete(channel + suffix + ".tmp"); } catch (IOException) {}
                        }
                    }
                } finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
