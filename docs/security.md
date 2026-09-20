# Security

## Ethical Boundaries

This application is designed exclusively for authorized monitoring of the user's own devices. The following are explicitly prohibited in the codebase:

- **No keylogging** -- no keystroke capture of any kind
- **No password collection** -- no interception or storage of credentials
- **No covert operation** -- the agent runs as a visible Windows Service, appears in Task Manager, and does not hide from the user
- **No unauthenticated access** -- all API endpoints require authentication
- **No hidden backdoors** -- no undocumented remote access mechanisms
- **No security control bypass** -- the agent does not disable Windows Defender, firewall, or UAC

## Threat Model

### Threats Addressed

| Threat | Mitigation |
|--------|-----------|
| Stolen/lost laptop | Agent continues collecting events offline; user sees last-known state and event history on phone |
| Unauthorized physical access | Login failure events trigger push notifications |
| Agent API key theft | Key is SHA-256 hashed server-side; agent stores key locally encrypted with DPAPI; RLS limits key scope to its own device |
| Network eavesdropping | All traffic over HTTPS/TLS 1.2+ |
| Backend data breach | RLS ensures users only see their own devices; API keys are SHA-256 hashed (high-entropy, so no brute-force concern) |
| Mobile app on stolen phone | Supabase Auth session with standard JWT expiry; app can require biometric unlock (future) |
| Replay attacks on event ingestion | Deterministic event UUIDs + ON CONFLICT DO NOTHING = safe replays |
| Pairing code brute force | 6-char alphanumeric (excludes ambiguous chars), 10-minute TTL, single-use |

### Threats NOT Addressed (Accepted Risks)

| Threat | Rationale |
|--------|-----------|
| Sophisticated attacker with admin access disabling the agent | Out of scope -- this is personal monitoring, not enterprise EDR |
| Physical access to an unlocked machine reading SQLite | Event data is operational metadata, not secrets |
| Supabase platform compromise | Inherent trust in the hosting provider |

## Authentication Architecture

### Device Authentication (Agent -> Backend)

```
Agent                           Supabase
  |   POST /register-device       |
  |  {machine_name, os_version}   |
  |------------------------------>|
  |                               | Creates device row (status: pairing)
  |                               | Stores SHA-256 hash of API key
  |   {device_id, api_key,        |
  |    pairing_code, expires_at}  |
  |<------------------------------|
  |                               |
  | Stores device_id + api_key    |
  | locally                       |

All subsequent agent requests include:
  x-device-api-key: <api_key>

Edge function validates:
  SHA-256(api_key) == devices.api_key_hash
  AND devices.status != 'pairing'  (must be paired first)
```

### Why SHA-256 Instead of bcrypt

API keys are high-entropy random strings (48 hex chars = 192 bits of entropy), not human-chosen passwords. SHA-256 is appropriate because:
- No brute-force risk due to key space size
- Constant-time indexable lookups via `WHERE api_key_hash = encode(digest(key, 'sha256'), 'hex')`
- bcrypt would require loading all hashes and comparing sequentially

### Device Pairing

```
Agent              Supabase             Mobile App
  |                    |                    |
  | Displays code:     |                    |
  | "A7X3K9"           |                    |
  |                    |                    |
  |                    |  POST /pair-device |
  |                    |<-------------------|
  |                    |  {code, name,      |
  |                    |   user JWT}        |
  |                    |                    |
  |                    | Validates:         |
  |                    | - code exists      |
  |                    | - not expired      |
  |                    | - not claimed      |
  |                    |                    |
  |                    | Updates:           |
  |                    | - device.user_id   |
  |                    | - device.status    |
  |                    |   = online         |
  |                    |------------------->|
  |                    |                    | Device appears
  |                    |                    | in dashboard
```

### Mobile User Authentication

Standard Supabase Auth flow:
1. User registers with email/password
2. Email confirmation (Supabase built-in)
3. Login returns JWT + refresh token
4. JWT included in all API requests
5. RLS policies reference `auth.uid()` to scope data

## Credential Management

### What Is Stored Where

| Secret | Location | Protection |
|--------|----------|-----------|
| Supabase URL | Agent: appsettings.json | Not a secret (public) |
| Supabase anon key | Agent: appsettings.json | Not a secret (public, limited by RLS) |
| Device API key | Agent: local JSON file | DPAPI-encrypted (Windows CurrentUser scope) |
| Supabase service role key | Edge function env only | Supabase dashboard secrets |
| Firebase server key | Edge function env only | Supabase dashboard secrets |
| User password | Never stored | Supabase Auth handles hashing |
| FCM device tokens | notification_tokens table | RLS-protected, user-scoped |

