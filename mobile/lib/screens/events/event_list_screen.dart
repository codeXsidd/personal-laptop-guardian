import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import 'package:timeago/timeago.dart' as timeago;

import '../../models/event.dart';
import '../../models/event_type.dart';
import '../../providers/event_provider.dart';
import '../../services/event_service.dart';
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
  String? _error;
  static const _pageSize = 50;

  String? _severityFilter;
  DateTimeRange? _dateRange;

  // Multi-select state
  bool _selectMode = false;
  final _selected = <String>{};

  @override
  void initState() {
    super.initState();
    _loadMore();
  }

  Future<void> _loadMore() async {
    if (_loading || !_hasMore) return;
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final newEvents = await ref.read(eventServiceProvider).getEvents(
            widget.deviceId,
            limit: _pageSize,
            offset: _events.length,
            eventType: widget.filterEventType,
            eventTypes: widget.filterEventTypes,
            minSeverity: _severityFilter,
            after: _dateRange?.start,
            before: _dateRange?.end.add(const Duration(days: 1)),
          );
      if (!mounted) return;
      setState(() {
        _events.addAll(newEvents);
        _hasMore = newEvents.length == _pageSize;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = 'Failed to load events. Tap to retry.');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _refresh() async {
    setState(() {
      _events.clear();
      _hasMore = true;
      _error = null;
    });
    await _loadMore();
  }

  void _applyFilters() {
    setState(() {
      _events.clear();
      _hasMore = true;
      _error = null;
    });
    _loadMore();
  }

  void _enterSelectMode(String eventId) {
    setState(() {
      _selectMode = true;
      _selected.add(eventId);
    });
  }

  void _exitSelectMode() {
    setState(() {
      _selectMode = false;
      _selected.clear();
    });
  }

  void _toggleSelection(String eventId) {
    setState(() {
      if (_selected.contains(eventId)) {
        _selected.remove(eventId);
        if (_selected.isEmpty) _selectMode = false;
      } else {
        _selected.add(eventId);
      }
    });
  }

  Future<void> _deleteSelected() async {
    final protectedCount = _events
        .where((e) => _selected.contains(e.id) && EventService.isProtectedType(e.eventType))
        .length;
    final deletableIds = _events
        .where((e) => _selected.contains(e.id) && !EventService.isProtectedType(e.eventType))
        .map((e) => e.id)
        .toList();

    if (deletableIds.isEmpty) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Selected events are protected security/audit logs and cannot be deleted.')),
        );
      }
      return;
    }

    final confirm = await _confirmDelete(
      'Delete ${deletableIds.length} event${deletableIds.length == 1 ? '' : 's'}?',
      protectedCount > 0
          ? '$protectedCount protected security event${protectedCount == 1 ? '' : 's'} will be skipped.'
          : null,
    );
    if (confirm != true) return;

    try {
      await ref.read(eventServiceProvider).deleteEvents(deletableIds);
      setState(() {
        _events.removeWhere((e) => deletableIds.contains(e.id));
        _selected.clear();
        _selectMode = false;
      });
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Deleted ${deletableIds.length} event${deletableIds.length == 1 ? '' : 's'}.')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Delete failed: $e')),
        );
      }
    }
  }

  Future<void> _deleteSingleEvent(ActivityEvent event) async {
    if (EventService.isProtectedType(event.eventType)) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('${EventTypes.displayName(event.eventType)} is a protected security event and cannot be deleted.')),
      );
      return;
    }
    final confirm = await _confirmDelete(
      'Delete this ${EventTypes.displayName(event.eventType)} event?',
      null,
    );
    if (confirm != true) return;

    try {
      await ref.read(eventServiceProvider).deleteEvent(event.id);
      setState(() => _events.removeWhere((e) => e.id == event.id));
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Event deleted.')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Delete failed: $e')),
        );
      }
    }
  }

  Future<void> _deleteOldEvents() async {
    final choice = await showModalBottomSheet<int>(
      context: context,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (ctx) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Center(
                child: Container(
                  width: 32, height: 4,
                  decoration: BoxDecoration(
                    color: Theme.of(ctx).colorScheme.outline.withValues(alpha: 0.3),
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
              ),
              const SizedBox(height: 16),
              Text('Delete Old Logs',
                  style: Theme.of(ctx).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.bold)),
              const SizedBox(height: 4),
              Text('Security & audit events are protected and will not be deleted.',
                  style: Theme.of(ctx).textTheme.bodySmall?.copyWith(
                      color: Theme.of(ctx).colorScheme.onSurfaceVariant)),
              const SizedBox(height: 16),
              ...[
                (7, 'Older than 7 days'),
                (30, 'Older than 30 days'),
                (90, 'Older than 90 days'),
              ].map((e) => ListTile(
                    leading: const Icon(Icons.delete_sweep),
                    title: Text(e.$2),
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                    onTap: () => Navigator.pop(ctx, e.$1),
                  )),
              ListTile(
                leading: const Icon(Icons.date_range),
                title: const Text('Custom date...'),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                onTap: () => Navigator.pop(ctx, -1),
              ),
            ],
          ),
        ),
      ),
    );
    if (choice == null || !mounted) return;

    DateTime cutoff;
    if (choice == -1) {
      final picked = await showDatePicker(
        context: context,
        initialDate: DateTime.now().subtract(const Duration(days: 30)),
        firstDate: DateTime(2024),
        lastDate: DateTime.now(),
        helpText: 'Delete events before this date',
      );
      if (picked == null || !mounted) return;
      cutoff = picked.toUtc();
    } else {
      cutoff = DateTime.now().toUtc().subtract(Duration(days: choice));
    }

    final dateFmt = DateFormat('MMM d, yyyy');
    final confirm = await _confirmDelete(
      'Delete all eligible events before ${dateFmt.format(cutoff.toLocal())}?',
      'Protected security/audit events will be kept.',
    );
    if (confirm != true) return;

    try {
      final count = await ref.read(eventServiceProvider).deleteEventsOlderThan(
            widget.deviceId,
            cutoff,
          );
      await _refresh();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Deleted $count old event${count == 1 ? '' : 's'}.')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Delete failed: $e')),
        );
      }
    }
  }

  Future<bool?> _confirmDelete(String title, String? subtitle) {
    return showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(title),
        content: subtitle != null ? Text(subtitle) : null,
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            style: FilledButton.styleFrom(
              backgroundColor: Theme.of(ctx).colorScheme.error,
            ),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
  }

  bool get _isTimeline =>
      widget.filterEventType == null && widget.filterEventTypes == null;

  bool get _hasActiveFilters =>
      _severityFilter != null || _dateRange != null;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: _selectMode ? _buildSelectAppBar(theme) : _buildNormalAppBar(theme),
      body: RefreshIndicator(
        onRefresh: _refresh,
        child: _buildBody(theme),
      ),
    );
  }

  PreferredSizeWidget _buildNormalAppBar(ThemeData theme) {
    return AppBar(
      title: Text(widget.title ?? 'Activity Timeline'),
      actions: [
        if (_hasActiveFilters)
          IconButton(
            icon: const Icon(Icons.filter_alt_off),
            tooltip: 'Clear filters',
            onPressed: () {
              setState(() {
                _severityFilter = null;
                _dateRange = null;
              });
              _applyFilters();
            },
          ),
        IconButton(
          icon: Badge(
            isLabelVisible: _hasActiveFilters,
            child: const Icon(Icons.filter_list),
          ),
          tooltip: 'Filter',
          onPressed: () => _showFilterSheet(context),
        ),
        PopupMenuButton<String>(
          icon: const Icon(Icons.more_vert),
          onSelected: (value) {
            if (value == 'select') {
              setState(() => _selectMode = true);
            } else if (value == 'delete_old') {
              _deleteOldEvents();
            }
          },
          itemBuilder: (_) => [
            const PopupMenuItem(value: 'select', child: Text('Select events')),
            const PopupMenuItem(value: 'delete_old', child: Text('Delete old logs...')),
          ],
        ),
      ],
    );
  }

  PreferredSizeWidget _buildSelectAppBar(ThemeData theme) {
    return AppBar(
      leading: IconButton(
        icon: const Icon(Icons.close),
        onPressed: _exitSelectMode,
      ),
      title: Text('${_selected.length} selected'),
      actions: [
        TextButton(
          onPressed: () {
            setState(() {
              if (_selected.length == _events.length) {
                _selected.clear();
              } else {
                _selected.addAll(_events.map((e) => e.id));
              }
            });
          },
          child: Text(_selected.length == _events.length ? 'Deselect All' : 'Select All'),
        ),
        IconButton(
          icon: const Icon(Icons.delete),
          tooltip: 'Delete selected',
          onPressed: _selected.isEmpty ? null : _deleteSelected,
        ),
      ],
    );
  }

  Widget _buildBody(ThemeData theme) {
    if (_error != null && _events.isEmpty) {
      return _ErrorState(
        message: _error!,
        onRetry: _refresh,
      );
    }

    if (_events.isEmpty && !_loading) {
      return ListView(
        children: [
          SizedBox(
            height: MediaQuery.of(context).size.height * 0.6,
            child: Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(Icons.event_busy, size: 48,
                      color: theme.colorScheme.outline),
                  const SizedBox(height: 12),
                  Text('No events found.',
                      style: theme.textTheme.bodyMedium
                          ?.copyWith(color: theme.colorScheme.outline)),
                  if (_hasActiveFilters) ...[
                    const SizedBox(height: 8),
                    TextButton(
                      onPressed: () {
                        setState(() {
                          _severityFilter = null;
                          _dateRange = null;
                        });
                        _applyFilters();
                      },
                      child: const Text('Clear filters'),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      );
    }

    if (_isTimeline) {
      return _buildGroupedTimeline(theme);
    }

    return ListView.builder(
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
        return _buildEventTile(_events[index]);
      },
    );
  }

  Widget _buildEventTile(ActivityEvent event) {
    return EventTile(
      event: event,
      selectMode: _selectMode,
      selected: _selected.contains(event.id),
      onTap: _selectMode
          ? () => _toggleSelection(event.id)
          : () => showEventDetail(context, event),
      onLongPress: _selectMode
          ? null
          : () => _enterSelectMode(event.id),
      onDelete: _selectMode ? null : () => _deleteSingleEvent(event),
    );
  }

  Widget _buildGroupedTimeline(ThemeData theme) {
    final groups = <String, List<ActivityEvent>>{};
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    final yesterday = today.subtract(const Duration(days: 1));
    final dateFmt = DateFormat('EEEE, MMM d');

    for (final event in _events) {
      final local = event.timestamp.toLocal();
      final day = DateTime(local.year, local.month, local.day);
      String label;
      if (day == today) {
        label = 'Today';
      } else if (day == yesterday) {
        label = 'Yesterday';
      } else {
        label = dateFmt.format(local);
      }
      (groups[label] ??= []).add(event);
    }

    final entries = groups.entries.toList();
    return CustomScrollView(
      slivers: [
        for (final entry in entries) ...[
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 16, 16, 4),
              child: Text(entry.key,
                  style: theme.textTheme.titleSmall
                      ?.copyWith(fontWeight: FontWeight.w600)),
            ),
          ),
          SliverPadding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            sliver: SliverList.builder(
              itemCount: entry.value.length,
              itemBuilder: (_, i) => _buildEventTile(entry.value[i]),
            ),
          ),
        ],
        if (_hasMore)
          SliverToBoxAdapter(
            child: Builder(builder: (_) {
              WidgetsBinding.instance.addPostFrameCallback((_) => _loadMore());
              return const Padding(
                padding: EdgeInsets.all(16),
                child: Center(child: CircularProgressIndicator()),
              );
            }),
          ),
      ],
    );
  }

  Future<void> _showFilterSheet(BuildContext context) async {
    final result = await showModalBottomSheet<_FilterResult>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (_) => _FilterSheet(
        currentSeverity: _severityFilter,
        currentDateRange: _dateRange,
      ),
    );
    if (result == null) return;
    setState(() {
      _severityFilter = result.severity;
      _dateRange = result.dateRange;
    });
    _applyFilters();
  }
}

