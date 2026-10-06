class Device {
  final String id;
  final String? userId;
  final String? deviceName;
  final String machineName;
  final String? osVersion;
  final String? agentVersion;
  final String status;
  final String powerState;
  final String userSessionState;
  final DateTime? lastSeenAt;
  final DateTime? lastStartupAt;
  final DateTime? lastShutdownAt;
  final DateTime? lastSleepAt;
  final DateTime? lastWakeAt;
  final DateTime? lastLockAt;
  final DateTime? lastUnlockAt;
  final DateTime? lastLoginAt;
  final DateTime? lastLogoutAt;
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
    this.powerState = 'unknown',
    this.userSessionState = 'unknown',
    this.lastSeenAt,
    this.lastStartupAt,
    this.lastShutdownAt,
    this.lastSleepAt,
    this.lastWakeAt,
    this.lastLockAt,
    this.lastUnlockAt,
    this.lastLoginAt,
    this.lastLogoutAt,
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
      powerState: json['power_state'] as String? ?? 'unknown',
      userSessionState: json['user_session_state'] as String? ?? 'unknown',
      lastSeenAt: json['last_seen_at'] != null
          ? DateTime.parse(json['last_seen_at'] as String)
          : null,
      lastStartupAt: json['last_startup_at'] != null
          ? DateTime.parse(json['last_startup_at'] as String)
          : null,
      lastShutdownAt: json['last_shutdown_at'] != null
          ? DateTime.parse(json['last_shutdown_at'] as String)
          : null,
      lastSleepAt: json['last_sleep_at'] != null
          ? DateTime.parse(json['last_sleep_at'] as String)
          : null,
      lastWakeAt: json['last_wake_at'] != null
          ? DateTime.parse(json['last_wake_at'] as String)
          : null,
      lastLockAt: json['last_lock_at'] != null
          ? DateTime.parse(json['last_lock_at'] as String)
          : null,
      lastUnlockAt: json['last_unlock_at'] != null
          ? DateTime.parse(json['last_unlock_at'] as String)
          : null,
      lastLoginAt: json['last_login_at'] != null
          ? DateTime.parse(json['last_login_at'] as String)
          : null,
      lastLogoutAt: json['last_logout_at'] != null
          ? DateTime.parse(json['last_logout_at'] as String)
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

  bool get isLaptopOn => powerState == 'on' || powerState == 'starting';

  bool get isLaptopOff => powerState == 'off' || powerState == 'shutting_down';

  String get powerStateDisplay => switch (powerState) {
    'on' => 'ON',
    'starting' => 'Starting',
    'sleeping' => 'Sleeping',
    'shutting_down' => 'Shutting Down',
    'off' => 'OFF',
    _ => 'Unknown',
  };

  String get userSessionDisplay => switch (userSessionState) {
    'logged_in' => 'Logged In',
    'logged_out' => 'Logged Out',
    'locked' => 'Locked',
    _ => 'Unknown',
  };

  String get stateExplanation {
    if (powerState == 'sleeping') {
      return 'Your laptop is sleeping. The Windows session has not been logged out.';
    }
    if (powerState == 'off' || (isOffline && powerState != 'on')) {
      return 'The laptop appears to be off or unreachable. The last known state is preserved.';
    }
    if (isLaptopOn && userSessionState == 'locked') {
      return 'Your laptop is running, but the Windows screen is locked.';
    }
    if (isLaptopOn && isOffline && userSessionState == 'logged_in') {
      return 'The laptop is running and the Windows user is logged in, but the PC currently has no network connection.';
    }
    if (isLaptopOn && isOnline && userSessionState == 'logged_in') {
      return 'Your laptop is running normally with an active Windows session.';
    }
    if (isLaptopOn && userSessionState == 'logged_out') {
      return 'Your laptop is running but no Windows user is logged in.';
    }
    if (powerState == 'shutting_down') {
      return 'Your laptop is shutting down.';
    }
    return '';
  }

  Duration get timeSinceLastSeen {
    if (lastSeenAt == null) return Duration.zero;
    return DateTime.now().toUtc().difference(lastSeenAt!);
  }
}
