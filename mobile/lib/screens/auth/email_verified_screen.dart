import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../providers/auth_provider.dart';

class EmailVerifiedScreen extends ConsumerWidget {
  final String? errorMessage;

  const EmailVerifiedScreen({super.key, this.errorMessage});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final deepState = ref.watch(deepLinkStateProvider);
    final theme = Theme.of(context);

    // Error if: route query param says error, OR deep link state says error
    final isError = errorMessage != null ||
        deepState.result == DeepLinkResult.error;
    final displayMessage = errorMessage ??
        deepState.errorMessage ??
        (isError
            ? 'The confirmation link may have expired or already been used.'
            : 'Your email has been verified successfully.');

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  isError ? Icons.error_outline : Icons.verified_outlined,
                  size: 64,
                  color: isError
                      ? theme.colorScheme.error
                      : theme.colorScheme.primary,
                ),
                const SizedBox(height: 16),
                Text(
                  isError ? 'Verification Failed' : 'Email Verified',
                  style: theme.textTheme.headlineSmall
                      ?.copyWith(fontWeight: FontWeight.bold),
                ),
                const SizedBox(height: 8),
                Text(
                  displayMessage,
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium,
                ),
                const SizedBox(height: 24),
                FilledButton(
                  onPressed: () {
                    ref.read(deepLinkStateProvider.notifier).state =
                        const DeepLinkState();
                    context.go(isError ? '/login' : '/devices');
                  },
                  child: Text(isError ? 'Go to Sign In' : 'Continue'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
