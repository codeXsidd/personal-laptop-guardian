import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/notification_settings.dart';
import '../services/device_service.dart';
import '../services/notification_service.dart';
import 'auth_provider.dart';
import 'device_provider.dart';
import 'event_provider.dart';

final notificationServiceProvider = Provider<NotificationService>((ref) {
  return NotificationService(ref.watch(supabaseClientProvider));
});

final notificationInitProvider = FutureProvider<void>((ref) async {
  final user = ref.watch(currentUserProvider);
  if (user == null) return;

  final service = ref.read(notificationServiceProvider);
  await service.initializeLocalNotifications();

  final granted = await service.requestPermission();
  if (!granted) {
    debugPrint('[Notification] Permission not granted');
    return;
  }

  final token = await service.getToken();
  if (token != null) {
    await service.registerToken(token);
    debugPrint('[Notification] FCM token registered');
  }

  final refreshSub = service.onTokenRefresh((newToken) async {
    await service.registerToken(newToken);
    debugPrint('[Notification] FCM token refreshed and registered');
  });

  final messageSub = FirebaseMessaging.onMessage.listen((message) {
    service.showForegroundNotification(message);
    final deviceId = message.data['device_id'];
    if (deviceId != null) {
      ref.invalidate(recentEventsProvider(deviceId));
      ref.invalidate(eventTypeCountsProvider(deviceId));
      ref.invalidate(latestHeartbeatProvider(deviceId));
    }
    ref.invalidate(devicesProvider);
  });

  // Initialize default notification settings for all paired devices
  try {
    final client = ref.read(supabaseClientProvider);
    final deviceService = DeviceService(client);
    final devices = await deviceService.getDevices();
    for (final device in devices) {
      await service.initializeDefaultSettings(device.id);
    }
    if (devices.isNotEmpty) {
      debugPrint('[Notification] Default settings initialized for ${devices.length} device(s)');
    }
  } catch (e) {
    debugPrint('[Notification] Failed to initialize default settings: $e');
  }

  ref.onDispose(() {
    refreshSub?.cancel();
    messageSub.cancel();
  });
});

final notificationSettingsProvider = FutureProvider.autoDispose.family<
    List<NotificationSetting>, String>((ref, deviceId) async {
  final service = ref.read(notificationServiceProvider);
  await service.initializeDefaultSettings(deviceId);
  return service.getSettings(deviceId);
});

final pendingNotificationPayloadProvider = StateProvider<String?>((ref) => null);
