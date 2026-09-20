class ActivityEvent {
  final String id;
  final String deviceId;
  final String eventType;
  final String severity;
  final DateTime timestamp;
  final Map<String, dynamic> payload;
  final DateTime syncedAt;

  ActivityEvent({
    required this.id,
    required this.deviceId,
    required this.eventType,
    required this.severity,
    required this.timestamp,
    required this.payload,
    required this.syncedAt,
  });

  factory ActivityEvent.fromJson(Map<String, dynamic> json) {
    return ActivityEvent(
      id: json['id'] as String,
      deviceId: json['device_id'] as String,
      eventType: json['event_type'] as String,
      severity: json['severity'] as String? ?? 'info',
      timestamp: DateTime.parse(json['timestamp'] as String),
      payload: (json['payload'] as Map?)?.cast<String, dynamic>() ?? {},
      syncedAt: DateTime.parse(json['synced_at'] as String),
    );
  }

  bool get isHighSeverity =>
      severity == 'high' || severity == 'critical';
}
