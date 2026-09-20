import 'package:supabase_flutter/supabase_flutter.dart';

import '../models/user_profile.dart';

class AuthService {
  static const String authCallbackUrl = 'com.laptopguardian.app://auth-callback';

  final SupabaseClient _client;

  AuthService(this._client);

  User? get currentUser => _client.auth.currentUser;
  bool get isAuthenticated => currentUser != null;
  Stream<AuthState> get authStateChanges => _client.auth.onAuthStateChange;

  Future<AuthResponse> signUp({
    required String email,
    required String password,
    required String fullName,
  }) async {
    return _client.auth.signUp(
      email: email,
      password: password,
      data: {'full_name': fullName},
      emailRedirectTo: authCallbackUrl,
    );
  }

  Future<void> signIn({
    required String email,
    required String password,
  }) async {
    await _client.auth.signInWithPassword(email: email, password: password);
  }

  Future<void> signOut() async {
    await _client.auth.signOut();
  }

  Future<UserProfile?> getProfile() async {
    final user = currentUser;
    if (user == null) return null;
    final data =
        await _client.from('profiles').select().eq('id', user.id).maybeSingle();
    if (data == null) return null;
    return UserProfile.fromJson(data);
  }

  Future<void> resetPassword(String email) async {
    await _client.auth.resetPasswordForEmail(
      email,
      redirectTo: authCallbackUrl,
    );
  }
}
