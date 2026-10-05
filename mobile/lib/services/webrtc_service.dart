import 'dart:async';
import 'dart:convert';

import 'package:flutter_webrtc/flutter_webrtc.dart';

class WebRtcSessionService {
  RTCPeerConnection? _pc;
  RTCDataChannel? _dc;
  final RTCVideoRenderer remoteRenderer = RTCVideoRenderer();
  bool _initialized = false;

  final void Function(String json) _sendSignaling;

  final _statusController = StreamController<String>.broadcast();
  final _connectedController = StreamController<bool>.broadcast();

  Stream<String> get statusStream => _statusController.stream;
  Stream<bool> get connectedStream => _connectedController.stream;

  bool get isConnected =>
      _pc?.connectionState ==
      RTCPeerConnectionState.RTCPeerConnectionStateConnected;

  WebRtcSessionService({required this._sendSignaling});

  Future<void> initialize() async {
    if (_initialized) return;
    await remoteRenderer.initialize();
    _initialized = true;
  }

  Future<void> startOffer() async {
    _statusController.add('Creating WebRTC connection...');

    final config = <String, dynamic>{
      'iceServers': [
        {
          'urls': [
            'stun:stun.l.google.com:19302',
            'stun:stun1.l.google.com:19302',
          ]
        },
      ],
      'sdpSemantics': 'unified-plan',
    };

    _pc = await createPeerConnection(config);

    _pc!.onTrack = (RTCTrackEvent event) {
      if (event.track.kind == 'video' && event.streams.isNotEmpty) {
        remoteRenderer.srcObject = event.streams.first;
        _statusController.add('Receiving video');
      }
    };

    _pc!.onIceCandidate = (RTCIceCandidate candidate) {
      _sendSignaling(jsonEncode({
        'type': 'webrtc_ice',
        'candidate': candidate.candidate,
        'sdp_mid': candidate.sdpMid,
        'sdp_m_line_index': candidate.sdpMLineIndex,
      }));
    };

    _pc!.onConnectionState = (RTCPeerConnectionState state) {
      _statusController.add('WebRTC: ${state.name}');
      final connected =
          state == RTCPeerConnectionState.RTCPeerConnectionStateConnected;
      _connectedController.add(connected);
      if (state == RTCPeerConnectionState.RTCPeerConnectionStateFailed ||
          state == RTCPeerConnectionState.RTCPeerConnectionStateDisconnected) {
        _statusController.add('WebRTC disconnected');
      }
    };

    // Add transceiver to receive video
    await _pc!.addTransceiver(
      kind: RTCRtpMediaType.RTCRtpMediaTypeVideo,
      init: RTCRtpTransceiverInit(direction: TransceiverDirection.RecvOnly),
    );

    // Create data channel for input
    _dc = await _pc!.createDataChannel(
      'input',
      RTCDataChannelInit()..ordered = true,
    );

    // Create and send offer
    final offer = await _pc!.createOffer();
    await _pc!.setLocalDescription(offer);

    _sendSignaling(jsonEncode({
      'type': 'webrtc_offer',
      'sdp': offer.sdp,
    }));

    _statusController.add('WebRTC offer sent');
  }

  Future<void> handleSignalingMessage(Map<String, dynamic> msg) async {
    final type = msg['type'] as String?;

    switch (type) {
      case 'webrtc_answer':
        final sdp = msg['sdp'] as String?;
        if (sdp != null && _pc != null) {
          await _pc!.setRemoteDescription(
            RTCSessionDescription(sdp, 'answer'),
          );
          _statusController.add('WebRTC answer received');
        }
        break;

      case 'webrtc_ice':
        final candidate = msg['candidate'] as String?;
        final sdpMid = msg['sdp_mid'] as String?;
        final sdpMLineIndex = msg['sdp_m_line_index'] as int?;
        if (candidate != null && _pc != null) {
          await _pc!.addCandidate(
            RTCIceCandidate(candidate, sdpMid, sdpMLineIndex),
          );
        }
        break;
    }
  }

  void sendInput(Map<String, dynamic> input) {
    if (_dc?.state != RTCDataChannelState.RTCDataChannelOpen) return;
    _dc!.send(RTCDataChannelMessage(jsonEncode({
      'type': 'input',
      ...input,
    })));
  }

  void sendPcControl(String action) {
    if (_dc?.state != RTCDataChannelState.RTCDataChannelOpen) return;
    _dc!.send(RTCDataChannelMessage(jsonEncode({
      'type': 'pc_control',
      'action': action,
    })));
  }

  Future<void> dispose() async {
    _dc?.close();
    _dc = null;
    await _pc?.close();
    _pc = null;
    await remoteRenderer.dispose();
    _statusController.close();
    _connectedController.close();
  }
}
