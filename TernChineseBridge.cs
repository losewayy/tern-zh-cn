using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

// Windows-native event bridge. No polling, no DOM scraping, no networking.
// Watches only selection changes in Tern's built-in "Pages" tab control.
public static class TernChineseBridge {
    private static readonly AutoResetEvent Changed = new AutoResetEvent(false);
    private static readonly ManualResetEvent Stopped = new ManualResetEvent(false);
    private static readonly object Gate = new object();
    private static string latest = null;
    private static string current = "";
    private static string exe, rootDir, endpoint;
    private static string logFile;
    private static Process observed;
    private static AutomationElement window;
    private static AutomationEventHandler selectionHandler;

    private static string Normalize(string name) {
        switch (name) {
            case "General": return "general";
            case "Agents": return "agents";
            case "Lenses": return "lenses";
            case "Databases": return "databases";
            case "Notebooks": return "notebooks";
            case "Carly": return "carly";
            case "Keyboard": return "keyboard";
            case "Git": return "git";
            case "Sessions": return "sessions";
            case "Hosts": return "hosts";
            case "Appearance": return "appearance";
            case "Terminal": return "terminal";
            case "Plugins": return "plugins";
            case "Advanced": return "advanced";
            default: return ""; // use original English on unlocalized pages
        }
    }
    private static bool IsSettingsTab(AutomationElement e) {
        try {
            if (e.Current.ControlType != ControlType.TabItem) return false;
            AutomationElement parent = TreeWalker.ControlViewWalker.GetParent(e);
            return parent != null && parent.Current.ControlType == ControlType.Tab &&
                parent.Current.Name == "Pages";
        } catch (ElementNotAvailableException) { return false; }
          catch (InvalidOperationException) { return false; }
    }
    private static void Queue(string page) {
        lock (Gate) { latest = page; }
        Changed.Set();
    }
    private static void OnSelected(object sender, AutomationEventArgs args) {
        AutomationElement e = sender as AutomationElement;
        if (e == null || !IsSettingsTab(e)) return;
        try { Queue(Normalize(e.Current.Name)); }
        catch (ElementNotAvailableException) {}
    }
    private static string InitialPage() {
        AutomationElementCollection items = window.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
        for (int i = 0; i < items.Count; ++i) {
            AutomationElement e = items[i];
            if (!IsSettingsTab(e)) continue;
            try {
                object selected = e.GetCurrentPropertyValue(SelectionItemPattern.IsSelectedProperty);
                if (selected is bool && (bool)selected) return Normalize(e.Current.Name);
            } catch (ElementNotAvailableException) {}
        }
        return "";
    }
    private static void Note(string message) {
        try {
            File.AppendAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                " " + message + Environment.NewLine);
        } catch {}
    }
    // Scope styles to Tern's own sheet. OMP's native settings use the same
    // .pf-doc classes in a surface and must never receive these translations.
    private static string PreparePageStyles(string page) {
        string input = Path.Combine(rootDir, "prefs-" + page + ".css");
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TernZhPlus", "scoped-styles");
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "prefs-" + page + ".css");
        string scope = "main#panel > .tn-stage > .pf.sheet > .pf-scroll > ";
        var scoped = new StringBuilder();
        foreach (string line in File.ReadLines(input, Encoding.UTF8)) {
            // Old full-tree selectors refer to obsolete child positions and
            // were not validated for the current settings tree.
            if (line.TrimStart().StartsWith("main:nth-of-type(1)>div:nth-of-type(1)>div:nth-of-type(7)", StringComparison.Ordinal)) continue;
            scoped.AppendLine(Regex.Replace(line, @"^(\s*)(?=(?:div)?\.pf-doc\b)", "$1" + scope));
        }
        File.WriteAllText(output, scoped.ToString(), new UTF8Encoding(false));
        return output;
    }
    // UIA exposes both Tern and OMP tabs as "Pages". Resolve the selected
    // native Tern page from its own control state instead of trusting the tab
    // that happened to fire the event; inspect no other state fields.
    // Extract only the top-level prefs field. Avoid deserializing unrelated
    // terminal/session state, which can be deeply nested and contain user data.
    private static string FindTopLevelPage(string json) {
        int depth = 0;
        for (int i = 0; i < json.Length; i++) {
            char c = json[i];
            if (c == '"') {
                int start = i + 1;
                bool escaped = false;
                while (++i < json.Length) {
                    if (escaped) { escaped = false; continue; }
                    if (json[i] == '\\') { escaped = true; continue; }
                    if (json[i] == '"') break;
                }
                if (i >= json.Length) return null;
                if (depth != 1 || i - start != 5 || json.Substring(start, 5) != "prefs") continue;
                int j = i + 1;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                if (j >= json.Length || json[j] != ':') continue;
                j++;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                if (j + 4 <= json.Length && json.Substring(j, 4) == "null") return "";
                Match page = Regex.Match(json.Substring(j), @"^\{\s*""page""\s*:\s*""([a-z]+)""");
                return page.Success ? page.Groups[1].Value : null;
            }
            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']') depth--;
        }
        return null;
    }
    private static string GetNativeSettingsPage() {
        var si = new ProcessStartInfo(exe, "ctl --control " + endpoint + " state");
        si.UseShellExecute = false;
        si.CreateNoWindow = true;
        si.RedirectStandardOutput = true;
        si.RedirectStandardError = true;
        try {
            using (var p = new Process()) {
                p.StartInfo = si;
                var output = new StringBuilder();
                p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) output.AppendLine(e.Data); };
                p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {};
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(2500)) { try { p.Kill(); } catch {} return null; }
                p.WaitForExit();
                if (p.ExitCode != 0) return null;
                return FindTopLevelPage(output.ToString());
            }
        } catch (Exception ex) { Note("settings state unavailable: " + ex.GetType().Name); return null; }
    }
    private static bool Update(string page) {
        var si = new ProcessStartInfo();
        si.FileName = exe;
        string args = "ctl --control " + endpoint + " css tern-zh-settings";
        if (!string.IsNullOrEmpty(page)) {
            try { args += " \"" + PreparePageStyles(page) + "\""; }
            catch (Exception ex) { Note("settings styles unavailable: " + ex.GetType().Name); return false; }
        }
        si.Arguments = args;
        si.UseShellExecute = false;
        si.CreateNoWindow = true;
        si.RedirectStandardOutput = true;
        si.RedirectStandardError = true;
        try {
            using (var p = Process.Start(si)) {
                if (!p.WaitForExit(2500)) {
                    try { p.Kill(); } catch {}
                    Note("timeout " + page);
                    return false;
                }
                string stdout = p.StandardOutput.ReadToEnd();
                if (p.ExitCode != 0 || stdout.IndexOf("\"ok\":true", StringComparison.Ordinal) < 0) {
                    Note("failed " + page + " exit=" + p.ExitCode);
                    return false;
                }
            }
            return true;
        } catch (Exception ex) {
            Note("error " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }
    public static void Run(int pid, int port, string ternExe, string pluginDir) {
        exe = ternExe;
        rootDir = pluginDir;
        endpoint = port.ToString();
        logFile = Path.Combine(Path.GetTempPath(), "tern-zh-bridge-" + pid + ".log");
        observed = Process.GetProcessById(pid);
        observed.EnableRaisingEvents = true;
        observed.Exited += delegate { Stopped.Set(); };
        observed.Refresh();
        if (observed.HasExited || observed.MainWindowHandle == IntPtr.Zero)
            throw new Exception("Tern window not ready");
        window = AutomationElement.FromHandle(observed.MainWindowHandle);
        selectionHandler = OnSelected;
        Automation.AddAutomationEventHandler(SelectionItemPattern.ElementSelectedEvent,
            window, TreeScope.Descendants, selectionHandler);
        Note("UI Automation listener attached to PID " + pid);
        Queue(InitialPage());
        WaitHandle[] signals = new WaitHandle[] { Changed, Stopped };
        try {
            while (true) {
                int reason = WaitHandle.WaitAny(signals);
                if (reason == 1 || observed.HasExited) break;
                string next;
                lock (Gate) { next = latest; }
                if (next == null) continue;
                Thread.Sleep(75); // Allow the selected native page state to settle.
                next = GetNativeSettingsPage();
                if (next == null || next == current) continue;
                if (Update(next)) {
                    current = next;
                    Note("selected=" + (next.Length > 0 ? next : "(original English)"));
                }
            }
        } finally {
            Automation.RemoveAutomationEventHandler(SelectionItemPattern.ElementSelectedEvent,
                window, selectionHandler);
            Note("listener stopped");
        }
    }
}
