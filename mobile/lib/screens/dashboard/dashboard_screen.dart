import 'dart:async';

import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:timeago/timeago.dart' as timeago;

import '../../models/device.dart';
import '../../models/event.dart';
import '../../models/event_type.dart';
import '../../models/heartbeat.dart';
import '../../providers/device_provider.dart';
import '../../providers/event_provider.dart';
import '../../services/device_service.dart';
import '../../providers/auth_provider.dart';
import '../events/event_list_screen.dart' show eventSubtitle;

class DashboardScreen extends ConsumerStatefulWidget {
  final String deviceId;

  const DashboardScreen({super.key, required this.deviceId});

  @override
  ConsumerState<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends ConsumerState<DashboardScreen> {
  Timer? _refreshTimer;

  @override
  void initState() {
    super.initState();
    _refreshTimer = Timer.periodic(
      const Duration(seconds: 30),
      (_) => _invalidateAll(),
    );
  }

  void _invalidateAll() {
    ref.invalidate(deviceByIdProvider(widget.deviceId));
    ref.invalidate(latestHeartbeatProvider(widget.deviceId));
    ref.invalidate(recentEventsProvider(widget.deviceId));
    ref.invalidate(eventTypeCountsProvider(widget.deviceId));
  }

  @override
  void dispose() {
    _refreshTimer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final deviceId = widget.deviceId;
    final deviceAsync = ref.watch(deviceByIdProvider(deviceId));
    final heartbeatAsync = ref.watch(latestHeartbeatProvider(deviceId));
    final recentAsync = ref.watch(recentEventsProvider(deviceId));
    final countsAsync = ref.watch(eventTypeCountsProvider(deviceId));

    return Scaffold(
      appBar: AppBar(
        title: const Text('Dashboard'),
        leading: IconButton(
          icon: const Icon(Icons.arrow_back),
          onPressed: () => context.pop(),
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.notifications_outlined),
            tooltip: 'Notification history',
            onPressed: () => context.push('/notifications/${widget.deviceId}'),
          ),
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: _invalidateAll,
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async => _invalidateAll(),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            deviceAsync.when(
              loading: () => const SizedBox.shrink(),
              error: (_, _) => const SizedBox.shrink(),
              data: (device) =>
                  device != null ? _DeviceHeader(device: device) : const SizedBox.shrink(),
            ),
            const SizedBox(height: 16),
            heartbeatAsync.when(
              loading: () => const _MetricsLoading(),
              error: (_, _) => const SizedBox.shrink(),
              data: (hb) => hb != null
                  ? _MetricsCards(heartbeat: hb)
                  : const _NoMetrics(),
            ),
            const SizedBox(height: 16),
            _QuickActions(deviceId: deviceId),
            const SizedBox(height: 16),
            deviceAsync.when(
              loading: () => const SizedBox.shrink(),
              error: (_, _) => const SizedBox.shrink(),
              data: (device) => device != null
                  ? _PcControls(device: device)
                  : const SizedBox.shrink(),
            ),
            const SizedBox(height: 16),
            deviceAsync.when(
              loading: () => const SizedBox.shrink(),
              error: (_, _) => const SizedBox.shrink(),
              data: (device) => device != null
                  ? _ConnectionStatus(device: device)
                  : const SizedBox.shrink(),
            ),
            const SizedBox(height: 16),
            _DeviceManagement(deviceId: deviceId),
            const SizedBox(height: 16),
            countsAsync.when(
              loading: () => const SizedBox.shrink(),
              error: (_, _) => const SizedBox.shrink(),
              data: (counts) =>
                  counts.isNotEmpty ? _EventBreakdown(counts: counts) : const SizedBox.shrink(),
            ),
            const SizedBox(height: 16),
            recentAsync.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (_, _) => const Text('Failed to load events'),
              data: (events) => _RecentEvents(events: events, deviceId: deviceId),
            ),
          ],
        ),
      ),
    );
  }
}

