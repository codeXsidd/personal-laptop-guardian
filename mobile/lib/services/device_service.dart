import 'package:supabase_flutter/supabase_flutter.dart';

import '../models/device.dart';
import '../models/heartbeat.dart';

class DeviceService {
  final SupabaseClient _client;

  DeviceService(this._client);

  Future<List<Device>> getDevices() async {
    final data = await _client
        .from('devices')
        .select()
        .order('created_at', ascending: false);
    return (data as List).map((e) => Device.fromJson(e)).toList();
  }

  Future<Device?> getDevice(String deviceId) async {
    final data = await _client
        .from('devices')
        .select()
        .eq('id', deviceId)
        .maybeSingle();
    if (data == null) return null;
    return Device.fromJson(data);
  }

  Future<void> updateDeviceName(String deviceId, String name) async {
    await _client
        .from('devices')
        .update({'device_name': name}).eq('id', deviceId);
  }

  Future<List<Heartbeat>> getHeartbeats(
    String deviceId, {
    int limit = 60,
  }) async {
    final data = await _client
        .from('heartbeats')
        .select()
        .eq('device_id', deviceId)
        .order('created_at', ascending: false)
        .limit(limit);
    return (data as List).map((e) => Heartbeat.fromJson(e)).toList();
  }

  Future<Heartbeat?> getLatestHeartbeat(String deviceId) async {
    final data = await _client
        .from('heartbeats')
        .select()
        .eq('device_id', deviceId)
        .order('created_at', ascending: false)
        .limit(1)
        .maybeSingle();
    if (data == null) return null;
    return Heartbeat.fromJson(data);
  }
}
