import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/pairing_service.dart';
import 'auth_provider.dart';

final pairingServiceProvider = Provider<PairingService>((ref) {
  return PairingService(ref.watch(supabaseClientProvider));
});
