// ============================================================
//  DiagnosticCapture.ps1
//  Compiles and runs a self-contained diagnostic that dumps:
//    - All monitors found
//    - All windows captured
//    - Which monitor each window maps to
//    - Any windows silently dropped and why
// ============================================================

$src = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// ── P/Invoke ─────────────────────────────────────────────────
static class N
{
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY     = 1;
    public const long WS_EX_TOOLWINDOW        = 0x80L;
    public const int  GWL_EXSTYLE             = -20;
    public const uint GW_OWNER                = 4;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width  => Right  - Left;
        public int Height => Bottom - Top;
    }
    [StructLayout(LayoutKind.Sequential)] public struct WINDOWPLACEMENT
    {
        public uint length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
        public static WINDOWPLACEMENT Create()
        { var w = new WINDOWPLACEMENT(); w.length = (uint)Marshal.SizeOf(w); return w; }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct MONITORINFOEX
    {
        public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        public static MONITORINFOEX Create()
        { var m = new MONITORINFOEX(); m.cbSize = (uint)Marshal.SizeOf(m); m.szDevice = ""; return m; }
    }

    public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    public delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT lp, IntPtr dw);

    [DllImport("user32")] public static extern bool EnumWindows(EnumWindowsProc f, IntPtr l);
    [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32", CharSet=CharSet.Auto)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32")] public static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT w);
    [DllImport("user32")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32", CharSet=CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFOEX m);
    [DllImport("user32")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc f, IntPtr dw);
    [DllImport("user32")] public static extern long GetWindowLongPtr(IntPtr h, int n);
    [DllImport("user32")] public static extern IntPtr GetWindow(IntPtr h, uint c);

    public static string Title(IntPtr h) { var s=new StringBuilder(512); return GetWindowText(h,s,512)>0?s.ToString():""; }
}

// ── Monitor enumeration ───────────────────────────────────────
static List<N.MONITORINFOEX> GetMonitors()
{
    var list = new List<N.MONITORINFOEX>();
    N.MonitorEnumProc cb = (hMon, hdc, ref lp, dw) => {
        var mi = N.MONITORINFOEX.Create();
        if (N.GetMonitorInfo(hMon, ref mi)) list.Add(mi);
        return true;
    };
    N.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
    return list;
}

static string ProcName(IntPtr hwnd)
{
    try { N.GetWindowThreadProcessId(hwnd, out uint pid); using var p=Process.GetProcessById((int)pid); return p.ProcessName; }
    catch { return "?"; }
}

// ── Main ─────────────────────────────────────────────────────
Console.OutputEncoding = Encoding.UTF8;
var sep  = new string('─', 70);
var monitors = GetMonitors();

Console.WriteLine(sep);
Console.WriteLine($"  MONITORS DETECTED: {monitors.Count}");
Console.WriteLine(sep);
foreach (var m in monitors)
{
    Console.WriteLine($"  Device : {m.szDevice}");
    Console.WriteLine($"  Bounds : ({m.rcMonitor.Left},{m.rcMonitor.Top}) {m.rcMonitor.Width}x{m.rcMonitor.Height}");
    Console.WriteLine($"  Work   : ({m.rcWork.Left},{m.rcWork.Top}) {m.rcWork.Width}x{m.rcWork.Height}");
    Console.WriteLine($"  Primary: {(m.dwFlags & N.MONITORINFOF_PRIMARY) != 0}");
    Console.WriteLine();
}

// Find primary work area (needed for correct coord conversion)
var primary = monitors.Find(m => (m.dwFlags & N.MONITORINFOF_PRIMARY) != 0);
Console.WriteLine($"  Primary work area origin: ({primary.rcWork.Left}, {primary.rcWork.Top})");
Console.WriteLine();

// ── Window capture ────────────────────────────────────────────
Console.WriteLine(sep);
Console.WriteLine("  WINDOW CAPTURE");
Console.WriteLine(sep);

int captured=0, skippedNotVisible=0, skippedNoTitle=0,
    skippedToolWindow=0, skippedOwned=0, skippedProcess=0,
    skippedNoMonitor=0;

var skippedProcessSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "explorer","searchhost","startmenuexperiencehost","shellexperiencehost",
      "applicationframehost","systemsettings","textinputhost" };

