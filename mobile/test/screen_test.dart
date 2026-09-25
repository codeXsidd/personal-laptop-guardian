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

List<ActivityEvent> _testEvents(String type, {int count = 5}) => List.generate(
      count,
      (i) => ActivityEvent(
        id: 'evt-$type-$i',
        deviceId: _deviceId,
        eventType: type,
        severity: i == 0 ? 'high' : 'info',
        timestamp: _now.subtract(Duration(hours: i)),
        payload: _payloadForType(type, i),
        syncedAt: _now,
      ),
    );

Map<String, dynamic> _payloadForType(String type, int i) {
  return switch (type) {
    'session_login' || 'session_logout' => {'username': 'TestUser'},
    'process_start' => {'process_name': 'notepad.exe', 'pid': 1234 + i},
    'process_stop' => {
      'process_name': 'notepad.exe',
      'pid': 1234 + i,
      'duration_s': 120 + i * 10
    },
    'usb_connected' || 'usb_disconnected' => {'device_name': 'USB Drive $i'},
    'network_connected' => {
      'adapter_name': 'Wi-Fi',
      'ip_address': '192.168.1.$i'
    },
    'eventlog_entry' => {
      'source': 'System',
      'message': 'Test event $i',
      'level': 'Information',
      'event_id': 100 + i,
      'channel': 'System'
    },
    'file_access' => {
      'access_type': 'read',
      'file_path': 'C:\\test$i.txt',
      'user': 'TestUser'
    },
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

MockEventService _buildFailingMockService() {
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
      )).thenThrow(Exception('Network error'));
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

    testWidgets('groups events by date', (tester) async {
      final mock = _buildMockService(_testEvents('session_login'));
      await tester.pumpWidget(_wrapScreen(
        const EventListScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Today'), findsOneWidget);
    });

    testWidgets('shows empty state with icon', (tester) async {
      final mock = _buildMockService([]);
      await tester.pumpWidget(_wrapScreen(
        const EventListScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('No events found.'), findsOneWidget);
      expect(find.byIcon(Icons.event_busy), findsOneWidget);
      expect(find.byType(ErrorWidget), findsNothing);
    });

    testWidgets('shows error state with retry', (tester) async {
      final mock = _buildFailingMockService();
      await tester.pumpWidget(_wrapScreen(
        const EventListScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.text('Failed to load events. Tap to retry.'), findsOneWidget);
      expect(find.byIcon(Icons.cloud_off), findsOneWidget);
      expect(find.text('Retry'), findsOneWidget);
    });

    testWidgets('has filter button in app bar', (tester) async {
      final mock = _buildMockService(_testEvents('session_login'));
      await tester.pumpWidget(_wrapScreen(
        const EventListScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      expect(find.byIcon(Icons.filter_list), findsOneWidget);
    });

    testWidgets('opens filter sheet', (tester) async {
      final mock = _buildMockService(_testEvents('session_login'));
      await tester.pumpWidget(_wrapScreen(
        const EventListScreen(deviceId: _deviceId),
        overrides: [eventServiceProvider.overrideWithValue(mock)],
      ));
      await tester.pumpAndSettle();

      await tester.tap(find.byIcon(Icons.filter_list));
      await tester.pumpAndSettle();

      expect(find.text('Filters'), findsOneWidget);
      expect(find.text('Minimum severity'), findsOneWidget);
      expect(find.text('Date range'), findsOneWidget);
      expect(find.text('Apply'), findsOneWidget);
      expect(find.text('Clear all'), findsOneWidget);
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

  group('Event detail', () {
    testWidgets('eventSubtitle returns correct text for each event type',
        (tester) async {
      expect(
          eventSubtitle(ActivityEvent(
            id: '1',
            deviceId: _deviceId,
            eventType: 'session_login',
            severity: 'info',
            timestamp: _now,
            payload: {'username': 'Admin'},
            syncedAt: _now,
          )),
          'Admin');

      expect(
          eventSubtitle(ActivityEvent(
            id: '2',
            deviceId: _deviceId,
            eventType: 'process_start',
            severity: 'info',
            timestamp: _now,
            payload: {'process_name': 'chrome.exe', 'pid': 5678},
            syncedAt: _now,
          )),
          'chrome.exe (PID 5678)');

      expect(
          eventSubtitle(ActivityEvent(
            id: '3',
            deviceId: _deviceId,
            eventType: 'usb_connected',
            severity: 'info',
            timestamp: _now,
            payload: {'device_name': 'SanDisk USB'},
            syncedAt: _now,
          )),
          'SanDisk USB');

      expect(
          eventSubtitle(ActivityEvent(
            id: '4',
            deviceId: _deviceId,
            eventType: 'network_connected',
            severity: 'info',
            timestamp: _now,
            payload: {'adapter_name': 'Ethernet', 'ip_address': '10.0.0.1'},
            syncedAt: _now,
          )),
          'Ethernet 10.0.0.1');

      expect(
          eventSubtitle(ActivityEvent(
            id: '5',
            deviceId: _deviceId,
            eventType: 'file_access',
            severity: 'info',
            timestamp: _now,
            payload: {'access_type': 'write', 'file_path': 'C:\\data.txt'},
            syncedAt: _now,
          )),
          'write C:\\data.txt');

      expect(
          eventSubtitle(ActivityEvent(
            id: '6',
            deviceId: _deviceId,
            eventType: 'eventlog_entry',
            severity: 'medium',
            timestamp: _now,
            payload: {
              'level': 'Warning',
              'source': 'Kernel',
              'message': 'Disk slow'
            },
            syncedAt: _now,
          )),
          '[Warning] Kernel: Disk slow');
    });
  });

  group('EventFilter equality', () {
    test('same fields are equal', () {
      final a = EventFilter(deviceId: 'd1', eventType: 'usb_connected');
      final b = EventFilter(deviceId: 'd1', eventType: 'usb_connected');
      expect(a, equals(b));
      expect(a.hashCode, equals(b.hashCode));
    });

    test('different eventTypes are not equal', () {
      final a = EventFilter(
          deviceId: 'd1', eventTypes: ['session_login', 'session_logout']);
      final b = EventFilter(
          deviceId: 'd1', eventTypes: ['process_start', 'process_stop']);
      expect(a, isNot(equals(b)));
    });

    test('null vs non-null eventTypes are not equal', () {
      final a = EventFilter(deviceId: 'd1');
      final b = EventFilter(
          deviceId: 'd1', eventTypes: ['session_login']);
      expect(a, isNot(equals(b)));
    });

    test('same eventTypes lists are equal', () {
      final a = EventFilter(
          deviceId: 'd1', eventTypes: ['a', 'b']);
      final b = EventFilter(
          deviceId: 'd1', eventTypes: ['a', 'b']);
      expect(a, equals(b));
      expect(a.hashCode, equals(b.hashCode));
    });
  });

  group('EventTile widget', () {
    testWidgets('renders event data correctly', (tester) async {
      final event = ActivityEvent(
        id: 'evt-1',
        deviceId: _deviceId,
        eventType: 'session_login',
        severity: 'high',
        timestamp: _now,
        payload: {'username': 'Admin'},
        syncedAt: _now,
      );

      await tester.pumpWidget(MaterialApp(
        home: Scaffold(body: EventTile(event: event)),
      ));
      await tester.pumpAndSettle();

      expect(find.text('Login'), findsOneWidget);
      expect(find.text('Admin'), findsOneWidget);
      expect(find.text('HIGH'), findsOneWidget);
    });

    testWidgets('opens detail sheet on tap', (tester) async {
      final event = ActivityEvent(
        id: 'evt-1',
        deviceId: _deviceId,
        eventType: 'process_start',
        severity: 'info',
        timestamp: _now,
        payload: {'process_name': 'notepad.exe', 'pid': 1234},
        syncedAt: _now,
      );

      await tester.pumpWidget(MaterialApp(
        home: Scaffold(body: EventTile(event: event)),
      ));
      await tester.pumpAndSettle();
      await tester.tap(find.byType(EventTile));
      await tester.pumpAndSettle();

      expect(find.text('App Started'), findsWidgets);
      expect(find.text('Metadata'), findsOneWidget);
      expect(find.text('Event Type'), findsOneWidget);
    });
  });
}
