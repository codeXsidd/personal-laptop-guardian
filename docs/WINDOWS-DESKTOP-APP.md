# Laptop Guardian — Windows Desktop App

## Overview

The Windows Desktop App is a WPF-based GUI dashboard that provides a visual interface for the Laptop Guardian agent service. It communicates with the running Windows service via named pipe IPC — it does **not** replace the agent, contain backend credentials, or run its own monitoring.

## Architecture

```
Desktop App (WPF)  ──── Named Pipe IPC ────  Agent Service
     │                                             │
     ├── Dashboard (metrics, status, events)       ├── Monitoring
     ├── Pair Phone (code, QR, countdown)          ├── Event Collection
     ├── Activity (filtered event log)             ├── Sync Engine
     ├── Device (identity, status)                 ├── Heartbeat
     └── Settings (tray, service controls)         └── IPC Server
```

The desktop app reads event history directly from the local SQLite database (`C:\ProgramData\LaptopGuardian\guardian.db`) in read-only mode. All commands (refresh pairing code, unpair) go through IPC.

## Technology

- **Framework**: WPF (.NET 10, `net10.0-windows`)
- **UI Pattern**: MVVM with CommunityToolkit.Mvvm
- **IPC**: Named pipes (`LaptopGuardianIPC`)
- **QR Codes**: QRCoder 1.8.0
- **Database**: Microsoft.Data.Sqlite (read-only)
- **System Tray**: WinForms NotifyIcon in WPF host
- **Styling**: Custom Windows 11-inspired theme with card layouts

## Screens

### Dashboard
- Protection status banner (Protected / Awaiting Pairing / Offline / Service Offline)
- Phone connection card showing paired/not-paired state
- System metrics (CPU, Memory, Disk, Battery) with progress bars
- Sync status (pending / total events)
- Recent activity list with severity icons and time-ago display

### Pair Phone
- Large 6-character pairing code display when not paired
- Countdown timer showing code expiry
- **Copy Code** button — copies to clipboard
- **Show QR** button — opens dialog with scannable QR code (`laptopguardian://pair?code=...`)
- **New Code** button — requests fresh code from agent via IPC
- When paired: shows "Phone Connected" with paired date and **Unpair Device** button

### Activity
- Scrollable event log with filter (All / Login-Logout / Process / USB / Network / File Access / System)
- Shows severity icon, event type, sync status, and relative time
- Refresh button to reload from database

### Device
- Device identity (ID, machine name, hostname, OS)
- Status indicators (service, network, paired)
- Paired-at timestamp
- Pending/total sync counts
- **Unpair Device** button with confirmation dialog

### Settings
- **Behavior**: Minimize to tray, launch on startup, show notifications
- **Service Controls**: Start / Stop / Restart buttons (invokes `sc.exe` with UAC elevation), status display, refresh, and link to Services panel
- **Save Settings** persists to `%LocalAppData%\LaptopGuardian\desktop-settings.json`

## System Tray

When "Minimize to tray" is enabled (default), closing the window hides to tray instead of exiting. The tray icon provides:
- Double-click to restore
- Right-click context menu: Open / Exit

## IPC Commands

The desktop app sends these commands to the agent service:

| Command | Purpose |
|---------|---------|
| `status` | Get service state, pairing status, metrics, event counts |
| `events` | Get recent pending events |
| `refresh-code` | Generate a new temporary pairing code |
| `unpair` | Unpair the device (clears pairing, calls backend) |

## Security

The desktop app does **not** contain:
- Supabase service-role key or URL
- Firebase server credentials
- Device API key (stored only in agent's encrypted identity)
- Any backend credentials

All sensitive operations are delegated to the agent service through IPC.

## Project Structure

```
windows-agent/
├── src/
│   ├── LaptopGuardian.Agent/         # Windows service (unchanged)
│   │   └── Ipc/IpcServer.cs          # Named pipe server
│   └── LaptopGuardian.Desktop/       # WPF desktop app
│       ├── App.xaml(.cs)              # Theme, DI, startup
│       ├── MainWindow.xaml(.cs)       # Sidebar nav, system tray
│       ├── ViewModels/                # MVVM ViewModels
│       ├── Views/                     # XAML UserControls + QR dialog
│       ├── Services/                  # IpcClient, AgentStatusService, EventStoreReader, QrCodeGenerator
│       ├── Converters/                # BoolToVisibility, InverseBool, PercentToWidth
│       └── GlobalUsings.cs
└── tests/
    └── LaptopGuardian.Desktop.Tests/  # 61 unit tests
```

## Building

```bash
cd windows-agent
dotnet build src/LaptopGuardian.Desktop/LaptopGuardian.Desktop.csproj
```

## Running

```bash
dotnet run --project src/LaptopGuardian.Desktop/LaptopGuardian.Desktop.csproj
```

The agent service must be running for the dashboard to show live data. Without the service, the app displays "Service Offline" status.

## Testing

```bash
cd windows-agent
dotnet test
```

Runs all 230 tests (169 agent + 61 desktop).

## Manual Test Procedure

1. Start the Laptop Guardian Windows service
2. Launch the desktop app
3. Verify Dashboard shows real metrics and "Agent running" status
4. Navigate to Pair Phone — verify code appears with countdown
5. Click "Copy Code" — verify code is in clipboard
6. Click "Show QR" — verify QR dialog opens with scannable code
7. Click "New Code" — verify countdown resets
8. Pair from mobile app using the code
9. Verify Dashboard shows "Phone Connected" card
10. Verify Pair Phone shows "Phone Connected" state with Unpair button
11. Navigate to Device — verify identity and status
12. Navigate to Activity — verify event list with filters
13. Navigate to Settings — verify service status shows "Running"
14. Click Stop — verify service stops (requires admin)
15. Click Start — verify service restarts
16. Close window — verify it minimizes to tray
17. Right-click tray icon — verify context menu
18. Exit from tray — verify app closes
