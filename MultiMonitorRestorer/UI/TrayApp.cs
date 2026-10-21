using Microsoft.Win32;
using MultiMonitorRestorer.Core;
using MultiMonitorRestorer.UI;

namespace MultiMonitorRestorer;

/// <summary>
/// Application orchestrator — runs as a System Tray icon with no visible window.
/// Inherits <see cref="ApplicationContext"/> so the app stays alive without a
/// main form; the message loop is kept alive by the tray icon.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    // ─── Fields ──────────────────────────────────────────────────────────────

    private readonly NotifyIcon        _trayIcon;
    private readonly ContextMenuStrip  _menu;
    private readonly ProfileStorage    _storage;

    /// <summary>
    /// Debounce timer: fires 2 seconds after the last DisplaySettingsChanged
    /// event.  This delay lets the OS finish repositioning windows before we
    /// attempt to restore them.
    /// </summary>
    private readonly System.Windows.Forms.Timer _debounceTimer;

    private const int DebounceMs = 2000;

    // ─── Constructor ─────────────────────────────────────────────────────────

    public TrayApp()
    {
        _storage = new ProfileStorage();

        // ── Debounce timer (not started until a display event fires)
        _debounceTimer = new System.Windows.Forms.Timer { Interval = DebounceMs };
        _debounceTimer.Tick += OnDebounceTimerTick;

        // ── Build tray context menu
        _menu = BuildContextMenu();

        // ── Build tray icon
        _trayIcon = new NotifyIcon
        {
            Text             = "Window Layout Restorer",
            Icon             = LoadTrayIcon(),
            ContextMenuStrip = _menu,
            Visible          = true,
        };
        _trayIcon.DoubleClick += (_, _) => OpenSettingsForm();

        // ── Subscribe to display change events
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    // ─── Display Change Handling ─────────────────────────────────────────────

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Reset the debounce timer on every event so rapid bursts collapse
        // to a single restore attempt 2 s after the last event.
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop(); // one-shot

        var monitors          = DisplayMonitor.EnumerateAll();
        var signature         = DisplayMonitor.BuildConfigurationSignature(monitors);
        var positionSignature = DisplayMonitor.BuildPositionSignature(monitors);
        var profile           = _storage.Load(signature, positionSignature);

        if (profile is null)
        {
            // No saved profile for this configuration — nothing to do
            return;
        }

        var mgr   = new WindowManager(monitors);
        int count = mgr.RestoreLayout(profile);

        _trayIcon.ShowBalloonTip(
            timeout:  3000,
            tipTitle: "Layout Restored",
            tipText:  $"Restored {count} window(s) using profile \"{profile.ProfileName}\".",
            tipIcon:  ToolTipIcon.Info);
    }

    // ─── Context Menu ─────────────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        // Header (non-clickable label)
        var header = new ToolStripLabel("Window Layout Restorer")
        {
            Font      = new Font(SystemFonts.MenuFont!.FontFamily, 9f, FontStyle.Bold),
            ForeColor = SystemColors.GrayText,
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        // Save Current Layout
        var saveItem = new ToolStripMenuItem("&Save Current Layout...");
        saveItem.Click += OnSaveLayoutClicked;
        menu.Items.Add(saveItem);

        // Restore Layout
        var restoreItem = new ToolStripMenuItem("&Restore Layout");
        restoreItem.Click += OnRestoreLayoutClicked;
        menu.Items.Add(restoreItem);

        menu.Items.Add(new ToolStripSeparator());

        // Manage Profiles
        var settingsItem = new ToolStripMenuItem("&Manage Profiles...");
        settingsItem.Click += (_, _) => OpenSettingsForm();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("E&xit");
        exitItem.Click += OnExitClicked;
        menu.Items.Add(exitItem);

        return menu;
    }

    // ─── Menu Actions ────────────────────────────────────────────────────────

    private void OnSaveLayoutClicked(object? sender, EventArgs e)
    {
        string? name = PromptForName("Save Current Layout", "Enter a name for this layout profile:");
        if (name is null) return; // user cancelled

        var monitors = DisplayMonitor.EnumerateAll();
        var mgr      = new WindowManager(monitors);
        var profile  = mgr.CaptureCurrentLayout(name);
        _storage.Save(profile);

        _trayIcon.ShowBalloonTip(
            timeout:  3000,
            tipTitle: "Layout Saved",
            tipText:  $"Saved {profile.Windows.Count} windows as \"{name}\".",
            tipIcon:  ToolTipIcon.Info);
    }

    private void OnRestoreLayoutClicked(object? sender, EventArgs e)
    {
        var monitors          = DisplayMonitor.EnumerateAll();
        var signature         = DisplayMonitor.BuildConfigurationSignature(monitors);
        var positionSignature = DisplayMonitor.BuildPositionSignature(monitors);
        var profile           = _storage.Load(signature, positionSignature);

        if (profile is null)
        {
            MessageBox.Show(
                "No saved profile found for the current monitor configuration.\n\n" +
                "Use \"Save Current Layout\" to create one.",
                "No Profile Found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var mgr   = new WindowManager(monitors);
        int count = mgr.RestoreLayout(profile);

        _trayIcon.ShowBalloonTip(
            timeout:  3000,
            tipTitle: "Layout Restored",
            tipText:  $"Restored {count} window(s) from \"{profile.ProfileName}\".",
            tipIcon:  ToolTipIcon.Info);
    }

    private void OpenSettingsForm()
    {
        // Dispose any previously closed instance
        var form = new SettingsForm(_storage, DisplayMonitor.EnumerateAll);
        form.Show();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        Cleanup();
        Application.Exit();
    }

    // ─── Utilities ────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows an input dialog for text entry. Returns <c>null</c> if cancelled.
    /// </summary>
    private static string? PromptForName(string title, string prompt)
    {
        using var form   = new Form();
        var label        = new Label  { Text = prompt, Left = 12, Top = 12, Width = 360, AutoSize = true };
        var textBox      = new TextBox { Left = 12, Top = 40, Width = 360, Text = "My Layout" };
        var btnOk        = new Button { Text = "Save",   Left = 220, Top = 75, Width = 75, DialogResult = DialogResult.OK };
        var btnCancel    = new Button { Text = "Cancel", Left = 302, Top = 75, Width = 75, DialogResult = DialogResult.Cancel };

        form.Text            = title;
        form.ClientSize      = new Size(392, 116);
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition   = FormStartPosition.CenterScreen;
        form.AcceptButton    = btnOk;
        form.CancelButton    = btnCancel;
        form.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });

        textBox.SelectAll();
        return form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text)
            ? textBox.Text.Trim()
            : null;
    }

    private static Icon LoadTrayIcon()
    {
        // Try to load the embedded icon resource; fall back to the system app icon
        try
        {
            using var stream = typeof(TrayApp).Assembly
                .GetManifestResourceStream("MultiMonitorRestorer.Resources.tray.ico");
            if (stream is not null)
                return new Icon(stream, 16, 16);
        }
        catch { /* fall through */ }

        return SystemIcons.Application;
    }

    // ─── Cleanup ─────────────────────────────────────────────────────────────

    private void Cleanup()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _debounceTimer.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _menu.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Cleanup();
        base.Dispose(disposing);
    }
}
