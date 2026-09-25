import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:supabase_flutter/supabase_flutter.dart';

import '../screens/auth/email_verified_screen.dart';
import '../screens/auth/login_screen.dart';
import '../screens/auth/register_screen.dart';
import '../screens/dashboard/dashboard_screen.dart';
import '../screens/devices/devices_screen.dart';
import '../screens/events/event_list_screen.dart';
import '../screens/events/eventlog_screen.dart';
import '../screens/events/file_audit_screen.dart';
import '../screens/events/login_history_screen.dart';
import '../screens/events/network_history_screen.dart';
import '../screens/events/process_history_screen.dart';
import '../screens/events/usb_history_screen.dart';
import '../screens/pairing/pair_device_screen.dart';
import '../screens/reports/reports_screen.dart';
import '../screens/settings/notification_settings_screen.dart';
import '../screens/settings/settings_screen.dart';
import '../screens/shell.dart';

final rootNavigatorKey = GlobalKey<NavigatorState>();
final _shellNavigatorKey = GlobalKey<NavigatorState>();

GoRouter buildRouter() {
  return GoRouter(
    navigatorKey: rootNavigatorKey,
    initialLocation: '/devices',
    redirect: (context, state) {
      final isLoggedIn = Supabase.instance.client.auth.currentUser != null;
      final loc = state.matchedLocation;
      final uri = state.uri.toString();

      // GoRouter intercepts deep link URIs and tries to route them as paths.
      // Auth callback URIs must be handled here so the user sees the
      // verification result screen instead of a "Page Not Found" error.
      if (uri.contains('auth-callback')) {
        final fragment = state.uri.fragment;
        if (fragment.contains('error=')) {
          final params = Uri.splitQueryString(fragment);
          final desc = params['error_description'] ?? params['error'] ?? '';
          debugPrint('[Router] Auth callback error: $desc');
          return Uri(
            path: '/auth-verified',
            queryParameters: {'error': desc.isNotEmpty ? desc : 'Verification failed'},
          ).toString();
        }
        // Success callback — supabase_flutter establishes session in background
        debugPrint('[Router] Auth callback success — showing verified screen');
        return '/auth-verified';
      }

      final isAuthRoute = loc == '/login' || loc == '/register';
      final isVerifiedRoute = loc == '/auth-verified';

      if (isVerifiedRoute) return null;
      if (!isLoggedIn && !isAuthRoute) return '/login';
      if (isLoggedIn && isAuthRoute) return '/devices';
      return null;
    },
    routes: [
      GoRoute(
        path: '/',
        redirect: (_, _) => '/login',
      ),
      GoRoute(
        path: '/login',
        builder: (context, state) => const LoginScreen(),
      ),
      GoRoute(
        path: '/register',
        builder: (context, state) => const RegisterScreen(),
      ),
      GoRoute(
        path: '/auth-verified',
        builder: (context, state) {
          final errorMessage = state.uri.queryParameters['error'];
          return EmailVerifiedScreen(errorMessage: errorMessage);
        },
      ),
      ShellRoute(
        navigatorKey: _shellNavigatorKey,
        builder: (context, state, child) => AppShell(child: child),
        routes: [
          GoRoute(
            path: '/devices',
            builder: (context, state) => const DevicesScreen(),
          ),
          GoRoute(
            path: '/dashboard/:deviceId',
            builder: (context, state) => DashboardScreen(
              deviceId: state.pathParameters['deviceId']!,
            ),
          ),
          GoRoute(
            path: '/events/:deviceId',
            builder: (context, state) => EventListScreen(
              deviceId: state.pathParameters['deviceId']!,
            ),
          ),
          GoRoute(
            path: '/events/:deviceId/sessions',
            builder: (context, state) => LoginHistoryScreen(
              deviceId: state.pathParameters['deviceId']!,
            ),
          ),
          GoRoute(
            path: '/events/:deviceId/processes',
            builder: (context, state) => ProcessHistoryScreen(
              deviceId: state.pathParameters['deviceId']!,
            ),
          ),
          GoRoute(
            path: '/events/:deviceId/usb',
            builder: (context, state) => UsbHistoryScreen(
              deviceId: state.pathParameters['deviceId']!,
            ),
          ),
          GoRoute(
            path: '/events/:deviceId/network',
            builder: (context, state) => NetworkHistoryScreen(
              deviceId: state.pathParameters['deviceId']!,
            ),
          ),
          GoRoute(
            path: '/events/:deviceId/files',
            builder: (context, state) => FileAuditScreen(
              deviceId: state.pathParameters['deviceId']!,
            ),
          ),
          GoRoute(
            path: '/events/:deviceId/eventlog',
            builder: (context, state) => EventLogScreen(
              deviceId: state.pathParameters['deviceId']!,
            ),
          ),
          GoRoute(
            path: '/reports/:deviceId',
            builder: (context, state) => ReportsScreen(
              deviceId: state.pathParameters['deviceId']!,
            ),
          ),
          GoRoute(
            path: '/settings',
            builder: (context, state) => const SettingsScreen(),
          ),
          GoRoute(
            path: '/settings/notifications/:deviceId',
            builder: (context, state) => NotificationSettingsScreen(
              deviceId: state.pathParameters['deviceId']!,
            ),
          ),
        ],
      ),
      GoRoute(
        path: '/pair',
        parentNavigatorKey: rootNavigatorKey,
        builder: (context, state) => const PairDeviceScreen(),
      ),
    ],
  );
}
