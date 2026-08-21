# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Stack

The upstream Ente Auth clients remain Flutter. This community fork adds a Windows-only desktop client built with C# 14, .NET 10 LTS, WinUI 3, and the Windows App SDK.

## Users

People who keep many TOTP, HOTP, and Steam authenticator entries and need secure, immediate access from a Windows desktop. The primary workflow is searching for an account and copying its current code with minimal interruption.

## Product Purpose

Provide an unofficial Windows client compatible with Ente Auth's end-to-end encrypted sync and offline data while behaving like a focused native Windows utility. Success means full desktop feature compatibility, reliable migration, and fast tray access without weakening secret protection.

## Positioning

The Windows client combines Ente-compatible encrypted authenticator storage with a native tray-first workflow. It remains a permanent community fork with a separate identity, data directory, update channel, and issue tracker.

## Operating Context

Users open the full app to add, organize, import, export, and manage authenticator entries. They use a compact tray surface for the frequent search-and-copy workflow. Online accounts synchronize encrypted entities through Ente; offline accounts remain local and migrate through encrypted backups.

## Capabilities and Constraints

- Windows 10 version 1809 or newer and Windows 11 only.
- Preserve Ente account, encryption, sync, offline, import/export, organization, lock, recovery, localization, theme, and update behavior before production release.
- Provide three launch modes: show window, start minimized, and tray only, plus a separate launch-at-sign-in setting.
- Tray code access always requires Windows Hello and fails closed when verification is unavailable or cancelled.
- The tray surface supports pinned and recent codes, search, countdown state, and one-click copy; editing and configuration stay in the full app.
- Do not access or overwrite the official client's secure storage. Online users re-sync; offline users import an encrypted backup.
- Preserve Ente wire formats and cryptographic compatibility. Cross-language vectors are a release gate.

## Brand Commitments

Use the provisional display name "Ente Auth Community" and clearly label it as an unofficial community fork. Preserve Ente's recognizable purple accent and product terminology without implying endorsement.

## Evidence on Hand

- The upstream Flutter implementation under `mobile/apps/auth/` is the behavioral compatibility oracle.
- `architecture/README.md` defines Ente's encryption model and primitives.
- Existing assets, import fixtures, localization strings, database models, and API clients may be reused under the repository license.
- No official endorsement, performance claims, security audit, signing identity, or release certificate is available and none may be implied.

## Product Principles

- Secrets stay protected even when convenience features fail.
- The common search-and-copy path is immediate and uncluttered.
- Compatibility is proven with fixtures and contracts, not assumed from similar algorithms.
- Native Windows behavior should feel predictable: one instance, honest taskbar presence, explicit quit, and accessible keyboard operation.
- The community client coexists safely with the official app.

## Accessibility & Inclusion

Support keyboard-only use, screen readers, visible focus, text scaling, Windows high contrast, reduced motion, non-color state cues, and clear recovery messages for biometric, sync, import, and clipboard failures.
