## Developer docs

Documentation and notes about more advanced or infrequently needed details.

### Android CLI

See [android-cli.md](android-cli.md) for working on the Android app without installing Android Studio.

### Android “On this day” notification black-screen reproduction

See [android-nonresponsive-reproduction-handoff.md](android-nonresponsive-reproduction-handoff.md)
for the portable handoff and verification protocol for the 2026-08-20
notification-triggered black-screen report. Large-library state and
WorkManager overlap are controlled modifiers in that protocol. Its retail-safe
trace profile is
[android-nonresponsive-perfetto.pbtxt](android-nonresponsive-perfetto.pbtxt),
with frozen analysis queries in
[android-nonresponsive-perfetto.sql](android-nonresponsive-perfetto.sql).

### VS Code

The [vscode](vscode) folder contains template launch configuration. Copy into a top-level `.vscode` to use them.
