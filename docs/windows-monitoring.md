# Windows Monitoring Guide

## Overview

The Laptop Guardian agent monitors several Windows subsystems. Each monitor runs independently and degrades gracefully if its data source is unavailable.

## Monitors

| Monitor | Source | Events | Default State |
|---------|--------|--------|---------------|
| StartupMonitor | Agent lifecycle | `agent_started` | Always on |
| SessionMonitor | Security Event Log | `session_login`, `session_logout`, `session_lock`, `session_unlock` | Always on |
| ProcessMonitor | Process snapshot polling | `process_start`, `process_stop` | Always on |
| UsbMonitor | WMI events | `usb_connected`, `usb_disconnected` | Always on |
| NetworkMonitor | .NET NetworkChange | `network_connected`, `network_disconnected`, `network_changed` | Always on |
| SystemMetricsMonitor | Performance counters, WMI, DriveInfo | `system_metrics` | Enabled (configurable) |
| EventLogMonitor | Windows Event Log channels | `eventlog_entry` | Enabled (configurable) |
| FileAuditMonitor | Security Event Log (Object Access) | `file_access` | Disabled (configurable) |

## Configuration

All monitor settings are in `appsettings.json` under the `Agent` section:

```json
{
  "Agent": {
    "SystemMetrics": {
      "Enabled": true,
      "IntervalSeconds": 300,
      "MinChangePercentForEvent": 5
    },
    "EventLogMonitor": {
      "Enabled": true,
      "Channels": [
        { "Name": "Application", "Levels": ["Error", "Critical"] },
        { "Name": "System", "Levels": ["Error", "Critical"] }
      ]
    },
    "FileAudit": {
      "Enabled": false,
      "Directories": [],
      "DuplicateWindowSeconds": 5
    }
  }
}
```

## System Metrics Monitor

Collects system-level metrics at a configurable interval (default: 5 minutes):

- **CPU**: Percentage via `Processor\% Processor Time` performance counter
- **Memory**: Used/total/percentage via GC memory info and `Memory\Available Bytes` counter
- **Disk**: Per-drive usage for fixed NTFS/ReFS drives via `DriveInfo`
- **Battery**: Percentage and charging state via WMI `Win32_Battery`
- **System**: Hostname and OS version

Metrics are also included in heartbeat payloads sent to the backend.

### Requirements

- No special permissions required for CPU, memory, disk metrics
- Battery information requires WMI access (always available for standard users)
- If a specific metric source is unavailable, that field is omitted from the event

### Graceful Degradation

| Metric | Failure Mode | Behavior |
|--------|-------------|----------|
| CPU | PerformanceCounter unavailable | Omitted from payload |
| Memory | GC API failure | Zeros reported |
| Disk | Drive inaccessible | Skipped |
| Battery | No battery / WMI failure | Omitted from payload |

## Event Log Monitor

Watches configurable Windows Event Log channels for events matching specified criteria.

### Configuration

Each channel entry specifies:
- `Name`: The Event Log channel (e.g., `Application`, `System`, `Security`)
- `Levels`: Which severity levels to capture (`Critical`, `Error`, `Warning`, `Information`, `Verbose`)
- `EventIds`: Optional list of specific event IDs to capture

```json
{
  "Channels": [
    { "Name": "Application", "Levels": ["Error", "Critical"] },
    { "Name": "System", "Levels": ["Error", "Critical"], "EventIds": [1000, 7034] }
  ]
}
```

### What Is Captured

- Channel name
- Event ID
- Provider/source name
- Level (mapped to event severity)
- Timestamp
- Event message (truncated to 1024 characters)
- User SID (when available)
- Machine name
- Task category (when available)

### What Is NOT Captured

- Raw event data/binary payloads
- Full event XML
- Security-sensitive fields beyond what the message contains

### Permissions

- `Application` and `System` logs: readable by all users
- `Security` log: requires `Event Log Readers` group membership
- If a channel is inaccessible, the monitor logs a warning and skips that channel

## File Audit Monitor

Monitors file access in explicitly configured directories using Windows Object Access Auditing.

### Important Limitations

