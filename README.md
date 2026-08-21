# Ente Auth Community for Windows

An experimental, unofficial native Windows client for Ente Auth. It is a permanent community fork and is not endorsed by Ente.

## Current implementation

- C# 14 and .NET 10 LTS domain libraries
- WinUI 3 / Windows App SDK desktop shell
- TOTP, HOTP, Steam formatting, and `otpauth://` parsing
- RFC 4226 and RFC 6238 compatibility tests
- SQLite storage with per-user DPAPI protection for OTP secrets
- Windows Hello-gated tray quick panel with search and one-click copy
- Local add, edit, pin, delete, and portable `otpauth` import/export workflows
- Show window, start minimized, and tray-only launch modes
- Tested startup-disposition policy; minimized startup uses WinAppSDK's requested startup state to avoid flashing a restored window first
- Packaged startup-task support and single-instance activation
- Ente authenticator key/entity/diff API contract client
- Ente-compatible libsodium secretstream entity encryption and secretbox authenticator-key wrapping
- DPAPI-protected local authenticator-key cache and get-or-create key lifecycle
- Bidirectional Ente sync engine with pull-before-push ordering, paging, cursors, uploads, updates, and deletion tombstones
- Reproducible Dart-to-.NET and .NET-to-Dart crypto interoperability evidence
- Ente Auth encrypted-backup v1 import/export with bounded Argon2id parameters and Dart fixture coverage
- Ente SRP-6a password login, TOTP continuation, encrypted key recovery, sealed-token opening, and DPAPI session storage
- Account-bound sync metadata that prevents a local vault from being uploaded into a different Ente account
- System theme, high-contrast-compatible native controls, keyboard focus, and reduced decorative motion

## Security status

This is a development build, not a production authenticator. Do not use it with real secrets yet. SRP password login and TOTP two-factor continuation now connect to the sync engine, but passkey-only login, legacy email-code/recovery flows, complete third-party importer parity, staging validation under real conflicts and interruptions, and independent security review remain release blockers. The app deliberately uses a separate package identity and data directory and does not read the official client's secure storage.

## Build

Requirements:

- Windows 10 version 1809 or newer
- Visual Studio 2022 with .NET desktop development and Windows App SDK tooling
- .NET 10 SDK

```powershell
dotnet restore .\EnteAuthCommunity.slnx
dotnet test .\tests\Ente.Auth.Core.Tests\Ente.Auth.Core.Tests.csproj
dotnet test .\tests\Ente.Auth.Infrastructure.Tests\Ente.Auth.Infrastructure.Tests.csproj
dotnet build .\src\Ente.Auth.App\Ente.Auth.App.csproj -p:Platform=x64
```


## Compatibility gates

Before a production release:

1. Complete passkey, legacy email-code, and recovery-key account flows.
2. Keep the committed Dart-to-.NET fixture and repeat the .NET-to-Dart check when either crypto dependency changes.
3. Add every remaining upstream third-party importer.
4. Pass Ente staging, interrupted-sync, account-switch, and conflict tests.
5. Validate Windows Hello, startup, tray, clipboard clearing, screen readers, high contrast, and installer updates on x64 and ARM64 hardware.
6. Complete an independent review of crypto interop, key lifetime, storage, logging, and release artifacts.

The Flutter implementation in `mobile/apps/auth` remains the behavioral compatibility oracle.

## Portable transfer format

Encrypted export produces Ente Auth's version-1 JSON format using Argon2id and XChaCha20-Poly1305 secretstream, and is the recommended transfer path. Import accepts those encrypted JSON backups and fails closed for a wrong password, modified ciphertext, unsupported version, malformed Base64, or hostile KDF limits.

Portable import/export remains available as newline-delimited `otpauth://` links for interoperability with other authenticators. Those files contain unencrypted authentication secrets, so the app shows a separate warning before writing one. Invalid imports are rejected as a whole and report their line numbers rather than leaving a partial import behind.
