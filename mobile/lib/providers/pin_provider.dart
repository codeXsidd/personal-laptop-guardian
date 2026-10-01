import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/pin_service.dart';

final pinServiceProvider = Provider<PinService>((ref) => PinService());

final isPinEnabledProvider = FutureProvider<bool>((ref) {
  return ref.watch(pinServiceProvider).isPinEnabled();
});

final isPinSetProvider = FutureProvider<bool>((ref) {
  return ref.watch(pinServiceProvider).isPinSet();
});

final appLockedProvider = StateProvider<bool>((ref) => false);
