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
├── Program.cs                          # Host builder, DI, HttpClient registration
├── appsettings.json                    # Configuration (Supabase URL/key via user-secrets)
├── Worker.cs                           # BackgroundService orchestrator
│
├── Configuration/
│   └── AgentOptions.cs                 # Strongly-typed config model
│
├── Identity/
│   ├── IDeviceIdentityService.cs       # Device ID generation, persistence, save
│   ├── DeviceIdentityService.cs        # DPAPI-encrypted API key storage
│   ├── ICredentialProtector.cs         # Encrypt/decrypt interface
│   └── DpapiCredentialProtector.cs     # Windows DPAPI implementation
│
├── Backend/
│   ├── IBackendClient.cs              # HTTP client interface for Supabase
│   ├── SupabaseBackendClient.cs       # Implementation (IHttpClientFactory)
│   ├── BackendModels.cs               # Response DTOs
│   └── BackendExceptions.cs           # Typed exceptions (Auth, RateLimit, etc.)
│
├── Connectivity/
│   ├── IConnectivityTracker.cs        # Online/offline detection interface
│   └── ConnectivityTracker.cs         # NetworkChange + HTTP health check
│
├── Sync/
│   ├── ISyncEngine.cs                 # Sync loop interface
│   └── SyncEngine.cs                  # Batch upload, exponential backoff, retry
│
├── Heartbeat/
│   ├── IHeartbeatService.cs           # Heartbeat + pairing detection interface
│   └── HeartbeatService.cs            # Periodic heartbeat, detects pairing via 200/401
│
├── Monitors/                           # One monitor per event source
│   ├── IEventMonitor.cs               # Common interface
│   ├── StartupMonitor.cs             # Agent start event
│   ├── SessionMonitor.cs             # Login/logout/lock/unlock via Security log
│   ├── ProcessMonitor.cs             # App start/stop via snapshot polling
│   ├── UsbMonitor.cs                 # USB connect/disconnect via WMI
│   └── NetworkMonitor.cs             # Network changes via NetworkChange events
│
├── Models/
│   ├── DeviceEvent.cs                 # Core event model with deterministic UUID
│   ├── DeviceIdentity.cs              # Device identity with registration state
│   ├── EventType.cs                   # Event type constants
│   ├── EventSeverity.cs              # Severity constants
│   └── SyncStatus.cs                 # Pending/Synced/Failed constants
│
└── Storage/
    ├── IEventStore.cs                 # Local persistence interface
    └── SqliteEventStore.cs            # SQLite WAL mode, reset failed events
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

#### Monitor Details

| Monitor | Source | Events | Windows API | Permissions |
|---------|--------|--------|-------------|-------------|
| StartupMonitor | Agent lifecycle | `agent_started` | N/A | None |
| SessionMonitor | Security Event Log | `session_login`, `session_logout`, `session_lock`, `session_unlock` | `EventLogWatcher` on Security log (EventIDs 4624, 4634, 4647, 4800, 4801) | Event Log Readers group |
| ProcessMonitor | Process snapshot polling (30s) | `process_start`, `process_stop` | `System.Diagnostics.Process.GetProcesses()` | None |
| UsbMonitor | WMI events | `usb_connected`, `usb_disconnected` | `ManagementEventWatcher` on `Win32_PnPEntity` | None |
| NetworkMonitor | .NET NetworkChange events | `network_connected`, `network_disconnected`, `network_changed` | `NetworkChange.NetworkAddressChanged` + `NetworkAvailabilityChanged` | None |

**SessionMonitor** filters to interactive logon types (2, 7, 10, 11) and excludes system accounts (SYSTEM, LOCAL SERVICE, machine accounts). Degrades gracefully if the Security log is inaccessible.

**ProcessMonitor** compares snapshots using PID + start time as a composite key to handle PID reuse. The first snapshot is treated as a baseline (no events emitted). Excludes well-known system processes (svchost, csrss, lsass, etc.). Processes shorter than the poll interval may be missed.

**UsbMonitor** uses WMI intrinsic events (2-second polling). Filters to USB devices by PNPDeviceID prefix (USB\, USBSTOR\, USBPRINT\, HID\). Does not read file contents or inspect USB storage.

**NetworkMonitor** debounces rapid-fire events with a 2-second window. Records initial network state on startup. Captures adapter name, type, IPv4/IPv6 addresses, and status.

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
POST /functions/v1/ingest-events (batch)
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
