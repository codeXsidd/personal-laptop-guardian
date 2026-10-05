import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../services/remote_session_service.dart';

class RemoteAccessScreen extends StatefulWidget {
  final String deviceId;
  final bool autoConnect;

  const RemoteAccessScreen({
    super.key,
    required this.deviceId,
    this.autoConnect = false,
  });

  @override
  State<RemoteAccessScreen> createState() => _RemoteAccessScreenState();
}

class _RemoteAccessScreenState extends State<RemoteAccessScreen> {
  final _service = RemoteSessionService();
  String _status = 'Ready to connect';
  RemoteSession? _session;
  Uint8List? _latestFrame;
  bool _isRequesting = false;
  bool _showKeyboard = false;
  bool _showControls = true;
  bool _cameraActive = false;
  Uint8List? _latestCameraFrame;
  final _keyboardFocus = FocusNode();

  final _imageKey = GlobalKey();

  StreamSubscription? _statusSub;
  StreamSubscription? _sessionSub;
  StreamSubscription? _frameSub;
  StreamSubscription? _cameraFrameSub;
  StreamSubscription? _cameraStateSub;

  @override
  void initState() {
    super.initState();
    _statusSub = _service.statusStream.listen((status) {
      if (mounted) setState(() => _status = status);
    });
    _sessionSub = _service.sessionStream.listen((session) {
      if (!mounted) return;
      setState(() {
        _session = session;
        if (session == null || session.isTerminal) {
          _latestFrame = null;
          _showKeyboard = false;
        }
      });
    });
    _frameSub = _service.frameStream.listen((frame) {
      if (mounted) setState(() => _latestFrame = frame);
    });
    _cameraFrameSub = _service.cameraFrameStream.listen((frame) {
      if (mounted) setState(() => _latestCameraFrame = frame);
    });
    _cameraStateSub = _service.cameraStateStream.listen((active) {
      if (mounted) {
        setState(() {
          _cameraActive = active;
          if (!active) _latestCameraFrame = null;
        });
      }
    });
    if (widget.autoConnect) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _requestAccess());
    }
  }

  @override
  void dispose() {
    _statusSub?.cancel();
    _sessionSub?.cancel();
    _frameSub?.cancel();
    _cameraFrameSub?.cancel();
    _cameraStateSub?.cancel();
    _keyboardFocus.dispose();
    _service.dispose();
    super.dispose();
  }

  Future<void> _requestAccess() async {
    setState(() => _isRequesting = true);
    await _service.requestSession(widget.deviceId);
    if (mounted) setState(() => _isRequesting = false);
  }

  Future<void> _endSession() async {
    if (_cameraActive) {
      _service.sendCameraControl('stop');
    }
    await _service.endSession();
    if (mounted) {
      setState(() {
        _latestFrame = null;
        _latestCameraFrame = null;
        _cameraActive = false;
        _status = 'Session ended';
        _showKeyboard = false;
      });
    }
  }

  (double nx, double ny)? _normalizePosition(Offset localPosition) {
    final renderBox =
        _imageKey.currentContext?.findRenderObject() as RenderBox?;
    if (renderBox == null) return null;
    final size = renderBox.size;
    final nx = (localPosition.dx / size.width).clamp(0.0, 1.0);
    final ny = (localPosition.dy / size.height).clamp(0.0, 1.0);
    return (nx, ny);
  }

  void _onTapUp(TapUpDetails details) {
    if (!_isSessionActive) return;
    final pos = _normalizePosition(details.localPosition);
    if (pos == null) return;
    _service.sendInput({
      'input_type': 'click',
      'nx': pos.$1,
      'ny': pos.$2,
    });
  }

  void _onLongPressEnd(LongPressEndDetails details) {
    if (!_isSessionActive) return;
    final pos = _normalizePosition(details.localPosition);
    if (pos == null) return;
    _service.sendInput({
      'input_type': 'right_click',
      'nx': pos.$1,
      'ny': pos.$2,
    });
  }

  void _onPanUpdate(DragUpdateDetails details) {
    if (!_isSessionActive) return;
    final pos = _normalizePosition(details.localPosition);
    if (pos == null) return;
    _service.sendInput({
      'input_type': 'mouse_move',
      'nx': pos.$1,
      'ny': pos.$2,
    });
  }

  void _onDoubleTap() {
    if (!_isSessionActive) return;
    _service.sendInput({'input_type': 'double_click'});
  }

  void _handleKeyEvent(KeyEvent event) {
    if (!_isSessionActive) return;
    final key = _mapFlutterKey(event.logicalKey);
    if (key == null) return;

    if (event is KeyDownEvent) {
      _service.sendInput({'input_type': 'key_down', 'key': key});
    } else if (event is KeyUpEvent) {
      _service.sendInput({'input_type': 'key_up', 'key': key});
    }
  }

  String? _mapFlutterKey(LogicalKeyboardKey key) {
    if (key == LogicalKeyboardKey.enter) return 'enter';
    if (key == LogicalKeyboardKey.backspace) return 'backspace';
    if (key == LogicalKeyboardKey.tab) return 'tab';
    if (key == LogicalKeyboardKey.escape) return 'escape';
    if (key == LogicalKeyboardKey.space) return ' ';
    if (key == LogicalKeyboardKey.delete) return 'delete';
    if (key == LogicalKeyboardKey.arrowUp) return 'up';
    if (key == LogicalKeyboardKey.arrowDown) return 'down';
    if (key == LogicalKeyboardKey.arrowLeft) return 'left';
    if (key == LogicalKeyboardKey.arrowRight) return 'right';
    if (key == LogicalKeyboardKey.home) return 'home';
    if (key == LogicalKeyboardKey.end) return 'end';
    if (key == LogicalKeyboardKey.pageUp) return 'page_up';
    if (key == LogicalKeyboardKey.pageDown) return 'page_down';
    if (key == LogicalKeyboardKey.shiftLeft) return 'shift_left';
    if (key == LogicalKeyboardKey.shiftRight) return 'shift_right';
    if (key == LogicalKeyboardKey.controlLeft) return 'control_left';
    if (key == LogicalKeyboardKey.controlRight) return 'control_right';
    if (key == LogicalKeyboardKey.altLeft) return 'alt_left';
    if (key == LogicalKeyboardKey.altRight) return 'alt_right';
    if (key == LogicalKeyboardKey.capsLock) return 'caps_lock';

    final label = key.keyLabel;
    if (label.length == 1) return label;
    if (label.startsWith('F') && label.length <= 3) {
      final n = int.tryParse(label.substring(1));
      if (n != null && n >= 1 && n <= 12) return label.toLowerCase();
    }
    return null;
  }

  void _sendPcControl(String action) {
    final labels = {
      'lock': 'Lock PC',
      'sleep': 'Sleep',
      'restart': 'Restart PC',
      'shutdown': 'Shut Down PC',
    };

    if (action == 'restart' || action == 'shutdown') {
      showDialog(
        context: context,
        builder: (ctx) => AlertDialog(
          title: Text(labels[action] ?? action),
          content: Text(
            action == 'restart'
                ? 'This will restart the PC. Unsaved work will be lost.'
                : 'This will shut down the PC. Unsaved work will be lost.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(ctx).pop(),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () {
                Navigator.of(ctx).pop();
                _service.sendPcControl(action);
                _endSession();
              },
              style: FilledButton.styleFrom(
                backgroundColor: Colors.red,
              ),
              child: Text(labels[action]!),
            ),
          ],
        ),
      );
    } else {
      _service.sendPcControl(action);
      if (action == 'lock') {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('PC locked'),
            duration: Duration(seconds: 2),
          ),
        );
      }
    }
  }

  bool get _isSessionActive => _service.isConnected && _latestFrame != null;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final isConnected = _service.isConnected;

    final showVideo = _isSessionActive;

    return Scaffold(
      appBar: showVideo
          ? null
          : AppBar(
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
      body: showVideo ? _buildScreenView(theme) : _buildControlPanel(theme),
    );
  }

  Widget _buildScreenView(ThemeData theme) {
    return KeyboardListener(
      focusNode: _keyboardFocus,
      autofocus: true,
      onKeyEvent: _handleKeyEvent,
      child: SafeArea(
        child: Column(
          children: [
            // Status bar
            _buildStatusBar(),
            // Screen image with touch handling + camera PiP
            Expanded(
              child: Stack(
                children: [
                  GestureDetector(
                    onTapUp: _onTapUp,
                    onDoubleTap: _onDoubleTap,
                    onLongPressEnd: _onLongPressEnd,
                    onPanUpdate: _onPanUpdate,
                    child: Container(
                      key: _imageKey,
                      color: Colors.black,
                      child: _latestFrame != null
                          ? Image.memory(
                              _latestFrame!,
                              fit: BoxFit.contain,
                              gaplessPlayback: true,
                              width: double.infinity,
                              height: double.infinity,
                            )
                          : const Center(
                              child: CircularProgressIndicator()),
                    ),
                  ),
                  if (_cameraActive && _latestCameraFrame != null)
                    Positioned(
                      right: 8,
                      top: 8,
                      child: Container(
                        width: 120,
                        height: 90,
                        decoration: BoxDecoration(
                          border: Border.all(color: Colors.white54, width: 1),
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: ClipRRect(
                          borderRadius: BorderRadius.circular(7),
                          child: Image.memory(
                            _latestCameraFrame!,
                            fit: BoxFit.cover,
                            gaplessPlayback: true,
                          ),
                        ),
                      ),
                    ),
                ],
              ),
            ),
            // Control toolbar
            if (_showControls) _buildToolbar(theme),
          ],
        ),
      ),
    );
  }

  Widget _buildStatusBar() {
    return GestureDetector(
      onTap: () => setState(() => _showControls = !_showControls),
      child: Container(
        color: Colors.black87,
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        child: Row(
          children: [
            Container(
              width: 8,
              height: 8,
              decoration: BoxDecoration(
                color: _service.isConnected
                    ? Colors.greenAccent
                    : Colors.orangeAccent,
                shape: BoxShape.circle,
              ),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                _status,
                style: const TextStyle(color: Colors.white70, fontSize: 12),
                overflow: TextOverflow.ellipsis,
              ),
            ),
            IconButton(
              icon: const Icon(Icons.stop, color: Colors.redAccent, size: 20),
              padding: EdgeInsets.zero,
              constraints: const BoxConstraints(),
              tooltip: 'End Session',
              onPressed: _endSession,
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildToolbar(ThemeData theme) {
    return Container(
      color: Colors.black87,
      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 4),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceEvenly,
        children: [
          _toolbarButton(
            icon: Icons.keyboard,
            label: 'KB',
            active: _showKeyboard,
            onTap: () {
              setState(() => _showKeyboard = !_showKeyboard);
              if (_showKeyboard) {
                _keyboardFocus.requestFocus();
                SystemChannels.textInput
                    .invokeMethod('TextInput.show');
              } else {
                SystemChannels.textInput
                    .invokeMethod('TextInput.hide');
              }
            },
          ),
          _toolbarButton(
            icon: Icons.arrow_upward,
            label: 'Scr↑',
            onTap: () => _service.sendInput({
              'input_type': 'scroll',
              'delta_y': 1,
            }),
          ),
          _toolbarButton(
            icon: Icons.arrow_downward,
            label: 'Scr↓',
            onTap: () => _service.sendInput({
              'input_type': 'scroll',
              'delta_y': -1,
            }),
          ),
          _toolbarButton(
            icon: _cameraActive ? Icons.videocam : Icons.videocam_off,
            label: _cameraActive ? 'Cam On' : 'Cam',
            active: _cameraActive,
            onTap: () {
              if (_cameraActive) {
                _service.sendCameraControl('stop');
              } else {
                _service.sendCameraControl('start');
              }
            },
          ),
          _toolbarButton(
            icon: Icons.lock_outline,
            label: 'Lock',
            onTap: () => _sendPcControl('lock'),
          ),
          _toolbarButton(
            icon: Icons.nightlight_round,
            label: 'Sleep',
            onTap: () => _sendPcControl('sleep'),
          ),
          _toolbarButton(
            icon: Icons.restart_alt,
            label: 'Restart',
            onTap: () => _sendPcControl('restart'),
          ),
          _toolbarButton(
            icon: Icons.power_settings_new,
            label: 'Off',
            onTap: () => _sendPcControl('shutdown'),
            color: Colors.redAccent,
          ),
        ],
      ),
    );
  }

  Widget _toolbarButton({
    required IconData icon,
    required String label,
    required VoidCallback onTap,
    bool active = false,
    Color? color,
  }) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(8),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              icon,
              color: active
                  ? Colors.blueAccent
                  : color ?? Colors.white70,
              size: 22,
            ),
            const SizedBox(height: 2),
            Text(
              label,
              style: TextStyle(
                color: active
                    ? Colors.blueAccent
                    : color ?? Colors.white60,
                fontSize: 10,
              ),
            ),
          ],
        ),
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
                        Icon(Icons.security,
                            size: 20, color: theme.colorScheme.primary),
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
