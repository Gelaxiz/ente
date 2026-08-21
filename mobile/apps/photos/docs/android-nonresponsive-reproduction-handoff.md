# Android “On this day” notification black-screen reproduction handoff

## Objective

Reproduce and characterize this exact coworker report on Android:

> Clicked on “On this day” notification, black screen, no way to exit apart
> from killing and restarting the app.

This is a diagnostic handoff. The receiving agent must not propose or
implement a product fix. The deliverable is evidence showing which stage
failed and whether the exact user-visible outcome was reproduced.

Do not substitute a generic slow launch, gallery jank, failed media download,
or Android ANR for the reported symptom. The target can be a
responsive-but-stuck black route; an ANR is not required.

## Current diagnostic priors

These are evidence-weighted investigation priors, not measured frequencies:

| Prior | Candidate cause | Why it is plausible | What weakens it |
| ---: | --- | --- | --- |
| **45%** | The real notification cold-start/resume path stalls before or during Flutter navigation/rendering. This includes the native PendingIntent/activity handoff, pre-logging startup, AppLock/inner-navigator readiness, and a UI-isolate stall. | It best explains a black foreground surface plus Android Back not being processed. The failing execution may have stopped before foreground logging or before logs were persisted. On Android 12+ the app's night-mode launch background is black, so a pre-first-frame stall can visually match the report. | A previous-day notification successfully started a fresh foreground engine on the same device while a background engine was active. That is not a process-absent control. |
| **35%** | The selected On-this-day memory becomes invalid or empty as the notification route is opened. | Source contains an exact pure-black/no-controls state: `AllMemoriesPage` stays black while an empty selected memory makes `FullScreenMemoryDataUpdater` and `FullScreenMemory` return `SizedBox.shrink()`. Remote deletion invalidation mutates the shared memory list in place. | With a healthy UI isolate, Android system Back should still pop this route. The logs do not prove that the incident memory became empty. |
| **12%** | The first memory asset is unavailable, corrupt, or stuck downloading/decrypting. | The viewer background is black and remote media is loaded asynchronously. | The viewer title and close control should still render, and Android Back should still work. |
| **8%** | Another Android/renderer/process condition. | The black execution itself was not captured. | There is no fatal, OOM, ANR, or process-exit evidence in the supplied archive. |

The large Library Sharing fan-in and foreground/WorkManager overlap found in
the later logs are now **modifiers**, not leading standalone causes. Test them
only after the real notification path is working in the harness.

## What the logs establish

The source archive is not committed. Transfer it through an approved secure
channel:

```text
ente-logs-2026-Aug-20-3-15.zip
SHA-256: 7a8d95278f962ab63560bf9b7c9db0e173eecd9bad2e3d3500541a75d27a0f26
```

The SHA-256 of the uncompressed `logs/2026-8-20.log` is:

```text
518832b69f4c7ea9be24e4a37ac88824db95cdd448f900e0406a749a1cace7c0
```

Verify the archive on the receiving machine:

```sh
log_archive=/path/to/ente-logs-2026-Aug-20-3-15.zip

# macOS
shasum -a 256 "$log_archive"

# Linux
sha256sum "$log_archive"
```

### Exact notification creation

Build `1.3.62+2242` scheduled the notification on 2026-08-17:

| Log | Evidence |
| --- | --- |
| `2026-8-17.log:6708-6710` | Title `On this day`, message `Look back on your memories 🌄`, channel `onThisDay`, payload `onThisDay_AcvwDGZtfVNWDsLk1oyqoV`, scheduled for 2026-08-20 08:00 device-local time. |
| `2026-8-17.log:6729` | Notification ID `689944494`. |

No later scheduling of that payload appears in the archive. The notification
was created under `+2242` and survived application updates before the
reported incident.

### Successful same-path control on 2026-08-19

The previous day provides a strong positive control:

| Time | Evidence |
| --- | --- |
| 08:19:02.274 | `+2249` starts a fresh foreground engine while a background process/engine is already active. |
| 08:19:02.355 | `BG task is alive, not clearing locks`. |
| 08:19:02.663 | Header makes the normal Home `getMemories called`. |
| 08:19:02.806 | `getNotificationAppLaunchDetails` completes, followed 68 µs later by a **second** `getMemories called`. That second call is the On-this-day tap handler signature. |
| 08:19:03.046 | Memory cache resolves successfully. |
| 08:19:03.050 | Viewer initializes the first asset, `IMG_20140819_083650.jpg`, remote ID `87890`, generated ID `38030`. |
| 08:19:03.439 | `Final image loaded`, about 389 ms after viewer initialization. |

Three adjacent assets began downloading during this successful control:
remote IDs `84927`, `88608`, and `98693`.

This proves that an On-this-day notification created by the older scheduling
build could open successfully after an update, even with a live background ML
task. It weakens “an update alone” and “background work alone” as explanations,
but it does not test a process-absent notification launch.

### The 08:45 session is ambiguous

