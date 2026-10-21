using MultiMonitorRestorer;

// Prevent multiple instances via a named mutex
const string MutexName = "Global\\MultiMonitorRestorer_SingleInstance";
using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

if (!createdNew)
{
    MessageBox.Show(
        "Window Layout Restorer is already running.\nCheck the system tray.",
        "Already Running",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
    return;
}

// Enable PerMonitorV2 DPI awareness (also declared in app.manifest, belt+suspenders)
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

// Run as a tray-only application (no main window)
Application.Run(new TrayApp());
