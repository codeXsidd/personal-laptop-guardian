import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:supabase_flutter/supabase_flutter.dart';

import 'package:laptop_guardian/app.dart';
import 'package:laptop_guardian/config/supabase_config.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  setUpAll(() async {
    final originalOnError = PlatformDispatcher.instance.onError;
    PlatformDispatcher.instance.onError = (error, stack) {
      if (error is AuthException) return true;
      return originalOnError?.call(error, stack) ?? false;
    };

    await Supabase.initialize(
      url: SupabaseConfig.url,
      publishableKey: SupabaseConfig.anonKey,
    );

    await Supabase.instance.client.auth.signInWithPassword(
      email: const String.fromEnvironment('TEST_EMAIL'),
      password: const String.fromEnvironment('TEST_PASSWORD'),
    );
  });

  group('Quick Action screens', () {
    late ProviderContainer container;

    setUp(() {
      container = ProviderContainer();
    });

    tearDown(() {
      container.dispose();
    });

    Future<void> pumpApp(WidgetTester tester) async {
      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: const LaptopGuardianApp(),
        ),
      );
      // Wait for Supabase init and data load
      for (var i = 0; i < 20; i++) {
        await tester.pump(const Duration(milliseconds: 500));
      }
      await tester.pumpAndSettle(const Duration(seconds: 2));
    }

    Future<void> testQuickAction(
      WidgetTester tester,
      String buttonLabel,
      String expectedTitle,
    ) async {
      await pumpApp(tester);

      // Should land on /devices since authenticated
      // Tap the device card
      final deviceCard = find.text('my laptop');
      if (deviceCard.evaluate().isNotEmpty) {
        await tester.tap(deviceCard);
        await tester.pumpAndSettle(const Duration(seconds: 5));
      }

      // Verify we're on Dashboard
      expect(find.text('Dashboard'), findsOneWidget,
          reason: 'Should be on Dashboard');

      // Scroll down to make Quick Actions visible
      final listView = find.byType(ListView);
      if (listView.evaluate().isNotEmpty) {
        await tester.drag(listView.first, const Offset(0, -400));
        await tester.pumpAndSettle();
      }

      // Tap the Quick Action
      final button = find.text(buttonLabel);
      expect(button, findsOneWidget,
          reason: '$buttonLabel not found on Dashboard');
      await tester.tap(button);
      await tester.pumpAndSettle(const Duration(seconds: 5));

      // Verify screen opened
      expect(find.text(expectedTitle), findsWidgets,
          reason: '$expectedTitle not visible after tapping $buttonLabel');

      // No red error widgets
      expect(find.byType(ErrorWidget), findsNothing,
          reason: '$buttonLabel screen has red error widget');

      // Test back navigation
      final backButton = find.byIcon(Icons.arrow_back);
      if (backButton.evaluate().isNotEmpty) {
        await tester.tap(backButton.first);
        await tester.pumpAndSettle(const Duration(seconds: 3));
        expect(find.text('Dashboard'), findsOneWidget,
            reason: 'Back from $buttonLabel should return to Dashboard');
      }
    }

    testWidgets('Timeline screen', (tester) async {
      await testQuickAction(tester, 'Timeline', 'Activity Timeline');
    });

    testWidgets('Sessions screen', (tester) async {
      await testQuickAction(tester, 'Sessions', 'Login / Session History');
    });

    testWidgets('Apps screen', (tester) async {
      await testQuickAction(tester, 'Apps', 'Application History');
    });

    testWidgets('USB screen', (tester) async {
      await testQuickAction(tester, 'USB', 'USB Device History');
    });

    testWidgets('Network screen', (tester) async {
      await testQuickAction(tester, 'Network', 'Network History');
    });

    testWidgets('Files screen', (tester) async {
      await testQuickAction(tester, 'Files', 'File Access Audit');
    });

    testWidgets('Event Log screen', (tester) async {
      await testQuickAction(tester, 'Event Log', 'Windows Event Log');
    });

    testWidgets('Reports screen', (tester) async {
      await testQuickAction(tester, 'Reports', 'Reports');
    });
  });
}
