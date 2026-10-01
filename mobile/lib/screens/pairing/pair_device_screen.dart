import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../providers/device_provider.dart';
import '../../providers/pairing_provider.dart';
import '../../services/pairing_service.dart';

class PairDeviceScreen extends ConsumerStatefulWidget {
  const PairDeviceScreen({super.key});

  @override
  ConsumerState<PairDeviceScreen> createState() => _PairDeviceScreenState();
}

class _PairDeviceScreenState extends ConsumerState<PairDeviceScreen> {
  final _codeController = TextEditingController();
  final _nameController = TextEditingController();
  bool _loading = false;
  String? _error;

  @override
  void dispose() {
    _codeController.dispose();
    _nameController.dispose();
    super.dispose();
  }

  Future<void> _pair() async {
    final code = _codeController.text.trim();
    final name = _nameController.text.trim();
    if (code.length < 6) {
      setState(() => _error = 'Enter the 6-character code from your laptop.');
      return;
    }
    if (name.isEmpty) {
      setState(() => _error = 'Give your device a name.');
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      await ref.read(pairingServiceProvider).pairDevice(
            pairingCode: code,
            deviceName: name,
          );
      ref.invalidate(devicesProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Device paired successfully!')),
        );
        context.go('/devices');
      }
    } on PairingException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } catch (e) {
      if (mounted) {
        setState(() => _error =
            'Could not reach the server. Check your internet connection and try again.');
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Widget _step(ThemeData theme, String number, String text) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 22,
          child: Text(
            number.isEmpty ? '•' : '$number.',
            style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onPrimaryContainer,
                fontWeight: FontWeight.w600),
          ),
        ),
        Expanded(
          child: Text(text,
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: theme.colorScheme.onPrimaryContainer)),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Pair Device'),
        leading: IconButton(
          icon: const Icon(Icons.close),
          onPressed: () => context.go('/devices'),
        ),
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Card(
              color: theme.colorScheme.primaryContainer,
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Icon(Icons.link,
                            size: 28, color: theme.colorScheme.primary),
                        const SizedBox(width: 8),
                        Text('How to pair',
                            style: theme.textTheme.titleMedium?.copyWith(
                                color: theme.colorScheme.onPrimaryContainer,
                                fontWeight: FontWeight.w600)),
                      ],
                    ),
                    const SizedBox(height: 12),
                    _step(theme, '1',
                        'Install & run the Guardian agent on your Windows laptop.'),
                    const SizedBox(height: 6),
                    _step(theme, '2',
                        'Open the agent log at C:\\ProgramData\\LaptopGuardian\\logs to find your 6-character pairing code.'),
                    const SizedBox(height: 6),
                    _step(theme, '3',
                        'Enter the code below and give your device a name.'),
                    const SizedBox(height: 6),
                    _step(theme, '',
                        'Code expires after 10 minutes. The agent will auto-generate a new code when needed.'),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 24),
            if (_error != null) ...[
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: theme.colorScheme.errorContainer,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Text(_error!,
                    style:
                        TextStyle(color: theme.colorScheme.onErrorContainer)),
              ),
              const SizedBox(height: 16),
            ],
            TextFormField(
              controller: _codeController,
              textCapitalization: TextCapitalization.characters,
              maxLength: 6,
              textAlign: TextAlign.center,
              style: theme.textTheme.headlineMedium
                  ?.copyWith(letterSpacing: 8, fontWeight: FontWeight.bold),
              inputFormatters: [
                FilteringTextInputFormatter.allow(RegExp(r'[A-Za-z0-9]')),
                UpperCaseTextFormatter(),
              ],
              decoration: const InputDecoration(
                labelText: 'Pairing Code',
                counterText: '',
              ),
            ),
            const SizedBox(height: 16),
            TextFormField(
              controller: _nameController,
              decoration: const InputDecoration(
                labelText: 'Device Name',
                hintText: 'e.g. My Work Laptop',
                prefixIcon: Icon(Icons.laptop),
              ),
            ),
            const SizedBox(height: 24),
            FilledButton(
              onPressed: _loading ? null : _pair,
              child: _loading
                  ? const SizedBox(
                      height: 20,
                      width: 20,
                      child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Pair Device'),
            ),
          ],
        ),
      ),
    );
  }
}

class UpperCaseTextFormatter extends TextInputFormatter {
  @override
  TextEditingValue formatEditUpdate(
      TextEditingValue oldValue, TextEditingValue newValue) {
    return TextEditingValue(
      text: newValue.text.toUpperCase(),
      selection: newValue.selection,
    );
  }
}