class _ErrorState extends StatelessWidget {
  final String message;
  final VoidCallback onRetry;

  const _ErrorState({required this.message, required this.onRetry});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return ListView(
      children: [
        SizedBox(
          height: MediaQuery.of(context).size.height * 0.6,
          child: Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.cloud_off, size: 48,
                    color: theme.colorScheme.error),
                const SizedBox(height: 12),
                Text(message,
                    style: theme.textTheme.bodyMedium
                        ?.copyWith(color: theme.colorScheme.error)),
                const SizedBox(height: 8),
                FilledButton.icon(
                  onPressed: onRetry,
                  icon: const Icon(Icons.refresh),
                  label: const Text('Retry'),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

class _FilterResult {
  final String? severity;
  final DateTimeRange? dateRange;
  _FilterResult({this.severity, this.dateRange});
}

class _FilterSheet extends StatefulWidget {
  final String? currentSeverity;
  final DateTimeRange? currentDateRange;

  const _FilterSheet({this.currentSeverity, this.currentDateRange});

  @override
  State<_FilterSheet> createState() => _FilterSheetState();
}

class _FilterSheetState extends State<_FilterSheet> {
  late String? _severity;
  late DateTimeRange? _dateRange;

  static const _severities = ['info', 'low', 'medium', 'high', 'critical'];

  @override
  void initState() {
    super.initState();
    _severity = widget.currentSeverity;
    _dateRange = widget.currentDateRange;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final dateFmt = DateFormat('MMM d, yyyy');

    return Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Center(
            child: Container(
              width: 32, height: 4,
              decoration: BoxDecoration(
                color: theme.colorScheme.outline.withValues(alpha: 0.3),
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
          const SizedBox(height: 16),
          Text('Filters',
              style: theme.textTheme.titleMedium
                  ?.copyWith(fontWeight: FontWeight.bold)),
          const SizedBox(height: 16),
          Text('Minimum severity',
              style: theme.textTheme.labelLarge),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            children: [
              ChoiceChip(
                label: const Text('All'),
                selected: _severity == null,
                onSelected: (_) => setState(() => _severity = null),
              ),
              for (final s in _severities)
                ChoiceChip(
                  label: Text(s[0].toUpperCase() + s.substring(1)),
                  selected: _severity == s,
                  onSelected: (_) => setState(() => _severity = s),
                  avatar: CircleAvatar(
                    backgroundColor: EventTypes.severityColor(s),
                    radius: 6,
                  ),
                ),
            ],
          ),
          const SizedBox(height: 16),
          Text('Date range', style: theme.textTheme.labelLarge),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                child: OutlinedButton.icon(
                  icon: const Icon(Icons.date_range, size: 18),
                  label: Text(_dateRange != null
                      ? '${dateFmt.format(_dateRange!.start)} – ${dateFmt.format(_dateRange!.end)}'
                      : 'Select dates'),
                  onPressed: () async {
                    final picked = await showDateRangePicker(
                      context: context,
                      firstDate: DateTime(2024),
                      lastDate: DateTime.now(),
                      initialDateRange: _dateRange,
                    );
                    if (picked != null) {
                      setState(() => _dateRange = picked);
                    }
                  },
                ),
              ),
              if (_dateRange != null)
                IconButton(
                  icon: const Icon(Icons.close, size: 18),
                  onPressed: () => setState(() => _dateRange = null),
                ),
            ],
          ),
          const SizedBox(height: 24),
          Row(
            children: [
              Expanded(
                child: OutlinedButton(
                  onPressed: () => Navigator.pop(context,
                      _FilterResult(severity: null, dateRange: null)),
                  child: const Text('Clear all'),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: FilledButton(
                  onPressed: () => Navigator.pop(context,
                      _FilterResult(severity: _severity, dateRange: _dateRange)),
                  child: const Text('Apply'),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
        ],
      ),
    );
  }
}

class EventTile extends StatelessWidget {
  final ActivityEvent event;
  final VoidCallback? onDelete;
  final VoidCallback? onTap;
  final VoidCallback? onLongPress;
  final bool selectMode;
  final bool selected;

  const EventTile({
    super.key,
    required this.event,
    this.onDelete,
    this.onTap,
    this.onLongPress,
    this.selectMode = false,
    this.selected = false,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final sevColor = EventTypes.severityColor(event.severity);
    final cat = EventTypes.category(event.eventType);
    final catLabel = EventTypes.categoryLabel(cat);

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      color: selected ? theme.colorScheme.primaryContainer.withValues(alpha: 0.3) : null,
      child: InkWell(
        borderRadius: BorderRadius.circular(16),
        onTap: onTap ?? () => showEventDetail(context, event),
        onLongPress: onLongPress ?? onDelete,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Row(
            children: [
              if (selectMode) ...[
                Checkbox(
                  value: selected,
                  onChanged: (_) => onTap?.call(),
                ),
                const SizedBox(width: 4),
              ],
              Column(
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
                  const SizedBox(height: 4),
                  Text(catLabel,
                      style: theme.textTheme.labelSmall?.copyWith(
                          fontSize: 8,
                          fontWeight: FontWeight.w600,
                          color: theme.colorScheme.outline)),
                ],
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(EventTypes.displayName(event.eventType),
                        style: theme.textTheme.bodyMedium
                            ?.copyWith(fontWeight: FontWeight.w500)),
                    Text(eventSubtitle(event),
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
                  Text(timeago.format(event.timestamp.toLocal()),
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
}

String eventSubtitle(ActivityEvent e) {
  final p = e.payload;
  return switch (e.eventType) {
    'session_login' || 'session_logout' || 'session_lock' || 'session_unlock' =>
      p['username']?.toString() ?? '',
    'login_failed' =>
      '${p['username'] ?? 'Unknown'} - ${p['failure_reason'] ?? ''}',
    'process_start' => _processStartSubtitle(p),
    'process_stop' => _processStopSubtitle(p),
    'usb_connected' || 'usb_disconnected' =>
      p['device_name']?.toString() ?? '',
    'network_connected' || 'network_changed' => _networkSubtitle(p),
    'network_disconnected' =>
      p['adapter_name']?.toString() ?? '',
    'eventlog_entry' =>
      '[${p['level'] ?? ''}] ${p['source'] ?? ''}: ${p['message'] ?? ''}',
    'file_access' =>
      '${p['access_type'] ?? ''} ${p['file_path'] ?? ''}',
    'system_metrics' =>
      'CPU ${p['cpu'] ?? 'N/A'}% | Mem ${p['memory'] ?? 'N/A'}%',
    'agent_started' => 'Agent version ${p['version'] ?? ''}',
    'system_startup' || 'system_shutdown' => '',
    _ => '',
  };
}

String _processStartSubtitle(Map<String, dynamic> p) {
  final name = p['process_name'] ?? '';
  final title = p['window_title'];
  if (title != null && title.toString().isNotEmpty) {
    return '$name — $title';
  }
  return '$name (PID ${p['pid'] ?? ''})';
}

String _processStopSubtitle(Map<String, dynamic> p) {
  final name = p['process_name'] ?? '';
  final dur = p['duration_seconds'] ?? p['duration_s'];
  if (dur != null) {
    final secs = (dur is num) ? dur.toInt() : int.tryParse(dur.toString()) ?? 0;
    if (secs >= 3600) {
      return '$name ran ${secs ~/ 3600}h ${(secs % 3600) ~/ 60}m';
    } else if (secs >= 60) {
      return '$name ran ${secs ~/ 60}m ${secs % 60}s';
    }
    return '$name ran ${secs}s';
  }
  return '$name (PID ${p['pid'] ?? ''})';
}

String _networkSubtitle(Map<String, dynamic> p) {
  final adapters = p['adapters'];
  if (adapters is List && adapters.isNotEmpty) {
    final first = adapters[0];
    if (first is Map) {
      final ssid = first['ssid'];
      final ipv4 = first['ipv4'];
      final signal = first['signal'];
      final parts = <String>[];
      if (ssid != null) parts.add(ssid.toString());
      if (ipv4 != null) parts.add(ipv4.toString());
      if (signal != null) parts.add(signal.toString());
      if (parts.isNotEmpty) return parts.join(' · ');
    }
  }
  final adapterName = p['adapter_name'];
  final ip = p['ip_address'] ?? p['ipv4'];
  return '${adapterName ?? ''} ${ip ?? ''}'.trim();
}