There are no foreground, notification-response, memory-viewer, crash, ANR, or
process-exit records between the completed 06:48 background task and the 08:45
session on 2026-08-20. The archive cannot determine whether 08:45 is the
coworker’s restart after killing the app or the original tap on a notification
that had been visible for about 45 minutes.

| Time | Evidence |
| --- | --- |
| 08:45:06.202 | WorkManager starts. |
| 08:45:07.844 | `+2251` starts a foreground engine 1.642 seconds later. |
| 08:45:07.851 | Last foreground heartbeat was 2026-08-19 19:57:11. |
| 08:45:08.230-08:45:08.231 | Header builds and makes the single normal Home `getMemories` call. |
| 08:45:08.244 | `getNotificationAppLaunchDetails` completes, but there is no second `getMemories` call and no viewer initialization. |
| 08:45:09.693 | Home loads 84,185 files in 1,498 ms. |
| 08:45:20.896 | Startup ML finishes enumerating 6,716 candidates after 12,430 ms. |

`getNotificationAppLaunchDetails` is called on every foreground-engine start.
Its presence does **not** mean `didNotificationLaunchApp` was true. Current
source would synchronously log a second `getMemories called` when an
On-this-day response is handled; that signature is absent at 08:45.

Preserve both interpretations:

- **If 08:45 is the restart:** its gallery/ML/WorkManager work is post-incident
  and cannot explain the earlier black process.
- **If 08:45 is the notification tap:** the response was not handled by the
  expected Dart path. The session then supports a launch-details/callback or
  pre-first-frame failure, not a successfully pushed memories route. Its
  WorkManager overlap may be a modifier.

Only reporter tap/kill/restart timing can resolve this branch.

### What is not proved

The archive does not establish:

- When the coworker tapped the notification.
- Whether the app process was absent, cached, foreground, or App-Locked then.
- Whether the Android PendingIntent reached Flutter.
- Whether the route was pushed.
- Whether frames continued while the surface was black.
- Whether the coworker tried Android system Back, the close control, or both.
- The exact On-this-day memory and first file selected at tap time.
- The process exit reason or the exact kill/restart time.

The payload does not identify the memory that the handler opens. Current
source only checks that the payload contains `onthisday`, then opens
the **first currently valid** `MemoryType.onThisDay` in the local
cache. The reporter’s pre-tap memories cache and referenced media state are
therefore more valuable than the payload ID alone.

## Source flow to inspect

Paths are relative to the repository root. Search by symbol because the exact
`+2251` source mapping may move line numbers.

| Stage | Path and symbol |
| --- | --- |
| Schedule at 08:00, require at least five items, payload = memory ID | `mobile/apps/photos/lib/services/memories_cache_service.dart` — `_scheduleOnThisDayNotifications` |
| Register warm and cold notification callbacks | `mobile/apps/photos/lib/services/notification_service.dart` — `initialize`, `_handleLaunchDetailsIfNeeded` |
| Register callback during Home startup and classify `onthisday` payload | `mobile/apps/photos/lib/ui/tabs/home_widget.dart` — `initState`, `_onDidReceiveNotificationResponse` |
| Read current cache, choose first On-this-day memory, push viewer | `mobile/apps/photos/lib/services/memories_cache_service.dart` — `getMemories`, `goToOnThisDayMemory`, `_routeToPage` |
| Mutate cached SmartMemory lists after deletions | Same file — `_invalidateDeletedFiles`, `_removeDeletedFilesFromSmartMemory` |
| Wait for the inner Photos navigator, then push a custom route | `mobile/apps/photos/lib/services/app_navigation_service.dart` — `pushPage`, `_waitForNavigator` |
| Always-black full-screen parent | `mobile/apps/photos/lib/ui/home/memories/all_memories_page.dart` — `AllMemoriesPage.build` |
| Empty-list shrink state, black viewer, close action | `mobile/apps/photos/lib/ui/home/memories/full_screen_memory.dart` — `FullScreenMemoryDataUpdater.build`, `FullScreenMemory.build`, `_MemoryTopOverlay` |
| Pre-foreground-log awaits | `mobile/apps/photos/lib/main.dart` — `main` before `_runInForeground` |
| Android 12+ light/night launch backgrounds | `mobile/apps/photos/android/app/src/main/res/values-v31/styles.xml`, `values-night-v31/styles.xml` — `LaunchTheme` |
| Large-library candidate enumeration | `mobile/apps/photos/lib/utils/ml_util.dart` — `_getOnlineFilesForMlIndexingCandidates` |
| Background task/foreground heartbeat logic | `mobile/apps/photos/lib/main.dart` — `runBackgroundTask`; `mobile/apps/photos/lib/services/process_activity.dart` |

The current checkout is an analysis reference, not a proven build mapping:

```sh
git clone https://github.com/ente-io/ente.git
cd ente
git checkout efb17776c64c78a55693ccf6e8d2bcc9e1443cc6
```