class _DeviceHeader extends StatelessWidget {
  final Device device;

  const _DeviceHeader({required this.device});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(Icons.laptop_windows, size: 32, color: theme.colorScheme.primary),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(device.displayName,
                          style: theme.textTheme.titleMedium
                              ?.copyWith(fontWeight: FontWeight.w600)),
                      if (device.machineName != device.displayName)
                        Text(device.machineName,
                            style: theme.textTheme.bodySmall
                                ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),
            _StatusRow(
              label: 'Laptop',
              value: device.powerStateDisplay,
              color: device.isLaptopOn
                  ? Colors.green
                  : device.isLaptopOff
                      ? Colors.red
                      : Colors.orange,
              icon: device.isLaptopOn
                  ? Icons.power_settings_new
                  : Icons.power_off,
            ),
            const SizedBox(height: 8),
            _StatusRow(
              label: 'Connection',
              value: device.isOnline
                  ? 'Online'
                  : device.isPairing
                      ? 'Pairing'
                      : 'Offline',
              color: device.isOnline
                  ? Colors.green
                  : device.isPairing
                      ? Colors.orange
                      : Colors.grey,
              icon: device.isOnline ? Icons.wifi : Icons.wifi_off,
            ),
            const SizedBox(height: 8),
            _StatusRow(
              label: 'Windows User',
              value: device.userSessionDisplay,
              color: device.userSessionState == 'logged_in'
                  ? Colors.green
                  : device.userSessionState == 'locked'
                      ? Colors.orange
                      : Colors.grey,
              icon: device.userSessionState == 'logged_in'
                  ? Icons.person
                  : device.userSessionState == 'locked'
                      ? Icons.lock
                      : Icons.person_off,
            ),
            if (device.stateExplanation.isNotEmpty) ...[
              const SizedBox(height: 10),
              Container(
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  color: theme.colorScheme.surfaceContainerHighest,
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Row(
                  children: [
                    Icon(Icons.info_outline, size: 16, color: theme.colorScheme.outline),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        device.stateExplanation,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: theme.colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],
            const SizedBox(height: 12),
            const Divider(height: 1),
            const SizedBox(height: 8),
            if (device.lastStartupAt != null)
              _DetailRow(label: 'Last Power On', time: device.lastStartupAt!),
            if (device.lastShutdownAt != null) ...[
              const SizedBox(height: 4),
              _DetailRow(label: 'Last Power Off', time: device.lastShutdownAt!),
            ],
            if (device.lastSleepAt != null) ...[
              const SizedBox(height: 4),
              _DetailRow(label: 'Last Sleep', time: device.lastSleepAt!),
            ],
            if (device.lastWakeAt != null) ...[
              const SizedBox(height: 4),
              _DetailRow(label: 'Last Wake', time: device.lastWakeAt!),
            ],
            if (device.lastLockAt != null) ...[
              const SizedBox(height: 4),
              _DetailRow(label: 'Last Lock', time: device.lastLockAt!),
            ],
            if (device.lastUnlockAt != null) ...[
              const SizedBox(height: 4),
              _DetailRow(label: 'Last Unlock', time: device.lastUnlockAt!),
            ],
            if (device.lastLoginAt != null) ...[
              const SizedBox(height: 4),
              _DetailRow(label: 'Last Login', time: device.lastLoginAt!),
            ],
            if (device.lastLogoutAt != null) ...[
              const SizedBox(height: 4),
              _DetailRow(label: 'Last Logout', time: device.lastLogoutAt!),
            ],
            if (device.lastSeenAt != null) ...[
              const SizedBox(height: 4),
              _DetailRow(
                label: device.isLaptopOff ? 'Last seen' : 'Last heartbeat',
                time: device.lastSeenAt!,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _StatusRow extends StatelessWidget {
  final String label;
  final String value;
  final Color color;
  final IconData icon;

  const _StatusRow({
    required this.label,
    required this.value,
    required this.color,
    required this.icon,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      children: [
        Container(
          width: 10,
          height: 10,
          decoration: BoxDecoration(color: color, shape: BoxShape.circle),
        ),
        const SizedBox(width: 8),
        SizedBox(
          width: 100,
          child: Text(label,
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
        ),
        Icon(icon, size: 16, color: color),
        const SizedBox(width: 4),
        Text(value,
            style: theme.textTheme.bodyMedium
                ?.copyWith(fontWeight: FontWeight.w600, color: color)),
      ],
    );
  }
}

class _DetailRow extends StatelessWidget {
  final String label;
  final DateTime time;

  const _DetailRow({required this.label, required this.time});

  static final _dateFmt = DateFormat('d MMM yyyy');
  static final _timeFmt = DateFormat('h:mm:ss a');

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final local = time.toLocal();
    final tz = local.timeZoneName;
    final formatted = '${_dateFmt.format(local)} ${_timeFmt.format(local)} $tz';
    return Row(
      children: [
        SizedBox(
          width: 118,
          child: Text(label,
              style: theme.textTheme.labelSmall
                  ?.copyWith(color: theme.colorScheme.outline)),
        ),
        Expanded(
          child: Text(formatted,
              style: theme.textTheme.labelSmall
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
        ),
        Text(timeago.format(local),
            style: theme.textTheme.labelSmall
                ?.copyWith(color: theme.colorScheme.outline)),
      ],
    );
  }
}

class _MetricsCards extends StatelessWidget {
  final Heartbeat heartbeat;

  const _MetricsCards({required this.heartbeat});

  @override
  Widget build(BuildContext context) {
    return GridView.count(
      crossAxisCount: 2,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      mainAxisSpacing: 8,
      crossAxisSpacing: 8,
      childAspectRatio: 1.6,
      children: [
        _MetricTile(
          icon: Icons.memory,
          label: 'CPU',
          value: heartbeat.cpuPercent != null
              ? '${heartbeat.cpuPercent!.toStringAsFixed(1)}%'
              : 'N/A',
          progress: heartbeat.cpuPercent != null
              ? heartbeat.cpuPercent! / 100
              : null,
          color: _usageColor(heartbeat.cpuPercent),
        ),
        _MetricTile(
          icon: Icons.storage,
          label: 'Memory',
          value: heartbeat.memoryPercent != null
              ? '${heartbeat.memoryPercent!.toStringAsFixed(1)}%'
              : 'N/A',
          progress: heartbeat.memoryPercent != null
              ? heartbeat.memoryPercent! / 100
              : null,
          color: _usageColor(heartbeat.memoryPercent),
        ),
        _MetricTile(
          icon: Icons.disc_full_outlined,
          label: 'Disk',
          value: heartbeat.diskPercent != null
              ? '${heartbeat.diskPercent!.toStringAsFixed(1)}%'
              : 'N/A',
          progress: heartbeat.diskPercent != null
              ? heartbeat.diskPercent! / 100
              : null,
          color: _usageColor(heartbeat.diskPercent),
        ),
        _MetricTile(
          icon: heartbeat.isCharging == true
              ? Icons.battery_charging_full
              : Icons.battery_std,
          label: 'Battery',
          value: heartbeat.batteryPercent != null
              ? '${heartbeat.batteryPercent!.toStringAsFixed(0)}%'
              : 'N/A',
          progress: heartbeat.batteryPercent != null
              ? heartbeat.batteryPercent! / 100
              : null,
          color: _batteryColor(heartbeat.batteryPercent),
        ),
      ],
    );
  }

  Color _usageColor(double? percent) {
    if (percent == null) return Colors.grey;
    if (percent >= 90) return Colors.red;
    if (percent >= 70) return Colors.orange;
    return Colors.green;
  }

  Color _batteryColor(double? percent) {
    if (percent == null) return Colors.grey;
    if (percent <= 15) return Colors.red;
    if (percent <= 30) return Colors.orange;
    return Colors.green;
  }
}

class _MetricTile extends StatelessWidget {
  final IconData icon;
  final String label;
  final String value;
  final double? progress;
  final Color color;

  const _MetricTile({
    required this.icon,
    required this.label,
    required this.value,
    this.progress,
    required this.color,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Row(
              children: [
                Icon(icon, size: 18, color: color),
                const SizedBox(width: 6),
                Text(label, style: theme.textTheme.bodySmall),
              ],
            ),
            Text(value,
                style: theme.textTheme.titleLarge
                    ?.copyWith(fontWeight: FontWeight.bold, color: color)),
            if (progress != null)
              ClipRRect(
                borderRadius: BorderRadius.circular(4),
                child: LinearProgressIndicator(
                  value: progress!.clamp(0, 1),
                  backgroundColor: color.withValues(alpha: 0.15),
                  valueColor: AlwaysStoppedAnimation(color),
                  minHeight: 4,
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _MetricsLoading extends StatelessWidget {
  const _MetricsLoading();

  @override
  Widget build(BuildContext context) {
    return const SizedBox(height: 100, child: Center(child: CircularProgressIndicator()));
  }
}

class _NoMetrics extends StatelessWidget {
  const _NoMetrics();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          children: [
            Icon(Icons.hourglass_empty,
                size: 32, color: theme.colorScheme.outline),
            const SizedBox(height: 8),
            Text('No metrics received yet.',
                style: theme.textTheme.bodyMedium),
            const SizedBox(height: 4),
            Text(
              'Metrics will appear once the laptop agent is running and paired.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.outline),
            ),
          ],
        ),
      ),
    );
  }
}

class _QuickActions extends StatelessWidget {
  final String deviceId;

  const _QuickActions({required this.deviceId});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final actions = [
      _QA('Timeline', Icons.timeline, '/events/$deviceId'),
      _QA('Sessions', Icons.login, '/events/$deviceId/sessions'),
      _QA('Apps', Icons.apps, '/events/$deviceId/processes'),
      _QA('USB', Icons.usb, '/events/$deviceId/usb'),
      _QA('Network', Icons.wifi, '/events/$deviceId/network'),
      _QA('Files', Icons.folder_open, '/events/$deviceId/files'),
      _QA('Event Log', Icons.article_outlined, '/events/$deviceId/eventlog'),
      _QA('Remote', Icons.screen_share, '/remote/$deviceId'),
      _QA('Camera', Icons.videocam, '/remote/$deviceId'),
      _QA('Reports', Icons.bar_chart, '/reports/$deviceId'),
    ];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Quick Actions',
            style: theme.textTheme.titleSmall
                ?.copyWith(fontWeight: FontWeight.w600)),
        const SizedBox(height: 8),
        GridView.count(
          crossAxisCount: 4,
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          mainAxisSpacing: 8,
          crossAxisSpacing: 8,
          children: actions
              .map((a) => _QuickActionTile(
                    label: a.label,
                    icon: a.icon,
                    onTap: () => context.push(a.route),
                  ))
              .toList(),
        ),
      ],
    );
  }
}

class _QA {
  final String label;
  final IconData icon;
  final String route;
  _QA(this.label, this.icon, this.route);
}

class _QuickActionTile extends StatelessWidget {
  final String label;
  final IconData icon;
  final VoidCallback onTap;

  const _QuickActionTile({
    required this.label,
    required this.icon,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Container(
        decoration: BoxDecoration(
          color: theme.colorScheme.surfaceContainerHighest,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(icon, size: 24, color: theme.colorScheme.primary),
            const SizedBox(height: 4),
            Text(label,
                style: theme.textTheme.labelSmall,
                textAlign: TextAlign.center,
                overflow: TextOverflow.ellipsis),
          ],
        ),
      ),
    );
  }
}

class _PcControls extends ConsumerStatefulWidget {
  final Device device;

  const _PcControls({required this.device});

  @override
  ConsumerState<_PcControls> createState() => _PcControlsState();
}

class _PcControlsState extends ConsumerState<_PcControls> {
  bool _busy = false;

  bool get _canControl => widget.device.isOnline && !_busy;

  Future<void> _send(String action, {bool confirm = false}) async {
    if (!_canControl) return;

    if (confirm) {
      final labels = {
        'restart': 'Restart PC',
        'shutdown': 'Shut Down PC',
      };
      final confirmed = await showDialog<bool>(
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
              onPressed: () => Navigator.of(ctx).pop(false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.of(ctx).pop(true),
              style: FilledButton.styleFrom(backgroundColor: Colors.red),
              child: Text(labels[action]!),
            ),
          ],
        ),
      );
      if (confirmed != true) return;
    }

    setState(() => _busy = true);
    try {
      final service = DeviceService(ref.read(supabaseClientProvider));
      await service.sendPcControl(widget.device.id, action);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(_successMessage(action)),
            duration: const Duration(seconds: 2),
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Failed: $e')),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  String _successMessage(String action) => switch (action) {
    'lock' => 'Lock command sent',
    'sleep' => 'Sleep command sent',
    'restart' => 'Restart command sent',
    'shutdown' => 'Shutdown command sent',
    _ => 'Command sent',
  };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final enabled = _canControl;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(Icons.settings_remote,
                    size: 20, color: theme.colorScheme.primary),
                const SizedBox(width: 8),
                Text('PC Controls',
                    style: theme.textTheme.titleSmall
                        ?.copyWith(fontWeight: FontWeight.w600)),
                const Spacer(),
                if (!widget.device.isOnline)
                  Text('PC Offline',
                      style: theme.textTheme.labelSmall
                          ?.copyWith(color: theme.colorScheme.outline)),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: _PcControlButton(
                    icon: Icons.lock_outline,
                    label: 'Lock PC',
                    onTap: enabled ? () => _send('lock') : null,
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: _PcControlButton(
                    icon: Icons.nightlight_round,
                    label: 'Sleep PC',
                    onTap: enabled ? () => _send('sleep') : null,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Row(
              children: [
                Expanded(
                  child: _PcControlButton(
                    icon: Icons.restart_alt,
                    label: 'Restart PC',
                    onTap: enabled
                        ? () => _send('restart', confirm: true)
                        : null,
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: _PcControlButton(
                    icon: Icons.power_settings_new,
                    label: 'Shut Down PC',
                    color: Colors.red,
                    onTap: enabled
                        ? () => _send('shutdown', confirm: true)
                        : null,
                  ),
                ),
              ],
            ),
            if (_busy) ...[
              const SizedBox(height: 8),
              const LinearProgressIndicator(),
            ],
          ],
        ),
      ),
    );
  }
}

