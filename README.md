# Multi-Monitor Window Layout Restorer

A lightweight, native Windows system tray utility built with C# and .NET 8 that saves and automatically restores window positions, sizes, and states across multi-monitor setups (e.g., docking/undocking laptops, switching display inputs, or waking from sleep).

---

## ⚡ Key Features

- **Automatic Layout Restoration on Display Change**: Listens to `SystemEvents.DisplaySettingsChanged` with an intelligent 2-second settle debounce, automatically snapping all your application windows back to their designated screens when displays connect or disconnect.
- **Multi-Monitor Coordinate Normalization**: Maps window bounds to monitor-relative coordinates, accounting for primary work-area offsets and negative virtual screen coordinates (e.g., monitors positioned to the left of or above the primary display).
- **DPI-Agnostic Profile Matching**: Robust against DPI scaling variations between sessions (e.g., laptop displays switching between scaled and native resolutions) using position-based fallback matching.
- **Canonical Alt-Tab Window Filtering**: Uses the Windows Shell Alt-Tab heuristic (`GetAncestor(GA_ROOTOWNER)` + `WS_EX_APPWINDOW`) to capture all real application windows (browsers, IDEs, terminals, Office apps) while ignoring background helpers, tool windows, and system overlays.
- **Fuzzy Window Fingerprinting**: Matches windows across restarts using process names and fuzzy title scoring (Longest Common Subsequence + Dice trigram similarity) so browser windows and code editors with changing titles restore accurately.
- **Lightweight System Tray UI**: Runs quietly in the notification area with minimal resource usage and a single-instance guard.

---

## 📋 Prerequisites

- **Operating System**: Windows 10 (version 1803+) or Windows 11 (64-bit)
- **To Build**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- **To Run**: No dependencies required if using the self-contained single-file build (`publish\MultiMonitorRestorer.exe`).

---

## 📦 Installation & Building

### Option 1: Quick Build (Batch Script)

Run the included `BUILD.bat` script from Command Prompt or PowerShell:

```cmd
BUILD.bat
```

Select:
- **`[1] Build (Release)`**: Compiles the project into `MultiMonitorRestorer\bin\Release\net8.0-windows\`.
- **`[3] Publish (Single-file self-contained .exe)`**: Packages a standalone `.exe` into the `publish\` directory that runs on any modern Windows x64 machine without requiring .NET to be pre-installed.

### Option 2: Build Manually via .NET CLI

Clone the repository and run:

```powershell
# Restore dependencies and build
dotnet build MultiMonitorRestorer\MultiMonitorRestorer.csproj -c Release

# Or publish a standalone single-file executable
dotnet publish MultiMonitorRestorer\MultiMonitorRestorer.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  -o .\publish
```

The resulting executable will be located at:
```
publish\MultiMonitorRestorer.exe
```

---

## 🚀 How to Use

### 1. Launch the Application
Run `MultiMonitorRestorer.exe`. The app starts silently and places an icon in your Windows **System Tray** (near the clock in the taskbar). If you do not see it immediately, click the **`^` (Show hidden icons)** arrow.

### 2. Save Your Current Layout
1. Arrange all your application windows (browsers, editors, chat apps, etc.) on your monitors exactly where you want them.
2. **Right-click the tray icon** and select **"Save Current Layout..."**.
3. Enter a friendly name (e.g., `Desk Setup`, `Dual 1440p`, `Laptop Only`) and click **Save**.
4. A notification balloon will confirm the number of windows saved for your current monitor setup.

### 3. Automatic Restoration
Whenever your monitor configuration changes (for example, unplugging or replugging an external display or docking station):
- The app automatically detects the display change.
- It waits 2 seconds for Windows to finish re-enumerating screens.
- It finds the layout profile matching your active display configuration and automatically moves all open windows back to their saved positions.

### 4. Manual Restoration
You can manually force a layout restore at any time:
- **Right-click the tray icon** -> Click **"Restore Layout"**.

### 5. Manage Profiles
- **Double-click the tray icon** or **Right-click -> "Manage Profiles..."** to open the profile manager.
- View all saved profiles, inspect which monitor signature they belong to, or delete outdated profiles.

---

## ⚙️ Start Automatically with Windows (Optional)

To have MultiMonitorRestorer run on system boot:

1. Press `Win + R`, type `shell:startup`, and press `Enter`.
2. Right-click inside the Startup folder -> **New** -> **Shortcut**.
3. Browse to your `MultiMonitorRestorer.exe` path and click **Finish**.

---

## 📁 Profile Storage

Profiles are stored as formatted JSON files in:
```
%AppData%\WindowLayoutRestorer\profiles\
```

Each profile contains:
- The monitor configuration signature and DPI-fallback position signature.
- Window process names, titles, target monitor device names (`\\.\DISPLAY#`), and monitor-relative placement coordinates.

---

## 🛠️ Diagnostics & Troubleshooting

A diagnostic tool is provided in the repository to inspect monitor enumeration and window capture logic:

```powershell
# Run the diagnostic project
dotnet run --project Diagnostic\Diagnostic.csproj -c Release
```

This will print:
- All detected displays, their virtual bounds, work areas, and primary status.
- Every visible window detected, showing whether it qualifies under the Alt-Tab heuristic and its calculated monitor-relative placement.

---

## 🏗️ Architecture & Technical Details

For an in-depth breakdown of:
- Virtual screen space coordinates and `GetWindowPlacement` caveats
- Window fingerprinting algorithms (LCS + Dice trigram matching)
- `User32.dll` P/Invoke definitions and DPI awareness (`PerMonitorV2`)

See [ARCHITECTURE.md](ARCHITECTURE.md).

---

## 📄 License

This project is open source and available under the [MIT License](LICENSE).