Its Photos pubspec build number is `2158`; CI derived the distributed
build numbers separately. Obtain the source commit and artifact for
`1.3.62+2251` before using source timing or a negative result as
release-exact evidence.

## Package to transfer to the receiving agent

### Readiness status

This repository change is a reproduction specification, not yet a runnable
execution bundle. It contains the evidence, experiment design, trace profile,
analysis queries, and artifact contracts. High-fidelity scored work is
**blocked** until the handoff owner supplies:

- the signed `+2251` artifact and source/CI mapping;
- the signed `+2242` scheduling artifact and, for an incident-exact negative,
  the available signed artifacts from the intervening update chain;
- a dedicated device or reproducible device/backend fixture;
- the synthetic On-this-day cache/media manifest and reset procedure;
- reporter timing, Back/close attempts, App Lock, phone-lock, and theme state,
  if obtainable; and
- an on-device UIAutomator/UiAutomator2 harness implementing the marker and
  selector contract below, with its build and run command.

Every unresolved item is represented by a manifest placeholder. A receiving
agent may perform exploratory manual trials before those items arrive, but
must report them as inconclusive and must not claim a high-fidelity negative
result. The Perfetto SQL expects `ENTE_REPRO` markers from the declared
harness; a look-alike host-only timestamp is not an equivalent scored input
clock.

Transfer:

- This document.
- `mobile/apps/photos/docs/android-nonresponsive-perfetto.pbtxt`.
- `mobile/apps/photos/docs/android-nonresponsive-perfetto.sql`.
- The verified log archive above.
- The exact signed `1.3.62+2251` APK/APKS/Play track and its source
  mapping.
- The signed `+2242` artifact used to create the scheduled notification and,
  for the incident-exact cohort, the available intervening signed builds.
- A restorable synthetic fixture and the completed manifest below.

Companion-file hashes at the time this handoff was written:

```text
android-nonresponsive-perfetto.pbtxt
99e3a302e0943c1749af78c6277772a144ec622b7b5cb684c317f0f1f42585ba

android-nonresponsive-perfetto.sql
54c416fac7eaad69d143fbe1a8fb221b14f384e2380002b401b52002c9050690
```

### Execution-package manifest

The handoff owner must replace every placeholder. Put credentials in the lab
secret manager, not this file.

```yaml
execution_mode: REPLACE_WITH_EXACT_PRODUCTION_OR_STAGING_RELEASE
backend_base_url: REPLACE_WITH_URL
backend_build_or_snapshot: REPLACE_WITH_ID
allowed_server_mutations: REPLACE_WITH_POLICY

artifact_2251_location: REPLACE_WITH_SECURE_LOCATION
artifact_2251_sha256: REPLACE_WITH_SHA256
artifact_2242_location: REPLACE_WITH_SECURE_LOCATION_OR_UNAVAILABLE
artifact_2242_sha256: REPLACE_WITH_SHA256_OR_UNAVAILABLE
observed_update_chain: 2242_TO_2244_TO_2245_TO_2249_TO_2250_TO_2251
update_chain_artifact_manifest: REPLACE_WITH_LOCATION_HASHES_AND_INSTALL_ORDER
application_id: io.ente.photos
version_under_test: 1.3.62+2251
source_commit_2251: REPLACE_WITH_CI_MAPPING
signing_certificate_sha256: REPLACE_WITH_FINGERPRINT
install_or_update_commands: REPLACE_WITH_EXACT_COMMANDS

device_model: Nothing A024
android_version: "16"
device_ram_mb: 11200
device_build_fingerprint: REPLACE_WITH_FINGERPRINT
display_width_height_density: REPLACE_WITH_VALUES
navigation_mode: REPLACE_WITH_GESTURE_OR_3_BUTTON
device_timezone: Asia/Kolkata
device_locale: REPLACE_WITH_LOCALE
ente_locale: REPLACE_WITH_LOCALE
android_theme_mode: REPLACE_WITH_LIGHT_OR_DARK
ente_theme_mode: REPLACE_WITH_LIGHT_DARK_OR_SYSTEM
automatic_date_time_state: REPLACE_WITH_STATE

fixture_owner: REPLACE_WITH_NAME_OR_TEAM
fixture_runbook: REPLACE_WITH_LOCATION
account_ids_non_secret: REPLACE_WITH_IDS
credential_secret_refs: REPLACE_WITH_SECRET_MANAGER_REFERENCES
backend_snapshot_small: REPLACE_WITH_ID
backend_snapshot_large: REPLACE_WITH_ID
device_snapshot_small: REPLACE_WITH_ID
device_snapshot_large: REPLACE_WITH_ID
on_this_day_fixture_manifest: REPLACE_WITH_LOCATION_AND_SHA256
pre_tap_memories_cache_manifest: REPLACE_WITH_LOCATION_AND_SHA256
first_memory_id: REPLACE_WITH_ID
first_file_ids_and_state: REPLACE_WITH_MANIFEST
deletion_invalidation_runbook: REPLACE_WITH_LOCATION

notification_title: On this day
notification_channel: onThisDay
reference_payload: onThisDay_AcvwDGZtfVNWDsLk1oyqoV
reference_notification_id: 689944494
notification_schedule_build: REPLACE_WITH_2242_OR_2251
notification_delivery_time: REPLACE_WITH_DEVICE_LOCAL_TIME
notification_tap_age_seconds: REPLACE_WITH_VALUE
android_notification_permission: REPLACE_WITH_STATE
ente_on_this_day_notifications: enabled
ente_app_lock_state: REPLACE_WITH_STATE
phone_lock_state_at_tap: REPLACE_WITH_STATE

ui_selector_manifest: REPLACE_WITH_LOCATION
foreground_layer_glob: REPLACE_AFTER_PERFETTO_PREFLIGHT
perfetto_config_path: mobile/apps/photos/docs/android-nonresponsive-perfetto.pbtxt
perfetto_config_sha256: 99e3a302e0943c1749af78c6277772a144ec622b7b5cb684c317f0f1f42585ba
perfetto_analysis_path: mobile/apps/photos/docs/android-nonresponsive-perfetto.sql
perfetto_analysis_sha256: 54c416fac7eaad69d143fbe1a8fb221b14f384e2380002b401b52002c9050690
```

