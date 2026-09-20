import 'package:flutter_test/flutter_test.dart';

import 'package:laptop_guardian/models/device.dart';
import 'package:laptop_guardian/models/event.dart';
import 'package:laptop_guardian/models/event_type.dart';
import 'package:laptop_guardian/models/heartbeat.dart';
import 'package:laptop_guardian/models/user_profile.dart';
import 'package:laptop_guardian/providers/auth_provider.dart';
import 'package:laptop_guardian/services/auth_service.dart';
import 'package:laptop_guardian/services/pairing_service.dart';

void main() {
  group('Device model', () {
    test('fromJson parses correctly', () {
      final json = {
        'id': 'abc-123',
        'user_id': 'user-1',
        'device_name': 'My Laptop',
        'machine_name': 'DESKTOP-ABC',
        'os_version': 'Windows 11',
        'agent_version': '1.0.0',
        'status': 'online',
        'last_seen_at': '2026-09-20T10:00:00.000Z',
        'heartbeat_interval_s': 60,
        'created_at': '2026-09-01T00:00:00.000Z',
        'updated_at': '2026-09-20T10:00:00.000Z',
      };
      final device = Device.fromJson(json);
      expect(device.id, 'abc-123');
      expect(device.displayName, 'My Laptop');
      expect(device.machineName, 'DESKTOP-ABC');
      expect(device.isOnline, true);
      expect(device.isOffline, false);
      expect(device.isPairing, false);
    });

    test('displayName falls back to machine name', () {
      final json = {
        'id': 'abc-123',
        'machine_name': 'DESKTOP-ABC',
        'status': 'offline',
        'created_at': '2026-09-01T00:00:00.000Z',
        'updated_at': '2026-09-01T00:00:00.000Z',
      };
      final device = Device.fromJson(json);
      expect(device.displayName, 'DESKTOP-ABC');
      expect(device.isOffline, true);
    });

    test('status pairing', () {
      final json = {
        'id': 'abc-123',
        'machine_name': 'HOST',
        'status': 'pairing',
        'created_at': '2026-09-01T00:00:00.000Z',
        'updated_at': '2026-09-01T00:00:00.000Z',
      };
      final device = Device.fromJson(json);
      expect(device.isPairing, true);
    });
  });

  group('ActivityEvent model', () {
    test('fromJson parses correctly', () {
      final json = {
        'id': 'evt-1',
        'device_id': 'dev-1',
        'event_type': 'session_login',
        'severity': 'info',
        'timestamp': '2026-09-20T10:15:30.000Z',
        'payload': {'username': 'siddh'},
        'synced_at': '2026-09-20T10:16:00.000Z',
      };
      final event = ActivityEvent.fromJson(json);
      expect(event.id, 'evt-1');
      expect(event.eventType, 'session_login');
      expect(event.severity, 'info');
      expect(event.payload['username'], 'siddh');
      expect(event.isHighSeverity, false);
    });

    test('isHighSeverity for high and critical', () {
      final high = ActivityEvent.fromJson({
        'id': 'e1',
        'device_id': 'd1',
        'event_type': 'login_failed',
        'severity': 'high',
        'timestamp': '2026-09-20T10:00:00Z',
        'payload': {},
        'synced_at': '2026-09-20T10:00:00Z',
      });
      expect(high.isHighSeverity, true);

      final critical = ActivityEvent.fromJson({
        'id': 'e2',
        'device_id': 'd1',
        'event_type': 'login_failed',
        'severity': 'critical',
        'timestamp': '2026-09-20T10:00:00Z',
        'payload': {},
        'synced_at': '2026-09-20T10:00:00Z',
      });
      expect(critical.isHighSeverity, true);
    });

    test('defaults for missing fields', () {
      final event = ActivityEvent.fromJson({
        'id': 'e1',
        'device_id': 'd1',
        'event_type': 'test',
        'timestamp': '2026-09-20T10:00:00Z',
        'synced_at': '2026-09-20T10:00:00Z',
      });
      expect(event.severity, 'info');
      expect(event.payload, isEmpty);
    });
  });

  group('Heartbeat model', () {
    test('fromJson parses all fields', () {
      final json = {
        'id': 'hb-1',
        'device_id': 'dev-1',
        'cpu_percent': 25.5,
        'memory_percent': 68.3,
        'disk_percent': 45.0,
        'battery_percent': 82.0,
        'is_charging': true,
        'ip_address': '192.168.1.100',
        'created_at': '2026-09-20T10:00:00.000Z',
      };
      final hb = Heartbeat.fromJson(json);
      expect(hb.cpuPercent, 25.5);
      expect(hb.memoryPercent, 68.3);
      expect(hb.batteryPercent, 82.0);
      expect(hb.isCharging, true);
      expect(hb.ipAddress, '192.168.1.100');
    });

    test('fromJson handles null metrics', () {
      final json = {
        'id': 'hb-2',
        'device_id': 'dev-1',
        'created_at': '2026-09-20T10:00:00.000Z',
      };
      final hb = Heartbeat.fromJson(json);
      expect(hb.cpuPercent, isNull);
      expect(hb.memoryPercent, isNull);
      expect(hb.batteryPercent, isNull);
      expect(hb.isCharging, isNull);
    });
  });

  group('UserProfile model', () {
    test('fromJson parses correctly', () {
      final json = {
        'id': 'user-1',
        'full_name': 'Test User',
        'email': 'test@example.com',
        'created_at': '2026-09-01T00:00:00.000Z',
      };
      final profile = UserProfile.fromJson(json);
      expect(profile.fullName, 'Test User');
      expect(profile.email, 'test@example.com');
    });
  });

  group('PairingResult model', () {
    test('fromJson parses correctly', () {
      final json = {
        'device_id': 'dev-1',
        'device_name': 'My Laptop',
        'machine_name': 'DESKTOP-ABC',
        'status': 'online',
        'os_version': 'Windows 11',
        'agent_version': '1.0.0',
      };
      final result = PairingResult.fromJson(json);
      expect(result.deviceId, 'dev-1');
      expect(result.deviceName, 'My Laptop');
      expect(result.status, 'online');
    });
  });

  group('AuthService redirect configuration', () {
    test('authCallbackUrl uses custom scheme', () {
      expect(AuthService.authCallbackUrl, 'com.laptopguardian.app://auth-callback');
    });

    test('authCallbackUrl is a valid URI', () {
      final uri = Uri.parse(AuthService.authCallbackUrl);
      expect(uri.scheme, 'com.laptopguardian.app');
      expect(uri.host, 'auth-callback');
    });
  });

  group('DeepLinkState', () {
    test('default state is none', () {
      const state = DeepLinkState();
      expect(state.result, DeepLinkResult.none);
      expect(state.errorMessage, isNull);
    });

    test('verified state', () {
      const state = DeepLinkState(result: DeepLinkResult.verified);
      expect(state.result, DeepLinkResult.verified);
      expect(state.errorMessage, isNull);
    });

    test('error state with message', () {
      const state = DeepLinkState(
        result: DeepLinkResult.error,
        errorMessage: 'Token expired',
      );
      expect(state.result, DeepLinkResult.error);
      expect(state.errorMessage, 'Token expired');
    });
  });

  group('Auth callback URI parsing', () {
    test('success callback has access_token in fragment', () {
      final uri = Uri.parse(
        'com.laptopguardian.app://auth-callback#access_token=abc&refresh_token=def&type=signup',
      );
      final params = Uri.splitQueryString(uri.fragment);
      expect(params.containsKey('access_token'), true);
      expect(params.containsKey('refresh_token'), true);
      expect(params['type'], 'signup');
      expect(params.containsKey('error'), false);
    });

    test('error callback has error in fragment', () {
      final uri = Uri.parse(
        'com.laptopguardian.app://auth-callback#error=access_denied&error_description=Email+link+is+invalid+or+has+expired',
      );
      final params = Uri.splitQueryString(uri.fragment);
      expect(params.containsKey('error'), true);
      expect(params['error'], 'access_denied');
      expect(params.containsKey('access_token'), false);
    });

    test('non-auth scheme is ignored', () {
      final uri = Uri.parse('https://example.com/auth-callback#access_token=abc');
      expect(uri.scheme, isNot('com.laptopguardian.app'));
    });
  });

  group('EventTypes', () {
    test('displayName maps all known types', () {
      expect(EventTypes.displayName('session_login'), 'Login');
      expect(EventTypes.displayName('process_start'), 'App Started');
      expect(EventTypes.displayName('usb_connected'), 'USB Connected');
      expect(EventTypes.displayName('file_access'), 'File Access');
      expect(EventTypes.displayName('system_metrics'), 'System Metrics');
    });

    test('displayName handles unknown types', () {
      expect(EventTypes.displayName('unknown_type'), 'unknown type');
    });

    test('category maps correctly', () {
      expect(EventTypes.category('session_login'), EventCategory.session);
      expect(EventTypes.category('process_start'), EventCategory.process);
      expect(EventTypes.category('usb_connected'), EventCategory.usb);
      expect(EventTypes.category('network_connected'), EventCategory.network);
      expect(EventTypes.category('file_access'), EventCategory.file);
      expect(EventTypes.category('eventlog_entry'), EventCategory.eventLog);
      expect(EventTypes.category('system_metrics'), EventCategory.system);
    });

    test('severityColor returns distinct colors', () {
      final critical = EventTypes.severityColor('critical');
      final high = EventTypes.severityColor('high');
      final info = EventTypes.severityColor('info');
      expect(critical, isNot(equals(high)));
      expect(high, isNot(equals(info)));
    });

    test('session types list', () {
      expect(EventTypes.sessionTypes, contains('session_login'));
      expect(EventTypes.sessionTypes, contains('login_failed'));
      expect(EventTypes.sessionTypes.length, 5);
    });

    test('process types list', () {
      expect(EventTypes.processTypes, contains('process_start'));
      expect(EventTypes.processTypes, contains('process_stop'));
      expect(EventTypes.processTypes.length, 2);
    });
  });
}
