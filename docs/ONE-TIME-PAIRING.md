# One-Time Device Pairing

## Overview

Laptop Guardian uses **one-time device pairing**. A 6-character pairing code is used only for the initial enrollment. After successful pairing, the device stays permanently linked to the user's account until explicitly unpaired.

## Device Lifecycle

```
UNREGISTERED → REGISTERED (UNPAIRED) → PAIRED → ONLINE ↔ OFFLINE
```

| State | Description |
|-------|-------------|
| **Unregistered** | Agent has not contacted the backend yet |
| **Registered (Unpaired)** | Agent has an API key and pairing code, waiting for user to enter code in mobile app |
| **Paired** | Device linked to a user account. Persists across all restarts |
| **Online** | Paired device actively sending heartbeats |
| **Offline** | Paired device not currently reachable. Still paired — will auto-reconnect |

**PAIRED ≠ ONLINE.** A device that is offline is still paired. It is never automatically unpaired due to being offline.

## How Pairing Works

### Initial Pairing (One-Time)

1. Windows Agent starts for the first time
2. Agent registers with the backend, receives an API key and a pairing code
3. Agent logs the pairing code (visible in the agent log file)
4. User opens the mobile app and enters the 6-character code
5. Backend links the device to the user's account
6. Agent detects pairing via the heartbeat response (`is_paired: true`)
7. Agent saves `PairedAt` timestamp to the identity file
8. Pairing is complete — no further codes needed

### After Pairing

- The agent **never** automatically generates a new pairing code
- Heartbeat interval changes from 15s (unpaired) to 60s (paired)
- Sync engine begins syncing events to the backend
- The identity file retains the encrypted API key and PairedAt timestamp

### Auto-Reconnect

The device automatically reconnects (no user action) after:

- Windows restart / reboot
- Internet disconnect and reconnect
- Agent service restart
- Phone app force-stop and relaunch
- Phone reboot

On service restart, the agent reads the stored identity file, restores `IsPaired = true` from the saved `PairedAt` timestamp, and resumes normal heartbeat at 60s intervals.

## Pairing Code Auto-Refresh

**Only before the first successful pairing**, the agent automatically refreshes expired pairing codes. Codes expire after 10 minutes. When a code expires, the agent requests a new one from the backend and logs it.

After successful pairing, auto-refresh is permanently disabled for that device.

## Unpairing

Unpairing is an **explicit user action** from the mobile app:

1. Open the device dashboard
2. Scroll to "Device Management"
3. Tap "Unpair Device"
4. Confirm in the dialog

This:
- Removes the user association from the device record
- Sets device status back to "pairing"
- Agent detects the change on next heartbeat (`is_paired: false`)
- Agent clears `PairedAt`, generates a new pairing code automatically
- User can re-pair with the new code

## Device Identity Persistence

The identity file is stored at:
```
C:\ProgramData\LaptopGuardian\device-identity.json
```

Contents:
- `DeviceId` — Local UUID (never changes)
- `ServerDeviceId` — Backend UUID (never changes after registration)
- `EncryptedApiKey` — DPAPI-encrypted API key (permanent credential)
- `PairingCode` — Current code (only relevant when unpaired)
- `PairingCodeExpiresAt` — Code expiry (only relevant when unpaired)
- `PairedAt` — Timestamp of first pairing (null when unpaired)
- `MachineName` — Windows machine name

The API key is the permanent device credential. It is encrypted with Windows DPAPI and persists across all restarts.

## Security

- Pairing codes are temporary (10-minute expiry) and single-use
- API keys are permanent credentials, encrypted at rest with DPAPI
- The pairing code is NOT a permanent credential
- After pairing, the code is irrelevant and eventually overwritten
- Unpair requires authenticated user who owns the device
- No credentials are logged (API keys, tokens, secrets)
- Device authentication uses SHA-256 hashed API keys

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| "Awaiting pairing" after restart | Device was never paired | Enter the pairing code in the mobile app |
| "Pairing code expired" | 10-minute window elapsed | Wait — agent auto-refreshes the code |
| Device shows "offline" | Laptop is off or disconnected | It will auto-reconnect when back online |
| "Device already paired" error | Code was already used | Device is already linked — check your devices list |
| Need to re-pair | Previous pairing needs reset | Use "Unpair Device" in the app, then re-pair with the new code |
