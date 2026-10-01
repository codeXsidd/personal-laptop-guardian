import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:supabase_flutter/supabase_flutter.dart';

import 'config/routes.dart';
import 'config/theme.dart';
import 'platform/platform_utils.dart';
import 'providers/auth_provider.dart';
import 'providers/notification_provider.dart';

class LaptopGuardianApp extends ConsumerStatefulWidget {
  const LaptopGuardianApp({super.key});

  @override
  ConsumerState<LaptopGuardianApp> createState() => _LaptopGuardianAppState();
}

class _LaptopGuardianAppState extends ConsumerState<LaptopGuardianApp> {
  GoRouter? _router;
  bool _notificationsInitialized = false;

  @override
  void dispose() {
    _router?.dispose();
    super.dispose();
  }

  void _initNotifications() {
    if (_notificationsInitialized || isDesktopPlatform) return;
    _notificationsInitialized = true;

    try {
      ref.read(notificationInitProvider);

      ref.read(notificationServiceProvider).onNotificationTap = (payload) {
        final parts = payload.split(':');
        if (parts.length >= 2 && _router != null) {
          final deviceId = parts[0];
          ref.read(pendingNotificationPayloadProvider.notifier).state = payload;
          _router!.go('/dashboard/$deviceId');
        }
      };

      _initFirebaseMessageHandlers();
    } catch (e) {
      debugPrint('[Notification] Init error: $e');
    }
  }

  void _initFirebaseMessageHandlers() {
    if (!isMobilePlatform) return;
    try {
      // Firebase messaging handlers for mobile only
      _setupMobileMessaging();
    } catch (e) {
      debugPrint('[Firebase] Messaging setup error: $e');
    }
  }

  void _setupMobileMessaging() {
    // Implemented via notification_provider on mobile
  }

  @override
  Widget build(BuildContext context) {
    final init = ref.watch(supabaseInitProvider);

    ref.listen<AsyncValue<AuthState>>(authStateProvider, (prev, next) {
      next.whenData((authState) {
        if (authState.event == AuthChangeEvent.signedIn) {
          final fromForm = ref.read(loginFormActiveProvider);
          if (!fromForm && _router != null && !isDesktopPlatform) {
            debugPrint('[DeepLink] signedIn event (not from form) — email verified');
            _router!.go('/auth-verified');
          }
        } else if (authState.event == AuthChangeEvent.signedOut) {
          debugPrint('[Auth] signedOut event — navigating to /login');
          _router?.go('/login');
        }
      });
    });

    return init.when(
      loading: () => _materialApp(
        home: const Scaffold(
          body: Center(child: CircularProgressIndicator()),
        ),
      ),
      error: (error, _) => _materialApp(
        home: Scaffold(
          body: Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.error_outline, size: 48, color: Colors.red),
                  const SizedBox(height: 16),
                  const Text(
                    'Failed to initialize',
                    style: TextStyle(fontSize: 20, fontWeight: FontWeight.bold),
                  ),
                  const SizedBox(height: 8),
                  Text(error.toString(), textAlign: TextAlign.center),
                  const SizedBox(height: 24),
                  ElevatedButton(
                    onPressed: () => ref.invalidate(supabaseInitProvider),
                    child: const Text('Retry'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
      data: (_) {
        _router ??= buildRouter();
        _initNotifications();
        return MaterialApp.router(
          title: 'Laptop Guardian',
          theme: AppTheme.light(),
          darkTheme: AppTheme.dark(),
          themeMode: ThemeMode.system,
          routerConfig: _router!,
          debugShowCheckedModeBanner: false,
        );
      },
    );
  }

  MaterialApp _materialApp({required Widget home}) {
    return MaterialApp(
      title: 'Laptop Guardian',
      theme: AppTheme.light(),
      darkTheme: AppTheme.dark(),
      themeMode: ThemeMode.system,
      debugShowCheckedModeBanner: false,
      home: home,
    );
  }
}