- This monitor can only capture file access events that Windows generates through its audit subsystem
- If Windows auditing was not enabled for a directory, no historical file access can be reconstructed
- Only events that occur while the agent is running and auditing is configured are captured

### Prerequisites

File audit monitoring requires three things to be configured:

1. **Windows Audit Policy** must be enabled:
   ```cmd
   auditpol /set /subcategory:"File System" /success:enable /failure:enable
   ```

2. **SACL (System Access Control List)** must be set on monitored directories:
   ```cmd
   icacls "D:\Projects" /setintegritylevel (OI)(CI)M
   ```
   Or via Windows Explorer:
   - Right-click folder > Properties > Security > Advanced > Auditing
   - Add an entry for "Everyone" with desired access types

3. **Agent configuration** must list the directories:
   ```json
   {
     "Agent": {
       "FileAudit": {
         "Enabled": true,
         "Directories": [
           "D:\\Projects",
           "D:\\Documents"
         ]
       }
     }
   }
   ```

### What Is Captured

- File path
- Access type (read_data, write_data, append_data, execute, delete, etc.)
- Username performing the access
- Windows event ID (4663 for object access, 4656 for handle request)
- Timestamp
- Process name and PID (when available from the event)
- Access mask

### What Is NOT Captured

- File contents (never read or transmitted)
- Directories not explicitly listed in configuration
- Events before auditing was enabled

### Duplicate Prevention

File access events can be very noisy. A configurable deduplication window (default: 5 seconds) suppresses repeated events for the same file path and access type.

### Startup Diagnostics

When the agent starts with file auditing enabled, it logs:
- Whether file auditing is enabled or disabled
- Each configured directory and whether it exists
- Whether the Security event log is accessible
- Any permission issues

### Graceful Degradation

| Condition | Behavior |
|-----------|----------|
| Auditing disabled in Windows | No events generated; agent logs warning |
| Directory doesn't exist | Logged as warning; will monitor if created later |
| Security log inaccessible | File audit monitor disabled; agent continues |
| Malformed audit event | Event skipped; logged at debug level |

### Supported Windows Versions

- Windows 10 Pro/Enterprise (NTFS volumes only)
- Windows 11 Pro/Enterprise (NTFS volumes only)
- Windows Home editions have limited audit policy support

## Permissions Summary

| Monitor | Permission Required | How to Configure |
|---------|-------------------|-----------------|
| StartupMonitor | None | N/A |
| SessionMonitor | Event Log Readers group | `net localgroup "Event Log Readers" <user> /add` |
| ProcessMonitor | None (user-level) | N/A |
| UsbMonitor | None (WMI access) | N/A |
| NetworkMonitor | None | N/A |
| SystemMetricsMonitor | None | N/A |
| EventLogMonitor | Depends on channel | Application/System: none. Security: Event Log Readers |
| FileAuditMonitor | Event Log Readers + audit policy | See prerequisites above |

## Privacy Behavior

Every monitor follows these principles:
- Only metadata is collected, never file contents or user data
- No keystrokes are captured
- No passwords are collected
- No clipboard contents are read
- No network packet payloads are captured
- No screenshots are taken
- USB monitoring records device identity only, not file listings

## Troubleshooting

### Session events not appearing

1. Verify the service account is in `Event Log Readers`:
   ```cmd
   net localgroup "Event Log Readers"
   ```
2. Check the agent log for "Cannot access Security event log"

### File audit events not appearing

1. Check audit policy: `auditpol /get /subcategory:"File System"`
2. Verify SACL on the directory: folder Properties > Security > Advanced > Auditing
3. Check agent config: `FileAudit.Enabled` must be `true` and directory listed
4. Check agent log for startup diagnostics

### System metrics showing -1 for CPU

The first CPU reading is always 0 (Windows performance counter initialization). The agent discards negative values from the payload. Subsequent readings should be valid.

### EventLog monitor not capturing expected events

1. Verify the channel name matches exactly (e.g., `Application`, not `application`)
2. Check the configured levels match the event's level
3. If using `EventIds` filter, verify the event IDs are correct
4. Check the agent log for "Access denied" or "Channel not found" messages