Use only disposable accounts and synthetic non-sensitive media. A raw
customer app-data directory contains account keys and must not be transferred.

Android Keystore-backed app state may not survive movement to another device,
application ID, or signing certificate. A “different machine” handoff therefore
needs either:

1. The same dedicated physical device shipped or remotely attached to the new
   machine, with app data preserved; or
2. A documented synthetic fixture that the new agent can recreate and
   re-synchronize using disposable credentials.

## Required fixtures

### V0: valid On-this-day memory

Create a synthetic account with:

- Smart memories and On-this-day notifications enabled.
- Notification permission granted.
- At least five eligible photos for the test month/day. Use multiple historical
  years and realistic JPEG dimensions to avoid a marginal scheduler fixture.
- An active cache entry at tap time.
- A known first file and recorded local/remote, thumbnail, original,
  ownership, collection, encryption-key, and download-cache state.
- Stable Wi-Fi for the primary cohort.

The exact scheduler gate is at least five items. Verify scheduling from the app
log; do not assume that matching capture dates is sufficient.

### V1: invalidation-race memory

Start from V0. After the notification has been scheduled, arrange for every
file in the selected On-this-day memory to be deleted through supported Ente
operations from another client, so the test client later receives them as
deleted files. Keep the test device stale until the trial.

The release end-to-end experiment must time remote-diff application so that:

1. `goToOnThisDayMemory` selects and passes a non-empty shared list to
   `AllMemoriesPage`; then
2. deletion invalidation removes the remaining items from that same list while
   the route is current.

If deletion is applied before selection, `getMemories` may simply
return no matching memory and Home will remain visible. That is a valid
control, not the black-screen mechanism.

Use this concrete unmodified-release exploration:

1. Restore the test device with V0 cached and the real notification visible.
2. Put that device offline without opening Ente. Record and later restore its
   Wi-Fi and mobile-data states.
3. From a second signed-in client, delete all selected memory files and wait
   until the backend accepts those deletions.
4. Tap the notification on the offline test device. A visible memory title,
   close control, or image proves that a non-empty stale list reached the
   route.
5. Emit a marker and restore Wi-Fi. Let the ordinary connectivity-triggered
   sync receive the deletions while the viewer is current.
6. Record whether the already-open viewer loses its controls and becomes the
   pure-black state.

For a closer race, restore the fixture and sweep network restoration at
pre-registered offsets of -500, 0, and +500 ms relative to the real
notification tap. Use an on-device harness so marker, network command, and tap
share one clock. A failure in this timing sweep is nondiagnostic unless logs
prove the required selection-before-invalidation ordering.

A test-only barrier or integration harness may be used to prove the ordering
and exact empty-list rendering state. Label that result “mechanism confirmed
in instrumented build”; it is not a release reproduction. Do not change
product behavior in this diagnostic task.

### S0 and S2 account-scale states

Use account scale only as a secondary modifier:

| State | FilesDB rows | Home-visible files | Clip vectors | ML candidates |
| --- | ---: | ---: | ---: | ---: |
| S0 — small/settled control | Use the smallest realistic fixture | Record | Record | approximately 0 |
| S2 — incident-scale state | approximately 118,587 | approximately 84,182 | approximately 78,903 | approximately 6,716 |

S2 came from a one-time fan-in of 171 shared albums and 23,797 new shared
FilesDB rows. If exact S2 reconstruction is required, use the existing
Library-Sharing fixture runbook or a sanitized staging snapshot. Do not block
notification-path testing on that expensive fixture: run V0/S0 first.

Keep pending uploads, model cache, network, battery, and thermal state matched
between compared cells.

## Producing a real notification

