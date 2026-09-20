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
    │   ├── event_list_screen.dart
    │   ├── event_detail_sheet.dart
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

## Screens

### Devices
Lists all paired devices with status indicator (online/offline/pairing), machine name, and last-seen time. Pull-to-refresh. FAB to pair new device.

### Dashboard
Per-device view with:
- Device header (name, status, OS version)
- System metrics cards (CPU, memory, disk, battery) with progress indicators
- Quick action grid for navigation
- 24h event type breakdown pie chart
- Recent activity timeline

### Activity Timeline
Generic paginated event list with infinite scroll. Supports filtering by event type (used by all specialized history screens). Tapping an event opens a bottom sheet with full payload details.

### Specialized History Screens
Each wraps EventListScreen with pre-configured filters:
- **Login History**: session_login, session_logout, session_lock, session_unlock, login_failed
- **App History**: process_start, process_stop
- **USB History**: usb_connected, usb_disconnected
- **Network History**: network_connected, network_disconnected, network_changed
- **File Audit**: file_access
- **Event Log**: eventlog_entry

### Reports
- Line charts for CPU, memory, and battery over time (from heartbeat history)
- Event summary table with counts per type (last 24 hours)

### Settings
- User profile display
- Sign out with confirmation dialog
- App version info

## Security

- Service role key is never in the Flutter app
- Supabase anon key is public (limited by RLS)
- Credentials are compile-time constants via `--dart-define`
- No credentials are logged
- After successful pairing, the device API key is not exposed
- JWT tokens are managed by supabase_flutter (auto-refresh)

## Testing

```bash
# Run 16 unit tests
flutter test

# Static analysis
flutter analyze
```

Tests cover:
- All model `fromJson` parsing (Device, ActivityEvent, Heartbeat, UserProfile, PairingResult)
- Null/missing field handling
- EventTypes display names, categories, severity colors
- Edge cases (empty payloads, unknown types)

## Manual Test Procedure

1. Build and install the debug APK:
   ```bash
   flutter build apk --debug \
     --dart-define=SUPABASE_URL=https://pfeubiedbwvjnlpiofmd.supabase.co \
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
   - Activity timeline shows recent events
   - Each history screen filters correctly
   - Reports show metric charts
   - Pull-to-refresh works on all screens
   - Sign out and sign in again — data persists