### What Is Never Stored or Transmitted

- Windows user passwords
- Keystrokes
- Screen contents
- Browser history or cookies
- File contents (only file access metadata from Windows audit logs)
- Clipboard contents
- Network packet payloads

### What Each Monitor Collects

| Monitor | Collected | NOT Collected |
|---------|-----------|---------------|
| SessionMonitor | Username, domain, logon type, session ID, event source | Password, authentication token |
| ProcessMonitor | Process name, PID, executable path, start/stop time, session ID | Command-line arguments, process memory, child process tree |
| UsbMonitor | Device name, class, manufacturer, PnP device ID | File listings, file contents, device serial numbers beyond PnP ID |
| NetworkMonitor | Adapter name, type, IPv4/IPv6, connection status, hostname | Network traffic, DNS queries, packet contents, remote endpoints |

### Monitor Permissions

| Monitor | Required Permission | Graceful Degradation |
|---------|-------------------|---------------------|
| SessionMonitor | Event Log Readers group membership | Logs warning, monitor disabled — agent continues without session events |
| ProcessMonitor | None (user-level access) | Cannot read executable path for elevated processes — path recorded as null |
| UsbMonitor | None (WMI access) | Logs warning if WMI unavailable — agent continues without USB events |
| NetworkMonitor | None | Always available on Windows |

## Network Security

- All HTTP traffic uses TLS 1.2+ (enforced by Supabase)
- Agent validates TLS certificates (no certificate pinning -- relies on system trust store)
- Supabase anon key is safe to embed (PostgREST + RLS enforces authorization)
- Service role key is never exposed to the agent or mobile app
- Agent retries use exponential backoff to avoid overwhelming the backend

## Local Data Security

### Agent SQLite Database
- Stored in `%ProgramData%\LaptopGuardian\guardian.db`
- Contains event history and sync state
- Event data is not encrypted at rest (it's operational metadata, not secrets)
- WAL mode enabled for concurrent read/write safety

### Agent Identity File
- Stored in `%ProgramData%\LaptopGuardian\device-identity.json`
- Contains device UUID, server device ID, pairing code, machine name
- API key is encrypted using Windows DPAPI (`System.Security.Cryptography.ProtectedData`) with `DataProtectionScope.CurrentUser`
- The plaintext API key is never written to disk; only the DPAPI-encrypted bytes (base64-encoded) are persisted
- DPAPI keys are tied to the Windows user profile — moving the identity file to another machine or user will fail decryption

### Windows Permissions
- Agent runs as `LOCAL SERVICE` or a dedicated service account
- Requires read access to Windows Event Logs (Security log requires `Event Log Readers` group membership)
- File auditing requires the directory to have a configured SACL (documented in setup)
- Does not require administrator privileges for normal operation after installation

## Row Level Security

All 8 user-facing tables have RLS enabled. Key principles:

- Users can only access data for devices they own
- Agent operations bypass RLS via service_role key in edge functions
- No INSERT/UPDATE/DELETE policies on agent-written tables (activity_events, heartbeats) -- writes go through edge functions only
- Pairing codes are visible to any authenticated user (needed for the pairing flow) but only unclaimed, unexpired ones

See [database.md](database.md) for the complete RLS policy listing.

## File Auditing Security Considerations

The file-access auditing feature relies on Windows Object Access Auditing:

1. **Requires explicit opt-in**: Admin must configure auditing on specific directories
2. **Requires Windows audit policy**: `auditpol /set /subcategory:"File System" /success:enable /failure:enable`
3. **Agent only reads audit events**: It does not modify ACLs, SACLs, or audit policy
4. **Only metadata is collected**: File path, access type, username, process -- never file contents

### Limitations
- Does not work on FAT32/exFAT volumes (no NTFS auditing)
- High-traffic directories generate significant event volume
- Windows Event Log buffer can overflow if the agent is offline for extended periods

## Incident Response

If a device API key is compromised:
1. User deletes the device from the mobile app
2. Backend invalidates the device record (cascade deletes pairing codes, events are preserved for audit)
3. Agent detects 401 responses and enters re-registration state
4. User re-registers and re-pairs with a new code