A scored notification trial must use a real notification and PendingIntent
created by Ente. These are exploratory only and do not count:

- Calling the Dart callback directly.
- Opening `AllMemoriesPage` directly.
- Launching MainActivity with `adb shell am start`.
- Posting a look-alike notification with `cmd notification post`.

Preferred high-fidelity cohort:

1. Install signed `+2242`.
2. Create V0 and wait for an app-log line that schedules the On-this-day
   notification.
3. Update in place without uninstalling or clearing app data. The archive's
   observed build sequence after scheduling is
   `+2242 → +2244 → +2245 → +2249 → +2250 → +2251`; reproduce that chain when
   the signed artifacts are available and record every omitted step.
4. Confirm the notification is delivered on channel `onThisDay`.
5. Preserve the process, lock, timezone, and notification-age state for the
   selected matrix cell.

Also run a direct-`+2251` scheduling cohort. Comparing it with the
cross-update cohort tests a launch regression separately from stale schedule
state. A direct-`+2251` negative cannot rule out PendingIntent or app-data
effects introduced by the update chain.

For a lab date:

- Disable automatic date/time only on an isolated test device if policy allows.
- Set the device before the desired test day, let Ente compute the current and
  next memories, and verify the 08:00 schedule in logs.
- Do not move time backwards between compared trials without restoring both
  backend and device snapshots.
- The Android notification timeout is 16 hours. Sweep tap ages such as
  immediate, 5 minutes, and 45 minutes if reporter timing is unavailable.

Do not force-stop the package before notification delivery. A force-stopped
package may not receive scheduled work. To prepare a cold-process tap after
the notification is visible:

```sh
app_package=io.ente.photos

adb shell input keyevent KEYCODE_HOME
adb shell am kill "$app_package"
sleep 7
adb shell pidof "$app_package"
```

`pidof` must print nothing. If it does, reject the cold-process
precondition. Do not dismiss or recreate the notification after this check.

Automate the actual shade interaction with UIAutomator/UiAutomator2:

1. Expand the notification shade.
2. Find a row owned by `io.ente.photos` with title
   `On this day`.
3. Record its visible title, app, channel, post time, and selector.
4. Emit an on-device `ENTE_REPRO` marker immediately before
   `UiObject2.click()`.
5. Click the real row, not a coordinate copied from another device.

If Flutter semantics do not expose controls, record calibrated coordinates
for that device only. Never reuse coordinates across display resolutions.

## Experiment matrix

Run Phase 1 on V0/S0 first. Restore the same device/backend fixture before
each cold or background-only trial as applicable.

| Cell | Entry path | Process state | WorkManager | Purpose |
| --- | --- | --- | --- | --- |
| A | Launcher | Cold | No active job | Startup negative control |
| B | Open the same On-this-day memory from Home after Home is ready | Cold start, then foreground | No active job | Viewer/data control |
| C | Real On-this-day notification | Warm background | No active job | Warm callback control |
| D | Real On-this-day notification | Cold process | No active job | Primary notification cold-start test |
| E | Real On-this-day notification | Background-only process; no foreground engine yet | Background job already committed | Workload/race modifier |

Run at least five valid trials per cell. Run ten D and E trials before a
high-fidelity negative conclusion.

Interpret Phase 1:

- D/E black while B succeeds: notification startup/navigation path is favored.
- D fails while C succeeds: cold-start readiness is favored.
- B/C/D all fail on the same memory: viewer/cache/media state is favored.
- E fails while D succeeds: background overlap is an amplifier.
- A fails: the symptom is not notification-specific in that fixture.

Phase 2 uses V1:

| Cell | Entry path | Invalidation timing | Purpose |
| --- | --- | --- | --- |
| F | Same memory from Home | No invalidation | V0-equivalent control |
| G | Real notification | Invalidation fully applied before tap | Confirms expected “no current memory/Home remains” behavior |
| H | Real notification | Invalidation after selection while route is current | Explores the release race; mechanism scoring requires ordering proof |

Then repeat B, D, E, and H on S2. Scale is supported only by matched S2-vs-S0
regression; it must not be inferred from a large row count alone.

App Lock and device-lock state are separate blocking factors. If the reporter
cannot provide them, run these pre-registered cohorts:

- Ente App Lock off, phone already unlocked.
- Ente App Lock on, Ente unlocked before backgrounding.
- Ente App Lock on, notification tapped from the locked phone.

Do not mix lock states within one matrix cell.

Also match Android and Ente theme modes. If reporter state is unavailable, run
dark mode first because the Android 12+ night launch surface is black, then a
matched light-mode control. Do not pool theme modes.

## WorkManager modifier

The WorkManager task is:

```text
io.ente.photos.androidPeriodicTask
```

It runs through:

```text
io.ente.photos/androidx.work.impl.background.systemjob.SystemJobService
```

Launch Ente normally once and confirm `WorkManager configured`.
Discover the active Android user and dynamic JobScheduler integer ID:

