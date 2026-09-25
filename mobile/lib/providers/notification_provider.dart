import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/notification_settings.dart';
import '../services/notification_service.dart';
import 'auth_provider.dart';

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

  service.onTokenRefresh((newToken) async {
    await service.registerToken(newToken);
    debugPrint('[Notification] FCM token refreshed and registered');
  });

  FirebaseMessaging.onMessage.listen((message) {
    service.showForegroundNotification(message);
  });
});

final notificationSettingsProvider = FutureProvider.family<
    List<NotificationSetting>, String>((ref, deviceId) async {
  final service = ref.read(notificationServiceProvider);
  await service.initializeDefaultSettings(deviceId);
  return service.getSettings(deviceId);
});

final pendingNotificationPayloadProvider = StateProvider<String?>((ref) => null);
