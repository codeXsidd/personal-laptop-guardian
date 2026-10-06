import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:supabase_flutter/supabase_flutter.dart';

class RemoteSession {
  final String id;
  final String deviceId;
  final String status;
  final DateTime createdAt;
  final DateTime expiresAt;
  final String? relayToken;

  RemoteSession({
    required this.id,
    required this.deviceId,
    required this.status,
    required this.createdAt,
    required this.expiresAt,
    this.relayToken,
  });

  factory RemoteSession.fromJson(Map<String, dynamic> json) {
    return RemoteSession(
      id: json['id'] as String,
      deviceId: json['device_id'] as String,
      status: json['status'] as String,
      createdAt: DateTime.parse(json['created_at'] as String),
      expiresAt: DateTime.parse(json['expires_at'] as String),
      relayToken: json['relay_token'] as String?,
    );
  }

  bool get isActive => status == 'active';
  bool get isPending => status == 'pending';
  bool get isApproved => status == 'approved';
  bool get isTerminal =>
      const {'ended', 'expired', 'revoked', 'rejected'}.contains(status);
}

class RemoteSessionService {
  final _supabase = Supabase.instance.client;
  RealtimeChannel? _channel;
  Timer? _statusPollTimer;
  String? _currentSessionId;
  String? _relayToken;
  bool _intentionalDisconnect = false;
  bool _disposed = false;

  final _frameController = StreamController<Uint8List>.broadcast();
  final _cameraFrameController = StreamController<Uint8List>.broadcast();
  final _statusController = StreamController<String>.broadcast();
  final _sessionController = StreamController<RemoteSession?>.broadcast();
  final _cameraStateController = StreamController<bool>.broadcast();
  bool _cameraActive = false;

  Stream<Uint8List> get frameStream => _frameController.stream;
  Stream<Uint8List> get cameraFrameStream => _cameraFrameController.stream;
  Stream<String> get statusStream => _statusController.stream;
  Stream<RemoteSession?> get sessionStream => _sessionController.stream;
  Stream<bool> get cameraStateStream => _cameraStateController.stream;
  bool get isCameraActive => _cameraActive;

  bool get isConnected => _channel != null;
  String? get currentSessionId => _currentSessionId;

  Future<RemoteSession?> requestSession(String deviceId) async {
    try {
      _intentionalDisconnect = false;
      _statusController.add('Requesting remote access...');
      debugPrint('[RemoteSession] Requesting session for device: $deviceId');

      final response = await _supabase.functions.invoke(
        'manage-remote-session',
        body: {
          'action': 'request',
          'device_id': deviceId,
        },
      );

      debugPrint('[RemoteSession] Request response: ${response.status}');
      if (response.status != 201) {
        final error = response.data?['error'] ?? 'Failed to request session';
        debugPrint('[RemoteSession] Request failed: $error');
        _statusController.add('Error: $error');
        return null;
      }

      final session = RemoteSession.fromJson(
        response.data['session'] as Map<String, dynamic>,
      );

      debugPrint(
          '[RemoteSession] Session created: ${session.id} status=${session.status}');
      _currentSessionId = session.id;
      _sessionController.add(session);
      _statusController.add('Waiting for PC approval...');

      _startStatusPolling(session.id);
      return session;
    } catch (e) {
      debugPrint('[RemoteSession] Request error: $e');
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
      final response = await _supabase.functions.invoke(
        'manage-remote-session',
        body: {
          'action': 'status',
          'session_id': sessionId,
        },
      );

      if (response.status != 200 || response.data == null) {
        debugPrint('[RemoteSession] Poll: status ${response.status}');
        return;
      }

      final session = RemoteSession.fromJson(
        response.data['session'] as Map<String, dynamic>,
      );
      debugPrint(
          '[RemoteSession] Poll: session ${session.id} status=${session.status}');
      _sessionController.add(session);

      if (session.isApproved || session.isActive) {
        _statusPollTimer?.cancel();
        _relayToken = session.relayToken;
        debugPrint(
            '[RemoteSession] Session approved/active — connecting via Realtime');
        _statusController.add('Approved! Connecting...');
        await _connectRealtime(sessionId);
      } else if (session.isTerminal) {
        _statusPollTimer?.cancel();
        debugPrint('[RemoteSession] Session terminal: ${session.status}');
        _statusController.add('Session ${session.status}');
        _currentSessionId = null;
        _sessionController.add(null);
      }
    } catch (e) {
      debugPrint('[RemoteSession] Poll error: $e');
    }
  }

