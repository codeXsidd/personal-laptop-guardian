import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:laptop_guardian/models/device.dart';
import 'package:laptop_guardian/models/event.dart';
import 'package:laptop_guardian/models/heartbeat.dart';
import 'package:laptop_guardian/providers/device_provider.dart';
import 'package:laptop_guardian/providers/event_provider.dart';
import 'package:laptop_guardian/screens/dashboard/dashboard_screen.dart';
import 'package:laptop_guardian/screens/events/event_list_screen.dart';
import 'package:laptop_guardian/screens/events/eventlog_screen.dart';
import 'package:laptop_guardian/screens/events/file_audit_screen.dart';
import 'package:laptop_guardian/screens/events/login_history_screen.dart';
import 'package:laptop_guardian/screens/events/network_history_screen.dart';
import 'package:laptop_guardian/screens/events/process_history_screen.dart';
import 'package:laptop_guardian/screens/events/usb_history_screen.dart';
import 'package:laptop_guardian/screens/reports/reports_screen.dart';
import 'package:laptop_guardian/services/event_service.dart';

class MockEventService extends Mock implements EventService {}

const _deviceId = 'test-device-123';
final _now = DateTime.now().toUtc();

Device _testDevice() => Device(
      id: _deviceId,
      userId: 'user-1',
      deviceName: 'my laptop',
      machineName: 'BOOK-E1FMTGU0OR',
      osVersion: 'Microsoft Windows NT 10.0.26200.0',
      agentVersion: '1.0.0',
      status: 'online',
      lastSeenAt: _now,
      heartbeatIntervalS: 60,
      createdAt: _now.subtract(const Duration(days: 7)),
      updatedAt: _now,
    );

Heartbeat _testHeartbeat() => Heartbeat(
      id: 'hb-1',
      deviceId: _deviceId,
      cpuPercent: 42.5,
      memoryPercent: 95.0,
      diskPercent: 88.3,
      batteryPercent: 67,
      isCharging: true,
      ipAddress: '192.168.1.5',
      createdAt: _now,
    );

List<Heartbeat> _testHeartbeats() => List.generate(
      10,
      (i) => Heartbeat(
        id: 'hb-$i',
        deviceId: _deviceId,
        cpuPercent: 30.0 + i * 5,
        memoryPercent: 70.0 + i * 2,
        diskPercent: 88.3,
        batteryPercent: 100.0 - i * 3,
        isCharging: i.isEven,
        createdAt: _now.subtract(Duration(minutes: i * 5)),
      ),
    );

List<ActivityEvent> _testEvents(String type) => List.generate(
      5,
      (i) => ActivityEvent(
        id: 'evt-$type-$i',
        deviceId: _deviceId,
        eventType: type,
        severity: 'info',
        timestamp: _now.subtract(Duration(hours: i)),
        payload: _payloadForType(type, i),
        syncedAt: _now,
      ),
    );

Map<String, dynamic> _payloadForType(String type, int i) {
  return switch (type) {
    'session_login' || 'session_logout' => {'username': 'TestUser'},
    'process_start' || 'process_stop' => {'process_name': 'notepad.exe'},
    'usb_connected' || 'usb_disconnected' => {'device_name': 'USB Drive $i'},
    'network_connected' => {'adapter_name': 'Wi-Fi'},
    'eventlog_entry' => {'source': 'System', 'message': 'Test event $i'},
    'file_access' => {'access_type': 'read', 'file_path': 'C:\\test$i.txt'},
    _ => {},
  };
}

Widget _wrapScreen(Widget screen, {List<Override>? overrides}) {
  return ProviderScope(
    overrides: overrides ?? [],
    child: MaterialApp(home: screen),
  );
}

MockEventService _buildMockService(List<ActivityEvent> events) {
  final mock = MockEventService();
  when(() => mock.getEvents(
        any(),
        limit: any(named: 'limit'),
        offset: any(named: 'offset'),
        eventType: any(named: 'eventType'),
        eventTypes: any(named: 'eventTypes'),
        minSeverity: any(named: 'minSeverity'),
        after: any(named: 'after'),
        before: any(named: 'before'),
      )).thenAnswer((_) async => events);
  when(() => mock.getEventTypeCounts(
        any(),
        after: any(named: 'after'),
      )).thenAnswer((_) async {
    final counts = <String, int>{};
    for (final e in events) {
      counts[e.eventType] = (counts[e.eventType] ?? 0) + 1;
    }
    return counts;
  });
  return mock;
}

