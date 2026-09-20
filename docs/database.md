# Database Schema

## Overview

The backend uses Supabase (PostgreSQL). The agent also uses a local SQLite database for offline-first storage. Both schemas are documented here.

---

## PostgreSQL (Supabase)

All tables use `uuid` primary keys generated with `gen_random_uuid()`. Timestamps are `timestamptz` (UTC).

### profiles

Extends Supabase Auth `auth.users`. Created via a trigger on user signup.

| Column       | Type                   | Constraints              | Description                      |
|-------------|------------------------|--------------------------|----------------------------------|
| id          | uuid                   | PK, references auth.users | User identity                   |
| full_name   | text                   | NOT NULL                 | Display name                     |
| email       | text                   | NOT NULL                 | Cached from auth.users           |
| fcm_token   | text                   | NULLABLE                 | Firebase Cloud Messaging token   |
| created_at  | timestamptz            | DEFAULT now()            | Profile creation time            |
| updated_at  | timestamptz            | DEFAULT now()            | Last profile update              |

### devices

| Column          | Type                   | Constraints              | Description                              |
|----------------|------------------------|--------------------------|------------------------------------------|
| id             | uuid                   | PK, DEFAULT gen_random_uuid() | Device identity                    |
| user_id        | uuid                   | FK → profiles(id), NOT NULL | Owning user                          |
| device_name    | text                   | NOT NULL                 | User-assigned name ("My Work Laptop")    |
| machine_name   | text                   | NOT NULL                 | Windows hostname                         |
| os_version     | text                   |                          | Windows version string                   |
| agent_version  | text                   |                          | Agent build version                      |
| api_key_hash   | text                   | NOT NULL, UNIQUE         | bcrypt hash of device API key            |
| status         | text                   | NOT NULL, DEFAULT 'offline' | online / offline / pairing            |
| last_seen_at   | timestamptz            |                          | Last heartbeat timestamp                 |
| heartbeat_interval_s | integer           | NOT NULL, DEFAULT 60     | Expected heartbeat interval              |
| created_at     | timestamptz            | DEFAULT now()            | Registration time                        |

**Indexes:**
- `idx_devices_user_id` on `user_id`
- `idx_devices_api_key_hash` on `api_key_hash` (unique)
- `idx_devices_status` on `status`

### events

The core table. Receives batched inserts from the agent.

| Column          | Type                   | Constraints              | Description                              |
|----------------|------------------------|--------------------------|------------------------------------------|
| id             | uuid                   | PK                       | Deterministic UUID from agent            |
| device_id      | uuid                   | FK → devices(id), NOT NULL | Source device                          |
| event_type     | text                   | NOT NULL                 | Enum-like (see Event Types below)        |
| severity       | text                   | NOT NULL, DEFAULT 'info' | info / low / medium / high / critical    |
| timestamp      | timestamptz            | NOT NULL                 | When the event occurred on the device    |
| payload        | jsonb                  | NOT NULL, DEFAULT '{}'   | Event-type-specific structured data      |
| synced_at      | timestamptz            | DEFAULT now()            | When the server received this event      |

**Indexes:**
- `idx_events_device_id_timestamp` on `(device_id, timestamp DESC)`
- `idx_events_device_id_event_type` on `(device_id, event_type)`
- `idx_events_timestamp` on `timestamp DESC`
- Partial index: `idx_events_high_severity` on `(device_id, timestamp DESC) WHERE severity IN ('high', 'critical')`

**Constraint:**
- `UNIQUE (id)` — enables `ON CONFLICT (id) DO NOTHING` for idempotent inserts

### pairing_codes

Short-lived codes for device-to-user pairing.

| Column          | Type                   | Constraints              | Description                              |
|----------------|------------------------|--------------------------|------------------------------------------|
| id             | uuid                   | PK, DEFAULT gen_random_uuid() | Internal ID                        |
| code           | text                   | NOT NULL, UNIQUE         | 6-char alphanumeric code                 |
| device_id      | uuid                   | FK → devices(id), NOT NULL | Device requesting pairing              |
| expires_at     | timestamptz            | NOT NULL                 | Code expiration (created_at + 10 min)    |
| claimed_by     | uuid                   | FK → profiles(id)        | User who claimed the code                |
| claimed_at     | timestamptz            |                          | When the code was claimed                |
| created_at     | timestamptz            | DEFAULT now()            |                                          |

### heartbeats

Lightweight table for tracking device liveness. Old records are periodically pruned.

| Column          | Type                   | Constraints              | Description                              |
|----------------|------------------------|--------------------------|------------------------------------------|
| id             | uuid                   | PK, DEFAULT gen_random_uuid() |                                     |
| device_id      | uuid                   | FK → devices(id), NOT NULL |                                        |
| cpu_percent    | real                   |                          | CPU usage at heartbeat time              |
| memory_percent | real                   |                          | Memory usage                             |
| disk_percent   | real                   |                          | Primary disk usage                       |
| battery_percent| real                   |                          | Battery level (null if no battery)       |
| is_charging    | boolean                |                          | Charging state                           |
| ip_address     | text                   |                          | Public or local IP                       |
| created_at     | timestamptz            | DEFAULT now()            |                                          |

**Indexes:**
- `idx_heartbeats_device_id_created` on `(device_id, created_at DESC)`

### notification_settings

Per-device notification preferences set by the user in the mobile app.