  Future<void> _connectRealtime(String sessionId) async {
    try {
      final channelName = 'remote-session-$sessionId';
      debugPrint('[RemoteSession] Joining Realtime channel: $channelName');

      _channel = _supabase.channel(
        channelName,
        opts: const RealtimeChannelConfig(
          ack: false,
          self: false,
          private: false,
        ),
      );

      _channel!
          .onBroadcast(
            event: 'frame',
            callback: (payload) {
              if (_disposed) return;
              try {
                // Supabase wraps broadcast: {event, payload: {data: ...}, type}
                final inner = payload['payload'];
                final data = (inner is Map ? inner['data'] : payload['data']) as String?;
                if (data == null) return;
                final bytes = base64Decode(data);
                _frameController.add(bytes);
              } catch (e) {
                debugPrint('[RemoteSession] Frame decode error: $e');
              }
            },
          )
          .onBroadcast(
            event: 'camera_frame',
            callback: (payload) {
              if (_disposed) return;
              try {
                final inner = payload['payload'];
                final data = (inner is Map ? inner['data'] : payload['data']) as String?;
                if (data == null) return;
                final bytes = base64Decode(data);
                _cameraFrameController.add(bytes);
              } catch (e) {
                debugPrint('[RemoteSession] Camera frame decode error: $e');
              }
            },
          )
          .onBroadcast(
            event: 'signal',
            callback: (payload) {
              if (_disposed) return;
              debugPrint('[RemoteSession] Signal received: $payload');
            },
          )
          .subscribe((status, error) {
            debugPrint(
                '[RemoteSession] Channel status: $status error: $error');
            if (_disposed) return;
            if (status == RealtimeSubscribeStatus.subscribed) {
              _statusController.add('Connected! Receiving screen...');
            } else if (status == RealtimeSubscribeStatus.channelError) {
              _statusController.add('Channel error: $error');
            } else if (status == RealtimeSubscribeStatus.closed) {
              if (!_intentionalDisconnect) {
                _statusController.add('Connection closed');
                disconnect();
              }
            }
          });
    } catch (e) {
      _statusController.add('Connection failed: ${e.toString()}');
      debugPrint('[RemoteSession] Realtime connect error: $e');
    }
  }

  void sendInput(Map<String, dynamic> input) {
    if (_channel == null) return;
    try {
      _channel!.sendBroadcastMessage(
        event: 'input',
        payload: {
          'type': 'input',
          ...input,
          if (_relayToken != null) 'relay_token': _relayToken,
        },
      );
    } catch (e) {
      debugPrint('[RemoteSession] Send input error: $e');
    }
  }

  void sendCameraControl(String action) {
    if (_channel == null) return;
    try {
      _channel!.sendBroadcastMessage(
        event: 'camera_control',
        payload: {
          'type': 'camera_control',
          'action': action,
          if (_relayToken != null) 'relay_token': _relayToken,
        },
      );
      _cameraActive = action == 'start';
      _cameraStateController.add(_cameraActive);
    } catch (e) {
      debugPrint('[RemoteSession] Send camera_control error: $e');
    }
  }

  void sendPcControl(String action) {
    if (_channel == null) return;
    try {
      _channel!.sendBroadcastMessage(
        event: 'pc_control',
        payload: {
          'type': 'pc_control',
          'action': action,
          if (_relayToken != null) 'relay_token': _relayToken,
        },
      );
    } catch (e) {
      debugPrint('[RemoteSession] Send pc_control error: $e');
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
    _cameraActive = false;
    _cameraStateController.add(false);
    if (_channel != null) {
      _channel!.unsubscribe();
      _channel = null;
    }
    _currentSessionId = null;
    _relayToken = null;
    _sessionController.add(null);
  }

  void dispose() {
    _disposed = true;
    disconnect();
    _frameController.close();
    _cameraFrameController.close();
    _statusController.close();
    _sessionController.close();
    _cameraStateController.close();
  }
}
