using System.Diagnostics;
using MultiMonitorRestorer.Utilities;

namespace MultiMonitorRestorer.Core;

/// <summary>
/// Core engine: enumerates all visible top-level windows, captures their
/// placement as monitor-relative snapshots, and restores them on demand.
/// </summary>
internal sealed class WindowManager
{
    // ─── Fields ──────────────────────────────────────────────────────────────

    private readonly IReadOnlyList<DisplayMonitor> _monitors;

    /// <summary>
    /// Process names to always skip (system shell artifacts).
    /// Lower-case, no extension.
    /// </summary>
    private static readonly HashSet<string> SkippedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",        // desktop / taskbar shell
        "searchhost",      // Windows search overlay
        "startmenuexperiencehost",
        "shellexperiencehost",
        "applicationframehost",
        "systemsettings",
        "textinputhost",
    };

    // ─── Constructor ─────────────────────────────────────────────────────────

    public WindowManager(IReadOnlyList<DisplayMonitor> monitors)
    {
        _monitors = monitors;
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Enumerates all eligible visible windows and returns a new
    /// <see cref="LayoutProfile"/> capturing their current placements.
    /// </summary>
    public LayoutProfile CaptureCurrentLayout(string profileName)
    {
        var signature         = DisplayMonitor.BuildConfigurationSignature(_monitors);
        var positionSignature = DisplayMonitor.BuildPositionSignature(_monitors);

        var profile = new LayoutProfile
        {
            MonitorSignature  = signature,
            PositionSignature = positionSignature,
            ProfileName       = profileName,
            SavedAt           = DateTime.UtcNow,
        };

        EnumVisibleWindows(hwnd =>
        {
            var snapshot = TryCreateSnapshot(hwnd);
            if (snapshot is not null)
                profile.Windows.Add(snapshot);
        });

        return profile;
    }

    /// <summary>
    /// Restores all windows listed in <paramref name="profile"/> to their
    /// saved positions.  Windows not currently open are silently skipped.
    /// </summary>
    /// <returns>Number of windows successfully repositioned.</returns>
    public int RestoreLayout(LayoutProfile profile)
    {
        // Build a lookup:  processName -> { title -> hwnd }
        var liveWindows = BuildLiveWindowIndex();
        int restored = 0;

        foreach (var snapshot in profile.Windows)
        {
            // Minimized windows are skipped — restoring them would be jarring
            if (snapshot.ShowCmd == NativeMethods.SW_SHOWMINIMIZED)
                continue;

            if (!liveWindows.TryGetValue(snapshot.ProcessName, out var candidates))
                continue;

            IntPtr hwnd = FuzzyMatcher.FindBestMatch(snapshot, candidates);
            if (hwnd == IntPtr.Zero)
                continue;

            // Find target monitor
            DisplayMonitor? targetMonitor = _monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, snapshot.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));

            // Fall back to primary if stored monitor is no longer connected
            targetMonitor ??= _monitors.FirstOrDefault(m => m.IsPrimary)
                           ?? _monitors.FirstOrDefault();

            if (targetMonitor is null)
                continue;

            ApplyPlacement(hwnd, snapshot, targetMonitor);
            restored++;
        }

        return restored;
    }

    // ─── Private: Enumeration ────────────────────────────────────────────────

    private static void EnumVisibleWindows(Action<IntPtr> callback)
    {
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsEligibleWindow(hwnd))
                callback(hwnd);
            return true; // continue enumeration
        }, IntPtr.Zero);
    }

    /// <summary>
    /// Returns true when a window should be included in capture/restore.
    /// Uses the canonical Alt-Tab heuristic — the same rule Windows itself
    /// uses to decide what appears in the task switcher.
    /// </summary>
    private static bool IsEligibleWindow(IntPtr hwnd)
    {
        // The Alt-Tab heuristic handles visibility, tool-window flag, and
        // owner-chain checks all in one place.
        if (!NativeMethods.IsAltTabWindow(hwnd))
            return false;

        // Must have a non-empty title to be useful for fingerprinting
        string title = NativeMethods.GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
            return false;

        // Skip system shell process windows (desktop, search, start menu, etc.)
        string procName = GetProcessName(hwnd);
        if (SkippedProcesses.Contains(procName))
            return false;

        return true;
    }

    // ─── Private: Snapshot creation ──────────────────────────────────────────

    private WindowSnapshot? TryCreateSnapshot(IntPtr hwnd)
    {
        string title   = NativeMethods.GetWindowTitle(hwnd);
        string process = GetProcessName(hwnd);
        if (string.IsNullOrEmpty(process))
            return null;

        var wp = NativeMethods.WINDOWPLACEMENT.Create();
        if (!NativeMethods.GetWindowPlacement(hwnd, ref wp))
            return null;

        // Identify which monitor this window belongs to
        IntPtr hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        DisplayMonitor? monitor = DisplayMonitor.FromHandle(hMonitor, _monitors);
        if (monitor is null)
            return null;

        // Convert from primary-relative workspace coords to monitor-relative coords
        // rcNormalPosition is workspace-relative (primary work-area origin = 0,0)
        // We subtract the target monitor's work-area origin to get monitor-relative.
        var raw = wp.rcNormalPosition;
        int relLeft   = raw.Left   - monitor.WorkArea.Left;
        int relTop    = raw.Top    - monitor.WorkArea.Top;
        int relRight  = raw.Right  - monitor.WorkArea.Left;
        int relBottom = raw.Bottom - monitor.WorkArea.Top;

        return new WindowSnapshot
        {
            ProcessName      = process,
            WindowTitle      = title,
            MonitorDeviceName = monitor.DeviceName,
            NormalLeft       = relLeft,
            NormalTop        = relTop,
            NormalRight      = relRight,
            NormalBottom     = relBottom,
            ShowCmd          = (int)wp.showCmd,
        };
    }

    // ─── Private: Restore ────────────────────────────────────────────────────

    private static void ApplyPlacement(IntPtr hwnd, WindowSnapshot snapshot, DisplayMonitor monitor)
    {
        var wp = NativeMethods.WINDOWPLACEMENT.Create();
        // Read current placement first to preserve ptMinPosition / ptMaxPosition
        NativeMethods.GetWindowPlacement(hwnd, ref wp);

        // Reverse the monitor-relative offset back to absolute workspace coords
        var absRect = new NativeMethods.RECT
        {
            Left   = snapshot.NormalLeft   + monitor.WorkArea.Left,
            Top    = snapshot.NormalTop    + monitor.WorkArea.Top,
            Right  = snapshot.NormalRight  + monitor.WorkArea.Left,
            Bottom = snapshot.NormalBottom + monitor.WorkArea.Top,
        };

        wp.rcNormalPosition = absRect;
        wp.showCmd          = (uint)snapshot.ShowCmd;

        // For maximized windows: centre of rcNormalPosition must lie within the
        // target monitor so Windows maximizes to the right display.
        // Our monitor-relative → absolute conversion already guarantees this.

        NativeMethods.SetWindowPlacement(hwnd, ref wp);
    }

    // ─── Private: Live window index ──────────────────────────────────────────

    /// <summary>
    /// Builds a two-level dictionary: processName -> (title -> hwnd).
    /// Titles within a process are deduplicated; first-seen wins.
    /// </summary>
    private static Dictionary<string, Dictionary<string, IntPtr>> BuildLiveWindowIndex()
    {
        var index = new Dictionary<string, Dictionary<string, IntPtr>>(StringComparer.OrdinalIgnoreCase);

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsEligibleWindow(hwnd))
                return true;

            string proc  = GetProcessName(hwnd);
            string title = NativeMethods.GetWindowTitle(hwnd);

            if (!index.TryGetValue(proc, out var byTitle))
            {
                byTitle = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
                index[proc] = byTitle;
            }

            byTitle.TryAdd(title, hwnd); // first-seen wins
            return true;
        }, IntPtr.Zero);

        return index;
    }

    // ─── Utility ─────────────────────────────────────────────────────────────

    private static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return string.Empty;
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
