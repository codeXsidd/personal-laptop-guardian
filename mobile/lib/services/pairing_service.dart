import 'package:supabase_flutter/supabase_flutter.dart';

class PairingResult {
  final String deviceId;
  final String deviceName;
  final String machineName;
  final String status;
  final String? osVersion;
  final String? agentVersion;

  PairingResult({
    required this.deviceId,
    required this.deviceName,
    required this.machineName,
    required this.status,
    this.osVersion,
    this.agentVersion,
  });

  factory PairingResult.fromJson(Map<String, dynamic> json) {
    return PairingResult(
      deviceId: json['device_id'] as String,
      deviceName: json['device_name'] as String,
      machineName: json['machine_name'] as String,
      status: json['status'] as String,
      osVersion: json['os_version'] as String?,
      agentVersion: json['agent_version'] as String?,
    );
  }
}

class PairingService {
  final SupabaseClient _client;

  PairingService(this._client);

  Future<PairingResult> pairDevice({
    required String pairingCode,
    required String deviceName,
  }) async {
    final response = await _client.functions.invoke(
      'pair-device',
      body: {
        'pairing_code': pairingCode.toUpperCase().trim(),
        'device_name': deviceName.trim(),
      },
    );

    if (response.status != 200) {
      final rawError = response.data is Map
          ? response.data['error'] as String? ?? ''
          : '';
      throw PairingException(_friendlyError(rawError, response.status));
    }

    return PairingResult.fromJson(response.data as Map<String, dynamic>);
  }

  String _friendlyError(String raw, int status) {
    final lower = raw.toLowerCase();
    if (lower.contains('expired')) {
      return 'Pairing code has expired. Open the Guardian agent log on your laptop to get a new code.';
    }
    if (lower.contains('already paired') || status == 409) {
      return 'This device has already been paired to an account.';
    }
    if (lower.contains('invalid') || lower.contains('not found')) {
      return 'Invalid pairing code. Check the 6-character code shown in the Guardian agent log on your laptop.';
    }
    if (status == 401) {
      return 'Please sign in again to pair a device.';
    }
    if (raw.isNotEmpty) return raw;
    return 'Pairing failed (error $status). Please try again.';
  }
}

class PairingException implements Exception {
  final String message;
  PairingException(this.message);

  @override
  String toString() => message;
}
