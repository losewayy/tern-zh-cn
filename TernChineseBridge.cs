using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
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
    private static bool Update(string page) {
        var si = new ProcessStartInfo();
        si.FileName = exe;
        string args = "ctl --control " + endpoint + " css tern-zh-settings";
        if (!string.IsNullOrEmpty(page)) {
            args += " \"" + Path.Combine(rootDir, "prefs-" + page + ".css") + "\"";
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
