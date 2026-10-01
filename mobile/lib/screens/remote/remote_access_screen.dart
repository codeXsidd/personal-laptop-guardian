import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../../services/remote_session_service.dart';

class RemoteAccessScreen extends StatefulWidget {
  final String deviceId;

  const RemoteAccessScreen({super.key, required this.deviceId});

  @override
  State<RemoteAccessScreen> createState() => _RemoteAccessScreenState();
}

class _RemoteAccessScreenState extends State<RemoteAccessScreen> {
  final _service = RemoteSessionService();
  String _status = 'Ready to connect';
  RemoteSession? _session;
  Uint8List? _latestFrame;
  bool _isRequesting = false;

  StreamSubscription? _statusSub;
  StreamSubscription? _sessionSub;
  StreamSubscription? _frameSub;

  @override
  void initState() {
    super.initState();
    _statusSub = _service.statusStream.listen((status) {
      if (mounted) setState(() => _status = status);
    });
    _sessionSub = _service.sessionStream.listen((session) {
      if (mounted) setState(() => _session = session);
    });
    _frameSub = _service.frameStream.listen((frame) {
      if (mounted) setState(() => _latestFrame = frame);
    });
  }

  @override
  void dispose() {
    _statusSub?.cancel();
    _sessionSub?.cancel();
    _frameSub?.cancel();
    _service.dispose();
    super.dispose();
  }

  Future<void> _requestAccess() async {
    setState(() => _isRequesting = true);
    await _service.requestSession(widget.deviceId);
    if (mounted) setState(() => _isRequesting = false);
  }

  Future<void> _endSession() async {
    await _service.endSession();
    if (mounted) {
      setState(() {
        _latestFrame = null;
        _status = 'Session ended';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final isConnected = _service.isConnected;
    final hasFrame = _latestFrame != null;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Remote Access'),
        actions: [
          if (isConnected || _session != null)
            IconButton(
              icon: const Icon(Icons.stop_circle_outlined),
              tooltip: 'End Session',
              onPressed: _endSession,
            ),
        ],
      ),
      body: hasFrame ? _buildScreenView() : _buildControlPanel(theme),
    );
  }

  Widget _buildScreenView() {
    return GestureDetector(
      onTapUp: (details) {
        final box = context.findRenderObject() as RenderBox;
        final size = box.size;
        final pos = details.localPosition;
        _service.sendInput({
          'input_type': 'mouse_click',
          'x': (pos.dx / size.width * 960).round(),
          'y': (pos.dy / size.height * 540).round(),
          'button': 'left',
        });
      },
      child: Stack(
        children: [
          Positioned.fill(
            child: Image.memory(
              _latestFrame!,
              fit: BoxFit.contain,
              gaplessPlayback: true,
            ),
          ),
          Positioned(
            top: 8,
            left: 8,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
              decoration: BoxDecoration(
                color: Colors.black54,
                borderRadius: BorderRadius.circular(16),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Container(
                    width: 8,
                    height: 8,
                    decoration: const BoxDecoration(
                      color: Colors.greenAccent,
                      shape: BoxShape.circle,
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text(
                    _status,
                    style: const TextStyle(color: Colors.white, fontSize: 12),
                  ),
                ],
              ),
            ),
          ),
          Positioned(
            bottom: 16,
            right: 16,
            child: FloatingActionButton.small(
              onPressed: _endSession,
              backgroundColor: Colors.red,
              child: const Icon(Icons.stop, color: Colors.white),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildControlPanel(ThemeData theme) {
    final isPending = _session?.isPending ?? false;
    final isApproved = _session?.isApproved ?? false;

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.desktop_windows_outlined,
              size: 72,
              color: theme.colorScheme.primary.withValues(alpha: 0.5),
            ),
            const SizedBox(height: 24),
            Text(
              'Remote PC Access',
              style: theme.textTheme.headlineSmall?.copyWith(
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 8),
            Text(
              _status,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 32),
            if (isPending || isApproved) ...[
              const CircularProgressIndicator(),
              const SizedBox(height: 16),
              TextButton(
                onPressed: _endSession,
                child: const Text('Cancel'),
              ),
            ] else if (!_isRequesting) ...[
              FilledButton.icon(
                onPressed: _requestAccess,
                icon: const Icon(Icons.screen_share),
                label: const Text('Request Remote Access'),
              ),
              const SizedBox(height: 16),
              Text(
                'Your PC must be online and the Laptop Guardian\n'
                'Desktop app must be running to approve the request.',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
            ] else
              const CircularProgressIndicator(),
            const SizedBox(height: 32),
            Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Icon(Icons.security, size: 20, color: theme.colorScheme.primary),
                        const SizedBox(width: 8),
                        Text('Security', style: theme.textTheme.titleSmall),
                      ],
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'Sessions require explicit approval on the PC '
                      'and are encrypted. The session remains active '
                      'until you or the PC explicitly disconnects.',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
