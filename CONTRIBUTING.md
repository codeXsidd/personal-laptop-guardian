# Contributing to Laptop Guardian

Thank you for your interest in contributing. This guide covers the prerequisites, build steps, testing, and rules for pull requests.

## Prerequisites

| Tool | Version | Link |
|------|---------|------|
| .NET SDK | 10.0+ | https://dotnet.microsoft.com/download |
| Flutter SDK | 3.13+ (Dart 3.13+) | https://docs.flutter.dev/get-started/install |
| Android Studio | Latest | https://developer.android.com/studio |
| Supabase CLI | Latest | https://supabase.com/docs/guides/cli |
| Node.js | 18+ (for Supabase functions) | https://nodejs.org |

## Repository layout

```
windows-agent/          C# / .NET 10
  src/
    LaptopGuardian.Agent/       Windows Worker Service
    LaptopGuardian.Desktop/     WPF desktop app
  tests/
    LaptopGuardian.Agent.Tests/
    LaptopGuardian.Desktop.Tests/
mobile/                 Flutter / Dart Android app
backend/
  supabase/
    functions/          Supabase Edge Functions (TypeScript)
    migrations/         PostgreSQL migrations
    tests/              SQL-based schema tests
```

## Building

### Windows Agent and Desktop

```bash
cd windows-agent
dotnet build
```

To publish the agent as a self-contained executable:

```bash
dotnet publish src/LaptopGuardian.Agent/LaptopGuardian.Agent.csproj -c Release
```

### Mobile app

```bash
cd mobile
flutter pub get
flutter build apk
```

### Supabase Edge Functions

```bash
cd backend
supabase start          # local Supabase instance
supabase functions serve # serve functions locally
```

## Running tests

### .NET tests

```bash
cd windows-agent
dotnet test
```

### Flutter tests

```bash
cd mobile
dart analyze            # static analysis
flutter test            # unit and widget tests
```

### Supabase schema tests

```bash
cd backend
supabase test db
```

## Code style

- **Dependency injection** -- register services through DI; avoid `new`-ing dependencies directly.
- **Interfaces** -- define interfaces for services so they can be mocked in tests.
- **Structured logging** -- use `ILogger<T>` (C#) or the project logger (Dart). No `Console.WriteLine` or `print` in production code.
- **Configuration** -- use `appsettings.json` / environment variables. Never hard-code connection strings or feature flags.
- **Small classes** -- prefer focused classes with a single responsibility.
- **No magic numbers** -- extract constants with descriptive names.

## Pull request guidelines

1. **One feature or fix per PR.** Keep changes focused and reviewable.
2. **Tests required.** Every new feature or bug fix must include relevant tests. PRs that lower test coverage will be asked for additions.
3. **Build must pass.** Run `dotnet build` and/or `flutter build apk` locally before opening the PR.
4. **Tests must pass.** Run `dotnet test` and/or `flutter test` locally before opening the PR.
5. **No secrets.** Triple-check that API keys, tokens, and credentials are not included. Use environment variables or `.env` files (which are gitignored).
6. **Describe the change.** Write a clear PR title and description explaining what changed and why.
7. **Keep commits clean.** Use descriptive commit messages. Squash fixup commits before requesting review.

## Security rules

These rules are non-negotiable. PRs that violate them will be closed immediately.

- **Never commit secrets.** No API keys, Supabase service-role keys, Firebase private keys, or any credentials in source code.
- **Never add keylogging.** The agent must not capture keystrokes or passwords.
- **Never add arbitrary shell/command execution.** No feature that allows running arbitrary commands on the monitored device.
- **Never create unauthenticated backdoors.** All remote management requires authenticated device pairing.
- **Never bypass security controls.** The agent starts via a legitimate installation, not covert persistence mechanisms.

If you discover a security vulnerability, please report it privately rather than opening a public issue.

## License

By contributing you agree that your contributions will be licensed under the [MIT License](LICENSE).
