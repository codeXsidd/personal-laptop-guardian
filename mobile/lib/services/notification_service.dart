import 'dart:io';

import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:supabase_flutter/supabase_flutter.dart';

import '../models/notification_settings.dart';
import '../models/notification_token.dart';

class NotificationService {
  final SupabaseClient _client;
  final FirebaseMessaging _messaging;
  final FlutterLocalNotificationsPlugin _localNotifications;

  NotificationService(
    this._client, [
    FirebaseMessaging? messaging,
    FlutterLocalNotificationsPlugin? localNotifications,
  ])  : _messaging = messaging ?? FirebaseMessaging.instance,
        _localNotifications =
            localNotifications ?? FlutterLocalNotificationsPlugin();

  static const _securityChannelId = 'security_alerts';
  static const _securityChannelName = 'Security / High Severity';
  static const _generalChannelId = 'general_activity';
  static const _generalChannelName = 'General Activity';

  Future<void> initializeLocalNotifications() async {
    const androidSettings =
        AndroidInitializationSettings('@mipmap/ic_launcher');
    const initSettings = InitializationSettings(android: androidSettings);
    await _localNotifications.initialize(
      initSettings,
      onDidReceiveNotificationResponse: _onNotificationTap,
    );

    final android = _localNotifications
        .resolvePlatformSpecificImplementation<
            AndroidFlutterLocalNotificationsPlugin>();
    if (android != null) {
      await android.createNotificationChannel(const AndroidNotificationChannel(
        _securityChannelId,
        _securityChannelName,
        description: 'High severity and security event alerts',
        importance: Importance.high,
      ));
      await android.createNotificationChannel(const AndroidNotificationChannel(
        _generalChannelId,
        _generalChannelName,
        description: 'General activity notifications',
        importance: Importance.defaultImportance,
      ));
    }
  }

  void Function(String payload)? onNotificationTap;

  void _onNotificationTap(NotificationResponse response) {
    final payload = response.payload;
    if (payload != null && onNotificationTap != null) {
      onNotificationTap!(payload);
    } else {
      _pendingPayload = payload;
    }
  }

  String? _pendingPayload;

  String? consumePendingPayload() {
    final p = _pendingPayload;
    _pendingPayload = null;
    return p;
  }

  Future<bool> requestPermission() async {
    final settings = await _messaging.requestPermission(
      alert: true,
      badge: true,
      sound: true,
    );
    return settings.authorizationStatus == AuthorizationStatus.authorized ||
        settings.authorizationStatus == AuthorizationStatus.provisional;
  }

  Future<String?> getToken() async {
    try {
      return await _messaging.getToken();
    } catch (e) {
      debugPrint('[Notification] Failed to get FCM token: $e');
      return null;
    }
  }

  void onTokenRefresh(void Function(String token) callback) {
    _messaging.onTokenRefresh.listen(callback);
  }

  Future<void> registerToken(String fcmToken) async {
    final userId = _client.auth.currentUser?.id;
    if (userId == null) return;

    await _client.from('notification_tokens').upsert(
      {
        'user_id': userId,
        'fcm_token': fcmToken,
        'device_label': Platform.operatingSystem,
      },
      onConflict: 'user_id,fcm_token',
    );
  }

  Future<void> unregisterCurrentToken() async {
    final token = await getToken();
    if (token == null) return;
    final userId = _client.auth.currentUser?.id;
    if (userId == null) return;

    await _client
        .from('notification_tokens')
        .delete()
        .eq('user_id', userId)
        .eq('fcm_token', token);
  }

  Future<List<NotificationToken>> getTokens() async {
    final data = await _client
        .from('notification_tokens')
        .select()
        .order('created_at', ascending: false);
    return data.map((e) => NotificationToken.fromJson(e)).toList();
  }

  // --- Notification Settings ---

  Future<List<NotificationSetting>> getSettings(String deviceId) async {
    final data = await _client
        .from('notification_settings')
        .select()
        .eq('device_id', deviceId)
        .order('event_type');
    return data.map((e) => NotificationSetting.fromJson(e)).toList();
  }

  Future<void> upsertSetting({
    required String deviceId,
    required String eventType,
    required bool enabled,
    required String minSeverity,
  }) async {
    final userId = _client.auth.currentUser?.id;
    if (userId == null) return;

    await _client.from('notification_settings').upsert(
      {
        'user_id': userId,
        'device_id': deviceId,
        'event_type': eventType,
        'enabled': enabled,
        'min_severity': minSeverity,
      },
      onConflict: 'user_id,device_id,event_type',
    );
  }

  Future<void> initializeDefaultSettings(String deviceId) async {
    final existing = await getSettings(deviceId);
    if (existing.isNotEmpty) return;

    final userId = _client.auth.currentUser?.id;
    if (userId == null) return;

    final rows = <Map<String, dynamic>>[];
    for (final cat in NotificationSetting.categories.values) {
      for (final eventType in cat.eventTypes) {
        rows.add({
          'user_id': userId,
          'device_id': deviceId,
          'event_type': eventType,
          'enabled': true,
          'min_severity': cat.defaultMinSeverity,
        });
      }
    }
    if (rows.isNotEmpty) {
      await _client
          .from('notification_settings')
          .upsert(rows, onConflict: 'user_id,device_id,event_type');
    }
  }

  // --- Foreground notification display ---

  Future<void> showForegroundNotification(RemoteMessage message) async {
    final data = message.data;
    final notification = message.notification;
    final severity = data['severity'] ?? 'info';
    final isHighSeverity =
        severity == 'high' || severity == 'critical';

    final channelId =
        isHighSeverity ? _securityChannelId : _generalChannelId;
    final channelName =
        isHighSeverity ? _securityChannelName : _generalChannelName;

    await _localNotifications.show(
      message.hashCode,
      notification?.title ?? data['title'] ?? 'Laptop Guardian',
      notification?.body ?? data['body'] ?? '',
      NotificationDetails(
        android: AndroidNotificationDetails(
          channelId,
          channelName,
          importance:
              isHighSeverity ? Importance.high : Importance.defaultImportance,
          priority:
              isHighSeverity ? Priority.high : Priority.defaultPriority,
        ),
      ),
      payload: data['event_id'] != null
          ? '${data['device_id'] ?? ''}:${data['event_id']}'
          : null,
    );
  }
}