| Column          | Type                   | Constraints              | Description                              |
|----------------|------------------------|--------------------------|------------------------------------------|
| id             | uuid                   | PK, DEFAULT gen_random_uuid() |                                     |
| user_id        | uuid                   | FK → profiles(id), NOT NULL |                                       |
| device_id      | uuid                   | FK → devices(id), NOT NULL |                                        |
| event_type     | text                   | NOT NULL                 | Which event type this rule covers        |
| min_severity   | text                   | NOT NULL, DEFAULT 'high' | Minimum severity to notify               |
| enabled        | boolean                | NOT NULL, DEFAULT true   |                                          |
| created_at     | timestamptz            | DEFAULT now()            |                                          |
| updated_at     | timestamptz            | DEFAULT now()            |                                          |

**Constraint:**
- `UNIQUE (user_id, device_id, event_type)`

---

## Event Types

| Event Type              | Severity Default | Payload Fields                                          |
|------------------------|------------------|---------------------------------------------------------|
| `system_startup`       | info             | `{ boot_time }`                                         |
| `system_shutdown`      | info             | `{ shutdown_reason }`                                   |
| `session_login`        | info             | `{ username, session_type, is_remote }`                 |
| `session_logout`       | info             | `{ username, session_type }`                            |
| `session_lock`         | info             | `{ username }`                                          |
| `session_unlock`       | info             | `{ username }`                                          |
| `login_failed`         | high             | `{ username, failure_reason, source_ip }`               |
| `process_start`        | info             | `{ process_name, pid, path, username }`                 |
| `process_stop`         | info             | `{ process_name, pid, exit_code, duration_s }`          |
| `usb_connected`        | medium           | `{ device_name, device_type, vendor_id, product_id }`   |
| `usb_disconnected`     | low              | `{ device_name, device_type, vendor_id, product_id }`   |
| `network_connected`    | info             | `{ adapter_name, ssid, ip_address, is_wifi }`           |
| `network_disconnected` | medium           | `{ adapter_name, ssid }`                                |
| `network_changed`      | info             | `{ adapter_name, old_ip, new_ip, ssid }`                |
| `eventlog_entry`       | varies           | `{ source, event_id, level, message, log_name }`        |
| `file_access`          | low              | `{ file_path, access_type, username, process_name }`    |
| `system_metrics`       | info             | `{ cpu, memory, disk, battery, is_charging }`           |

---

## SQLite (Agent Local)

### events (local)

Mirrors the server schema with sync metadata.

| Column          | Type       | Description                              |
|----------------|------------|------------------------------------------|
| id             | TEXT (PK)  | Deterministic UUID                       |
| event_type     | TEXT       | Same enum as server                      |
| severity       | TEXT       | info / low / medium / high / critical    |
| timestamp      | TEXT       | ISO 8601 UTC                             |
| payload        | TEXT       | JSON string                              |
| sync_status    | TEXT       | pending / synced / failed                |
| retry_count    | INTEGER    | Number of sync attempts                  |
| created_at     | TEXT       | ISO 8601 UTC                             |
| synced_at      | TEXT       | ISO 8601 UTC (null until synced)         |

**Indexes:**
- `idx_local_events_sync_status` on `sync_status`
- `idx_local_events_timestamp` on `timestamp DESC`

### device_identity (local)

Single-row table persisting device registration state.

| Column          | Type       | Description                              |
|----------------|------------|------------------------------------------|
| device_id      | TEXT (PK)  | Device UUID                              |
| api_key        | TEXT       | Plaintext API key (encrypted at rest via DPAPI) |
| paired_at      | TEXT       | ISO 8601 UTC                             |
| supabase_url   | TEXT       | Backend URL                              |

---

## Row Level Security Policies

### profiles
- `SELECT`: `auth.uid() = id`
- `UPDATE`: `auth.uid() = id`

### devices
- `SELECT`: `auth.uid() = user_id`
- `INSERT`: via edge function only (pairing flow)
- `UPDATE`: `auth.uid() = user_id` (limited columns: device_name, status)

### events
- `SELECT`: `device_id IN (SELECT id FROM devices WHERE user_id = auth.uid())`
- `INSERT`: via edge function only (agent uses device API key, not user JWT)

### heartbeats
- `SELECT`: `device_id IN (SELECT id FROM devices WHERE user_id = auth.uid())`
- `INSERT`: via edge function only

### pairing_codes
- `SELECT`: `auth.uid() IS NOT NULL` (any authenticated user can look up a code to claim it)
- `UPDATE (claim)`: `auth.uid() IS NOT NULL AND claimed_by IS NULL AND expires_at > now()`
- `INSERT`: via edge function only (agent initiates)

### notification_settings
- `SELECT`: `auth.uid() = user_id`
- `INSERT`: `auth.uid() = user_id`
- `UPDATE`: `auth.uid() = user_id`
- `DELETE`: `auth.uid() = user_id`

---

## Database Functions

### `ingest_events(device_api_key text, events jsonb)`
Server-side function called by the agent. Validates API key, performs `INSERT ... ON CONFLICT (id) DO NOTHING`, returns count of newly inserted events. Triggers notification check for high-severity events.

### `update_device_heartbeat(device_api_key text, metrics jsonb)`
Updates `devices.last_seen_at`, inserts a `heartbeats` row, sets `devices.status = 'online'`.

### `mark_offline_devices()`
Called on a schedule (pg_cron or edge function cron). Sets `status = 'offline'` for devices where `now() - last_seen_at > heartbeat_interval_s * 3`.

### `cleanup_old_heartbeats(retention_days integer)`
Deletes heartbeat rows older than retention period. Called on a daily schedule.
