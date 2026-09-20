import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:timeago/timeago.dart' as timeago;

import '../../models/device.dart';
import '../../models/event.dart';
import '../../models/event_type.dart';
import '../../models/heartbeat.dart';
import '../../providers/device_provider.dart';
import '../../providers/event_provider.dart';

class DashboardScreen extends ConsumerWidget {
  final String deviceId;

  const DashboardScreen({super.key, required this.deviceId});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final deviceAsync = ref.watch(deviceByIdProvider(deviceId));
    final heartbeatAsync = ref.watch(latestHeartbeatProvider(deviceId));
    final recentAsync = ref.watch(recentEventsProvider(deviceId));
    final countsAsync = ref.watch(eventTypeCountsProvider(deviceId));

    return Scaffold(
      appBar: AppBar(
        title: const Text('Dashboard'),
        leading: IconButton(
          icon: const Icon(Icons.arrow_back),
          onPressed: () => context.go('/devices'),
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: () {
              ref.invalidate(deviceByIdProvider(deviceId));
              ref.invalidate(latestHeartbeatProvider(deviceId));
              ref.invalidate(recentEventsProvider(deviceId));
              ref.invalidate(eventTypeCountsProvider(deviceId));
            },
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(deviceByIdProvider(deviceId));
          ref.invalidate(latestHeartbeatProvider(deviceId));
          ref.invalidate(recentEventsProvider(deviceId));
          ref.invalidate(eventTypeCountsProvider(deviceId));
        },
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
    final statusColor = device.isOnline ? Colors.green : Colors.grey;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          children: [
            Icon(Icons.laptop_windows, size: 40, color: theme.colorScheme.primary),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(device.displayName,
                      style: theme.textTheme.titleMedium
                          ?.copyWith(fontWeight: FontWeight.w600)),
                  Text(device.machineName,
                      style: theme.textTheme.bodySmall
                          ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
                  if (device.osVersion != null)
                    Text(device.osVersion!,
                        style: theme.textTheme.bodySmall
                            ?.copyWith(color: theme.colorScheme.outline)),
                ],
              ),
            ),
            Column(
              children: [
                Container(
                  width: 12,
                  height: 12,
                  decoration: BoxDecoration(
                      color: statusColor, shape: BoxShape.circle),
                ),
                const SizedBox(height: 4),
                Text(device.status.toUpperCase(),
                    style: theme.textTheme.labelSmall
                        ?.copyWith(color: statusColor)),
                if (device.lastSeenAt != null) ...[
                  const SizedBox(height: 2),
                  Text(timeago.format(device.lastSeenAt!),
                      style: theme.textTheme.labelSmall
                          ?.copyWith(color: theme.colorScheme.outline)),
                ],
              ],
            ),
          ],
        ),
      ),
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
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Text('No metrics received yet.',
            style: Theme.of(context).textTheme.bodyMedium),
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
                    onTap: () => context.go(a.route),
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
              onPressed: () => context.go('/events/$deviceId'),
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
                Text(_summaryText(event),
                    style: theme.textTheme.labelSmall
                        ?.copyWith(color: theme.colorScheme.outline),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis),
              ],
            ),
          ),
          Text(timeago.format(event.timestamp),
              style: theme.textTheme.labelSmall
                  ?.copyWith(color: theme.colorScheme.outline)),
        ],
      ),
    );
  }

  String _summaryText(ActivityEvent e) {
    final p = e.payload;
    return switch (e.eventType) {
      'session_login' || 'session_logout' || 'session_lock' || 'session_unlock' =>
        p['username']?.toString() ?? '',
      'login_failed' =>
        '${p['username'] ?? 'Unknown'} - ${p['failure_reason'] ?? ''}',
      'process_start' => p['process_name']?.toString() ?? '',
      'process_stop' =>
        '${p['process_name'] ?? ''} (${p['duration_s'] ?? 0}s)',
      'usb_connected' || 'usb_disconnected' =>
        p['device_name']?.toString() ?? '',
      'network_connected' =>
        p['adapter_name']?.toString() ?? '',
      'network_disconnected' =>
        p['adapter_name']?.toString() ?? '',
      'eventlog_entry' =>
        '${p['source'] ?? ''}: ${p['message'] ?? ''}',
      'file_access' =>
        '${p['access_type'] ?? ''} ${p['file_path'] ?? ''}',
      _ => '',
    };
  }
}
