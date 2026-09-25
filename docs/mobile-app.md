# Mobile App

## Overview

The Flutter Android app provides a dashboard for monitoring paired Windows laptops. It uses Supabase Auth for authentication and queries the Supabase PostgREST API for device data, activity events, and heartbeat metrics.

## Technology Stack

- Flutter 3.47+ / Dart 3.13+
- Riverpod for state management
- go_router for navigation
- supabase_flutter for backend integration
- fl_chart for data visualization
- Material 3 design system

## Architecture

```
lib/
├── main.dart                    # Entry point, Supabase init
├── app.dart                     # MaterialApp.router with theme
├── config/
│   ├── supabase_config.dart     # Compile-time env vars (--dart-define)
│   ├── theme.dart               # Material 3 light/dark themes
│   └── routes.dart              # GoRouter configuration
├── models/                      # Typed data models
│   ├── device.dart
│   ├── event.dart
│   ├── event_type.dart
│   ├── heartbeat.dart
│   └── user_profile.dart
├── services/                    # Supabase API calls
│   ├── auth_service.dart
│   ├── device_service.dart
│   ├── event_service.dart
│   └── pairing_service.dart
├── providers/                   # Riverpod state providers
│   ├── auth_provider.dart
│   ├── device_provider.dart
│   ├── event_provider.dart
│   └── pairing_provider.dart
└── screens/
    ├── shell.dart               # Bottom navigation shell
    ├── auth/
    │   ├── login_screen.dart
    │   └── register_screen.dart
    ├── pairing/
    │   └── pair_device_screen.dart
    ├── devices/
    │   └── devices_screen.dart
    ├── dashboard/
    │   └── dashboard_screen.dart
    ├── events/
    │   ├── event_list_screen.dart    # Reusable paginated event list
    │   ├── event_detail_sheet.dart   # Event detail bottom sheet
    │   ├── login_history_screen.dart
    │   ├── process_history_screen.dart
    │   ├── usb_history_screen.dart
    │   ├── network_history_screen.dart
    │   ├── file_audit_screen.dart
    │   └── eventlog_screen.dart
    ├── reports/
    │   └── reports_screen.dart
    └── settings/
        └── settings_screen.dart
```

## Configuration

Supabase credentials are passed at build time, never committed:

```bash
flutter run \
  --dart-define=SUPABASE_URL=https://your-project.supabase.co \
  --dart-define=SUPABASE_ANON_KEY=your-anon-key
```

