namespace MultiMonitorRestorer.Core;

/// <summary>
/// Container for a complete saved window layout. One instance corresponds
/// to one JSON file on disk and matches a specific monitor configuration.
/// </summary>
public sealed class LayoutProfile
{
    /// <summary>
    /// Deterministic string key produced by
    /// <see cref="DisplayMonitor.BuildConfigurationSignature"/>.
    /// Example: <c>"1920x1080@(0,0)[P];2560x1440@(1920,-120)"</c>
    /// </summary>
    public string MonitorSignature { get; set; } = string.Empty;

    /// <summary>UTC timestamp when this profile was last saved.</summary>
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User-visible label shown in the tray menu.</summary>
    public string ProfileName { get; set; } = "My Layout";

    /// <summary>
    /// DPI-agnostic position-only key produced by
    /// <see cref="DisplayMonitor.BuildPositionSignature"/>.
    /// Used as a fallback lookup when DPI scaling changes cause the
    /// full <see cref="MonitorSignature"/> to no longer match.
    /// Example: <c>"2mon;@(-2560,0);@(0,0)[P]"</c>
    /// </summary>
    public string PositionSignature { get; set; } = string.Empty;

    /// <summary>Snapshots of all captured windows, in capture order.</summary>
    public List<WindowSnapshot> Windows { get; set; } = new();
}
