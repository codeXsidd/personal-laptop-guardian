import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../models/notification_settings.dart';
import '../../providers/notification_provider.dart';

class NotificationSettingsScreen extends ConsumerStatefulWidget {
  final String deviceId;

  const NotificationSettingsScreen({super.key, required this.deviceId});

  @override
  ConsumerState<NotificationSettingsScreen> createState() =>
      _NotificationSettingsScreenState();
}

class _NotificationSettingsScreenState
    extends ConsumerState<NotificationSettingsScreen> {
  final _categoryStates = <String, _CategoryState>{};
  bool _saving = false;

  static const _severityLevels = ['info', 'low', 'medium', 'high', 'critical'];

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final settingsAsync =
        ref.watch(notificationSettingsProvider(widget.deviceId));

    return Scaffold(
      appBar: AppBar(title: const Text('Notification Settings')),
      body: settingsAsync.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.error_outline, size: 48, color: theme.colorScheme.error),
              const SizedBox(height: 12),
              Text('Failed to load settings',
                  style: theme.textTheme.bodyMedium),
              const SizedBox(height: 8),
              FilledButton(
                onPressed: () => ref.invalidate(
                    notificationSettingsProvider(widget.deviceId)),
                child: const Text('Retry'),
              ),
            ],
          ),
        ),
        data: (settings) {
          _initCategoryStates(settings);
          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Text('Choose which events trigger push notifications.',
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
              const SizedBox(height: 16),
              for (final cat
                  in NotificationSetting.categories.values) ...[
                _buildCategoryCard(theme, cat),
                const SizedBox(height: 12),
              ],
            ],
          );
        },
      ),
    );
  }

  void _initCategoryStates(List<NotificationSetting> settings) {
    if (_categoryStates.isNotEmpty) return;
    for (final cat in NotificationSetting.categories.values) {
      final matching = settings
          .where((s) => cat.eventTypes.contains(s.eventType))
          .toList();
      final enabled =
          matching.isEmpty || matching.any((s) => s.enabled);
      final severity = matching.isNotEmpty
          ? matching.first.minSeverity
          : cat.defaultMinSeverity;
      _categoryStates[cat.key] = _CategoryState(
        enabled: enabled,
        minSeverity: severity,
      );
    }
  }

  Widget _buildCategoryCard(ThemeData theme, NotificationCategory cat) {
    final state = _categoryStates[cat.key]!;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: Text(cat.label,
                  style: theme.textTheme.titleSmall
                      ?.copyWith(fontWeight: FontWeight.w600)),
              subtitle: Text(
                cat.eventTypes.join(', '),
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
              ),
              value: state.enabled,
              onChanged: (val) => _toggleCategory(cat, val),
            ),
            if (state.enabled) ...[
              const SizedBox(height: 4),
              Text('Minimum severity', style: theme.textTheme.labelMedium),
              const SizedBox(height: 4),
              Wrap(
                spacing: 6,
                children: _severityLevels.map((s) {
                  return ChoiceChip(
                    label: Text(s[0].toUpperCase() + s.substring(1)),
                    selected: state.minSeverity == s,
                    onSelected: (_) => _setSeverity(cat, s),
                    visualDensity: VisualDensity.compact,
                  );
                }).toList(),
              ),
            ],
          ],
        ),
      ),
    );
  }

  void _toggleCategory(NotificationCategory cat, bool enabled) {
    setState(() {
      _categoryStates[cat.key] = _CategoryState(
        enabled: enabled,
        minSeverity: _categoryStates[cat.key]!.minSeverity,
      );
    });
    _saveCategorySettings(cat);
  }

  void _setSeverity(NotificationCategory cat, String severity) {
    setState(() {
      _categoryStates[cat.key] = _CategoryState(
        enabled: _categoryStates[cat.key]!.enabled,
        minSeverity: severity,
      );
    });
    _saveCategorySettings(cat);
  }

  Future<void> _saveCategorySettings(NotificationCategory cat) async {
    if (_saving) return;
    setState(() => _saving = true);

    final state = _categoryStates[cat.key]!;
    final service = ref.read(notificationServiceProvider);

    try {
      for (final eventType in cat.eventTypes) {
        await service.upsertSetting(
          deviceId: widget.deviceId,
          eventType: eventType,
          enabled: state.enabled,
          minSeverity: state.minSeverity,
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Failed to save: $e')),
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }
}

class _CategoryState {
  final bool enabled;
  final String minSeverity;
  const _CategoryState({required this.enabled, required this.minSeverity});
}