The anon key is safe to include (it's public and limited by RLS). The service role key is never used in the app.

## Authentication Flow

1. User registers with email, password, and full name
2. Supabase sends confirmation email
3. After confirmation, user signs in
4. JWT is automatically included in all Supabase queries
5. RLS policies scope data to the authenticated user's devices

## Device Pairing Flow

1. Install the Windows agent on the laptop
2. Agent registers with Supabase and displays a 6-character code
3. In the mobile app, tap "Pair Device" and enter the code
4. App calls the `pair-device` edge function
5. Device appears in the devices list with "online" status

## Event History Screens

### Activity Timeline

The main event list screen (`EventListScreen`) provides:

- **Date-grouped display**: events are grouped under "Today", "Yesterday", or named dates (e.g. "Monday, Sep 15")
- **Pagination**: loads 50 events at a time with infinite scroll
- **Pull-to-refresh**: swipe down to reload
- **Filtering**: tap the filter icon to filter by:
  - Minimum severity (info, low, medium, high, critical)
  - Date range (calendar picker)
- **Error state**: shows a retry button when network fails
- **Empty state**: shows icon and message when no events match

### Specialized History Screens

Each wraps `EventListScreen` with pre-configured type filters:

| Screen | Event types | Title |
|--------|------------|-------|
| Login History | session_login, session_logout, session_lock, session_unlock, login_failed | Login / Session History |
| App History | process_start, process_stop | Application History |
| USB History | usb_connected, usb_disconnected | USB Device History |
| Network History | network_connected, network_disconnected, network_changed | Network History |
| File Audit | file_access | File Access Audit |
| Event Log | eventlog_entry | Windows Event Log |

All specialized screens inherit filtering, pagination, error/empty states, and date grouping from `EventListScreen`.

### Event Detail

Tapping any event opens a bottom sheet with:

- Event type icon and human-readable name
- Severity badge with color coding
- Human-readable summary line
- Formatted timestamp
- **Metadata section**: event type, category, severity, timestamp, device ID
- **Details section**: all structured payload key-value pairs, with nested JSON formatted for readability
- Event ID and sync timestamp
- Copy Event ID button

### Supported Event Types

| Type | Display Name | Category |
|------|-------------|----------|
| agent_started | Agent Started | System |
| system_startup | System Startup | System |
| system_shutdown | System Shutdown | System |
| session_login | Login | Session |
| session_logout | Logout | Session |
| session_lock | Screen Lock | Session |
| session_unlock | Screen Unlock | Session |
| login_failed | Login Failed | Session |
| process_start | App Started | Process |
| process_stop | App Stopped | Process |
| usb_connected | USB Connected | USB |
| usb_disconnected | USB Disconnected | USB |
| network_connected | Network Connected | Network |
| network_disconnected | Network Disconnected | Network |
| network_changed | Network Changed | Network |
| eventlog_entry | Event Log | Event Log |
| file_access | File Access | File |
| system_metrics | System Metrics | System |

### Pagination

- Page size: 50 events
- Infinite scroll triggers load when user reaches the bottom
- Filtered queries use server-side `offset`/`limit` via Supabase `.range()`
- Events are always ordered newest-first (`timestamp DESC`)

### Authorization

- All event queries include `device_id` filter scoped to the authenticated user's devices
- Supabase RLS policies enforce that users can only read their own devices' events
- The app never sends queries without a device ID
- Device IDs come from the authenticated devices list, not from URL parameters alone

## Reports

- Line charts for CPU, memory, and battery over time (from heartbeat history)
- Event summary table with counts per type (last 24 hours)

## Security

- Service role key is never in the Flutter app
- Supabase anon key is public (limited by RLS)
- Credentials are compile-time constants via `--dart-define`
- No credentials are logged
- After successful pairing, the device API key is not exposed
- JWT tokens are managed by supabase_flutter (auto-refresh)
- File audit events show metadata only — file contents are never collected or displayed

## Testing

```bash
# Run all tests (60 tests)
flutter test

# Static analysis
flutter analyze
```

Tests cover:

- All model `fromJson` parsing (Device, ActivityEvent, Heartbeat, UserProfile, PairingResult)
- Null/missing field handling and defaults
- EventTypes display names, icons, categories, severity colors
- All type group lists (session, process, USB, network)
- All 8 event history screens: rendering, empty state
- Dashboard: data rendering, null heartbeat state, Quick Actions
- Reports: data rendering, empty data state
- Timeline: date grouping, error state, filter UI
- Event detail: subtitle formatting for all event types
- EventTile: rendering, tap-to-open detail
- EventFilter: equality including eventTypes list
- Error state with retry button

## Manual Test Procedure

1. Build and install the debug APK:
   ```bash
   flutter build apk --debug \
     --dart-define=SUPABASE_URL=https://your-project.supabase.co \
     --dart-define=SUPABASE_ANON_KEY=<your-anon-key>
   adb install build/app/outputs/flutter-apk/app-debug.apk
   ```

2. Sign in with test account or create a new one

3. Pair a device:
   - Start the Windows agent
   - Note the 6-character pairing code from the agent console output
   - In the app, tap "Pair Device" and enter the code

4. Verify:
   - Device appears in the devices list with "online" status
   - Dashboard shows system metrics (CPU, memory, disk, battery)
   - Activity timeline shows recent events grouped by date
   - Filtering by severity and date range works
   - Each history screen filters correctly
   - Tapping an event opens the detail sheet with metadata
   - Reports show metric charts
   - Pull-to-refresh works on all screens
   - Error state shows when offline with retry button
   - Sign out and sign in again — data persists

## Troubleshooting

- **Empty event screens**: Verify the Windows agent is running and syncing. Check the agent console for sync errors.
- **No heartbeat data**: The agent sends heartbeats every 60 seconds. Wait for at least one heartbeat after pairing.
- **Login fails**: Ensure the email is confirmed. Check Supabase Auth logs.
- **Events not updating**: Pull-to-refresh forces a reload. Events are cached by Riverpod providers until invalidated.
- **Filters show no results**: Clear filters using the "Clear filters" button or the crossed-out filter icon in the app bar.
