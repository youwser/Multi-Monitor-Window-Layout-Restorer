# Architecture Blueprint: Multi-Monitor Window Layout Restorer

> **Status:** Awaiting Approval — Plan Mode Only. No implementation code has been written.

---

## 1. Project Overview

A lightweight C# .NET 8 WinForms application that runs as a System Tray background process. It detects monitor configuration changes and automatically restores saved window positions and states to match a stored layout profile for that monitor setup.

### Target Framework
- **.NET 8 (Windows)** — `net8.0-windows`
- WinForms (for tray icon and settings dialog)
- Single-file, self-contained publish recommended

---

## 2. Critical Technical Analysis: `GetWindowPlacement` & Multi-Monitor Coordinates

> [!IMPORTANT]
> This is the single most common failure point in multi-monitor window restorers. Understanding the coordinate system is essential before writing any code.

### The Problem: Two Coordinate Spaces

Windows uses **two distinct coordinate systems** that are easily confused:

| Coordinate Type | Origin Point | Usage |
|---|---|---|
| **Virtual Screen Space** | Top-left of the leftmost/topmost monitor | `SetWindowPos`, `WINDOWPLACEMENT.rcNormalPosition` on non-primary monitors — **with a caveat** |
| **Work Area / Primary Monitor Space** | Top-left of the **primary** monitor (0, 0) | Legacy APIs, older documentation |

### The `WINDOWPLACEMENT` Gotcha

`GetWindowPlacement` returns a `WINDOWPLACEMENT` struct containing `rcNormalPosition`. This rect is **workspace coordinates** — it is relative to the **primary monitor's work area origin**, NOT true virtual screen space. This means:

- If a window is on the **primary monitor** at `(100, 100)`, `rcNormalPosition` returns `(100, 100)`.
- If a window is on a **secondary monitor** positioned to the left of the primary (e.g., at virtual `(-1920, 0)`), `rcNormalPosition` will return approximately `(-1820, 100)` — i.e., offset by the monitor's position relative to the primary.
- The offset accounts for taskbar/work area insets on the primary monitor.

### The Solution: Monitor-Relative Positioning

We **must NOT store raw `rcNormalPosition` values directly**. Instead, the `WindowManager` will:

1. Call `GetWindowPlacement` to get the raw rect and window state (`SW_SHOWMAXIMIZED`, `SW_SHOWNORMAL`, etc.).
2. Use `MonitorFromWindow` to determine **which monitor** the window lives on.
3. Use `GetMonitorInfo` to get that monitor's `rcWork` (work area).
4. Convert `rcNormalPosition` to **monitor-relative coordinates** by subtracting the monitor's work area origin.
5. On restore, reverse the process: add the target monitor's work area origin back to get the absolute placement to pass to `SetWindowPlacement`.

This ensures layouts are **portable across different monitor arrangements** (e.g., if the user rearranges monitors in Display Settings, windows still restore to the correct logical monitor).

### Maximized Windows

If `showCmd == SW_SHOWMAXIMIZED`, `SetWindowPlacement` will maximize the window to the correct monitor automatically **as long as** `rcNormalPosition` places the window's center within that monitor's bounds. We store the pre-maximize normal rect (also provided by `WINDOWPLACEMENT`) and set `showCmd` accordingly.

---

## 3. System Architecture

```
+----------------------------------------------------------+
|                     TrayApp (Entry Point)                 |
|  - NotifyIcon + ContextMenuStrip                         |
|  - Wires SystemEvents.DisplaySettingsChanged             |
|  - Orchestrates: DisplayMonitor -> WindowManager ->      |
|                  ProfileStorage                          |
+-----------------------------+----------------------------+
                              |
          +-------------------+-------------------+
          v                   v                   v
  +---------------+  +-----------------+  +------------------+
  | DisplayMonitor|  |  WindowManager  |  |  ProfileStorage  |
  |               |  |                 |  |                  |
  | Enumerates    |  | Enumerates all  |  | Load/Save JSON   |
  | active screens|  | visible windows |  | profiles keyed   |
  | Builds config |  | Fingerprints &  |  | by monitor sig.  |
  | signature     |  | snapshots/re-   |  |                  |
  |               |  | stores layouts  |  |                  |
  +---------------+  +-----------------+  +------------------+
          |                   |
          v                   v
  +--------------------------------------------------------+
  |                    NativeMethods (P/Invoke)             |
  |  EnumWindows, IsWindowVisible, GetWindowText,          |
  |  GetWindowThreadProcessId, GetWindowPlacement,         |
  |  SetWindowPlacement, MonitorFromWindow, GetMonitorInfo,|
  |  EnumDisplayMonitors                                   |
  +--------------------------------------------------------+
```

