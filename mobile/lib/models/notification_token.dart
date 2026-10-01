class NotificationToken {
  final String id;
  final String userId;
  final String fcmToken;
  final String? deviceLabel;
  final DateTime createdAt;
  final DateTime updatedAt;

  NotificationToken({
    required this.id,
    required this.userId,
    required this.fcmToken,
    this.deviceLabel,
    required this.createdAt,
    required this.updatedAt,
  });

  factory NotificationToken.fromJson(Map<String, dynamic> json) {
    return NotificationToken(
      id: json['id'] as String,
      userId: json['user_id'] as String,
      fcmToken: json['fcm_token'] as String,
      deviceLabel: json['device_label'] as String?,
      createdAt: DateTime.parse(json['created_at'] as String),
      updatedAt: DateTime.parse(json['updated_at'] as String),
    );
  }
}