```sh
app_package=io.ente.photos
android_user=$(adb shell am get-current-user | tr -d '\r')

adb shell am broadcast \
  -a androidx.work.diagnostics.REQUEST_DIAGNOSTICS \
  -p "$app_package"
adb shell dumpsys jobscheduler
```

Force only the identified job:

```sh
job_id=REPLACE_WITH_INTEGER_JOB_ID
adb shell cmd jobscheduler run -f -u "$android_user" \
  "$app_package" "$job_id"
```

After the background task has committed to work, tap the already-delivered
real notification. Use pre-registered offsets of approximately 0.5, 1.5, and
3 seconds, then verify ordering retrospectively from exported logs:

```text
[bg] [BgTaskUtils] Task started
[BG TASK] No recent foreground activity, proceeding with background work
[BG TASK] sync starting
Starting app in foreground
BG task is alive, not clearing locks
```

The 1.642-second delta belongs to the ambiguous 08:45 session. Treat it as an
incident timing target only if reporter timing identifies 08:45 as the
notification tap; otherwise it describes the post-kill restart.

Reject an overlap trial if foreground was already considered active, the
background job ended before the tap, or the exported log cannot demonstrate
concurrent engines. Do not replace real overlap with synthetic CPU load.

## Optional logging-only diagnostic build

Run the unmodified signed release first. In a separate diagnostic cohort, a
source-mapped release/profile build may add logging-only probes for:

- `didNotificationLaunchApp`, callback receipt, response type, and
  payload category;
- selected memory ID, item count, and first-file IDs before navigation;
- inner-navigator availability and route-push start/completion;
- viewer build and current item count;
- deletion invalidation before/after counts for the selected memory;
- first viewer frame and first media-load callback.

Use only synthetic fixture identifiers. Do not log keys, tokens, filenames
from real users, or decrypted metadata. Freeze one diagnostic build for all
compared diagnostic cells. Its result can locate the failed stage or confirm a
mechanism, but it cannot replace reproduction on the unmodified `+2251`
artifact. These probes are diagnostic instrumentation, not a product fix.

## Capture setup

Required artifacts for every scored trial:

- Screen recording with visible touches from before opening the shade through
  all exit attempts.
- A screenshot of the actual notification before tap.
- Ente exported log, even if it contains no launch record.
- All-buffer logcat, including system, events, and crash buffers.
- The supplied Perfetto/System Trace profile.
- WindowManager/activity state and one UI hierarchy dump after the five-second
  black threshold.
- Before/after `dumpsys gfxinfo` and `dumpsys meminfo`.
- `dumpsys activity exit-info <package>` after the trial.
- Immediate bugreport for a persistent black surface, ANR, or process death.
- Exact notification selector, process/lock state, and host/device timestamps.

The retail-safe trace profile captures FrameTimeline, scheduler, Binder,
disk/database, memory, power, Android logs, and the Ente process. It does not
require a Dart VM service.

Preflight the device:

```sh
trial_dir=/path/to/results/PREFLIGHT
mkdir -p "$trial_dir"

adb shell getprop ro.build.type > "$trial_dir/ro-build-type.txt"
adb shell getprop ro.debuggable > "$trial_dir/ro-debuggable.txt"
adb shell getprop ro.build.version.sdk > "$trial_dir/android-sdk.txt"
adb shell perfetto --version > "$trial_dir/perfetto-version.txt"
adb shell perfetto --query --long > "$trial_dir/perfetto-data-sources.txt"
adb shell atrace --list_categories > "$trial_dir/atrace-categories.txt"
```

Use the Home/Albums/Home/swipe sequence only for Perfetto and selector
preflight. It is not the scored reproduction interaction.

Start a scored 180-second trace:

```sh
config_file=mobile/apps/photos/docs/android-nonresponsive-perfetto.pbtxt
trial_id=D01
trial_dir="/path/to/results/$trial_id"
remote_trace="/data/misc/perfetto-traces/ente-$trial_id.pftrace"
mkdir -p "$trial_dir"

adb shell perfetto --background-wait --txt -c - \
  -o "$remote_trace" < "$config_file"

# Run the notification interaction immediately after capture starts.
# After the configured trace ends:
sleep 185
adb pull "$remote_trace" "$trial_dir/"
```

Copy the exact config and SQL into the trial directory and preserve their
hashes. Run this quality gate in Perfetto:

```sql
SELECT *
FROM stats
WHERE severity = 'data_loss' AND value != 0;
```

Any returned data-loss row invalidates the trace. During healthy preflight,
require nonzero Ente scheduler rows, foreground FrameTimeline rows,
`ENTE_REPRO` Android-log rows, process rows, and expected counter tracks using
`android-nonresponsive-perfetto.sql`.

For a **scored** trial, zero Ente FrameTimeline rows are valid when the target
is a pre-first-Flutter-frame stall. In that case require all of the following
instead: WindowManager/activity evidence that Ente is resumed, an Ente-owned
starting-window or surface layer, a live Ente process with scheduler rows, the
notification-tap marker, continuous screen evidence of the black surface, and
no process exit during the observation window. Record the frame metrics as
`null_no_first_app_frame`; do not turn zero rows into a zero-millisecond frame
latency. This exception does not relax the healthy preflight gate.

