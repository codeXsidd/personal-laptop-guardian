import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/device.dart';
import '../models/heartbeat.dart';
import '../services/device_service.dart';
import 'auth_provider.dart';

final deviceServiceProvider = Provider<DeviceService>((ref) {
  return DeviceService(ref.watch(supabaseClientProvider));
});

final devicesProvider = FutureProvider<List<Device>>((ref) async {
  return ref.read(deviceServiceProvider).getDevices();
});

final deviceByIdProvider =
    FutureProvider.family<Device?, String>((ref, deviceId) async {
  return ref.read(deviceServiceProvider).getDevice(deviceId);
});

final latestHeartbeatProvider =
    FutureProvider.family<Heartbeat?, String>((ref, deviceId) async {
  return ref.read(deviceServiceProvider).getLatestHeartbeat(deviceId);
});

final heartbeatHistoryProvider =
    FutureProvider.family<List<Heartbeat>, String>((ref, deviceId) async {
  return ref.read(deviceServiceProvider).getHeartbeats(deviceId, limit: 60);
});
