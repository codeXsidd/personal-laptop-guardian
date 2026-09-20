class Device {
  final String id;
  final String? userId;
  final String? deviceName;
  final String machineName;
  final String? osVersion;
  final String? agentVersion;
  final String status;
  final DateTime? lastSeenAt;
  final int heartbeatIntervalS;
  final DateTime createdAt;
  final DateTime updatedAt;

  Device({
    required this.id,
    this.userId,
    this.deviceName,
    required this.machineName,
    this.osVersion,
    this.agentVersion,
    required this.status,
    this.lastSeenAt,
    this.heartbeatIntervalS = 60,
    required this.createdAt,
    required this.updatedAt,
  });

  factory Device.fromJson(Map<String, dynamic> json) {
    return Device(
      id: json['id'] as String,
      userId: json['user_id'] as String?,
      deviceName: json['device_name'] as String?,
      machineName: json['machine_name'] as String,
      osVersion: json['os_version'] as String?,
      agentVersion: json['agent_version'] as String?,
      status: json['status'] as String? ?? 'offline',
      lastSeenAt: json['last_seen_at'] != null
          ? DateTime.parse(json['last_seen_at'] as String)
          : null,
      heartbeatIntervalS: json['heartbeat_interval_s'] as int? ?? 60,
      createdAt: DateTime.parse(json['created_at'] as String),
      updatedAt: DateTime.parse(json['updated_at'] as String),
    );
  }

  String get displayName => deviceName ?? machineName;

  bool get isOnline => status == 'online';

  bool get isOffline => status == 'offline';

  bool get isPairing => status == 'pairing';

  Duration get timeSinceLastSeen {
    if (lastSeenAt == null) return Duration.zero;
    return DateTime.now().toUtc().difference(lastSeenAt!);
  }
}
