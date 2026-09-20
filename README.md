# Personal Laptop Guardian

A personal security system that monitors your Windows laptop and sends real-time alerts to your Android phone. The Windows agent detects login attempts, USB connections, network changes, process activity, and more — then syncs everything through Supabase to a Flutter mobile app with push notifications.

**This software is for authorized use on your own devices only.**

## Architecture

```
Windows Agent (C# .NET 10)  →  Supabase (PostgreSQL + Edge Functions)  →  Flutter App (Android)
     ↕                                    ↕                                      ↕
  Local SQLite                     Firebase Cloud Messaging              Push Notifications
```

## Project Structure

```
├── windows-agent/     # C# .NET 10 Worker Service — laptop monitoring agent
├── backend/           # Supabase project — database, auth, edge functions
├── mobile/            # Flutter Android app — dashboard and notifications
├── docs/              # Architecture, API, database, security documentation
└── scripts/           # Setup and utility scripts
```

## Key Features

- Offline-first: events are stored locally and synced when connectivity returns
- Device pairing via one-time code
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

## Getting Started — Windows Agent

```bash
# Build the solution
cd windows-agent
dotnet build LaptopGuardian.slnx

# Run tests
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

## Documentation

- [Architecture](docs/architecture.md)
- [Requirements](docs/requirements.md)
- [Database Schema](docs/database.md)
- [API Reference](docs/api.md)
- [Security](docs/security.md)

## License

MIT
