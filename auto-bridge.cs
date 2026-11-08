using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
    private static readonly Dictionary<string, string> MenuNames = new Dictionary<string, string>(StringComparer.Ordinal) {
        { "Paste", "粘贴" }, { "Split right", "向右分屏" }, { "Split down", "向下分屏" },
        { "Move pane to tab…", "将区块移至标签页…" }, { "Park pane", "暂存区块" },
        { "Find in scrollback", "查找历史输出" }, { "Open scrollback in editor", "在编辑器打开历史输出" },
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

    private static string SettingsPage(AutomationElement root) {
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
        foreach (AutomationElement tab in tabs) {
            try {
                var parent = TreeWalker.ControlViewWalker.GetParent(tab);
                if (parent == null || parent.Current.Name != "Pages") continue;
                var group = TreeWalker.ControlViewWalker.GetParent(parent);
                if (group == null || group.Current.Name != "Settings") continue;
                object selected = tab.GetCurrentPropertyValue(SelectionItemPattern.IsSelectedProperty);
                if (selected is bool && (bool)selected) return tab.Current.Name;
            } catch (ElementNotAvailableException) {} catch (InvalidOperationException) {}
        }
        return "";
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
        var menus = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu));
        foreach (AutomationElement menu in menus) {
            var entries = new List<string>();
            for (var child = TreeWalker.ControlViewWalker.GetFirstChild(menu); child != null; child = TreeWalker.ControlViewWalker.GetNextSibling(child)) {
                if (child.Current.ControlType != ControlType.MenuItem) continue;
                string visible = child.Current.Name;
                string match = null;
                foreach (string english in MenuNames.Keys) {
                    if ((visible == english || visible.StartsWith(english + " ", StringComparison.Ordinal)) && (match == null || english.Length > match.Length)) match = english;
                }
                if (match == null) return "";
                entries.Add(match);
            }
            if (entries.Count != 10 && entries.Count != 12) continue;
            if (entries[0] != "Paste" || entries[1] != "Split right" || entries[2] != "Split down" || entries[entries.Count - 1] != "Close block") continue;
            var css = new StringBuilder();
            for (int i = 0; i < entries.Count; i++) {
                string selector = "body > div.pop > button.mi:nth-of-type(" + (i + 1) + "):nth-last-of-type(" + (entries.Count - i) + ")";
                css.Append(selector).Append("{font-size:0!important;}\n");
                css.Append(selector).Append("::before{content:\"").Append(Escape(MenuNames[entries[i]])).Append("\";font-size:13px;display:inline-block;order:1;}\n");
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
                    string lastPage = null, lastPalette = null, lastMenu = null;
                    try {
                        Automation.AddAutomationEventHandler(SelectionItemPattern.ElementSelectedEvent, root, TreeScope.Descendants, selection);
                        Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree, structure);
                        Automation.AddAutomationPropertyChangedEventHandler(root, TreeScope.Descendants, value, ValuePattern.ValueProperty);
                        Publish(channel + ".version", Version(parent));
                        while (!parent.HasExited && IsWindow(hwnd)) {
                            try {
                                string page = SettingsPage(root);
                                if (page != lastPage) { Publish(channel + ".page", page); lastPage = page; }
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
                        foreach (string suffix in new[] { ".page", ".palette", ".menu", ".version" }) {
                            try { File.Delete(channel + suffix); File.Delete(channel + suffix + ".tmp"); } catch (IOException) {}
                        }
                    }
                } finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
