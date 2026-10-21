using System.Runtime.InteropServices;
using System.Text;

namespace MultiMonitorRestorer.Core;

/// <summary>
/// All P/Invoke declarations for User32.dll and related Win32 APIs.
/// No business logic lives here — pure interop layer.
/// </summary>
internal static class NativeMethods
{
    // ─── Constants ───────────────────────────────────────────────────────────

    /// <summary>showCmd values for WINDOWPLACEMENT.</summary>
    internal const int SW_SHOWNORMAL    = 1;
    internal const int SW_SHOWMINIMIZED = 2;
    internal const int SW_SHOWMAXIMIZED = 3;

    /// <summary>dwFlags for MonitorFromWindow — return nearest monitor if window is off-screen.</summary>
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    internal const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

    /// <summary>dwFlags for GetMonitorInfo — set if monitor is the primary display.</summary>
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;

    /// <summary>Window extended style: tool window (excluded from Alt+Tab and our capture).</summary>
    internal const long WS_EX_TOOLWINDOW = 0x00000080L;

    /// <summary>
    /// Window extended style: force into Alt+Tab list even when owned.
    /// A window with this flag is always eligible regardless of having an owner.
    /// </summary>
    internal const long WS_EX_APPWINDOW = 0x00040000L;

    /// <summary>GetWindowLongPtr index for extended window style.</summary>
    internal const int GWL_EXSTYLE = -20;

    /// <summary>GetAncestor flag — walks the owner chain to the root owner.</summary>
    internal const uint GA_ROOTOWNER = 3;

    // ─── Structs ─────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width  => Right  - Left;
        public int Height => Bottom - Top;

        public override string ToString() => $"({Left},{Top})-({Right},{Bottom})";
    }

    /// <summary>
    /// Describes size, position, and state of a window.
    /// NOTE: rcNormalPosition is relative to the primary monitor's work-area origin,
    ///       NOT raw virtual-screen coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        public uint   length;
        public uint   flags;
        public uint   showCmd;
        public POINT  ptMinPosition;
        public POINT  ptMaxPosition;
        public RECT   rcNormalPosition;

        /// <summary>Returns a correctly-initialized instance with length set.</summary>
        public static WINDOWPLACEMENT Create()
        {
            var wp = new WINDOWPLACEMENT();
            wp.length = (uint)Marshal.SizeOf(wp);
            return wp;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MONITORINFOEX
    {
        public uint   cbSize;
        public RECT   rcMonitor;   // Full monitor bounds in virtual-screen coords
        public RECT   rcWork;      // Work area (excludes taskbar)
        public uint   dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;    // e.g. "\\.\DISPLAY1"

        public static MONITORINFOEX Create()
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = (uint)Marshal.SizeOf(mi);
            mi.szDevice = string.Empty;
            return mi;
        }
    }

    // ─── Delegates ───────────────────────────────────────────────────────────

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    // ─── User32.dll Imports ──────────────────────────────────────────────────

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    /// <summary>Returns extended window style flags.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern long GetWindowLongPtr(IntPtr hWnd, int nIndex);

    /// <summary>
    /// Tests whether a window is a real top-level application window
    /// (not a child, owner, or legacy IsWindowVisible check substitute).
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    internal const uint GW_OWNER = 4;

    // ─── Helper Wrappers ─────────────────────────────────────────────────────

    /// <summary>Reads the window title safely, returning an empty string on failure.</summary>
    internal static string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(512);
        return GetWindowText(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>Gets the extended style bitmask for a window handle.</summary>
    internal static long GetExStyle(IntPtr hWnd) => GetWindowLongPtr(hWnd, GWL_EXSTYLE);

    /// <summary>
    /// Walks the owner chain to the topmost owner. Used by the Alt-Tab heuristic
    /// to detect splash screens and pop-overs (whose root owner has a taskbar button).
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    /// <summary>Returns true if the window is minimized (iconic).</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    /// <summary>
    /// Implements the canonical Alt-Tab window visibility heuristic.
    /// Returns true when a window should appear in the task switcher and
    /// therefore qualifies for layout capture.
    /// Based on Raymond Chen's "How to detect if a window would appear in
    /// the Alt+Tab list" algorithm.
    /// </summary>
    internal static bool IsAltTabWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd))
            return false;

        // Skip tool windows unless they also carry WS_EX_APPWINDOW
        long exStyle = GetExStyle(hwnd);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
            return false;

        // Walk the owner chain.  If the root owner of this window has a
        // different handle than the window itself (and that owner is also
        // visible), then this window is an owned popup/splash and shouldn't
        // get its own capture entry.  Exception: WS_EX_APPWINDOW overrides.
        if ((exStyle & WS_EX_APPWINDOW) != 0)
            return true;   // explicitly forced into Alt-Tab

        IntPtr rootOwner = GetAncestor(hwnd, GA_ROOTOWNER);
        if (rootOwner != hwnd)
        {
            // The root owner is a different window.  Only exclude this window
            // if the root owner itself is a visible, non-iconic application
            // window (the typical splash-screen or secondary-window case).
            if (IsWindowVisible(rootOwner) && !IsIconic(rootOwner))
                return false;
        }

        return true;
    }
}