class _PcControlButton extends StatelessWidget {
  final IconData icon;
  final String label;
  final VoidCallback? onTap;
  final Color? color;

  const _PcControlButton({
    required this.icon,
    required this.label,
    this.onTap,
    this.color,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final effectiveColor =
        onTap != null ? (color ?? theme.colorScheme.primary) : theme.disabledColor;

    return OutlinedButton.icon(
      onPressed: onTap,
      icon: Icon(icon, size: 18, color: effectiveColor),
      label: Text(label, style: TextStyle(color: effectiveColor)),
      style: OutlinedButton.styleFrom(
        padding: const EdgeInsets.symmetric(vertical: 12),
        side: BorderSide(color: effectiveColor.withValues(alpha: 0.4)),
      ),
    );
  }
}

class _EventBreakdown extends StatelessWidget {
  final Map<String, int> counts;

  const _EventBreakdown({required this.counts});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final total = counts.values.fold(0, (a, b) => a + b);
    final sorted = counts.entries.toList()
      ..sort((a, b) => b.value.compareTo(a.value));

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Events (24h)',
                style: theme.textTheme.titleSmall
                    ?.copyWith(fontWeight: FontWeight.w600)),
            Text('$total total',
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.outline)),
            const SizedBox(height: 12),
            SizedBox(
              height: 140,
              child: PieChart(
                PieChartData(
                  sectionsSpace: 2,
                  centerSpaceRadius: 30,
                  sections: sorted.take(6).map((e) {
                    final pct = total > 0 ? e.value / total * 100 : 0.0;
                    return PieChartSectionData(
                      value: e.value.toDouble(),
                      title: pct >= 10 ? '${pct.toStringAsFixed(0)}%' : '',
                      radius: 40,
                      titleStyle: const TextStyle(
                          fontSize: 10,
                          fontWeight: FontWeight.bold,
                          color: Colors.white),
                      color: _colorForType(e.key),
                    );
                  }).toList(),
                ),
              ),
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 12,
              runSpacing: 4,
              children: sorted.take(6).map((e) {
                return Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(
                      width: 10,
                      height: 10,
                      decoration: BoxDecoration(
                        color: _colorForType(e.key),
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: 4),
                    Text('${EventTypes.displayName(e.key)} (${e.value})',
                        style: theme.textTheme.labelSmall),
                  ],
                );
              }).toList(),
            ),
          ],
        ),
      ),
    );
  }

  static final _typeColors = <String, Color>{
    'session_login': Colors.blue,
    'session_logout': Colors.indigo,
    'session_lock': Colors.blueGrey,
    'session_unlock': Colors.cyan,
    'login_failed': Colors.red,
    'process_start': Colors.teal,
    'process_stop': Colors.brown,
    'usb_connected': Colors.purple,
    'usb_disconnected': Colors.deepPurple,
    'network_connected': Colors.green,
    'network_disconnected': Colors.orange,
    'network_changed': Colors.lime,
    'eventlog_entry': Colors.amber,
    'file_access': Colors.pink,
    'system_metrics': Colors.grey,
    'agent_started': Colors.lightBlue,
  };

  Color _colorForType(String type) =>
      _typeColors[type] ?? Colors.grey.shade400;
}