---

## 4. Core Class Definitions

### 4.1 `NativeMethods` (static class)

Centralizes all P/Invoke declarations. No business logic.

```
Responsibilities:
  - Declare all extern User32.dll and Shcore.dll imports
  - Define all interop structs (WINDOWPLACEMENT, RECT, MONITORINFO, etc.)
  - Expose delegate types (EnumWindowsProc, MonitorEnumProc)

Key P/Invoke signatures:
  - bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam)
  - bool IsWindowVisible(IntPtr hWnd)
  - int  GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount)
  - bool GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId)
  - bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl)
  - bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl)
  - IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags)
  - bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi)
  - bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
                             MonitorEnumProc lpfnEnum, IntPtr dwData)
```

**Structs to define:**

| Struct | Fields |
|---|---|
| `WINDOWPLACEMENT` | `length`, `flags`, `showCmd`, `ptMinPosition`, `ptMaxPosition`, `rcNormalPosition` |
| `RECT` | `Left`, `Top`, `Right`, `Bottom` |
| `POINT` | `X`, `Y` |
| `MONITORINFO` | `cbSize`, `rcMonitor`, `rcWork`, `dwFlags` |

---

### 4.2 `DisplayMonitor` (class)

Represents a single physical monitor and its properties. Also provides static factory methods to enumerate all connected monitors.

```
Properties:
  - string  DeviceName        // e.g., "\\.\DISPLAY1"
  - RECT    Bounds            // virtual screen coordinates
  - RECT    WorkArea          // excludes taskbar
  - bool    IsPrimary
  - uint    WidthPx           // from Bounds
  - uint    HeightPx          // from Bounds

Static Methods:
  - IReadOnlyList<DisplayMonitor> EnumerateAll()
      -> Calls EnumDisplayMonitors + GetMonitorInfo per handle

  - string BuildConfigurationSignature(IEnumerable<DisplayMonitor> monitors)
      -> Sorts monitors by (Left, Top), then creates a deterministic
         string key, e.g.:
         "1920x1080@(0,0)[PRIMARY];2560x1440@(1920,-120)"
      -> This key is used as the profile filename/lookup key
```

> **Note:** Sorting by position before building the signature ensures the key is stable regardless of the order `EnumDisplayMonitors` returns monitors.

---

### 4.3 `WindowSnapshot` (record / data model)

An immutable data record representing a captured window's identity and placement. Serialized to JSON.

```
Properties:
  - string  ProcessName           // e.g., "chrome"
  - string  WindowTitle           // Full title at capture time
  - string  MonitorDeviceName     // Which monitor it was on
  - int     NormalLeft            // Monitor-relative normal rect
  - int     NormalTop
  - int     NormalRight
  - int     NormalBottom
  - int     ShowCmd               // SW_SHOWNORMAL=1, SW_SHOWMAXIMIZED=3, SW_SHOWMINIMIZED=2
```

---

### 4.4 `WindowManager` (class)

The core engine. Responsible for enumerating, fingerprinting, snapshotting, and restoring windows.

