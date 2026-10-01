import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:supabase_flutter/supabase_flutter.dart';
import 'package:window_manager/window_manager.dart';

import 'app.dart';
import 'main_mobile.dart' as mobile;
import 'platform/platform_utils.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();

  if (isMobilePlatform) {
    await mobile.initMobile();
  }

  if (isDesktopPlatform) {
    await _initDesktopWindow();
  }

  final container = ProviderContainer();

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

Future<void> _initDesktopWindow() async {
  await windowManager.ensureInitialized();

  const windowOptions = WindowOptions(
    size: Size(1366, 768),
    minimumSize: Size(1024, 600),
    center: true,
    title: 'Laptop Guardian',
    titleBarStyle: TitleBarStyle.normal,
  );

  await windowManager.waitUntilReadyToShow(windowOptions, () async {
    await windowManager.show();
    await windowManager.focus();
  });
}