class _RecentEvents extends StatelessWidget {
  final List<ActivityEvent> events;
  final String deviceId;

  const _RecentEvents({required this.events, required this.deviceId});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text('Recent Activity',
                style: theme.textTheme.titleSmall
                    ?.copyWith(fontWeight: FontWeight.w600)),
            TextButton(
              onPressed: () => context.push('/events/$deviceId'),
              child: const Text('View All'),
            ),
          ],
        ),
        if (events.isEmpty)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 16),
            child: Text('No events yet.',
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: theme.colorScheme.outline)),
          )
        else
          ...events.take(10).map((e) => _EventRow(event: e)),
      ],
    );
  }
}

class _ConnectionStatus extends StatelessWidget {
  final Device device;

  const _ConnectionStatus({required this.device});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Status Details',
                style: theme.textTheme.titleSmall
                    ?.copyWith(fontWeight: FontWeight.w600)),
            const SizedBox(height: 12),
            _statusDetail(theme, 'Laptop Power', device.powerStateDisplay),
            _statusDetail(theme, 'Connection',
                device.isOnline ? 'Online' : device.isPairing ? 'Pairing' : 'Offline'),
            _statusDetail(theme, 'Windows User', device.userSessionDisplay),
            if (device.lastStartupAt != null)
              _statusDetail(theme, 'Last Startup',
                  _formatTime(device.lastStartupAt!)),
            if (device.lastShutdownAt != null)
              _statusDetail(theme, 'Last Shutdown',
                  _formatTime(device.lastShutdownAt!)),
            if (device.lastSeenAt != null)
              _statusDetail(theme, 'Last Heartbeat',
                  _formatTime(device.lastSeenAt!)),
            if (device.osVersion != null)
              _statusDetail(theme, 'OS', device.osVersion!),
            if (device.agentVersion != null)
              _statusDetail(theme, 'Agent Version', device.agentVersion!),
          ],
        ),
      ),
    );
  }

  static final _dateFmt = DateFormat('d MMM yyyy');
  static final _timeFmt = DateFormat('h:mm:ss a');

  static String _formatTime(DateTime dt) {
    final local = dt.toLocal();
    final tz = local.timeZoneName;
    return '${_dateFmt.format(local)} ${_timeFmt.format(local)} $tz';
  }

  Widget _statusDetail(ThemeData theme, String label, String value) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Row(
        children: [
          SizedBox(
            width: 120,
            child: Text(label,
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
          ),
          Expanded(
            child: Text(value,
                style: theme.textTheme.bodySmall
                    ?.copyWith(fontWeight: FontWeight.w500)),
          ),
        ],
      ),
    );
  }
}

