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
      final error = response.data is Map
          ? response.data['error'] as String? ?? 'Pairing failed'
          : 'Pairing failed (${response.status})';
      throw PairingException(error);
    }

    return PairingResult.fromJson(response.data as Map<String, dynamic>);
  }
}

class PairingException implements Exception {
  final String message;
  PairingException(this.message);

  @override
  String toString() => message;
}
