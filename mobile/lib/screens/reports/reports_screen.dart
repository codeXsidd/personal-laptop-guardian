import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../models/event_type.dart';
import '../../models/heartbeat.dart';
import '../../providers/device_provider.dart';
import '../../providers/event_provider.dart';

class ReportsScreen extends ConsumerWidget {
  final String deviceId;

  const ReportsScreen({super.key, required this.deviceId});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final heartbeatsAsync = ref.watch(heartbeatHistoryProvider(deviceId));
    final countsAsync = ref.watch(eventTypeCountsProvider(deviceId));
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text('Reports')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          heartbeatsAsync.when(
            loading: () => const Center(child: CircularProgressIndicator()),
            error: (_, _) => const Text('Failed to load metrics'),
            data: (heartbeats) {
              if (heartbeats.isEmpty) {
                return Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Text('No heartbeat data available.',
                        style: theme.textTheme.bodyMedium),
                  ),
                );
              }
              return Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _CpuChart(heartbeats: heartbeats),
                  const SizedBox(height: 16),
                  _MemoryChart(heartbeats: heartbeats),
                  const SizedBox(height: 16),
                  _BatteryChart(heartbeats: heartbeats),
                ],
              );
            },
          ),
          const SizedBox(height: 16),
          countsAsync.when(
            loading: () => const SizedBox.shrink(),
            error: (_, _) => const SizedBox.shrink(),
            data: (counts) => _EventSummaryCard(counts: counts),
          ),
        ],
      ),
    );
  }
}

class _CpuChart extends StatelessWidget {
  final List<Heartbeat> heartbeats;

  const _CpuChart({required this.heartbeats});

  @override
  Widget build(BuildContext context) {
    return _MetricLineChart(
      title: 'CPU Usage',
      heartbeats: heartbeats,
      getValue: (h) => h.cpuPercent,
      color: Colors.blue,
      maxY: 100,
    );
  }
}

class _MemoryChart extends StatelessWidget {
  final List<Heartbeat> heartbeats;

  const _MemoryChart({required this.heartbeats});

  @override
  Widget build(BuildContext context) {
    return _MetricLineChart(
      title: 'Memory Usage',
      heartbeats: heartbeats,
      getValue: (h) => h.memoryPercent,
      color: Colors.purple,
      maxY: 100,
    );
  }
}

class _BatteryChart extends StatelessWidget {
  final List<Heartbeat> heartbeats;

  const _BatteryChart({required this.heartbeats});

  @override
  Widget build(BuildContext context) {
    return _MetricLineChart(
      title: 'Battery Level',
      heartbeats: heartbeats,
      getValue: (h) => h.batteryPercent,
      color: Colors.green,
      maxY: 100,
    );
  }
}

class _MetricLineChart extends StatelessWidget {
  final String title;
  final List<Heartbeat> heartbeats;
  final double? Function(Heartbeat) getValue;
  final Color color;
  final double maxY;

  const _MetricLineChart({
    required this.title,
    required this.heartbeats,
    required this.getValue,
    required this.color,
    required this.maxY,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final reversed = heartbeats.reversed.toList();
    final spots = <FlSpot>[];
    for (var i = 0; i < reversed.length; i++) {
      final v = getValue(reversed[i]);
      if (v != null) {
        spots.add(FlSpot(i.toDouble(), v));
      }
    }

    if (spots.isEmpty) return const SizedBox.shrink();

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title,
                style: theme.textTheme.titleSmall
                    ?.copyWith(fontWeight: FontWeight.w600)),
            const SizedBox(height: 12),
            SizedBox(
              height: 160,
              child: LineChart(
                LineChartData(
                  gridData: FlGridData(
                    show: true,
                    drawVerticalLine: false,
                    horizontalInterval: 25,
                    getDrawingHorizontalLine: (v) => FlLine(
                      color: theme.colorScheme.outline.withValues(alpha: 0.15),
                      strokeWidth: 1,
                    ),
                  ),
                  titlesData: FlTitlesData(
                    topTitles: const AxisTitles(),
                    rightTitles: const AxisTitles(),
                    bottomTitles: AxisTitles(
                      sideTitles: SideTitles(
                        showTitles: true,
                        reservedSize: 22,
                        interval: (spots.length / 4).ceilToDouble().clamp(1, 100),
                        getTitlesWidget: (val, _) {
                          final idx = val.toInt();
                          if (idx < 0 || idx >= reversed.length) {
                            return const SizedBox.shrink();
                          }
                          return Text(
                            DateFormat('HH:mm')
                                .format(reversed[idx].createdAt.toLocal()),
                            style: const TextStyle(fontSize: 9),
                          );
                        },
                      ),
                    ),
                    leftTitles: AxisTitles(
                      sideTitles: SideTitles(
                        showTitles: true,
                        reservedSize: 32,
                        interval: 25,
                        getTitlesWidget: (val, _) => Text('${val.toInt()}%',
                            style: const TextStyle(fontSize: 9)),
                      ),
                    ),
                  ),
                  borderData: FlBorderData(show: false),
                  minY: 0,
                  maxY: maxY,
                  lineBarsData: [
                    LineChartBarData(
                      spots: spots,
                      isCurved: true,
                      color: color,
                      barWidth: 2,
                      dotData: const FlDotData(show: false),
                      belowBarData: BarAreaData(
                        show: true,
                        color: color.withValues(alpha: 0.1),
                      ),
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

class _EventSummaryCard extends StatelessWidget {
  final Map<String, int> counts;

  const _EventSummaryCard({required this.counts});

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
            Text('Event Summary (24h)',
                style: theme.textTheme.titleSmall
                    ?.copyWith(fontWeight: FontWeight.w600)),
            const SizedBox(height: 4),
            Text('$total events recorded',
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.outline)),
            const SizedBox(height: 12),
            ...sorted.map((e) => Padding(
                  padding: const EdgeInsets.symmetric(vertical: 3),
                  child: Row(
                    children: [
                      Icon(EventTypes.icon(e.key), size: 16,
                          color: theme.colorScheme.primary),
                      const SizedBox(width: 8),
                      Expanded(
                          child: Text(EventTypes.displayName(e.key),
                              style: theme.textTheme.bodySmall)),
                      Text('${e.value}',
                          style: theme.textTheme.bodySmall
                              ?.copyWith(fontWeight: FontWeight.w600)),
                    ],
                  ),
                )),
          ],
        ),
      ),
    );
  }
}
