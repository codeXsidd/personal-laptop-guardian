class NotificationSetting {
  final String id;
  final String userId;
  final String deviceId;
  final String eventType;
  final String minSeverity;
  final bool enabled;
  final DateTime createdAt;
  final DateTime updatedAt;

  NotificationSetting({
    required this.id,
    required this.userId,
    required this.deviceId,
    required this.eventType,
    required this.minSeverity,
    required this.enabled,
    required this.createdAt,
    required this.updatedAt,
  });

  factory NotificationSetting.fromJson(Map<String, dynamic> json) {
    return NotificationSetting(
      id: json['id'] as String,
      userId: json['user_id'] as String,
      deviceId: json['device_id'] as String,
      eventType: json['event_type'] as String,
      minSeverity: (json['min_severity'] as String?) ?? 'high',
      enabled: (json['enabled'] as bool?) ?? true,
      createdAt: DateTime.parse(json['created_at'] as String),
      updatedAt: DateTime.parse(json['updated_at'] as String),
    );
  }

  static const categories = <String, NotificationCategory>{
    'security': NotificationCategory(
      key: 'security',
      label: 'Security / High Severity',
      eventTypes: ['login_failed'],
      defaultMinSeverity: 'high',
    ),
    'session': NotificationCategory(
      key: 'session',
      label: 'Login / Session Events',
      eventTypes: [
        'session_login',
        'session_logout',
        'session_lock',
        'session_unlock',
      ],
      defaultMinSeverity: 'info',
    ),
    'usb': NotificationCategory(
      key: 'usb',
      label: 'USB Events',
      eventTypes: ['usb_connected', 'usb_disconnected'],
      defaultMinSeverity: 'info',
    ),
    'network': NotificationCategory(
      key: 'network',
      label: 'Network Events',
      eventTypes: [
        'network_connected',
        'network_disconnected',
        'network_changed',
      ],
      defaultMinSeverity: 'info',
    ),
    'process': NotificationCategory(
      key: 'process',
      label: 'Application / Process Events',
      eventTypes: ['process_start', 'process_stop'],
      defaultMinSeverity: 'medium',
    ),
    'file': NotificationCategory(
      key: 'file',
      label: 'File Audit Events',
      eventTypes: ['file_access'],
      defaultMinSeverity: 'info',
    ),
  };
}

class NotificationCategory {
  final String key;
  final String label;
  final List<String> eventTypes;
  final String defaultMinSeverity;

  const NotificationCategory({
    required this.key,
    required this.label,
    required this.eventTypes,
    required this.defaultMinSeverity,
  });
}
