# Personal Laptop Guardian

A personal security system that monitors your Windows laptop and sends real-time alerts to your Android phone. The Windows agent detects login attempts, USB connections, network changes, process activity, and more -- then syncs everything through Supabase to a Flutter mobile app with push notifications.

**This software is for authorized use on your own devices only.**

## Architecture

```
Windows Agent (C# .NET 10)  ->  Supabase (PostgreSQL + Edge Functions)  ->  Flutter App (Android)
     |                                    |                                      |
  Local SQLite                     Firebase Cloud Messaging              Push Notifications
```

## Project Structure

```
├── windows-agent/     # C# .NET 10 Worker Service — laptop monitoring agent
├── backend/           # Supabase project — database, auth, edge functions
│   ├── supabase/
│   │   ├── migrations/   # 12 SQL migration files (schema, functions, RLS)
│   │   └── tests/        # pgTAP database tests
│   └── functions/        # 5 Edge Functions + shared utilities
├── mobile/            # Flutter Android app — dashboard, event history, reports
├── docs/              # Architecture, API, database, security documentation
└── scripts/           # Setup and utility scripts
```

## Key Features

- Offline-first: events are stored locally and synced when connectivity returns
- Device pairing via one-time 6-character code
- Push notifications for high-severity events
- Login/logout and failed login detection
- Process and application monitoring
- USB device tracking
- Network change monitoring
- Windows Event Log collection
- File-access auditing for configured directories
- System metrics (CPU, memory, disk, battery)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0 or later)
- Windows 10/11 (for running the agent)
- [Supabase CLI](https://supabase.com/docs/guides/cli) (for local backend development)
- Docker (for local Supabase)

## Getting Started -- Windows Agent

```bash
# Build the solution
cd windows-agent
dotnet build LaptopGuardian.slnx

# Run tests (165 agent tests)
dotnet test LaptopGuardian.slnx

# Run the agent locally (console mode)
dotnet run --project src/LaptopGuardian.Agent
```

The agent stores data in `%ProgramData%\LaptopGuardian\` by default. Override via `appsettings.json`:

```json
{
  "Agent": {
    "DataDirectory": "C:\\custom\\path"
  }
}
```

## Getting Started -- Backend

```bash
# Start local Supabase (requires Docker)
cd backend
supabase start

# Migrations run automatically on start
# Run database tests
supabase test db

# Deploy edge functions (local)
supabase functions serve
```

### Edge Functions

| Function | Auth | Description |
|----------|------|-------------|
| `register-device` | None | Agent registers itself, gets API key + pairing code |
| `pair-device` | JWT | Mobile app claims a pairing code, links device to user |
| `ingest-events` | API Key | Agent uploads event batches (max 100, idempotent) |
| `heartbeat` | API Key | Agent sends liveness signal + system metrics |
| `send-notification` | Service Role Key | Sends FCM push notifications for qualifying events |

### Database

8 tables with Row Level Security:
- `profiles` -- user accounts (auto-created from Supabase Auth)
- `devices` -- registered Windows laptops
- `activity_events` -- events from the agent (deterministic UUIDs for dedup)
- `pairing_codes` -- short-lived device pairing codes
- `heartbeats` -- device liveness and system metrics
- `notification_tokens` -- FCM push tokens
- `notification_settings` -- per-device notification preferences
- `admin_actions` -- audit log

## Getting Started -- Mobile App

```bash
cd mobile

# Install dependencies
flutter pub get

# Run tests (73 tests)
flutter test

# Static analysis
flutter analyze

# Build debug APK (requires Supabase credentials)
flutter build apk --debug \
  --dart-define=SUPABASE_URL=https://your-project.supabase.co \
  --dart-define=SUPABASE_ANON_KEY=your-anon-key

# Run on connected device
flutter run \
  --dart-define=SUPABASE_URL=https://your-project.supabase.co \
  --dart-define=SUPABASE_ANON_KEY=your-anon-key
```

Supabase credentials are passed at build time via `--dart-define`. They are never committed to the repository.

### Mobile App Screens

| Screen | Description |
|--------|-------------|
| Login / Register | Supabase Auth email/password |
| Devices | List of paired devices with status |
| Pair Device | Enter 6-character pairing code |
| Dashboard | Device status, metrics, recent events, quick actions |
| Activity Timeline | Date-grouped events with filtering, pagination, detail sheet |
| Login History | Session login/logout/lock/unlock events |
| App History | Process start/stop events |
| USB History | USB connect/disconnect events |
| Network History | Network events |
| File Audit | File access audit events |
| Windows Event Log | System/Application event log entries |
| Reports | CPU/memory/battery charts, event breakdown |
| Notification Settings | Per-device notification category toggles and severity thresholds |
| Settings | User profile, notifications, sign out |

## Implementation Status

- [x] **Phase 1** -- Windows Agent Foundation (complete)
- [x] **Phase 2** -- Supabase Backend Foundation (complete, schema + edge functions + pgTAP tests)
- [x] **Phase 3** -- Agent-to-Backend Integration (complete, sync + pairing + heartbeat)
- [x] **Phase 4** -- Core Windows Monitors (complete, session/process/USB/network)
- [x] **Phase 5** -- Advanced Windows Monitoring (complete, file audit/event log/system metrics)
- [x] **Phase 6** -- Flutter Mobile App (complete, auth/pairing/dashboard/history/reports)
- [x] **Phase 7** -- Event History Screens (complete, filtering/date-grouping/error-states/detail-enhancement)
- [x] **Phase 8** -- Push Notifications (complete, FCM/Edge Function/notification settings/channels)
- [x] **Phase 9** -- Polish, Hardening & Security (complete)
  - SQLite: busy_timeout, corruption recovery, 30-day retention cleanup
  - Supabase: JWT auth fix, pair-device race condition fix, RLS device-ownership checks, data retention functions
  - Flutter: stream subscription leak fixes, mounted checks, friendly error messages, dead code cleanup
  - Windows Agent: stop timeouts (30s), Debug.WriteLine removal
  - Security: no secrets in logs, no raw error exposure, .gitignore hardened

## Documentation

- [Architecture](docs/architecture.md)
- [Requirements](docs/requirements.md)
- [Database Schema](docs/database.md)
- [API Reference](docs/api.md)
- [Security](docs/security.md)
- [Windows Monitoring](docs/windows-monitoring.md)
- [Mobile App](docs/mobile-app.md)

## License

MIT
