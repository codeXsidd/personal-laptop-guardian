import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../providers/pin_provider.dart';

class PinLockScreen extends ConsumerStatefulWidget {
  final VoidCallback onUnlocked;

  const PinLockScreen({super.key, required this.onUnlocked});

  @override
  ConsumerState<PinLockScreen> createState() => _PinLockScreenState();
}

class _PinLockScreenState extends ConsumerState<PinLockScreen> {
  String _pin = '';
  String? _error;
  int _attempts = 0;

  void _addDigit(String digit) {
    if (_pin.length >= 8) return;
    setState(() {
      _pin += digit;
      _error = null;
    });
    if (_pin.length >= 4) _tryUnlock();
  }

  void _deleteDigit() {
    if (_pin.isEmpty) return;
    setState(() {
      _pin = _pin.substring(0, _pin.length - 1);
      _error = null;
    });
  }

  Future<void> _tryUnlock() async {
    final ok = await ref.read(pinServiceProvider).verifyPin(_pin);
    if (ok) {
      ref.read(appLockedProvider.notifier).state = false;
      widget.onUnlocked();
    } else {
      _attempts++;
      setState(() {
        _pin = '';
        _error = _attempts >= 5
            ? 'Too many attempts ($_attempts)'
            : 'Incorrect PIN';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      body: SafeArea(
        child: Column(
          children: [
            const Spacer(),
            Icon(
              Icons.shield_outlined,
              size: 64,
              color: theme.colorScheme.primary,
            ),
            const SizedBox(height: 16),
            Text(
              'Laptop Guardian',
              style: theme.textTheme.headlineSmall?.copyWith(
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 8),
            Text(
              'Enter your PIN to unlock',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 32),
            // PIN dots
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: List.generate(8, (i) {
                return Container(
                  margin: const EdgeInsets.symmetric(horizontal: 6),
                  width: 16,
                  height: 16,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: i < _pin.length
                        ? theme.colorScheme.primary
                        : theme.colorScheme.surfaceContainerHighest,
                    border: Border.all(
                      color: i < _pin.length
                          ? theme.colorScheme.primary
                          : theme.colorScheme.outline,
                    ),
                  ),
                );
              }),
            ),
            if (_error != null) ...[
              const SizedBox(height: 12),
              Text(
                _error!,
                style: TextStyle(
                  color: theme.colorScheme.error,
                  fontWeight: FontWeight.w500,
                ),
              ),
            ],
            const Spacer(),
            // Number pad
            _buildNumPad(theme),
            const SizedBox(height: 32),
          ],
        ),
      ),
    );
  }

  Widget _buildNumPad(ThemeData theme) {
    return Column(
      children: [
        for (var row in [
          ['1', '2', '3'],
          ['4', '5', '6'],
          ['7', '8', '9'],
          ['', '0', 'del']
        ])
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 6),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: row.map((key) {
                if (key.isEmpty) {
                  return const SizedBox(width: 80, height: 64);
                }
                if (key == 'del') {
                  return SizedBox(
                    width: 80,
                    height: 64,
                    child: InkWell(
                      borderRadius: BorderRadius.circular(32),
                      onTap: _deleteDigit,
                      child: const Center(
                        child: Icon(Icons.backspace_outlined, size: 24),
                      ),
                    ),
                  );
                }
                return SizedBox(
                  width: 80,
                  height: 64,
                  child: InkWell(
                    borderRadius: BorderRadius.circular(32),
                    onTap: () => _addDigit(key),
                    child: Center(
                      child: Text(
                        key,
                        style: theme.textTheme.headlineSmall?.copyWith(
                          fontWeight: FontWeight.w500,
                        ),
                      ),
                    ),
                  ),
                );
              }).toList(),
            ),
          ),
      ],
    );
  }
}
