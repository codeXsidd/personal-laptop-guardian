# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2026-10-05

### Fixed
- Windows session state: dashboard no longer shows "LOGGED OUT" while the user is actively working
- Removed Event 4634 (internal session cleanup) which produced false logout events during lock/sleep/unlock
- Removed logon type 7 (unlock) from login detection — unlocks tracked separately via SessionSwitch
- Added SystemEvents.SessionSwitch as primary lock/unlock detection (works without Security audit policy)
- Added WTS API initial session state detection on agent startup — resolves stale state after service restart
- Cross-source deduplication between SessionSwitch and Security Event Log within 30-second window
- Last Login and Last Logout timestamps are now independent and no longer appear identical

### Added
- Camera Quick Action on Android dashboard for direct access to camera feature
- WTSGetActiveConsoleSessionId / WTSQuerySessionInformation for authoritative interactive user detection
- Diagnostic logging for session state transitions (source, reason, session ID, connect state)

### Changed
- Notification text: "PC locked/unlocked" → "Windows workstation locked/unlocked"

### Security
- Hardened .gitignore: added *.sqlite, logs/, *.key, *.pem, *.jks, *.keystore, *.p12
- Sanitized documentation: removed real Supabase project references from tracked markdown files

## [1.0.0] - 2026-10-05

### Added
- Windows Agent service with automatic startup and offline-first SQLite storage
- Session monitoring: login, logout, lock, unlock detection via Windows Security Event Log
- Power state tracking: startup, shutdown, sleep, wake
- Process monitoring with open/close times and duration
- USB device connect/disconnect detection
- Network connectivity monitoring
- Windows Event Log collection
- File access auditing for configured directories
- System metrics (CPU, memory, disk, battery)
- Automatic event synchronization with Supabase backend
- Event deduplication via UUID-based idempotency
- Supabase Edge Functions: ingest-events, send-notification, manage-remote-session, remote-relay
- Flutter Android companion app with Material 3 design
- Device pairing via 6-digit code
- Real-time dashboard with 3-dimensional state (power, connection, user session)
- Remote desktop streaming via Supabase Realtime broadcast
- Webcam streaming with explicit start/stop controls
- PC controls: lock, sleep, restart, shutdown
- Push notifications via Firebase Cloud Messaging (FCM v1 API)
- Notification history with clear/mark-read/swipe-dismiss
- Data management with configurable retention (7/30/90/365 days)
- PIN lock for Android app security
- Deep link support for remote access from notifications
- Code-signed Windows binaries
- MIT License
- Third-party license notices

### Security
- No keylogging, password capture, or credential theft
- Remote access requires authenticated user + paired device + valid session
- Camera requires explicit user action, stops on disconnect
- PC controls limited to lock/sleep/restart/shutdown only
- All secrets via environment variables
- DPAPI-protected PIN storage
- Supabase RLS for data isolation
