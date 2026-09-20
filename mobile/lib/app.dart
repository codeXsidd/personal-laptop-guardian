import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:supabase_flutter/supabase_flutter.dart';

import 'config/routes.dart';
import 'config/theme.dart';
import 'providers/auth_provider.dart';

class LaptopGuardianApp extends ConsumerStatefulWidget {
  const LaptopGuardianApp({super.key});

  @override
  ConsumerState<LaptopGuardianApp> createState() => _LaptopGuardianAppState();
}

class _LaptopGuardianAppState extends ConsumerState<LaptopGuardianApp> {
  GoRouter? _router;

  @override
  void dispose() {
    _router?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final init = ref.watch(supabaseInitProvider);

    // When auth state changes to signedIn from a deep link (not from the
    // login form), navigate to the verification-success screen.
    ref.listen<AsyncValue<AuthState>>(authStateProvider, (prev, next) {
      next.whenData((authState) {
        if (authState.event == AuthChangeEvent.signedIn) {
          final fromForm = ref.read(loginFormActiveProvider);
          if (!fromForm && _router != null) {
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
                  Text(
                    error.toString(),
                    textAlign: TextAlign.center,
                  ),
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
