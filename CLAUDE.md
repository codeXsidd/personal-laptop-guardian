# Personal Laptop Guardian

## Project purpose

Build a legitimate personal/authorized Windows laptop monitoring and device-management application.

The software must only be used on laptops owned by the user or explicitly authorized by the administrator.

## Primary architecture

Windows Laptop
→ Windows Agent
→ Local SQLite
→ Secure Backend API
→ Supabase
→ Flutter Android App
→ Firebase Cloud Messaging

## Technology

Windows Agent:

* C#
* .NET 10
* Windows Worker Service
* SQLite

Backend:

* Supabase
* PostgreSQL
* Supabase Edge Functions where appropriate

Mobile:

* Flutter
* Android first

Notifications:

* Firebase Cloud Messaging

## Core capabilities

* Laptop startup detection
* Login/logout events
* Application/process activity
* USB/device events
* Network information
* Windows Event Log collection
* File-access auditing for explicitly configured directories
* Offline event storage
* Automatic synchronization when connectivity returns
* Device pairing
* Device online/offline state
* Historical activity
* Phone notifications

## Offline-first requirement

Every important event must first be persisted locally.

If the laptop is offline:

* continue collecting events
* save them in SQLite
* mark them pending

When connectivity returns:

* synchronize pending events
* use event IDs for idempotency
* prevent duplicates
* retry failures
* preserve events across restart/reboot

## Security requirements

Never:

* hard-code credentials
* commit AWS secrets
* commit Supabase service-role keys
* commit Firebase private keys
* collect passwords
* collect keystrokes
* create an unauthenticated backdoor
* create covert persistence intended to bypass security controls

The device agent is allowed to start automatically after a legitimate one-time installation and pairing.

Remote management must require authenticated device pairing.

## File auditing

Do not claim that all historical file access can be reconstructed automatically.

Use Windows auditing for explicitly configured folders.

Clearly document:

* required Windows settings
* permissions
* supported Windows configurations
* limitations

## Engineering requirements

Use:

* dependency injection
* interfaces
* clean separation of concerns
* typed models
* configuration files
* structured logging
* unit tests
* integration tests where practical
* database migrations
* error handling
* retry mechanisms
* cancellation support

Avoid:

* unnecessary complexity
* duplicated code
* huge classes
* hard-coded configuration
* magic numbers

## Development workflow

For every phase:

1. Inspect the repository.
2. Explain the intended changes.
3. Implement only the requested phase.
4. Build the affected projects.
5. Run tests.
6. Fix compilation errors.
7. Fix test failures.
8. Review the implementation.
9. Update documentation.
10. Show exact manual test commands/procedures.
11. Do not claim success without actually building/testing.

## Git discipline

Before every major commit:

* inspect git diff
* inspect changed files
* run tests
* check for secrets
* check `.gitignore`
* check generated files
* check logs/databases are excluded

## Coding philosophy

Prefer a simple reliable implementation over a sophisticated implementation that is difficult to test.

Do not invent undocumented Windows APIs or cloud behavior.

When uncertain about a Windows capability, verify it against official documentation before implementing it.

## Definition of done

A feature is not complete until:

* code compiles
* tests pass
* errors are handled
* documentation is updated
* manual test procedure exists
* offline/online behavior is verified where relevant
* security implications have been reviewed
