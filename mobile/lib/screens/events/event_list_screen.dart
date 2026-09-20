import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:timeago/timeago.dart' as timeago;

import '../../models/event.dart';
import '../../models/event_type.dart';
import '../../providers/event_provider.dart';
import 'event_detail_sheet.dart';

class EventListScreen extends ConsumerStatefulWidget {
  final String deviceId;
  final String? filterEventType;
  final List<String>? filterEventTypes;
  final String? title;

  const EventListScreen({
    super.key,
    required this.deviceId,
    this.filterEventType,
    this.filterEventTypes,
    this.title,
  });

  @override
  ConsumerState<EventListScreen> createState() => _EventListScreenState();
}

class _EventListScreenState extends ConsumerState<EventListScreen> {
  final _events = <ActivityEvent>[];
  bool _loading = false;
  bool _hasMore = true;
  static const _pageSize = 50;

  @override
  void initState() {
    super.initState();
    _loadMore();
  }

  Future<void> _loadMore() async {
    if (_loading || !_hasMore) return;
    setState(() => _loading = true);

    try {
      final newEvents = await ref.read(eventServiceProvider).getEvents(
            widget.deviceId,
            limit: _pageSize,
            offset: _events.length,
            eventType: widget.filterEventType,
            eventTypes: widget.filterEventTypes,
          );
      setState(() {
        _events.addAll(newEvents);
        _hasMore = newEvents.length == _pageSize;
      });
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _refresh() async {
    setState(() {
      _events.clear();
      _hasMore = true;
    });
    await _loadMore();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.title ?? 'Activity Timeline'),
      ),
      body: RefreshIndicator(
        onRefresh: _refresh,
        child: _events.isEmpty && !_loading
            ? Center(
                child: Text('No events found.',
                    style: theme.textTheme.bodyMedium
                        ?.copyWith(color: theme.colorScheme.outline)),
              )
            : ListView.builder(
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                itemCount: _events.length + (_hasMore ? 1 : 0),
                itemBuilder: (context, index) {
                  if (index >= _events.length) {
                    _loadMore();
                    return const Padding(
                      padding: EdgeInsets.all(16),
                      child: Center(child: CircularProgressIndicator()),
                    );
                  }
                  return _EventTile(event: _events[index]);
                },
              ),
      ),
    );
  }
}

class _EventTile extends StatelessWidget {
  final ActivityEvent event;

  const _EventTile({required this.event});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final sevColor = EventTypes.severityColor(event.severity);

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: InkWell(
        borderRadius: BorderRadius.circular(16),
        onTap: () => showEventDetail(context, event),
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Row(
            children: [
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(
                  color: sevColor.withValues(alpha: 0.12),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Icon(EventTypes.icon(event.eventType),
                    size: 20, color: sevColor),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(EventTypes.displayName(event.eventType),
                        style: theme.textTheme.bodyMedium
                            ?.copyWith(fontWeight: FontWeight.w500)),
                    Text(_subtitle(event),
                        style: theme.textTheme.bodySmall
                            ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis),
                  ],
                ),
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(timeago.format(event.timestamp),
                      style: theme.textTheme.labelSmall
                          ?.copyWith(color: theme.colorScheme.outline)),
                  const SizedBox(height: 2),
                  Container(
                    padding:
                        const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
                    decoration: BoxDecoration(
                      color: sevColor.withValues(alpha: 0.12),
                      borderRadius: BorderRadius.circular(4),
                    ),
                    child: Text(event.severity.toUpperCase(),
                        style: theme.textTheme.labelSmall
                            ?.copyWith(color: sevColor, fontSize: 9)),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  String _subtitle(ActivityEvent e) {
    final p = e.payload;
    return switch (e.eventType) {
      'session_login' || 'session_logout' || 'session_lock' || 'session_unlock' =>
        p['username']?.toString() ?? '',
      'login_failed' =>
        '${p['username'] ?? 'Unknown'} - ${p['failure_reason'] ?? ''}',
      'process_start' => '${p['process_name'] ?? ''} (PID ${p['pid'] ?? ''})',
      'process_stop' =>
        '${p['process_name'] ?? ''} ran ${p['duration_s'] ?? 0}s',
      'usb_connected' || 'usb_disconnected' =>
        p['device_name']?.toString() ?? '',
      'network_connected' =>
        '${p['adapter_name'] ?? ''} ${p['ip_address'] ?? ''}',
      'network_disconnected' =>
        p['adapter_name']?.toString() ?? '',
      'network_changed' =>
        '${p['adapter_name'] ?? ''} ${p['old_ip'] ?? ''} → ${p['new_ip'] ?? ''}',
      'eventlog_entry' =>
        '[${p['level'] ?? ''}] ${p['source'] ?? ''}: ${p['message'] ?? ''}',
      'file_access' =>
        '${p['access_type'] ?? ''} ${p['file_path'] ?? ''}',
      'system_metrics' =>
        'CPU ${p['cpu'] ?? 'N/A'}% | Mem ${p['memory'] ?? 'N/A'}%',
      _ => '',
    };
  }
}