N.EnumWindows((hwnd, _) => {
    string skipReason = "";

    if (!N.IsWindowVisible(hwnd))          { skippedNotVisible++;  return true; }
    string title = N.Title(hwnd);
    if (string.IsNullOrWhiteSpace(title))  { skippedNoTitle++;     return true; }
    long exStyle = N.GetWindowLongPtr(hwnd, N.GWL_EXSTYLE);
    if ((exStyle & N.WS_EX_TOOLWINDOW)!=0) { skippedToolWindow++;  return true; }
    IntPtr owner = N.GetWindow(hwnd, N.GW_OWNER);
    if (owner != IntPtr.Zero)             { skippedOwned++;        return true; }
    string proc = ProcName(hwnd);
    if (skippedProcessSet.Contains(proc)) { skippedProcess++;      return true; }

    // Get placement
    var wp = N.WINDOWPLACEMENT.Create();
    N.GetWindowPlacement(hwnd, ref wp);

    // Get monitor
    IntPtr hMon = N.MonitorFromWindow(hwnd, N.MONITOR_DEFAULTTONEAREST);
    var mi = N.MONITORINFOEX.Create();
    bool monOk = N.GetMonitorInfo(hMon, ref mi);

    // Find matching monitor in our list
    var matchedMon = monitors.Find(m =>
        string.Equals(m.szDevice, mi.szDevice, StringComparison.OrdinalIgnoreCase));
    bool monitorMatched = matchedMon.szDevice != null;

    if (!monOk || !monitorMatched)
    {
        skippedNoMonitor++;
        skipReason = $"  !! MONITOR LOOKUP FAILED: GetMonitorInfo={monOk}, szDevice=\"{mi.szDevice}\" matched={monitorMatched}";
    }
    else
    {
        // Compute monitor-relative coords
        // rcNormalPosition = workspace coords (primary work-area relative)
        // Step 1: workspace → virtual screen (add primary work area origin)
        int virtualLeft   = wp.rcNormalPosition.Left   + primary.rcWork.Left;
        int virtualTop    = wp.rcNormalPosition.Top    + primary.rcWork.Top;
        int virtualRight  = wp.rcNormalPosition.Right  + primary.rcWork.Left;
        int virtualBottom = wp.rcNormalPosition.Bottom + primary.rcWork.Top;
        // Step 2: virtual → monitor-relative (subtract this monitor's work area)
        int relLeft   = virtualLeft   - matchedMon.rcWork.Left;
        int relTop    = virtualTop    - matchedMon.rcWork.Top;
        int relRight  = virtualRight  - matchedMon.rcWork.Left;
        int relBottom = virtualBottom - matchedMon.rcWork.Top;

        var showName = wp.showCmd switch { 1=>"Normal", 2=>"Minimized", 3=>"Maximized", _=>wp.showCmd.ToString() };

        // Also show the CURRENT (buggy) code's calculation for comparison
        int bugLeft = wp.rcNormalPosition.Left   - matchedMon.rcWork.Left;
        int bugTop  = wp.rcNormalPosition.Top    - matchedMon.rcWork.Top;

        captured++;
        Console.WriteLine($"  [{captured:D3}] {proc,-22} | {showName,-10} | Monitor: {matchedMon.szDevice}");
        Console.WriteLine($"        Title : {(title.Length>60?title[..60]+"…":title)}");
        Console.WriteLine($"        rcNorm: ({wp.rcNormalPosition.Left},{wp.rcNormalPosition.Top}) → ({wp.rcNormalPosition.Right},{wp.rcNormalPosition.Bottom})  [workspace coords]");
        Console.WriteLine($"        Correct relative : ({relLeft},{relTop})→({relRight},{relBottom})");
        Console.WriteLine($"        Current code rel : ({bugLeft},{bugTop}) [DIFF if non-zero primary work origin or secondary monitor]");
        Console.WriteLine($"        Match? {(relLeft==bugLeft&&relTop==bugTop?"✓ same":"✗ DIFFERENT — BUG")}");
        Console.WriteLine();
    }
    if (skipReason != "") Console.WriteLine(skipReason);
    return true;
}, IntPtr.Zero);

Console.WriteLine(sep);
Console.WriteLine($"  SUMMARY");
Console.WriteLine(sep);
Console.WriteLine($"  Captured          : {captured}");
Console.WriteLine($"  Skipped invisible : {skippedNotVisible}");
Console.WriteLine($"  Skipped no title  : {skippedNoTitle}");
Console.WriteLine($"  Skipped tool win  : {skippedToolWindow}");
Console.WriteLine($"  Skipped owned win : {skippedOwned}");
Console.WriteLine($"  Skipped sys proc  : {skippedProcess}");
Console.WriteLine($"  Skipped no monitor: {skippedNoMonitor}");
Console.WriteLine(sep);
'@

# Write to temp file and compile+run with dotnet-script (csx) approach
$tmpCs  = "$env:TEMP\mmr_diag.cs"
$tmpExe = "$env:TEMP\mmr_diag"
$src | Set-Content $tmpCs -Encoding UTF8

Write-Host "Compiling diagnostic..."
& "C:\Users\Acer\AppData\Local\dotnet\dotnet.exe" script $tmpCs 2>&1
