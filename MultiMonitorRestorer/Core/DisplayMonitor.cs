using System.Security.Cryptography;
using System.Text;

namespace MultiMonitorRestorer.Core;

/// <summary>
/// Represents a single physical monitor and exposes static helpers
/// to enumerate all connected displays and build a deterministic
/// configuration signature used as the profile lookup key.
/// </summary>
internal sealed class DisplayMonitor
{
    // ─── Properties ──────────────────────────────────────────────────────────

    /// <summary>GDI device name, e.g. <c>\\.\DISPLAY1</c>.</summary>
    public string DeviceName { get; }

    /// <summary>Full monitor bounds in virtual-screen coordinates.</summary>
    public NativeMethods.RECT Bounds { get; }

    /// <summary>Work area (monitor bounds minus taskbar) in virtual-screen coordinates.</summary>
    public NativeMethods.RECT WorkArea { get; }

    /// <summary>True if this is the primary display.</summary>
    public bool IsPrimary { get; }

    public int WidthPx  => Bounds.Width;
    public int HeightPx => Bounds.Height;

    // ─── Constructor ─────────────────────────────────────────────────────────

    private DisplayMonitor(string deviceName, NativeMethods.RECT bounds, NativeMethods.RECT workArea, bool isPrimary)
    {
        DeviceName = deviceName;
        Bounds     = bounds;
        WorkArea   = workArea;
        IsPrimary  = isPrimary;
    }

    // ─── Static Enumeration ──────────────────────────────────────────────────

    /// <summary>
    /// Enumerates all currently connected monitors via <c>EnumDisplayMonitors</c>.
    /// </summary>
    public static IReadOnlyList<DisplayMonitor> EnumerateAll()
    {
        var monitors = new List<DisplayMonitor>();

        // Use an explicit delegate to avoid C# restrictions on ref-parameter discards in lambdas
        NativeMethods.MonitorEnumProc callback = MonitorEnumCallback;
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        return monitors;

        // Local function captures 'monitors' list
        bool MonitorEnumCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData)
        {
            var mi = NativeMethods.MONITORINFOEX.Create();
            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                monitors.Add(new DisplayMonitor(
                    mi.szDevice,
                    mi.rcMonitor,
                    mi.rcWork,
                    (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
            }
            return true; // continue enumeration
        }
    }

    /// <summary>
    /// Finds the <see cref="DisplayMonitor"/> that owns the given HMONITOR handle.
    /// Returns <c>null</c> if no match is found.
    /// </summary>
    public static DisplayMonitor? FromHandle(IntPtr hMonitor, IReadOnlyList<DisplayMonitor> monitors)
    {
        var mi = NativeMethods.MONITORINFOEX.Create();
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            return null;

        // Match by device name — most reliable cross-session identifier
        return monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, mi.szDevice, StringComparison.OrdinalIgnoreCase));
    }

    // ─── Signature Building ──────────────────────────────────────────────────

    /// <summary>
    /// Produces a stable, deterministic string that uniquely identifies a
    /// monitor configuration (count, resolutions, positions).
    /// Example: <c>"1920x1080@(0,0)[P];2560x1440@(1920,-120)"</c>
    /// Sorted by position so the key is stable regardless of enumeration order.
    /// </summary>
    public static string BuildConfigurationSignature(IEnumerable<DisplayMonitor> monitors)
    {
        var sorted = monitors
            .OrderBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .ToList();

        var parts = sorted.Select(m =>
            $"{m.WidthPx}x{m.HeightPx}@({m.Bounds.Left},{m.Bounds.Top}){(m.IsPrimary ? "[P]" : string.Empty)}");

        return string.Join(";", parts);
    }

    /// <summary>
    /// Builds a position-only (DPI-agnostic) signature using only monitor
    /// count and their virtual-screen origins.  Used as a fallback when the
    /// full signature (which includes resolution) doesn't match — e.g. after
    /// the user changes DPI scaling on the laptop screen.
    /// Example: <c>"2mon@(0,0);@(-2560,0)"</c>
    /// </summary>
    public static string BuildPositionSignature(IEnumerable<DisplayMonitor> monitors)
    {
        var sorted = monitors
            .OrderBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .ToList();

        var parts = sorted.Select(m =>
            $"@({m.Bounds.Left},{m.Bounds.Top}){(m.IsPrimary ? "[P]" : string.Empty)}");

        return $"{sorted.Count}mon;" + string.Join(";", parts);
    }

    /// <summary>
    /// Returns a short (8-char hex) SHA-256 hash of the configuration signature,
    /// safe to use as a filename prefix.
    /// </summary>
    public static string HashSignature(string signature)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    public override string ToString() =>
        $"{DeviceName} {WidthPx}x{HeightPx} @ ({Bounds.Left},{Bounds.Top}){(IsPrimary ? " [PRIMARY]" : string.Empty)}";
}
