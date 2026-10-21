using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Diag;

// ═══════════════════════════════════════════════════════════════
//  Multi-Monitor Restorer — Capture Diagnostic
// ═══════════════════════════════════════════════════════════════

static class P
{
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY     = 1;
    public const long WS_EX_TOOLWINDOW        = 0x80L;
    public const int  GWL_EXSTYLE             = -20;
    public const uint GW_OWNER                = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom;
                         public int W => Right-Left; public int H => Bottom-Top; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WP {
        public uint length, flags, showCmd;
        public PT ptMin, ptMax;
        public RECT rcNormal;
        public static WP Create() { var w=new WP(); w.length=(uint)Marshal.SizeOf(w); return w; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
    public struct MI {
        public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string szDevice;
        public static MI Create() { var m=new MI(); m.cbSize=(uint)Marshal.SizeOf(m); m.szDevice=""; return m; }
    }

    public delegate bool EWP(IntPtr h, IntPtr l);
    public delegate bool MEP(IntPtr hM, IntPtr hdc, ref RECT lp, IntPtr dw);

    [DllImport("user32")] public static extern bool EnumWindows(EWP f, IntPtr l);
    [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32",CharSet=CharSet.Auto)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32")] public static extern bool GetWindowPlacement(IntPtr h, ref WP w);
    [DllImport("user32")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32",CharSet=CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr h, ref MI m);
    [DllImport("user32")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MEP f, IntPtr dw);
    [DllImport("user32")] public static extern long GetWindowLongPtr(IntPtr h, int n);
    [DllImport("user32")] public static extern IntPtr GetWindow(IntPtr h, uint c);
}

class Program
{
    static readonly HashSet<string> SkipProcs = new(StringComparer.OrdinalIgnoreCase)
        { "explorer","searchhost","startmenuexperiencehost","shellexperiencehost",
          "applicationframehost","systemsettings","textinputhost" };

    static string Title(IntPtr h)
    { var s=new StringBuilder(512); return P.GetWindowText(h,s,512)>0?s.ToString():""; }

    static string ProcName(IntPtr h)
    { try { P.GetWindowThreadProcessId(h,out uint pid); using var pr=Process.GetProcessById((int)pid); return pr.ProcessName; } catch { return "?"; } }

    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var line = new string('─', 72);

        // ── Enumerate monitors ─────────────────────────────────
        var monitors = new List<P.MI>();
        P.MEP mcb = MonitorCallback;
        P.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, mcb, IntPtr.Zero);

        bool MonitorCallback(IntPtr hM, IntPtr hdc, ref P.RECT lp, IntPtr dw)
        {
            var mi = P.MI.Create();
            if (P.GetMonitorInfo(hM, ref mi)) monitors.Add(mi);
            return true;
        }

        Console.WriteLine(line);
        Console.WriteLine($"  MONITORS DETECTED: {monitors.Count}");
        Console.WriteLine(line);
        foreach (var m in monitors)
        {
            bool isPrimary = (m.dwFlags & P.MONITORINFOF_PRIMARY) != 0;
            Console.WriteLine($"  {m.szDevice}{(isPrimary?" [PRIMARY]":"")}");
            Console.WriteLine($"    Virtual bounds : ({m.rcMonitor.Left,6},{m.rcMonitor.Top,6})  {m.rcMonitor.W}x{m.rcMonitor.H}");
            Console.WriteLine($"    Work area      : ({m.rcWork.Left,6},{m.rcWork.Top,6})  {m.rcWork.W}x{m.rcWork.H}");
            Console.WriteLine();
        }

        var primary = monitors.FirstOrDefault(m => (m.dwFlags & P.MONITORINFOF_PRIMARY) != 0);
        Console.WriteLine($"  Primary work-area origin: ({primary.rcWork.Left}, {primary.rcWork.Top})");
        Console.WriteLine($"  (If both are 0 the two coordinate methods produce identical results)");
        Console.WriteLine();

        // ── Window capture ─────────────────────────────────────
        int captured=0, sNotVis=0, sNoTitle=0, sTool=0, sOwned=0, sProc=0, sNoMon=0, sBug=0;

        Console.WriteLine(line);
        Console.WriteLine("  CAPTURED WINDOWS");
        Console.WriteLine(line);

        P.EWP ewp = WindowCallback;
        P.EnumWindows(ewp, IntPtr.Zero);

        bool WindowCallback(IntPtr hwnd, IntPtr _)
        {
            if (!P.IsWindowVisible(hwnd))                   { sNotVis++; return true; }
            string title = Title(hwnd);
            if (string.IsNullOrWhiteSpace(title))           { sNoTitle++; return true; }
            long ex = P.GetWindowLongPtr(hwnd, P.GWL_EXSTYLE);
            if ((ex & P.WS_EX_TOOLWINDOW) != 0)             { sTool++;  return true; }
            if (P.GetWindow(hwnd, P.GW_OWNER) != IntPtr.Zero){ sOwned++; return true; }
            string proc = ProcName(hwnd);
            if (SkipProcs.Contains(proc))                   { sProc++;  return true; }

            var wp = P.WP.Create();
            P.GetWindowPlacement(hwnd, ref wp);

            IntPtr hMon = P.MonitorFromWindow(hwnd, P.MONITOR_DEFAULTTONEAREST);
            var mi = P.MI.Create();
            bool ok = P.GetMonitorInfo(hMon, ref mi);
            var matched = monitors.FirstOrDefault(m =>
                string.Equals(m.szDevice, mi.szDevice, StringComparison.OrdinalIgnoreCase));
            bool found = matched.szDevice != null;

            if (!ok || !found)
            {
                sNoMon++;
                Console.WriteLine($"  !! DROPPED — monitor lookup failed");
                Console.WriteLine($"     Process: {proc}");
                Console.WriteLine($"     Title  : {(title.Length>60?title[..60]+"…":title)}");
                Console.WriteLine($"     GetMonitorInfo={ok}  szDevice=\"{mi.szDevice}\"  found={found}");
                Console.WriteLine();
                return true;
            }

            captured++;
            var show = wp.showCmd switch { 1=>"Normal",2=>"Minimized",3=>"Maximized",_=>wp.showCmd.ToString() };

            // Current code: directly subtracts monitor work area from rcNormalPosition
            int curL = wp.rcNormal.Left   - matched.rcWork.Left;
            int curT = wp.rcNormal.Top    - matched.rcWork.Top;
            int curR = wp.rcNormal.Right  - matched.rcWork.Left;
            int curB = wp.rcNormal.Bottom - matched.rcWork.Top;

            // Correct: workspace→virtual (+primary work origin), then virtual→monitor-relative
            int fixL = (wp.rcNormal.Left   + primary.rcWork.Left) - matched.rcWork.Left;
            int fixT = (wp.rcNormal.Top    + primary.rcWork.Top)  - matched.rcWork.Top;
            int fixR = (wp.rcNormal.Right  + primary.rcWork.Left) - matched.rcWork.Left;
            int fixB = (wp.rcNormal.Bottom + primary.rcWork.Top)  - matched.rcWork.Top;

            bool same = curL==fixL && curT==fixT && curR==fixR && curB==fixB;
            if (!same) sBug++;

            Console.WriteLine($"  [{captured:D3}] {proc,-24} {show,-10} | {matched.szDevice}");
            Console.WriteLine($"        Title  : {(title.Length>65?title[..65]+"…":title)}");
            Console.WriteLine($"        rcNorm : ({wp.rcNormal.Left},{wp.rcNormal.Top}) → ({wp.rcNormal.Right},{wp.rcNormal.Bottom})");
            Console.WriteLine($"        Stored : ({curL},{curT}) → ({curR},{curB})  {(same?"✓ OK":"← CURRENT CODE")}");
            if (!same)
                Console.WriteLine($"        Fixed  : ({fixL},{fixT}) → ({fixR},{fixB})  ← CORRECT");
            Console.WriteLine();
            return true;
        }

        Console.WriteLine(line);
        Console.WriteLine("  SUMMARY");
        Console.WriteLine(line);
        Console.WriteLine($"  Captured          : {captured}");
        Console.WriteLine($"  Coord bug count   : {sBug}  (windows with wrong stored coords)");
        Console.WriteLine($"  Skipped invisible : {sNotVis}");
        Console.WriteLine($"  Skipped no title  : {sNoTitle}");
        Console.WriteLine($"  Skipped tool-win  : {sTool}");
        Console.WriteLine($"  Skipped owned-win : {sOwned}");
        Console.WriteLine($"  Skipped sys-proc  : {sProc}");
        Console.WriteLine($"  Dropped no-monitor: {sNoMon}");
        Console.WriteLine(line);
        Console.WriteLine();
        Console.WriteLine("Press ENTER to exit.");
        Console.ReadLine();
    }
}
