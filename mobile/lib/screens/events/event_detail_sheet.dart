import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../models/event.dart';
import '../../models/event_type.dart';

void showEventDetail(BuildContext context, ActivityEvent event) {
  showModalBottomSheet(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
    ),
    builder: (_) => _EventDetailSheet(event: event),
  );
}

class _EventDetailSheet extends StatelessWidget {
  final ActivityEvent event;

  const _EventDetailSheet({required this.event});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final sevColor = EventTypes.severityColor(event.severity);
    final fmt = DateFormat('yyyy-MM-dd HH:mm:ss');

    return DraggableScrollableSheet(
      initialChildSize: 0.5,
      minChildSize: 0.3,
      maxChildSize: 0.85,
      expand: false,
      builder: (context, scrollController) {
        return ListView(
          controller: scrollController,
          padding: const EdgeInsets.all(20),
          children: [
            Center(
              child: Container(
                width: 32,
                height: 4,
                decoration: BoxDecoration(
                  color: theme.colorScheme.outline.withValues(alpha: 0.3),
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
            ),
            const SizedBox(height: 16),
            Row(
              children: [
                Icon(EventTypes.icon(event.eventType),
                    size: 28, color: sevColor),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(EventTypes.displayName(event.eventType),
                      style: theme.textTheme.titleLarge
                          ?.copyWith(fontWeight: FontWeight.bold)),
                ),
                Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                  decoration: BoxDecoration(
                    color: sevColor.withValues(alpha: 0.12),
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Text(event.severity.toUpperCase(),
                      style: TextStyle(
                          color: sevColor,
                          fontWeight: FontWeight.w600,
                          fontSize: 12)),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(fmt.format(event.timestamp.toLocal()),
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.outline)),
            const SizedBox(height: 16),
            const Divider(),
            const SizedBox(height: 8),
            Text('Details',
                style: theme.textTheme.titleSmall
                    ?.copyWith(fontWeight: FontWeight.w600)),
            const SizedBox(height: 8),
            ...event.payload.entries.map((e) => Padding(
                  padding: const EdgeInsets.symmetric(vertical: 4),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      SizedBox(
                        width: 120,
                        child: Text(_formatKey(e.key),
                            style: theme.textTheme.bodySmall?.copyWith(
                                color: theme.colorScheme.onSurfaceVariant,
                                fontWeight: FontWeight.w500)),
                      ),
                      Expanded(
                        child: Text('${e.value}',
                            style: theme.textTheme.bodySmall),
                      ),
                    ],
                  ),
                )),
            const SizedBox(height: 16),
            const Divider(),
            const SizedBox(height: 8),
            _InfoRow('Event ID', event.id),
            _InfoRow('Synced at', fmt.format(event.syncedAt.toLocal())),
          ],
        );
      },
    );
  }

  String _formatKey(String key) {
    return key.replaceAll('_', ' ').replaceAllMapped(
        RegExp(r'(^|\s)\w'),
        (m) => m.group(0)!.toUpperCase());
  }
}

class _InfoRow extends StatelessWidget {
  final String label;
  final String value;

  const _InfoRow(this.label, this.value);

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 120,
            child: Text(label,
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.outline)),
          ),
          Expanded(
            child: Text(value,
                style: theme.textTheme.bodySmall),
          ),
        ],
      ),
    );
  }
}
