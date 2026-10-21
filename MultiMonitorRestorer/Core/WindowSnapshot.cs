namespace MultiMonitorRestorer.Core;

/// <summary>
/// Immutable snapshot of a single window's identity and placement
/// at the time it was captured. Serialized directly to JSON.
/// </summary>
/// <remarks>
/// Coordinates (<see cref="NormalLeft"/> … <see cref="NormalBottom"/>) are stored
/// <b>relative to the owning monitor's work-area origin</b>, not raw virtual-screen
/// space.  This makes profiles portable when monitors are rearranged.
/// </remarks>
public sealed class WindowSnapshot
{
    /// <summary>
    /// Lowercase process executable name (no extension), e.g. <c>"chrome"</c>.
    /// Used as the primary identity key when matching windows on restore.
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Full window title at the time of capture, e.g.
    /// <c>"GitHub – Google Chrome"</c>.
    /// Used as the secondary (fuzzy-matched) identity key.
    /// </summary>
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>
    /// GDI device name of the monitor this window was on, e.g. <c>"\\.\DISPLAY2"</c>.
    /// Used to resolve the target monitor on restore.
    /// </summary>
    public string MonitorDeviceName { get; set; } = string.Empty;

    /// <summary>Left edge, relative to the owning monitor's work area.</summary>
    public int NormalLeft { get; set; }

    /// <summary>Top edge, relative to the owning monitor's work area.</summary>
    public int NormalTop { get; set; }

    /// <summary>Right edge, relative to the owning monitor's work area.</summary>
    public int NormalRight { get; set; }

    /// <summary>Bottom edge, relative to the owning monitor's work area.</summary>
    public int NormalBottom { get; set; }

    /// <summary>
    /// Win32 show-command value from <c>WINDOWPLACEMENT.showCmd</c>.
    /// <list type="bullet">
    ///   <item>1 = SW_SHOWNORMAL</item>
    ///   <item>2 = SW_SHOWMINIMIZED (skipped on restore by default)</item>
    ///   <item>3 = SW_SHOWMAXIMIZED</item>
    /// </list>
    /// </summary>
    public int ShowCmd { get; set; } = NativeMethods.SW_SHOWNORMAL;
}
