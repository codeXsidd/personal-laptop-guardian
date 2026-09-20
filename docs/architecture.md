# Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Windows Laptop                              │
│                                                                     │
│  ┌───────────────────────────────────────┐                          │
│  │  Windows Agent (.NET 10 Worker Svc)   │                          │
│  │                                        │                          │
│  │  ┌──────────┐  ┌──────────────────┐   │                          │
│  │  │ Event    │  │  Sync Engine     │   │                          │
│  │  │ Monitors │──▶  (offline queue) │───┼──── HTTPS ──┐            │
│  │  └──────────┘  └──────────────────┘   │             │            │
│  │                 ┌──────────────────┐   │             │            │
│  │                 │  Local SQLite    │   │             │            │
│  │                 └──────────────────┘   │             │            │
│  └───────────────────────────────────────┘             │            │
└─────────────────────────────────────────────────────────┼────────────┘
                                                          │
                                                          ▼
                                              ┌───────────────────┐
                                              │  Supabase Backend  │
                                              │                     │
                                              │  ┌───────────────┐ │
                                              │  │ PostgreSQL DB  │ │
                                              │  │ (RLS enforced) │ │
                                              │  └───────────────┘ │
                                              │  ┌───────────────┐ │
                                              │  │ Edge Functions │ │
                                              │  │ (notifications)│ │
                                              │  └───────────────┘ │
                                              │  ┌───────────────┐ │
                                              │  │ Supabase Auth  │ │
                                              │  └───────────────┘ │
                                              │  ┌───────────────┐ │
                                              │  │ Realtime (WSS) │ │
                                              │  └───────────────┘ │
                                              └─────────┬─────────┘
                                                        │
                                              ┌─────────┼─────────┐
                                              │         │         │
                                              ▼         ▼         ▼
                                          HTTPS     FCM Push   Realtime
                                              │         │         │
                                              └─────────┼─────────┘
                                                        │
                                                        ▼
                                              ┌───────────────────┐
                                              │  Flutter App      │
                                              │  (Android)         │
                                              │                     │
                                              │  ┌───────────────┐ │
                                              │  │ Dashboard     │ │
                                              │  │ Event History │ │
                                              │  │ Notifications │ │
                                              │  │ Device Mgmt   │ │
                                              │  └───────────────┘ │
                                              └───────────────────┘
```

## Component Architecture

### Windows Agent

```
LaptopGuardian.Agent/
├── Program.cs                          # Host builder, DI registration
├── appsettings.json                    # Configuration
├── Worker.cs                           # BackgroundService entry point
│
├── Configuration/
│   └── AgentOptions.cs                 # Strongly-typed config model
│
├── Identity/
│   ├── IDeviceIdentityService.cs       # Device ID generation and persistence
│   └── DeviceIdentityService.cs
│
├── Monitors/                           # One monitor per event source
│   ├── IEventMonitor.cs                # Common interface
│   ├── StartupMonitor.cs              # System startup/shutdown
│   ├── SessionMonitor.cs              # Login/logout/lock/unlock
│   ├── ProcessMonitor.cs             # Application/process start/stop
│   ├── UsbMonitor.cs                 # USB device connect/disconnect
│   ├── NetworkMonitor.cs             # Network adapter changes
│   ├── EventLogMonitor.cs            # Windows Event Log collector
│   ├── FileAuditMonitor.cs           # NTFS audit events for configured dirs
│   └── SystemMetricsMonitor.cs       # CPU, memory, disk, battery
│
├── Models/
│   ├── DeviceEvent.cs                  # Core event model
│   ├── EventType.cs                    # Event type enum
│   ├── EventSeverity.cs               # Severity enum
│   ├── SyncStatus.cs                  # Pending/Synced/Failed enum
│   └── DeviceInfo.cs                  # Static device metadata
│
├── Storage/
│   ├── IEventStore.cs                  # Local persistence interface
│   ├── SqliteEventStore.cs            # SQLite implementation
│   └── Migrations/                    # SQLite schema migrations
│
├── Sync/
│   ├── ISyncEngine.cs                  # Synchronization interface
│   ├── SyncEngine.cs                  # Batch upload, retry, dedup
│   └── IBackendClient.cs             # HTTP client interface for Supabase
│   └── SupabaseClient.cs
│
├── Pairing/
│   ├── IPairingService.cs             # Device pairing interface
│   └── PairingService.cs
│
└── Logging/
    └── StructuredLogger.cs            # Serilog configuration
