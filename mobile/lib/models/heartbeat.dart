class Heartbeat {
  final String id;
  final String deviceId;
  final double? cpuPercent;
  final double? memoryPercent;
  final double? diskPercent;
  final double? batteryPercent;
  final bool? isCharging;
  final String? ipAddress;
  final DateTime createdAt;

  Heartbeat({
    required this.id,
    required this.deviceId,
    this.cpuPercent,
    this.memoryPercent,
    this.diskPercent,
    this.batteryPercent,
    this.isCharging,
    this.ipAddress,
    required this.createdAt,
  });

  factory Heartbeat.fromJson(Map<String, dynamic> json) {
    return Heartbeat(
      id: json['id'] as String,
      deviceId: json['device_id'] as String,
      cpuPercent: (json['cpu_percent'] as num?)?.toDouble(),
      memoryPercent: (json['memory_percent'] as num?)?.toDouble(),
      diskPercent: (json['disk_percent'] as num?)?.toDouble(),
      batteryPercent: (json['battery_percent'] as num?)?.toDouble(),
      isCharging: json['is_charging'] as bool?,
      ipAddress: json['ip_address'] as String?,
      createdAt: DateTime.parse(json['created_at'] as String),
    );
  }
}
