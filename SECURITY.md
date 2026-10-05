# Security Policy

## Authorized Use Only

Laptop Guardian is a **personal device monitoring and management tool**. It must only be installed and operated on laptops you own or are explicitly authorized to manage. Unauthorized installation on someone else's device is a violation of applicable laws.

## Reporting Security Vulnerabilities

If you discover a security vulnerability, please report it through one of the following channels:

- **GitHub Issues**: Open an issue with the label `security` at the project repository.
- **Email**: Contact the maintainer directly at the email listed in the repository profile.

Please include steps to reproduce the issue and any relevant details about the environment. We will acknowledge reports within 72 hours and aim to provide a fix or mitigation plan promptly.

## What This Project Does NOT Do

Laptop Guardian is designed with strict boundaries. The software **never**:

- Logs keystrokes
- Captures or stores passwords
- Steals credentials or tokens from other applications
- Executes arbitrary shell commands from remote input
- Bypasses User Account Control (UAC)
- Disables or bypasses Windows Defender or other security software
- Activates the camera silently or without user-visible indication
- Creates covert persistence mechanisms intended to evade security controls

## Permitted Remote Operations

Remote device management is limited to a fixed set of operations:

- **Lock** the workstation
- **Sleep** the machine
- **Restart** the machine
- **Shutdown** the machine

These are predefined commands with no user-supplied parameters. There is no mechanism for arbitrary command execution.

## Remote Access Requirements

Every remote management action requires all of the following:

1. **Authenticated user** — signed in through Supabase Auth.
2. **Trusted paired device** — the Android companion app must complete the device pairing flow.
3. **Authorized Windows PC** — the target laptop must be registered and paired to the same user account.
4. **Valid remote session** — an active, authenticated session between the paired mobile device and the Windows agent.

If any of these conditions is not met, the request is rejected.

## Camera Access

The remote camera feature operates under explicit consent and visibility:

- The camera stream starts **only** when the user presses the **Start Camera** button in the Android app.
- The stream stops automatically on disconnect, user logout, or device-pairing revocation.
- There is no background or silent camera activation.

## Secrets Management

- All secrets (Supabase keys, Firebase credentials, API tokens) are loaded from **environment variables** at runtime.
- No credentials are hard-coded in source code.
- The `.gitignore` is configured to exclude `.env` files, key files, and other sensitive material.

## Local PIN Storage

The optional device PIN is protected using **Windows DPAPI** (Data Protection API), which encrypts the value under the current Windows user's credentials. The plaintext PIN is never written to disk.

## Data Isolation

All user data stored in Supabase is protected by **Row Level Security (RLS)** policies. Each user can only read and write their own device data. Service-role keys are never exposed to client applications.
