import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import 'package:timeago/timeago.dart' as timeago;

import '../../models/event.dart';
import '../../models/event_type.dart';
import '../../providers/event_provider.dart';
import '../../services/event_service.dart';

final _lastViewedProvider = StateProvider<DateTime?>((ref) => null);

class NotificationHistoryScreen extends ConsumerStatefulWidget {
  final String deviceId;

  const NotificationHistoryScreen({super.key, required this.deviceId});

  @override
  ConsumerState<NotificationHistoryScreen> createState() =>
      _NotificationHistoryScreenState();
}

class _NotificationHistoryScreenState
    extends ConsumerState<NotificationHistoryScreen> {
  final _notifications = <ActivityEvent>[];
  bool _loading = false;
  String? _error;
  DateTime? _lastViewed;

  static const _excludedTypes = ['system_metrics'];

  @override
  void initState() {
    super.initState();
    _lastViewed = ref.read(_lastViewedProvider);
    _load();
  }

  @override
  void dispose() {
    ref.read(_lastViewedProvider.notifier).state = DateTime.now().toUtc();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final events = await ref.read(eventServiceProvider).getEvents(
            widget.deviceId,
            limit: 100,
          );
      if (!mounted) return;
      setState(() {
        _notifications
          ..clear()
          ..addAll(events.where((e) => !_excludedTypes.contains(e.eventType)));
        _loading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = 'Failed to load notifications.';
        _loading = false;
      });
    }
  }

  Future<void> _clearAll() async {
    final confirm = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Clear all notifications?'),
        content: const Text(
            'This removes all non-protected events from the server. '
            'Security/audit events will be kept.'),
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
            child: const Text('Clear All'),
          ),
        ],
      ),
    );
    if (confirm != true || !mounted) return;

    try {
      final deletable = _notifications
          .where((e) => !EventService.isProtectedType(e.eventType))
          .map((e) => e.id)
          .toList();
      if (deletable.isNotEmpty) {
        await ref.read(eventServiceProvider).deleteEvents(deletable);
      }
      await _load();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
              content: Text('Cleared ${deletable.length} notification(s).')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Clear failed: $e')),
        );
      }
    }
  }

  void _markAllRead() {
    ref.read(_lastViewedProvider.notifier).state = DateTime.now().toUtc();
    setState(() => _lastViewed = DateTime.now().toUtc());
  }

  Future<void> _clearSingle(ActivityEvent event) async {
    if (EventService.isProtectedType(event.eventType)) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
            content: Text('Protected security event cannot be cleared.')),
      );
      return;
    }
    try {
      await ref.read(eventServiceProvider).deleteEvent(event.id);
      setState(() => _notifications.removeWhere((e) => e.id == event.id));
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Failed: $e')),
        );
      }
    }
  }

  bool _isUnread(ActivityEvent event) {
    if (_lastViewed == null) return true;
    return event.syncedAt.isAfter(_lastViewed!);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final unreadCount = _notifications.where(_isUnread).length;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Notifications'),
        actions: [
          if (unreadCount > 0)
            TextButton.icon(
              icon: const Icon(Icons.done_all, size: 18),
              label: const Text('Mark Read'),
              onPressed: _markAllRead,
            ),
          PopupMenuButton<String>(
            onSelected: (v) {
              if (v == 'clear_all') _clearAll();
            },
            itemBuilder: (_) => [
              const PopupMenuItem(
                  value: 'clear_all', child: Text('Clear All')),
            ],
          ),
        ],
      ),
      body: _buildBody(theme),
    );
  }

  Widget _buildBody(ThemeData theme) {
    if (_loading) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_error != null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.error_outline, size: 48, color: theme.colorScheme.error),
            const SizedBox(height: 12),
            Text(_error!, style: TextStyle(color: theme.colorScheme.error)),
            const SizedBox(height: 8),
            FilledButton.icon(
              onPressed: _load,
              icon: const Icon(Icons.refresh),
              label: const Text('Retry'),
            ),
          ],
        ),
      );
    }
    if (_notifications.isEmpty) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.notifications_none,
                size: 48, color: theme.colorScheme.outline),
            const SizedBox(height: 12),
            Text('No notifications yet.',
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: theme.colorScheme.outline)),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.builder(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        itemCount: _notifications.length,
        itemBuilder: (_, i) =>
            _NotificationTile(
              event: _notifications[i],
              isUnread: _isUnread(_notifications[i]),
              onClear: () => _clearSingle(_notifications[i]),
            ),
      ),
    );
  }
}

class _NotificationTile extends StatelessWidget {
  final ActivityEvent event;
  final bool isUnread;
  final VoidCallback onClear;

  const _NotificationTile({
    required this.event,
    required this.isUnread,
    required this.onClear,
  });

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final sevColor = EventTypes.severityColor(event.severity);
    final dateFmt = DateFormat('d MMM yyyy');
    final timeFmt = DateFormat('h:mm:ss a');
    final local = event.timestamp.toLocal();
    final tz = local.timeZoneName;
    final dateStr = dateFmt.format(local);
    final timeStr = '${timeFmt.format(local)} $tz';

    return Dismissible(
      key: ValueKey(event.id),
      direction: DismissDirection.endToStart,
      background: Container(
        alignment: Alignment.centerRight,
        padding: const EdgeInsets.only(right: 20),
        decoration: BoxDecoration(
          color: theme.colorScheme.error.withValues(alpha: 0.12),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Icon(Icons.delete, color: theme.colorScheme.error),
      ),
      confirmDismiss: (_) async {
        if (EventService.isProtectedType(event.eventType)) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Protected event cannot be cleared.')),
          );
          return false;
        }
        return true;
      },
      onDismissed: (_) => onClear(),
      child: Card(
        margin: const EdgeInsets.only(bottom: 6),
        color: isUnread
            ? theme.colorScheme.primaryContainer.withValues(alpha: 0.15)
            : null,
        child: ListTile(
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          leading: Stack(
            children: [
              CircleAvatar(
                backgroundColor: sevColor.withValues(alpha: 0.12),
                child: Icon(EventTypes.icon(event.eventType),
                    size: 20, color: sevColor),
              ),
              if (isUnread)
                Positioned(
                  right: 0,
                  top: 0,
                  child: Container(
                    width: 10,
                    height: 10,
                    decoration: BoxDecoration(
                      color: theme.colorScheme.primary,
                      shape: BoxShape.circle,
                      border: Border.all(
                          color: theme.colorScheme.surface, width: 1.5),
                    ),
                  ),
                ),
            ],
          ),
          title: Text(
            EventTypes.displayName(event.eventType),
            style: theme.textTheme.bodyMedium?.copyWith(
              fontWeight: isUnread ? FontWeight.w600 : FontWeight.w400,
            ),
          ),
          subtitle: Text(
            '$dateStr\n$timeStr · ${timeago.format(local)}',
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          ),
          trailing: EventService.isProtectedType(event.eventType)
              ? Icon(Icons.shield, size: 16, color: theme.colorScheme.outline)
              : null,
        ),
      ),
    );
  }
}