```
Dependencies (injected):
  - IEnumerable<DisplayMonitor> currentMonitors

Public Methods:

  LayoutProfile CaptureCurrentLayout()
    1. Call EnumWindows to collect all visible top-level HWNDs
    2. For each HWND:
       a. Call IsWindowVisible -- skip if false
       b. Call GetWindowText -- skip if empty title
       c. Call GetWindowThreadProcessId -> OpenProcess -> get Process.ProcessName
       d. Call MonitorFromWindow(MONITOR_DEFAULTTONEAREST) -> get HMONITOR
       e. Map HMONITOR to DisplayMonitor object
       f. Call GetWindowPlacement -> get rcNormalPosition + showCmd
       g. Convert rcNormalPosition to monitor-relative coords
       h. Build WindowSnapshot record
    3. Return LayoutProfile with list of snapshots + monitor signature

  void RestoreLayout(LayoutProfile profile)
    1. Enumerate current windows (same as capture, step 1-3)
    2. Build a lookup: key = (ProcessName, FuzzyTitle) -> HWND
    3. For each WindowSnapshot in profile:
       a. Find best-matching HWND via fuzzy title match
          (strategy: exact match first, then longest-common-subsequence ratio)
       b. Find target DisplayMonitor by DeviceName
       c. Convert monitor-relative coords back to absolute WINDOWPLACEMENT
       d. Call SetWindowPlacement

Private Helpers:
  - bool IsEligibleWindow(IntPtr hWnd)     // visible, has title, not a tool window
  - string GetProcessName(IntPtr hWnd)
  - DisplayMonitor FindMonitorForHandle(IntPtr hWnd)
  - double FuzzyTitleScore(string stored, string current)
                                           // returns 0.0-1.0; threshold ~0.6
```

**Window Eligibility Filter** — windows are skipped if:
- `IsWindowVisible` returns false
- Title string is empty or whitespace
- Has extended style `WS_EX_TOOLWINDOW` (utility/palette windows)
- Process name is `"explorer"` and title is blank (desktop shell artifacts)

---

### 4.5 `LayoutProfile` (class / data model)

Container for a full saved layout. Serialized as a `.json` file.

```
Properties:
  - string                   MonitorSignature   // key used to match profile to config
  - DateTime                 SavedAt
  - string                   ProfileName        // user-facing label
  - List<WindowSnapshot>     Windows
```

---

### 4.6 `ProfileStorage` (class)

Handles all file I/O for layout profiles using `System.Text.Json`.

```
Constructor:
  - Takes a storagePath (default: %AppData%\WindowLayoutRestorer\profiles\)

Methods:
  - void             Save(LayoutProfile profile)
      -> Filename: SHA256(MonitorSignature)[0..8] + "_" + sanitizedName + ".json"

  - LayoutProfile?   Load(string monitorSignature)
      -> Scans profile directory, deserializes each, finds matching signature

  - IList<LayoutProfile> LoadAll()

  - void             Delete(string monitorSignature)
```

> **Tip:** Using the first 8 chars of the SHA256 hash of the monitor signature as a filename prefix prevents illegal filename characters while keeping files human-inspectable.

---

### 4.7 `TrayApp` (ApplicationContext subclass)

WinForms `ApplicationContext` that owns the tray icon lifecycle and serves as the application orchestrator.

```
Fields:
  - NotifyIcon       _trayIcon
  - ContextMenuStrip _menu
  - WindowManager    _windowManager
  - ProfileStorage   _profileStorage
  - Timer            _restoreDebounceTimer  // 2s debounce on DisplaySettingsChanged

Lifecycle:
  - Constructor: build tray icon + menu, subscribe to SystemEvents.DisplaySettingsChanged
  - OnDisplaySettingsChanged:
      1. Start/reset 2-second debounce timer
      2. On timer tick: re-enumerate monitors, call ProfileStorage.Load(newSignature)
      3. If profile found: call WindowManager.RestoreLayout(profile)
      4. Show balloon notification with result

Menu Actions:
  - "Save Current Layout"  -> WindowManager.Capture() -> prompt name -> ProfileStorage.Save()
  - "Restore Layout"       -> Load profile for current sig -> WindowManager.Restore()
  - "Manage Profiles..."   -> Open lightweight WinForms settings dialog
  - "Exit"                 -> Cleanup, Application.Exit()
```