On retail Android, exact `android.input.inputevent` tracing is
normally unavailable. Use the `input` atrace category, on-device
markers, visible-touch recording, and FrameTimeline. Do not require Flutter
DevTools or Dart timeline slices from a release APK.

Basic logcat setup on a dedicated test device:

```sh
app_package=io.ente.photos
trial_dir=/path/to/results/TRIAL_ID
mkdir -p "$trial_dir"

adb logcat -b all -d -v epoch > "$trial_dir/logcat-before-clear.txt"
adb logcat -c
adb shell dumpsys gfxinfo "$app_package" reset
adb logcat -b all -v epoch > "$trial_dir/logcat.txt" &
logcat_pid=$!

# Run the trial, then:
kill -INT "$logcat_pid"
wait "$logcat_pid" || true
```

Do not clear logcat on a shared device. Do not repeatedly run intrusive
`dumpsys` commands inside the first five seconds; use Perfetto for
that interval.

## Scored interaction

The on-device harness must emit these markers:

```text
SCORED_WINDOW_START trial=D01 cell=D
INPUT_BEGIN trial=D01 seq=01 action=notification_tap
STATE_REACHED trial=D01 seq=01 action=ente_foreground
OBSERVATION trial=D01 state=black_5s
INPUT_BEGIN trial=D01 seq=02 action=android_back
STATE_REACHED or STATE_TIMEOUT trial=D01 seq=02 action=non_black_ente
INPUT_BEGIN trial=D01 seq=03 action=close_control
STATE_REACHED or STATE_TIMEOUT trial=D01 seq=03 action=non_black_ente
SCORED_WINDOW_END trial=D01
```

Protocol:

1. Record the visible real Ente notification.
2. Emit `INPUT_BEGIN` and click it.
3. Confirm `io.ente.photos` is the resumed foreground activity.
4. Observe for five seconds. Do not interact during this classification
   interval.
5. If the screen is black, emit the observation marker, capture
   WindowManager/activity state, one screenshot, and one hierarchy dump.
6. Emit the Back marker and send one Android system Back event. Wait two
   seconds for a non-black Ente surface or for Ente to leave foreground.
7. If Back fails, attempt the Ente close control only if it is visibly or
   semantically present. Emit a marker and wait two seconds. If no close
   control exists, record `close_absent`; do not tap a guessed
   coordinate.
8. Retain the black surface long enough to complete trace evidence, then record
   process state.
9. End the failed process using the lab runbook and launch Ente normally.
   Record whether Home recovers.

Use separate paired trials if testing Back first would prevent a close-control
test. The notification and fixture must be freshly restored for each.

For the Home viewer control B, open the same memory shown by the notification,
not a different On-this-day card. A diagnostic build may log the memory ID and
first file; an exact release run needs the fixture manifest to establish that
identity.

## Exact reproduction criteria

Classify **exact black-screen reproduction** only when all are true:

1. The recording proves a tap on the real app-generated
   `On this day` notification.
2. `io.ente.photos` becomes and remains the resumed foreground app.
3. Once Ente owns the foreground surface, that app-owned content region stays
   at least 98% near-black for five continuous seconds using the pre-registered
   pixel threshold, and no memory image, title, progress indicator, or close
   control is visible. This includes a launch surface that never reaches its
   first Flutter frame.
4. One Android system Back input does not expose Home, another non-black Ente
   surface, or leave Ente foreground within two seconds.
5. If a close control exists, one valid tap also does not restore a non-black
   Ente surface within two seconds. If it does not exist, record that absence.
6. The Ente process remains alive and foreground during the black state.
7. Ending that process and starting Ente normally restores a usable Home.

Exclude status/navigation bars from the pixel crop. Calibrate the threshold on
the target device before scoring and freeze it for every cell.

Classify related outcomes separately:

| Outcome | Classification |
| --- | --- |
| Black/no controls, but Android Back exits | Partial reproduction of the empty/invalid viewer state; not the full “no way to exit” report. |
| Media area black, but title/close is visible | Media-load failure; not exact. |
| Notification tap leaves Home visible | Notification handler dropped/no current memory; not exact. |
| Splash or black lasts under five seconds, then viewer/Home appears | Slow launch; not exact. |
| Process crashes or is killed | Crash/process-death variant; not exact unless reporter clarifies that this was observed. |
| Android records `Input dispatching timed out` or reason ANR | Hard ANR in addition to the visual symptom. |

## Mechanism attribution

### Support for notification cold-start/navigation stall

Require:

