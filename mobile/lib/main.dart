import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:supabase_flutter/supabase_flutter.dart';

import 'app.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  final container = ProviderContainer();

  // supabase_flutter throws unhandled AuthException when a deep link carries
  // error params (expired / already-used confirmation links). Suppress the
  // exception here so it doesn't crash the app — GoRouter's redirect already
  // routes the user to the verification-result screen.
  final originalOnError = PlatformDispatcher.instance.onError;
  PlatformDispatcher.instance.onError = (error, stack) {
    if (error is AuthException) {
      debugPrint('[DeepLink] Suppressed AuthException: ${error.message}');
      return true;
    }
    return originalOnError?.call(error, stack) ?? false;
  };

  runApp(UncontrolledProviderScope(
    container: container,
    child: const LaptopGuardianApp(),
  ));
}
