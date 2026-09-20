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
│   │   ├── migrations/   # 11 SQL migration files (schema, functions, RLS)
│   │   └── tests/        # pgTAP database tests
│   └── functions/        # 4 Edge Functions + shared utilities
├── mobile/            # Flutter Android app — dashboard and notifications (planned)
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

# Run tests (99 tests)
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

## Implementation Status

- [x] **Phase 1** -- Windows Agent Foundation (complete)
- [x] **Phase 2** -- Supabase Backend Foundation (complete, schema + edge functions + pgTAP tests)
- [x] **Phase 3** -- Agent-to-Backend Integration (complete, sync + pairing + heartbeat)
- [x] **Phase 4** -- Core Windows Monitors (complete, session/process/USB/network)
- [ ] **Phase 5** -- Flutter Mobile App
- [ ] **Phase 6** -- Push Notifications

## Documentation

- [Architecture](docs/architecture.md)
- [Requirements](docs/requirements.md)
- [Database Schema](docs/database.md)
- [API Reference](docs/api.md)
- [Security](docs/security.md)

## License

MIT
