using MultiMonitorRestorer.Core;

namespace MultiMonitorRestorer.UI;

/// <summary>
/// A simple WinForms dialog for managing saved layout profiles.
/// Shows all saved profiles in a list with options to rename, delete,
/// and restore them manually.
/// </summary>
internal sealed class SettingsForm : Form
{
    // ─── Dependencies ────────────────────────────────────────────────────────

    private readonly ProfileStorage _storage;
    private readonly Func<IReadOnlyList<DisplayMonitor>> _getMonitors;

    // ─── Controls ────────────────────────────────────────────────────────────

    private readonly ListView      _listView;
    private readonly Button        _btnDelete;
    private readonly Button        _btnRestore;
    private readonly Button        _btnOpenFolder;
    private readonly Button        _btnClose;
    private readonly Label         _lblCurrentSig;

    // ─── Constructor ─────────────────────────────────────────────────────────

    public SettingsForm(ProfileStorage storage, Func<IReadOnlyList<DisplayMonitor>> getMonitors)
    {
        _storage     = storage;
        _getMonitors = getMonitors;

        Text            = "Window Layout Restorer — Saved Profiles";
        Size            = new Size(680, 420);
        MinimumSize     = new Size(480, 300);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;

        // ── Current signature label
        _lblCurrentSig = new Label
        {
            Dock      = DockStyle.Top,
            Height    = 40,
            Padding   = new Padding(8, 8, 8, 0),
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 8f),
        };

        // ── ListView
        _listView = new ListView
        {
            Dock          = DockStyle.Fill,
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = true,
            MultiSelect   = false,
        };
        _listView.Columns.Add("Profile Name",      200);
        _listView.Columns.Add("Monitor Config",    260);
        _listView.Columns.Add("Saved",             120);
        _listView.SelectedIndexChanged += (_, _) => UpdateButtonStates();

        // ── Bottom button strip
        var buttonPanel = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            Height        = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding       = new Padding(6),
        };

        _btnClose = new Button { Text = "Close", Width = 80 };
        _btnClose.Click += (_, _) => Close();

        _btnOpenFolder = new Button { Text = "Open Folder", Width = 100 };
        _btnOpenFolder.Click += (_, _) =>
            System.Diagnostics.Process.Start("explorer.exe", _storage.ProfilesDirectory);

        _btnRestore = new Button { Text = "Restore Selected", Width = 130, Enabled = false };
        _btnRestore.Click += OnRestoreClicked;

        _btnDelete = new Button { Text = "Delete", Width = 80, Enabled = false };
        _btnDelete.Click += OnDeleteClicked;

        buttonPanel.Controls.AddRange(new Control[]
            { _btnClose, _btnOpenFolder, _btnRestore, _btnDelete });

        Controls.Add(_listView);
        Controls.Add(buttonPanel);
        Controls.Add(_lblCurrentSig);

        Load += (_, _) => RefreshList();
    }

    // ─── List Management ─────────────────────────────────────────────────────

    private void RefreshList()
    {
        _listView.Items.Clear();
        var profiles = _storage.LoadAll();

        var currentSig = DisplayMonitor.BuildConfigurationSignature(_getMonitors());
        _lblCurrentSig.Text = $"Current monitor config:  {currentSig}";

        foreach (var p in profiles.OrderByDescending(p => p.SavedAt))
        {
            var item = new ListViewItem(p.ProfileName)
            {
                Tag = p,
            };
            item.SubItems.Add(p.MonitorSignature);
            item.SubItems.Add(p.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));

            // Highlight profiles matching the current monitor config
            if (string.Equals(p.MonitorSignature, currentSig, StringComparison.Ordinal))
                item.ForeColor = Color.DarkGreen;

            _listView.Items.Add(item);
        }

        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        bool hasSelection      = _listView.SelectedItems.Count > 0;
        _btnDelete.Enabled    = hasSelection;
        _btnRestore.Enabled   = hasSelection;
    }

    // ─── Button Handlers ─────────────────────────────────────────────────────

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count == 0) return;
        var profile = (LayoutProfile)_listView.SelectedItems[0].Tag!;

        var confirm = MessageBox.Show(
            $"Delete profile \"{profile.ProfileName}\"?",
            "Confirm Delete",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm == DialogResult.Yes)
        {
            _storage.Delete(profile.MonitorSignature);
            RefreshList();
        }
    }

    private void OnRestoreClicked(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count == 0) return;
        var profile  = (LayoutProfile)_listView.SelectedItems[0].Tag!;
        var monitors = _getMonitors();
        var mgr      = new WindowManager(monitors);
        int count    = mgr.RestoreLayout(profile);

        MessageBox.Show(
            $"Restored {count} window(s) from \"{profile.ProfileName}\".",
            "Restore Complete",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
