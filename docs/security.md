# Security

## Ethical Boundaries

This application is designed exclusively for authorized monitoring of the user's own devices. The following are explicitly prohibited in the codebase:

- **No keylogging** — no keystroke capture of any kind
- **No password collection** — no interception or storage of credentials
- **No covert operation** — the agent runs as a visible Windows Service, appears in Task Manager, and does not hide from the user
- **No unauthenticated access** — all API endpoints require authentication
- **No hidden backdoors** — no undocumented remote access mechanisms
- **No security control bypass** — the agent does not disable Windows Defender, firewall, or UAC

## Threat Model

### Threats Addressed

| Threat | Mitigation |
|--------|-----------|
| Stolen/lost laptop | Agent continues collecting events offline; user sees last-known state and event history on phone |
| Unauthorized physical access | Login failure events trigger push notifications |
| Agent API key theft | Key is hashed server-side; agent stores key encrypted with Windows DPAPI; RLS limits key scope to its own device |
| Network eavesdropping | All traffic over HTTPS/TLS 1.2+ |
| Backend data breach | RLS ensures users only see their own devices; API keys are bcrypt-hashed |
| Mobile app on stolen phone | Supabase Auth session with standard JWT expiry; app can require biometric unlock (future) |
| Replay attacks on event ingestion | Deterministic event UUIDs + ON CONFLICT DO NOTHING = safe replays |
| Pairing code brute force | 6-char alphanumeric (2.1B combinations), 10-minute TTL, rate-limited |

### Threats NOT Addressed (Accepted Risks)

| Threat | Rationale |
|--------|-----------|
| Sophisticated attacker with admin access disabling the agent | Out of scope — this is personal monitoring, not enterprise EDR |
| Physical access to an unlocked machine reading SQLite | DPAPI encrypts the API key; event data is not secrets |
| Supabase platform compromise | Inherent trust in the hosting provider |

## Authentication Architecture

### Device Authentication (Agent → Backend)

```
┌──────────┐                    ┌──────────────┐
│  Agent   │                    │   Supabase   │
│          │   POST /register   │              │
│          │───────────────────▶│  Creates     │
│          │   {machine_name,   │  device row  │
│          │    os_version}     │  (status:    │
│          │◀───────────────────│   pairing)   │
│          │   {device_id,      │  Stores      │
│          │    api_key,        │  bcrypt hash │
│          │    pairing_code}   │              │
│          │                    │              │
│  Stores  │                    │              │
│  device_id + api_key          │              │
│  encrypted via DPAPI          │              │
└──────────┘                    └──────────────┘

All subsequent agent requests include:
  x-device-api-key: <api_key>

Edge function validates:
  bcrypt_verify(api_key, devices.api_key_hash)
  AND devices.status != 'pairing'  (must be paired first)
```

### Device Pairing

```
┌──────────┐      ┌──────────────┐      ┌──────────────┐
│  Agent   │      │   Supabase   │      │  Mobile App  │
│          │      │              │      │              │
│  Displays│      │              │      │  User enters │
│  code:   │      │              │      │  code or     │
│  "A7X3K9"│      │              │      │  scans QR    │
│          │      │              │      │              │
│          │      │  POST /pair  │◀─────│  POST /pair  │
│          │      │  {code,      │      │  {code,      │
│          │      │   user JWT}  │      │   name}      │
│          │      │              │      │              │
│          │      │  Validates:  │      │              │
│          │      │  - code exists│     │              │
│          │      │  - not expired│     │              │
│          │      │  - not claimed│     │              │
│          │      │              │      │              │
│          │      │  Updates:    │      │              │
│          │      │  - device.   │──────▶  Device      │
│          │      │    user_id   │      │  appears in  │
│          │      │  - device.   │      │  dashboard   │
│          │      │    status=   │      │              │
│          │      │    online    │      │              │
└──────────┘      └──────────────┘      └──────────────┘
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
| Device API key | Agent: local encrypted store | Windows DPAPI encryption |
| Supabase service role key | Edge function env only | Supabase dashboard secrets |
| Firebase server key | Edge function env only | Supabase dashboard secrets |
| User password | Never stored | Supabase Auth handles hashing |
| FCM device token | profiles.fcm_token | RLS-protected, user-scoped |

### What Is Never Stored or Transmitted

- Windows user passwords
- Keystrokes
- Screen contents (until remote-view feature, which requires explicit auth)
- Browser history or cookies
- File contents (only file access metadata from Windows audit logs)

## Network Security

- All HTTP traffic uses TLS 1.2+ (enforced by Supabase)
- Agent validates TLS certificates (no certificate pinning — relies on system trust store)
- Supabase anon key is safe to embed (PostgREST + RLS enforces authorization)
- Service role key is never exposed to the agent or mobile app
- Agent retries use exponential backoff to avoid overwhelming the backend

## Local Data Security

### Agent SQLite Database
- Stored in `%ProgramData%\LaptopGuardian\guardian.db`
- Contains event history and sync state
- API key stored separately and encrypted with DPAPI
- Event data is not encrypted at rest (it's operational metadata, not secrets)

### Windows Permissions
- Agent runs as `LOCAL SERVICE` or a dedicated service account
- Requires read access to Windows Event Logs (Security log requires `Event Log Readers` group membership)
- File auditing requires the directory to have a configured SACL (documented in setup)
- Does not require administrator privileges for normal operation after installation

## File Auditing Security Considerations

The file-access auditing feature relies on Windows Object Access Auditing:

1. **Requires explicit opt-in**: Admin must configure auditing on specific directories
2. **Requires Windows audit policy**: `auditpol /set /subcategory:"File System" /success:enable /failure:enable`
3. **Agent only reads audit events**: It does not modify ACLs, SACLs, or audit policy
4. **Only metadata is collected**: File path, access type, username, process — never file contents

### Limitations
- Does not work on FAT32/exFAT volumes (no NTFS auditing)
- High-traffic directories generate significant event volume
- Windows Event Log buffer can overflow if the agent is offline for extended periods

## Rate Limiting

| Endpoint | Limit | Window |
|----------|-------|--------|
| ingest-events | 10 requests | 1 minute per device |
| heartbeat | 2 requests | 1 minute per device |
| register-device | 5 requests | 1 hour per IP |
| pair-device | 10 attempts | 1 hour per user |

## Incident Response

If a device API key is compromised:
1. User deletes the device from the mobile app
2. Backend invalidates the API key hash
3. Agent detects 401 responses and enters re-pairing state
4. User re-pairs with a new code
