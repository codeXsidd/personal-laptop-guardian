import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../providers/auth_provider.dart';
import '../../providers/device_provider.dart';
import '../../providers/event_provider.dart';
import '../../providers/notification_provider.dart';
import '../../providers/pin_provider.dart';
import 'pin_setup_screen.dart';

class SettingsScreen extends ConsumerWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final profileAsync = ref.watch(userProfileProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          profileAsync.when(
            loading: () => const Center(child: CircularProgressIndicator()),
            error: (_, _) => const SizedBox.shrink(),
            data: (profile) {
              if (profile == null) return const SizedBox.shrink();
              return Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Row(
                    children: [
                      CircleAvatar(
                        radius: 24,
                        backgroundColor: theme.colorScheme.primaryContainer,
                        child: Text(
                          profile.fullName.isNotEmpty
                              ? profile.fullName[0].toUpperCase()
                              : '?',
                          style: TextStyle(
                              color: theme.colorScheme.primary,
                              fontWeight: FontWeight.bold,
                              fontSize: 20),
                        ),
                      ),
                      const SizedBox(width: 16),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(profile.fullName,
                                style: theme.textTheme.titleMedium
                                    ?.copyWith(fontWeight: FontWeight.w600)),
                            Text(profile.email,
                                style: theme.textTheme.bodySmall?.copyWith(
                                    color: theme.colorScheme.onSurfaceVariant)),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
          const SizedBox(height: 16),
          _buildNotificationsCard(context, ref),
          const SizedBox(height: 16),
          _buildPinCard(context, ref),
          const SizedBox(height: 16),
          _buildDataManagementCard(context, ref),
          const SizedBox(height: 16),
          Card(
            child: Column(
              children: [
                ListTile(
                  leading: const Icon(Icons.info_outline),
                  title: const Text('About'),
                  subtitle: const Text('Laptop Guardian v1.1.0'),
                ),
                const Divider(height: 1),
                ListTile(
                  leading: Icon(Icons.logout, color: theme.colorScheme.error),
                  title: Text('Sign Out',
                      style: TextStyle(color: theme.colorScheme.error)),
                  onTap: () async {
                    final confirm = await showDialog<bool>(
                      context: context,
                      builder: (ctx) => AlertDialog(
                        title: const Text('Sign Out'),
                        content: const Text(
                            'Are you sure you want to sign out?'),
                        actions: [
                          TextButton(
                            onPressed: () => Navigator.pop(ctx, false),
                            child: const Text('Cancel'),
                          ),
                          FilledButton(
                            onPressed: () => Navigator.pop(ctx, true),
                            child: const Text('Sign Out'),
                          ),
                        ],
                      ),
                    );
                    if (confirm == true && context.mounted) {
                      try {
                        await ref.read(notificationServiceProvider).unregisterCurrentToken();
                      } catch (e) {
                        debugPrint('[Settings] Token unregister error: $e');
                      }
                      await ref.read(authServiceProvider).signOut();
                      if (context.mounted) context.go('/login');
                    }
                  },
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildPinCard(BuildContext context, WidgetRef ref) {
    final pinEnabled = ref.watch(isPinEnabledProvider);

    return pinEnabled.when(
      loading: () => const SizedBox.shrink(),
      error: (_, _) => const SizedBox.shrink(),
      data: (enabled) {
        return Card(
          child: Column(
            children: [
              ListTile(
                leading: const Icon(Icons.lock_outline),
                title: const Text('App PIN Lock'),
                subtitle: Text(enabled ? 'Enabled' : 'Disabled'),
                trailing: Switch(
                  value: enabled,
                  onChanged: (value) async {
                    if (value) {
                      Navigator.of(context).push(
                        MaterialPageRoute(
                          builder: (_) => const PinSetupScreen(),
                        ),
                      );
                    } else {
                      await ref.read(pinServiceProvider).removePin();
                      ref.invalidate(isPinEnabledProvider);
                      ref.invalidate(isPinSetProvider);
                    }
                  },
                ),
              ),
              if (enabled) ...[
                const Divider(height: 1),
                ListTile(
                  leading: const Icon(Icons.edit),
                  title: const Text('Change PIN'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () {
                    Navigator.of(context).push(
                      MaterialPageRoute(
                        builder: (_) => const PinSetupScreen(isChange: true),
                      ),
                    );
                  },
                ),
              ],
            ],
          ),
        );
      },
    );
  }

  Widget _buildDataManagementCard(BuildContext context, WidgetRef ref) {
    final devicesAsync = ref.watch(devicesProvider);
    return devicesAsync.when(
      loading: () => const SizedBox.shrink(),
      error: (_, _) => const SizedBox.shrink(),
      data: (devices) {
        if (devices.isEmpty) return const SizedBox.shrink();
        return Card(
          child: Column(
            children: [
              ListTile(
                leading: const Icon(Icons.delete_sweep_outlined),
                title: const Text('Data Management'),
                subtitle: const Text('Delete old activity logs'),
              ),
              const Divider(height: 1),
              for (final device in devices)
                ListTile(
                  title: Text(device.displayName),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => _showRetentionDialog(context, ref, device.id, device.displayName),
                ),
            ],
          ),
        );
      },
    );
  }

  Future<void> _showRetentionDialog(
    BuildContext context,
    WidgetRef ref,
    String deviceId,
    String deviceName,
  ) async {
    final options = <(String label, int days)>[
      ('Older than 7 days', 7),
      ('Older than 30 days', 30),
      ('Older than 90 days', 90),
      ('Older than 1 year', 365),
    ];

    final selected = await showDialog<int>(
      context: context,
      builder: (ctx) => SimpleDialog(
        title: Text('Delete logs — $deviceName'),
        children: [
          for (final opt in options)
            SimpleDialogOption(
              onPressed: () => Navigator.pop(ctx, opt.$2),
              child: Text(opt.$1),
            ),
          SimpleDialogOption(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Cancel'),
          ),
        ],
      ),
    );

    if (selected == null || !context.mounted) return;

    final confirm = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Confirm deletion'),
        content: Text(
          'Delete non-security events older than $selected days for $deviceName?\n\n'
          'Security/audit events (logins, logouts, startups, shutdowns) will be preserved.',
        ),
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

    if (confirm != true || !context.mounted) return;

    try {
      final cutoff = DateTime.now().toUtc().subtract(Duration(days: selected));
      final deleted = await ref
          .read(eventServiceProvider)
          .deleteEventsOlderThan(deviceId, cutoff);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Deleted $deleted old event(s).')),
        );
      }
    } catch (e) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Delete failed: $e')),
        );
      }
    }
  }

  Widget _buildNotificationsCard(BuildContext context, WidgetRef ref) {
    final devicesAsync = ref.watch(devicesProvider);
    return devicesAsync.when(
      loading: () => const SizedBox.shrink(),
      error: (_, _) => const SizedBox.shrink(),
      data: (devices) {
        if (devices.isEmpty) return const SizedBox.shrink();
        return Card(
          child: Column(
            children: [
              for (var i = 0; i < devices.length; i++) ...[
                if (i > 0) const Divider(height: 1),
                ListTile(
                  leading: const Icon(Icons.notifications_outlined),
                  title: Text('Notifications — ${devices[i].displayName}'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => context.push(
                      '/settings/notifications/${devices[i].id}'),
                ),
              ],
            ],
          ),
        );
      },
    );
  }
}
