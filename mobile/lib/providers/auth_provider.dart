import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:supabase_flutter/supabase_flutter.dart';

import '../config/supabase_config.dart';
import '../models/user_profile.dart';
import '../services/auth_service.dart';

final supabaseInitProvider = FutureProvider<void>((ref) async {
  if (!SupabaseConfig.isConfigured) {
    throw StateError(
      'SUPABASE_URL and SUPABASE_ANON_KEY must be set via --dart-define',
    );
  }
  await Supabase.initialize(
    url: SupabaseConfig.url,
    publishableKey: SupabaseConfig.anonKey,
  ).timeout(const Duration(seconds: 10));
});

final supabaseClientProvider = Provider<SupabaseClient>((ref) {
  return Supabase.instance.client;
});

final authServiceProvider = Provider<AuthService>((ref) {
  return AuthService(ref.watch(supabaseClientProvider));
});

final authStateProvider = StreamProvider<AuthState>((ref) {
  return ref.watch(authServiceProvider).authStateChanges;
});

final currentUserProvider = Provider<User?>((ref) {
  return ref.watch(authServiceProvider).currentUser;
});

final userProfileProvider = FutureProvider<UserProfile?>((ref) async {
  final user = ref.watch(currentUserProvider);
  if (user == null) return null;
  return ref.read(authServiceProvider).getProfile();
});

final loginFormActiveProvider = StateProvider<bool>((ref) => false);

enum DeepLinkResult { none, verified, error }

class DeepLinkState {
  final DeepLinkResult result;
  final String? errorMessage;
  const DeepLinkState({this.result = DeepLinkResult.none, this.errorMessage});
}

final deepLinkStateProvider =
    StateProvider<DeepLinkState>((ref) => const DeepLinkState());