```

#### Monitor Architecture

Each monitor implements `IEventMonitor`:

```csharp
public interface IEventMonitor : IDisposable
{
    string MonitorName { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
```

Monitors emit events through an `IEventStore` that writes to SQLite. The `SyncEngine` runs on a separate timer, picks up pending events, and uploads them in batches.

#### Offline-First Data Flow

```
Monitor detects event
    │
    ▼
Create DeviceEvent with deterministic UUID
    │
    ▼
Write to SQLite (status = Pending)
    │
    ▼
SyncEngine timer fires
    │
    ▼
Query: SELECT * FROM events WHERE status = 'Pending' LIMIT batch_size
    │
    ▼
POST /rest/v1/rpc/ingest_events (batch)
    │
    ├── Success: UPDATE status = 'Synced'
    │
    └── Failure: INCREMENT retry_count, UPDATE status = 'Failed' if max retries
                 Next cycle: exponential backoff
```

### Supabase Backend

```
backend/
├── supabase/
│   ├── config.toml                     # Supabase project config
│   ├── migrations/
│   │   ├── 001_create_profiles.sql
│   │   ├── 002_create_devices.sql
│   │   ├── 003_create_events.sql
│   │   ├── 004_create_pairing_codes.sql
│   │   ├── 005_create_heartbeats.sql
│   │   └── 006_enable_rls.sql
│   └── seed.sql
│
├── functions/
│   ├── ingest-events/
│   │   └── index.ts                    # Batch event ingestion with dedup
│   ├── pair-device/
│   │   └── index.ts                    # Device pairing flow
│   ├── send-notification/
│   │   └── index.ts                    # FCM push dispatch
│   └── _shared/
│       ├── cors.ts
│       ├── auth.ts                     # Device API key validation
│       └── types.ts
```

#### Authentication Model

Two authentication paths exist:

1. **Mobile app → Supabase Auth**: Standard email/password JWT. RLS policies reference `auth.uid()`.
2. **Agent → Device API key**: A per-device key issued during pairing. Validated by edge functions and a custom RLS helper function `is_device_authenticated(device_id)`.

### Flutter Mobile App

```
mobile/
├── lib/
│   ├── main.dart
│   ├── app.dart
│   │
│   ├── config/
│   │   ├── supabase_config.dart
│   │   ├── routes.dart
│   │   └── theme.dart
│   │
│   ├── models/
│   │   ├── device.dart
│   │   ├── event.dart
│   │   ├── event_type.dart
│   │   └── user_profile.dart
│   │
│   ├── services/
│   │   ├── auth_service.dart
│   │   ├── device_service.dart
│   │   ├── event_service.dart
│   │   ├── notification_service.dart
│   │   └── pairing_service.dart
│   │
│   ├── providers/                       # Riverpod state management
│   │   ├── auth_provider.dart
│   │   ├── device_provider.dart
│   │   └── event_provider.dart
│   │
│   └── screens/
│       ├── auth/
│       │   ├── login_screen.dart
│       │   └── register_screen.dart
│       ├── pairing/
│       │   └── pair_device_screen.dart
│       ├── dashboard/
│       │   └── dashboard_screen.dart
│       ├── events/
│       │   ├── event_list_screen.dart
│       │   ├── login_history_screen.dart
│       │   ├── process_history_screen.dart
│       │   ├── usb_history_screen.dart
│       │   ├── network_history_screen.dart
│       │   └── file_audit_screen.dart
│       └── settings/
│           └── settings_screen.dart
│
├── test/
├── pubspec.yaml
├── android/
└── ios/                                 # Deferred
```

## Key Design Decisions

### Why Worker Service (not Windows Service directly)?
.NET Worker Service provides hosted service lifecycle, dependency injection, configuration, and logging out of the box. It can be installed as a Windows Service via `Microsoft.Extensions.Hosting.WindowsServices`.

### Why SQLite for local storage?
- Zero-configuration embedded database
- Reliable WAL mode for concurrent reads/writes
- Battle-tested on Windows
- `Microsoft.Data.Sqlite` is the official .NET driver
- Survives process crashes (ACID transactions)

### Why deterministic event UUIDs?
Each event gets a UUID derived from (device_id + event_type + timestamp + unique_payload_hash). This allows the server to safely deduplicate without requiring the agent to track server-side acknowledgments per-event. If a batch is sent but the agent doesn't receive the response (network drop), re-sending the same batch is safe.

### Why Supabase Edge Functions for ingestion?
Direct PostgREST inserts would work for simple cases, but edge functions allow:
- Device API key validation
- Batch processing in a single HTTP call
- FCM notification triggering
- Rate limiting
- Input validation beyond what RLS provides

### Why Riverpod for Flutter state?
Riverpod provides compile-time safety, testability, and fine-grained rebuilds. It handles async data (Supabase queries) naturally with `AsyncValue`.