void main() {
  setUpAll(() {
    registerFallbackValue(DateTime.now());
  });

  group('Dashboard screen', () {
    testWidgets('renders with data and no errors', (tester) async {
      await tester.pumpWidget(_wrapScreen(
        const DashboardScreen(deviceId: _deviceId),
        overrides: [
          deviceByIdProvider(_deviceId)
              .overrideWith((ref) async => _testDevice()),
          latestHeartbeatProvider(_deviceId)
              .overrideWith((ref) async => _testHeartbeat()),
          recentEventsProvider(_deviceId)
              .overrideWith((ref) async => _testEvents('session_login')),
          eventTypeCountsProvider(_deviceId).overrideWith(
              (ref) async => {'session_login': 10, 'process_start': 25}),
        ],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Dashboard'), findsOneWidget);
      expect(find.text('my laptop'), findsOneWidget);
      expect(find.text('BOOK-E1FMTGU0OR'), findsOneWidget);
      expect(find.text('42.5%'), findsOneWidget);
      expect(find.text('95.0%'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);

      // Scroll down to reveal Quick Actions (may be off-screen)
      await tester.drag(find.byType(ListView), const Offset(0, -300));
      await tester.pumpAndSettle();
      expect(find.text('Quick Actions'), findsOneWidget);
    });

    testWidgets('renders with null heartbeat (no metrics)', (tester) async {
      await tester.pumpWidget(_wrapScreen(
        const DashboardScreen(deviceId: _deviceId),
        overrides: [
          deviceByIdProvider(_deviceId)
              .overrideWith((ref) async => _testDevice()),
          latestHeartbeatProvider(_deviceId)
              .overrideWith((ref) async => null),
          recentEventsProvider(_deviceId)
              .overrideWith((ref) async => <ActivityEvent>[]),
          eventTypeCountsProvider(_deviceId)
              .overrideWith((ref) async => <String, int>{}),
        ],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Dashboard'), findsOneWidget);
      expect(find.text('No metrics received yet.'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });
  });

  group('Timeline screen (EventListScreen)', () {
    testWidgets('renders with title and events', (tester) async {
      final mock = _buildMockService(_testEvents('session_login'));
      await tester.pumpWidget(_wrapScreen(
        const EventListScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Activity Timeline'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });

    testWidgets('shows empty state', (tester) async {
      final mock = _buildMockService([]);
      await tester.pumpWidget(_wrapScreen(
        const EventListScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('No events found.'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });
  });

  group('Sessions screen (LoginHistoryScreen)', () {
    testWidgets('renders with correct title', (tester) async {
      final mock = _buildMockService(_testEvents('session_login'));
      await tester.pumpWidget(_wrapScreen(
        const LoginHistoryScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Login / Session History'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });

    testWidgets('shows empty state', (tester) async {
      final mock = _buildMockService([]);
      await tester.pumpWidget(_wrapScreen(
        const LoginHistoryScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('No events found.'), findsOneWidget);
    });
  });

  group('Apps screen (ProcessHistoryScreen)', () {
    testWidgets('renders with correct title', (tester) async {
      final mock = _buildMockService(_testEvents('process_start'));
      await tester.pumpWidget(_wrapScreen(
        const ProcessHistoryScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Application History'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });

    testWidgets('shows empty state', (tester) async {
      final mock = _buildMockService([]);
      await tester.pumpWidget(_wrapScreen(
        const ProcessHistoryScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('No events found.'), findsOneWidget);
    });
  });

  group('USB screen (UsbHistoryScreen)', () {
    testWidgets('renders with correct title', (tester) async {
      final mock = _buildMockService(_testEvents('usb_connected'));
      await tester.pumpWidget(_wrapScreen(
        const UsbHistoryScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('USB Device History'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });

    testWidgets('shows empty state', (tester) async {
      final mock = _buildMockService([]);
      await tester.pumpWidget(_wrapScreen(
        const UsbHistoryScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('No events found.'), findsOneWidget);
    });
  });

  group('Network screen (NetworkHistoryScreen)', () {
    testWidgets('renders with correct title', (tester) async {
      final mock = _buildMockService(_testEvents('network_connected'));
      await tester.pumpWidget(_wrapScreen(
        const NetworkHistoryScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Network History'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });

    testWidgets('shows empty state', (tester) async {
      final mock = _buildMockService([]);
      await tester.pumpWidget(_wrapScreen(
        const NetworkHistoryScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('No events found.'), findsOneWidget);
    });
  });

  group('Files screen (FileAuditScreen)', () {
    testWidgets('renders with correct title', (tester) async {
      final mock = _buildMockService(_testEvents('file_access'));
      await tester.pumpWidget(_wrapScreen(
        const FileAuditScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('File Access Audit'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });

    testWidgets('shows empty state', (tester) async {
      final mock = _buildMockService([]);
      await tester.pumpWidget(_wrapScreen(
        const FileAuditScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('No events found.'), findsOneWidget);
    });
  });

  group('Event Log screen (EventLogScreen)', () {
    testWidgets('renders with correct title', (tester) async {
      final mock = _buildMockService(_testEvents('eventlog_entry'));
      await tester.pumpWidget(_wrapScreen(
        const EventLogScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Windows Event Log'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });

    testWidgets('shows empty state', (tester) async {
      final mock = _buildMockService([]);
      await tester.pumpWidget(_wrapScreen(
        const EventLogScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('No events found.'), findsOneWidget);
    });
  });

  group('Reports screen', () {
    testWidgets('renders with data', (tester) async {
      await tester.pumpWidget(_wrapScreen(
        const ReportsScreen(deviceId: _deviceId),
        overrides: [
          heartbeatHistoryProvider(_deviceId)
              .overrideWith((ref) async => _testHeartbeats()),
          eventTypeCountsProvider(_deviceId).overrideWith(
              (ref) async => {'session_login': 10, 'process_start': 25}),
        ],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Reports'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });

    testWidgets('handles empty data', (tester) async {
      await tester.pumpWidget(_wrapScreen(
        const ReportsScreen(deviceId: _deviceId),
        overrides: [
          heartbeatHistoryProvider(_deviceId)
              .overrideWith((ref) async => <Heartbeat>[]),
          eventTypeCountsProvider(_deviceId)
              .overrideWith((ref) async => <String, int>{}),
        ],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Reports'), findsOneWidget);
      expect(find.text('No heartbeat data available.'), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });
  });
}