> **IMPORTANT:** The `DisplaySettingsChanged` event fires **before** the OS has finished repositioning windows. The 2-second debounce timer is critical — without it, we attempt to set window placements while the display driver is still reconfiguring, causing positions to be immediately overridden by the OS.

---

## 5. JSON Profile Schema

A saved layout file (`profiles/a3f9c812_work-desk.json`) looks like:

```json
{
  "monitorSignature": "1920x1080@(0,0)[PRIMARY];2560x1440@(1920,-120)",
  "savedAt": "2026-10-11T12:34:56Z",
  "profileName": "Work Desk",
  "windows": [
    {
      "processName": "chrome",
      "windowTitle": "GitHub - Google Chrome",
      "monitorDeviceName": "\\\\.\\DISPLAY1",
      "normalLeft": 50,
      "normalTop": 30,
      "normalRight": 1870,
      "normalBottom": 1020,
      "showCmd": 3
    },
    {
      "processName": "code",
      "windowTitle": "ARCHITECTURE.md - Multi-Monitor Window Layout Restorer - Visual Studio Code",
      "monitorDeviceName": "\\\\.\\DISPLAY2",
      "normalLeft": 0,
      "normalTop": 0,
      "normalRight": 2560,
      "normalBottom": 1440,
      "showCmd": 3
    },
    {
      "processName": "slack",
      "windowTitle": "Slack",
      "monitorDeviceName": "\\\\.\\DISPLAY1",
      "normalLeft": 0,
      "normalTop": 800,
      "normalRight": 960,
      "normalBottom": 1060,
      "showCmd": 1
    }
  ]
}
```

**`showCmd` Reference:**

| Value | Constant | Meaning |
|---|---|---|
| 1 | `SW_SHOWNORMAL` | Normal window, use `rcNormalPosition` |
| 2 | `SW_SHOWMINIMIZED` | Minimized (we skip restoring these by default) |
| 3 | `SW_SHOWMAXIMIZED` | Maximized to the target monitor |

---

## 6. Project File Structure

```
MultiMonitorRestorer/
+-- MultiMonitorRestorer.csproj
+-- Program.cs                    # Entry point: Application.Run(new TrayApp())
+-- Core/
|   +-- NativeMethods.cs          # All P/Invoke, structs, delegates
|   +-- DisplayMonitor.cs         # Monitor model + enumeration
|   +-- WindowSnapshot.cs         # Immutable window data record
|   +-- LayoutProfile.cs          # Profile container model
|   +-- WindowManager.cs          # Capture + restore engine
|   +-- ProfileStorage.cs         # JSON file I/O
+-- UI/
|   +-- TrayApp.cs                # ApplicationContext, tray icon orchestrator
|   +-- SettingsForm.cs           # Profile management dialog (WinForms)
+-- Utilities/
|   +-- FuzzyMatcher.cs           # Title fuzzy-match scoring utility
+-- profiles/                     # Runtime: %AppData%\WindowLayoutRestorer\profiles\
    +-- (generated .json files)
```

---

## 7. Key Design Decisions & Rationale

| Decision | Rationale |
|---|---|
| **Monitor-relative coords** | Prevents breakage when monitors are rearranged without deleting profiles |
| **Composite fingerprint key** (ProcessName + Title) | HWNDs are ephemeral; this is the most reliable cross-session identity |
| **Fuzzy title matching** | Handles apps that append file names or tab counts to their titles (e.g., browsers, VS Code) |
| **2-second debounce** | `DisplaySettingsChanged` fires before the OS finishes reconfiguring; early calls race with the driver |
| **No HWND caching** | We re-enumerate windows fresh on every capture and restore — avoids stale handle errors |
| **`System.Text.Json`** (not Newtonsoft) | Zero extra dependency; built into .NET 8 |
| **`ApplicationContext` not `Form`** | Allows a true background/tray-only app without a visible main window |

---

## 8. Out of Scope (v1)

- UWP / Windows Store apps (they have different window management APIs)
- Per-virtual-desktop layout profiles
- Auto-save on layout change (only manual save + auto-restore on monitor change)
- Remote/network profile sync

---

*Blueprint authored in Plan Mode. Implementation begins upon user approval.*
