import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:supabase_flutter/supabase_flutter.dart';

import '../config/supabase_config.dart';

class RemoteSession {
  final String id;
  final String deviceId;
  final String status;
  final DateTime createdAt;
  final DateTime expiresAt;

  RemoteSession({
    required this.id,
    required this.deviceId,
    required this.status,
    required this.createdAt,
    required this.expiresAt,
  });

  factory RemoteSession.fromJson(Map<String, dynamic> json) {
    return RemoteSession(
      id: json['id'] as String,
      deviceId: json['device_id'] as String,
      status: json['status'] as String,
      createdAt: DateTime.parse(json['created_at'] as String),
      expiresAt: DateTime.parse(json['expires_at'] as String),
    );
  }

  bool get isActive => status == 'active';
  bool get isPending => status == 'pending';
  bool get isApproved => status == 'approved';
  bool get isTerminal => const {'ended', 'expired', 'revoked', 'rejected'}.contains(status);
}

class RemoteSessionService {
  final _supabase = Supabase.instance.client;
  WebSocket? _ws;
  StreamSubscription? _wsSub;
  Timer? _statusPollTimer;
  String? _currentSessionId;
  int _reconnectAttempts = 0;
  static const _maxReconnectAttempts = 5;
  bool _intentionalDisconnect = false;

  final _frameController = StreamController<Uint8List>.broadcast();
  final _statusController = StreamController<String>.broadcast();
  final _sessionController = StreamController<RemoteSession?>.broadcast();

  Stream<Uint8List> get frameStream => _frameController.stream;
  Stream<String> get statusStream => _statusController.stream;
  Stream<RemoteSession?> get sessionStream => _sessionController.stream;

  bool get isConnected => _ws != null;
  String? get currentSessionId => _currentSessionId;

  Future<RemoteSession?> requestSession(String deviceId) async {
    try {
      _intentionalDisconnect = false;
      _reconnectAttempts = 0;
      _statusController.add('Requesting remote access...');

      final response = await _supabase.functions.invoke(
        'manage-remote-session',
        body: {
          'action': 'request',
          'device_id': deviceId,
        },
      );

      if (response.status != 201) {
        final error = response.data?['error'] ?? 'Failed to request session';
        _statusController.add('Error: $error');
        return null;
      }

      final session = RemoteSession.fromJson(
        response.data['session'] as Map<String, dynamic>,
      );

      _currentSessionId = session.id;
      _sessionController.add(session);
      _statusController.add('Waiting for PC approval...');

      _startStatusPolling(session.id);
      return session;
    } catch (e) {
      _statusController.add('Error: ${e.toString()}');
      return null;
    }
  }

  void _startStatusPolling(String sessionId) {
    _statusPollTimer?.cancel();
    _statusPollTimer = Timer.periodic(
      const Duration(seconds: 2),
      (_) => _checkSessionStatus(sessionId),
    );
  }

  Future<void> _checkSessionStatus(String sessionId) async {
    try {
      final result = await _supabase
          .from('remote_sessions')
          .select()
          .eq('id', sessionId)
          .single();

      final session = RemoteSession.fromJson(result);
      _sessionController.add(session);

      if (session.isApproved || session.isActive) {
        _statusPollTimer?.cancel();
        _statusController.add('Approved! Connecting...');
        await _connectRelay(sessionId);
      } else if (session.isTerminal) {
        _statusPollTimer?.cancel();
        _statusController.add('Session ${session.status}');
        _currentSessionId = null;
        _sessionController.add(null);
      }
    } catch (e) {
      debugPrint('[RemoteSession] Poll error: $e');
    }
  }

  Future<void> _connectRelay(String sessionId) async {
    try {
      final supabaseUrl = SupabaseConfig.url;
      final wsUrl = supabaseUrl
          .replaceFirst('https://', 'wss://')
          .replaceFirst('http://', 'ws://');

      final token = _supabase.auth.currentSession?.accessToken;
      if (token == null) {
        _statusController.add('Error: Not authenticated');
        return;
      }

      final uri = Uri.parse(
        '$wsUrl/functions/v1/remote-relay'
        '?session_id=$sessionId'
        '&role=mobile'
        '&token=${Uri.encodeComponent(token)}',
      );

      _ws = await WebSocket.connect(uri.toString());
      _reconnectAttempts = 0;
      _statusController.add('Connected! Receiving screen...');

      _wsSub = _ws!.listen(
        (data) {
          if (data is List<int>) {
            _frameController.add(Uint8List.fromList(data));
          } else if (data is String) {
            _handleTextMessage(data);
          }
        },
        onError: (error) {
          debugPrint('[RemoteSession] WS error: $error');
          _attemptReconnect(sessionId);
        },
        onDone: () {
          if (!_intentionalDisconnect) {
            _attemptReconnect(sessionId);
          }
        },
      );
    } catch (e) {
      _statusController.add('Connection failed: ${e.toString()}');
      debugPrint('[RemoteSession] Connect error: $e');
    }
  }

  Future<void> _attemptReconnect(String sessionId) async {
    if (_intentionalDisconnect) return;
    if (_reconnectAttempts >= _maxReconnectAttempts) {
      _statusController.add('Reconnection failed. Please try again.');
      disconnect();
      return;
    }

    _reconnectAttempts++;
    _wsSub?.cancel();
    _ws?.close();
    _ws = null;

    final delay = Duration(seconds: _reconnectAttempts * 2);
    _statusController.add('Reconnecting (attempt $_reconnectAttempts/$_maxReconnectAttempts)...');
    await Future.delayed(delay);

    if (_intentionalDisconnect || _currentSessionId == null) return;
    await _connectRelay(sessionId);
  }

  void _handleTextMessage(String data) {
    try {
      final msg = jsonDecode(data) as Map<String, dynamic>;
      final type = msg['type'] as String?;

      if (type == 'peer_left') {
        _statusController.add('PC disconnected');
        disconnect();
      } else if (type == 'peer_joined') {
        _statusController.add('PC connected! Receiving screen...');
      }
    } catch (e) {
      debugPrint('[RemoteSession] Text message parse error: $e');
    }
  }

  void sendInput(Map<String, dynamic> input) {
    if (_ws == null) return;
    try {
      _ws!.add(jsonEncode({
        'type': 'input',
        ...input,
      }));
    } catch (e) {
      debugPrint('[RemoteSession] Send input error: $e');
    }
  }

  Future<void> endSession() async {
    _intentionalDisconnect = true;
    if (_currentSessionId == null) return;

    try {
      await _supabase.functions.invoke(
        'manage-remote-session',
        body: {
          'action': 'end',
          'session_id': _currentSessionId,
        },
      );
    } catch (e) {
      debugPrint('[RemoteSession] End error: $e');
    }

    disconnect();
  }

  Future<void> revokeSession() async {
    _intentionalDisconnect = true;
    if (_currentSessionId == null) return;

    try {
      await _supabase.functions.invoke(
        'manage-remote-session',
        body: {
          'action': 'revoke',
          'session_id': _currentSessionId,
        },
      );
    } catch (e) {
      debugPrint('[RemoteSession] Revoke error: $e');
    }

    disconnect();
  }

  void disconnect() {
    _intentionalDisconnect = true;
    _statusPollTimer?.cancel();
    _wsSub?.cancel();
    _ws?.close();
    _ws = null;
    _currentSessionId = null;
    _reconnectAttempts = 0;
    _sessionController.add(null);
  }

  void dispose() {
    disconnect();
    _frameController.close();
    _statusController.close();
    _sessionController.close();
  }
}
