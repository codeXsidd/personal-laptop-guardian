# Requirements

## Functional Requirements

### Windows Agent (C# / .NET 10 Worker Service)

#### Startup and Identity
- FR-A01: Run as a Windows Worker Service that starts automatically after installation
- FR-A02: Generate and persist a unique device identity (GUID) on first run
- FR-A03: Support one-time device pairing with the mobile app via a short-lived pairing code
- FR-A04: Authenticate all backend communication using a device-specific API key obtained during pairing

#### Event Collection
- FR-A10: Detect laptop startup/shutdown events
- FR-A11: Detect user login and logout events (interactive, remote, unlock)
- FR-A12: Monitor application and process start/stop events
- FR-A13: Detect USB device connect/disconnect events
- FR-A14: Monitor network adapter changes (connect, disconnect, SSID changes, IP changes)
- FR-A15: Collect relevant Windows Event Log entries (Security, System, Application)
- FR-A16: Integrate with Windows file-access auditing for explicitly configured directories
- FR-A17: Collect system metrics (CPU, memory, disk, battery)

#### Offline-First Storage and Sync
- FR-A20: Persist every event to local SQLite before attempting network transmission
- FR-A21: Mark events as pending, synced, or failed
- FR-A22: Assign each event a deterministic UUID for server-side idempotency
- FR-A23: When online, synchronize pending events in batches to the backend
- FR-A24: Retry failed synchronization with exponential backoff
- FR-A25: Preserve pending events across service restart and system reboot
- FR-A26: Send periodic heartbeat to the backend (configurable interval, default 60s)

#### Configuration
- FR-A30: Read configuration from appsettings.json and environment variables
- FR-A31: Support configuring: Supabase URL, polling intervals, monitored directories, event log channels, log level
- FR-A32: Never require hard-coded secrets in source code

### Backend (Supabase)

#### Device Management
- FR-B01: Register new devices with a unique identity
- FR-B02: Generate and validate short-lived pairing codes (6-character alphanumeric, 10-minute TTL)
- FR-B03: Issue a device-specific API key upon successful pairing
- FR-B04: Track device online/offline status based on heartbeats (offline after 3× heartbeat interval)

#### Event Ingestion
- FR-B10: Accept batched event uploads from authenticated devices
- FR-B11: Deduplicate events by event UUID (idempotent inserts)
- FR-B12: Store events with full metadata in PostgreSQL
- FR-B13: Trigger push notification for high-severity events via Firebase Cloud Messaging

#### Queries
- FR-B20: Provide paginated event queries filtered by device, event type, time range, and severity
- FR-B21: Provide device status and last-seen information
- FR-B22: Provide aggregated activity summaries (events per day, top applications, etc.)

#### Security
- FR-B30: Enforce Row Level Security on all tables
- FR-B31: Authenticate mobile users via Supabase Auth (email/password)
- FR-B32: Authenticate agent requests via device API key (validated by a custom header)
- FR-B33: Rate-limit event ingestion per device

### Mobile App (Flutter / Android)

#### Authentication and Pairing
- FR-M01: User registration and login via Supabase Auth
- FR-M02: Device pairing via QR code or manual code entry
- FR-M03: Display pairing status and paired device list

#### Dashboard
- FR-M10: Show device online/offline status with last-seen timestamp
- FR-M11: Show recent activity feed across all event types
- FR-M12: Show summary cards (events today, active applications, network status)

#### Event History
- FR-M20: Browse login/logout history with timestamps and session type
- FR-M21: Browse application/process activity with durations
- FR-M22: Browse USB device history
- FR-M23: Browse network change history
- FR-M24: Browse file-access audit history
- FR-M25: Browse Windows Event Log entries
- FR-M26: Filter and search across all event types by date range and keyword

#### Notifications
- FR-M30: Receive push notifications for high-severity events
- FR-M31: Configure which event types trigger notifications
- FR-M32: In-app notification history

## Non-Functional Requirements

- NFR-01: Agent idle CPU usage must be < 1% on a modern processor
- NFR-02: Agent memory usage must be < 50 MB during normal operation
- NFR-03: Events must reach the mobile app within 15 seconds of detection (when online)
- NFR-04: All agent-to-backend communication must use HTTPS (TLS 1.2+)
- NFR-05: Local SQLite database must handle 100,000+ events without performance degradation
- NFR-06: Mobile app must cache recent events for offline viewing
- NFR-07: Agent must recover gracefully from backend outages without data loss
- NFR-08: All components must use structured logging (JSON format)

## Explicitly Out of Scope

- Keylogging or keystroke capture
- Password collection or credential interception
- Covert or hidden operation that bypasses security controls
- Unauthenticated remote access
- GPS/location tracking (IP-based geolocation only if added later)
- iOS support (Android first)
- Remote control features (deferred to later phase)