- B succeeds on the identical memory and first-file state.
- D or E repeatedly produces the exact or partial black state.
- C-vs-D distinguishes warm from cold callback handling.
- Trace/log evidence locates the last reached stage:
  - no foreground log: native/pre-log startup candidate;
  - foreground log but no second `getMemories`: notification response
    not handled;
  - second `getMemories` but no route/viewer markers: cache/navigation
    candidate;
  - viewer marker then no frames/input response: UI/render stall.

A background modifier is supported only if E fails materially more often than
D with identical notification and fixture state.

### Support for empty/invalid selected memory

Require:

- Evidence that selection initially had at least one item.
- Evidence that the same `SmartMemory.memories` list reached zero
  while its route was current.
- Black/no-controls begins with that transition.
- F remains healthy and G remains on Home or returns without opening a black
  viewer.
- H reproduces the black/no-controls state.

If only an instrumented build proves this ordering, report mechanism support
but keep release reproduction status separate.

### Scale and WorkManager modifiers

Use matched contrasts only:

- S2 D minus S0 D tests large-account startup scale.
- E minus D at the same scale tests WorkManager overlap.
- S2 H minus S0 H tests whether scale widens the invalidation race.

The 12.4-second ML enumeration, 84,185-file Home load, or
`BG task is alive` marker is not itself reproduction. Associate it
with the symptom only when its trace interval overlaps the black/no-input
interval and matched controls remain healthy.

## Validity gates

Mark a scored trial invalid if:

- The notification is synthetic, wrong, duplicated ambiguously, or not from
  the declared Ente build.
- The process state differs from its cell.
- The On-this-day memory or first-file state differs between matched cells.
- App Lock, device-lock, theme, timezone, notification age, network, thermal,
  model cache, locale, or pending uploads drift.
- A control opens a different memory.
- WorkManager overlap is claimed without interleaved engine evidence.
- Perfetto reports data loss, omits Ente scheduler/process evidence, or has
  neither foreground frames nor the complete pre-first-frame evidence defined
  above.
- The screen recording misses the tap, five-second interval, or exit attempts.
- The app process dies before the functional black-state checks complete.

Use **not reproduced under this protocol** only when:

- Exact `+2251` artifact/source mapping and a high-fidelity device are
  available.
- The notification was scheduled under `+2242` and the declared in-place
  update-chain cohort reached `+2251` with app data and the scheduled
  notification preserved. If any observed intermediate build is unavailable,
  scope the negative conclusion to exclude full update-chain effects.
- The real notification, V0 state, process/lock state, and first asset are all
  verified. V1 is additionally required before declaring the empty/invalid
  memory hypothesis not reproduced.
- D and E each have at least ten valid trials across pre-registered notification
  ages, with at least five valid trials in every other required cell.
- No exact or partial black state occurs.
- Diagnostic stage markers and trace capture are complete, including either
  foreground frames or the pre-first-frame evidence exception.

Otherwise use **inconclusive**, especially when the reporter’s pre-tap cache or
exact artifact is unavailable. A negative result on a debug build, look-alike
notification, small account only, or different memory cannot rule out the
incident.

## Trial record

Record at least:

```text
trial_id
matrix_cell
artifact_version
artifact_sha256
source_commit
signing_certificate_sha256
device_model
android_build_fingerprint
application_id
device_locale
ente_locale
android_theme_mode
ente_theme_mode
backend_snapshot
device_snapshot
account_scale_state
filesdb_rows
home_visible_files
vector_entries
ml_candidates
on_this_day_fixture_state
selected_memory_id
selected_memory_item_count_before_tap
selected_memory_item_count_after_sync
first_file_ids
first_file_local_remote_cached_state
notification_schedule_build
update_chain_applied
update_chain_artifact_manifest
notification_id
notification_payload
notification_delivery_time
notification_tap_time
notification_age_seconds
process_state_at_tap
app_lock_state
phone_lock_state
workmanager_active
background_start_time
foreground_start_time
notification_handler_reached
second_get_memories_seen
route_push_seen
viewer_init_seen
first_media_load_seen
first_flutter_frame_seen
foreground_frame_rows
frame_gate_mode
ente_foreground_confirmed
black_start_time
black_duration_ms
near_black_pixel_percent
close_control_present
android_back_result
close_control_result
frames_continued_during_black
input_dispatch_continued_during_black
hard_anr
process_exit_reason
restart_home_recovered
perfetto_config_sha256
perfetto_analysis_sha256
perfetto_data_loss_rows
valid_trial
invalid_reason
artifact_directory
```

The receiving agent’s final handback must contain:

- Exact artifact hashes, source mapping, device, and backend details.
- Fixture and notification manifests without secrets.
- Completed trial table.
- Raw screen recordings, screenshots, Ente logs, logcat, Perfetto traces,
  WindowManager/activity/UI dumps, exit-info, and bugreports.
- Separate conclusions for:
  - exact functional reproduction;
  - partial black/no-controls reproduction;
  - hard ANR;
  - notification cold-start/navigation hypothesis;
  - empty/invalid-memory hypothesis;
  - account-scale and WorkManager modifiers.

Do not include a proposed product or code fix.