class _DeviceManagement extends ConsumerWidget {
  final String deviceId;

  const _DeviceManagement({required this.deviceId});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Device Management',
                style: theme.textTheme.titleSmall
                    ?.copyWith(fontWeight: FontWeight.w600)),
            const SizedBox(height: 12),
            SizedBox(
              width: double.infinity,
              child: OutlinedButton.icon(
                onPressed: () => _confirmUnpair(context, ref),
                icon: const Icon(Icons.link_off, size: 18),
                label: const Text('Unpair Device'),
                style: OutlinedButton.styleFrom(
                  foregroundColor: theme.colorScheme.error,
                  side: BorderSide(color: theme.colorScheme.error.withValues(alpha: 0.5)),
                ),
              ),
            ),
            const SizedBox(height: 8),
            Text(
              'Unpairing removes this device from your account. '
              'The agent will generate a new pairing code so you can re-pair later.',
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.outline),
            ),
          ],
        ),
      ),
    );
  }

  void _confirmUnpair(BuildContext context, WidgetRef ref) {
    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Unpair Device?'),
        content: const Text(
          'This will remove the device from your account. '
          'You can re-pair it later using a new pairing code.\n\n'
          'The agent on the laptop will continue running and will '
          'automatically generate a new code.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () {
              Navigator.of(ctx).pop();
              _doUnpair(context, ref);
            },
            style: FilledButton.styleFrom(
              backgroundColor: Theme.of(context).colorScheme.error,
            ),
            child: const Text('Unpair'),
          ),
        ],
      ),
    );
  }

  void _doUnpair(BuildContext context, WidgetRef ref) async {
    final messenger = ScaffoldMessenger.of(context);
    final router = GoRouter.of(context);
    final service = DeviceService(ref.read(supabaseClientProvider));

    try {
      await service.unpairDevice(deviceId);
      messenger.showSnackBar(
        const SnackBar(content: Text('Device unpaired successfully')),
      );
      ref.invalidate(devicesProvider);
      router.go('/devices');
    } catch (e) {
      messenger.showSnackBar(
        SnackBar(content: Text('Failed to unpair: $e')),
      );
    }
  }
}

class _EventRow extends StatelessWidget {
  final ActivityEvent event;

  const _EventRow({required this.event});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        children: [
          Icon(EventTypes.icon(event.eventType), size: 18,
              color: EventTypes.severityColor(event.severity)),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(EventTypes.displayName(event.eventType),
                    style: theme.textTheme.bodySmall
                        ?.copyWith(fontWeight: FontWeight.w500)),
                Text(eventSubtitle(event),
                    style: theme.textTheme.labelSmall
                        ?.copyWith(color: theme.colorScheme.outline),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis),
              ],
            ),
          ),
          Text(timeago.format(event.timestamp.toLocal()),
              style: theme.textTheme.labelSmall
                  ?.copyWith(color: theme.colorScheme.outline)),
        ],
      ),
    );
  }
}
