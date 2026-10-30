using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Windows.Automation;

// Changes only visual styles for the rows currently shown by Tern.
// Driven by UI Automation selection and text-value events, not a timer.
// The native commands, keyboard shortcuts and menu actions stay untouched.
public static class TernCommandLabels {
    private static readonly object Gate = new object();
    private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
    private static Dictionary<string,string> names = new Dictionary<string,string>(StringComparer.Ordinal);
    private static AutomationElement root;
    private static Thread worker;
    private static bool active;
    private static string tern, port, output, paletteCache = null;
    private static AutomationEventHandler onSelect;
    private static AutomationPropertyChangedEventHandler onValue;

    private static void Request(object sender, AutomationEventArgs e) {
        try {
            AutomationElement el = sender as AutomationElement;
            if (el == null) return;
            ControlType type = el.Current.ControlType;
            if (type == ControlType.ListItem || type == ControlType.MenuItem) Wake.Set();
        } catch (ElementNotAvailableException) {}
    }
    private static void Edited(object sender, AutomationPropertyChangedEventArgs e) {
        try {
            AutomationElement el = sender as AutomationElement;
            if (el != null && el.Current.ControlType == ControlType.Edit
                && el.Current.Name == "Search") Wake.Set();
        } catch (ElementNotAvailableException) {}
    }
    public static void Attach(IntPtr hwnd, string ternExe, int controlPort, string pluginDir) {
        root = AutomationElement.FromHandle(hwnd);
        tern = ternExe;
        port = controlPort.ToString();
        output = Path.Combine(Path.GetTempPath(), "tern-zh-command-" + Process.GetCurrentProcess().Id);
        names.Clear();
        foreach(string line in File.ReadAllLines(Path.Combine(pluginDir,"ui-labels.tsv"), Encoding.UTF8)) {
            int split = line.IndexOf('\t');
            if (split <= 0 || split == line.Length - 1) continue;
            names[line.Substring(0,split)] = line.Substring(split+1);
        }
        onSelect = Request;
        onValue = Edited;
        Automation.AddAutomationEventHandler(SelectionItemPattern.ElementSelectedEvent, root, TreeScope.Descendants, onSelect);
        Automation.AddAutomationPropertyChangedEventHandler(root, TreeScope.Descendants, onValue, ValuePattern.ValueProperty);
        active = true;
        worker = new Thread(Loop);
        worker.IsBackground = true;
        worker.Name = "Tern Chinese command labels";
        worker.Start();
        Wake.Set();
    }
    public static void Detach() {
        active = false;
        Wake.Set();
        try { if(worker != null) worker.Join(1500); } catch {}
        try {Automation.RemoveAutomationEventHandler(SelectionItemPattern.ElementSelectedEvent, root, onSelect);} catch {}
        try {Automation.RemoveAutomationPropertyChangedEventHandler(root, onValue);} catch {}
    }
    private static void Loop() {
        while (true) {
            Wake.WaitOne();
            if(!active)break;
            try {
                Apply("tern-zh-palette", PaletteStyles(), ref paletteCache);
            }catch(ElementNotAvailableException) {
                // A transient UI tree during dismissal will be revisited on the next event.
            }catch(Exception) {}
        }
    }
    private static bool InsidePalette(AutomationElement el) {
        AutomationElement e=el;
        for(int i=0;i<12 && e!=null;i++){
            try {
                if(e.Current.Name=="Command palette" && e.Current.ControlType==ControlType.Window)return true;
                e=TreeWalker.ControlViewWalker.GetParent(e);
            } catch {return false;}
        }
        return false;
    }
    private static string Lookup(string visible) {
        if (String.IsNullOrEmpty(visible)) return null;
        string zh;
        if(names.TryGetValue(visible, out zh)) return zh;
        // Accessibility names append state or a shortcut without a separator.
        string[] endings = {"Current", "On", "Off", "Disabled", "Ctrl+", "Alt+", "Shift+", "⌘", "⌃"};
        foreach(string end in endings) {
            int at=visible.IndexOf(end,StringComparison.Ordinal);
            if(at>0 && names.TryGetValue(visible.Substring(0,at).TrimEnd(), out zh))return zh;
        }
        string result = null;
        int longest = -1;
        foreach(KeyValuePair<string,string> item in names) {
            string en = item.Key;
            if(en.Length <= longest || visible.Length <= en.Length || !visible.StartsWith(en, StringComparison.Ordinal)) continue;
            string suffix = visible.Substring(en.Length);
            if(en.EndsWith("…") || suffix.StartsWith("Current") || suffix.StartsWith("Ctrl") ||
               suffix.StartsWith("Shift") || suffix.StartsWith("Alt") || suffix.StartsWith("On") ||
               suffix.StartsWith("Off") || suffix.StartsWith(" ")) {
                result = item.Value;
                longest = en.Length;
            }
        }
        return result;
    }
    private static string Escape(string value) {
        return value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r"," ").Replace("\n"," ");
    }
    private static string PaletteStyles() {
        AutomationElementCollection lists = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
        StringBuilder sb = new StringBuilder();
        for(int k=0;k<lists.Count;k++) {
            AutomationElement list=lists[k];
            if(!InsidePalette(list))continue;
            int n=0;
            for(AutomationElement child=TreeWalker.ControlViewWalker.GetFirstChild(list);child!=null;) {
                AutomationElement next=null;
                try {
                    next=TreeWalker.ControlViewWalker.GetNextSibling(child);
                    n++;
                    if(child.Current.ControlType==ControlType.ListItem){
                        string zh=Lookup(child.Current.Name);
                        if(!String.IsNullOrEmpty(zh)) {
                            string selector="#pal .ck-list > .tn-pl-item:nth-of-type("+n+") .ck-label";
                            sb.Append(selector).Append("{font-size:0!important;white-space:nowrap!important;}\n");
                            sb.Append(selector).Append("::before{content:\"").Append(Escape(zh))
                              .Append("\";font-size:14px;display:inline-block;white-space:nowrap;}\n");
                            sb.Append("#pal .ck-list > .tn-pl-item:nth-of-type(").Append(n)
                              .Append(") .ck-sub{font-size:11px!important;}\n");
                        }
                    }
                } catch(ElementNotAvailableException) {}
                child=next;
            }
            break;
        }
        return sb.ToString();
    }
    private static void Apply(string sheet,string css, ref string cache){
        if(css==cache)return;
        string file=output+"-"+sheet+".css";
        string arguments="ctl --control "+port+" css "+sheet;
        if(css.Length>0) {
            File.WriteAllText(file,css,new UTF8Encoding(false));
            arguments+=" \""+file+"\"";
        }
        ProcessStartInfo si=new ProcessStartInfo(tern,arguments);
        si.UseShellExecute=false;
        si.CreateNoWindow=true;
        si.RedirectStandardOutput=true;
        si.RedirectStandardError=true;
        using(Process p=Process.Start(si)){
            if(!p.WaitForExit(2200)){try{p.Kill();}catch{}return;}
            string response=p.StandardOutput.ReadToEnd();
            if(p.ExitCode==0 && response.Contains("\"ok\":true"))cache=css;
        }
    }
}
