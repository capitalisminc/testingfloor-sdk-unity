# Changelog

## 0.4.0

Telemetry schema 2. Values that stay true for a while are sent once, when they
change, instead of on every event, and objects other than the player get paths
on the map. Testing Floor reads schema 1 and schema 2 data the same way.

### Added

- `TestingFloor.RegisterStateProvider(...)`, `SetState(key, value)` and
  `ClearState(key)` for session state: settings, device, build, inventory,
  outfit and similar values. The SDK reads state before each event and once a
  second and queues a `$tf_state` record with only the changed keys before the
  event. A cleared key is sent as `null`. A keyframe repeats every value when
  the play session starts, when a recording starts, every 5 minutes and after
  any record is dropped.
- `TestingFloor.TrackObject(kind, transform, id)` and
  `TrackObject(kind, Func<Vector3?>, id)` return a `TrackedObject` handle with
  `SetState(key, value)` and `Remove(reason)`. Objects are checked 4 times a
  second and sent in batched `$tf_objects` records when they move at least
  0.25 m. Ids default to `kind#n`.
- The **Tracked Object** component (`TestingFloorTrackedObject`) tracks a
  GameObject while it is enabled and removes it with the reason `disabled`.
- `objectSampleRateHz` (1–10, default 4) and `maxTrackedObjects` (default 256)
  settings.
- With recording delivery completion: `$tf_telemetry_complete` takes the next
  seq, dropped state and object records count toward `dropped_events`, state
  and object records stop carrying a closed recording's UUID, and a Play run
  that registers with a new recording sends a keyframe of state and objects.

### Changed

- Every record is sent with `$schema: 2` and `$tf.seq`, which counts every
  record of a play session from 1. Keyframes set `$tf.keyframe: true`.
- `viewport.width`, `viewport.height` and `camera.fov_axis` move from every
  event to session state. Camera pose, `camera.fov` and the player position
  stay on events.
- `RegisterContextProvider` is now for small values that describe each moment,
  such as level, health and game clock. Move values that rarely change to a
  state provider to make events smaller. A game that registers no state
  provider keeps all of its context on every event.

## 0.3.0

- Register each Play run with its recorder and close recording telemetry with an acknowledged flush marker.
- Keep completion markers behind earlier batch acknowledgments, report dropped events, and detach later gameplay from stopped recordings.

## 0.2.0 — Unreleased

### Changed (breaking)

- The C# namespace is now `TestingFloorSdk`. Update imports to `using TestingFloorSdk;` so `TestingFloor.Track(...)` resolves directly. No old namespace alias is retained.

- Canonical recorder handoffs require `recording_uuid`; removed recorder-session aliases and the old sidecar filename.
- Play-session identity is distinct from recording identity, with no automatic session bookend events.
- Typed properties reserve `$` keys, omit nonfinite values, and capture arrays by value.
- Both SDKs use a bounded 4,096-event queue including requests in flight, drop newest on overflow, and retain transient failures for ordered retry.
- Automated engine tests cannot enable live collection or forced QR markers.

- Event builders are single-use; `Send()` returns queue acceptance. Transport events, send outcomes, and platform serialization details are internal.
- Removed the unused frame-count QR setting. Camera-only movement and camera FOV-axis/forward metadata now match the shared contract.

### Added

- Read-only `TestingFloor.PlayerId` exposes the Player ID shown in Testing Floor: the profile ID, falling back to the device ID. Each play-session ID is one run.

- A shared SDK contract covering identity, units, fields, delivery limits, retry behavior, and safe defaults.
- Current setup, lifecycle, diagnostics, testing, and migration documentation.

## [0.1.0] — 2026-04-19

Initial release.

### Added

- `TestingFloor.Track(eventType).Set(...).Send()` fluent API.
- `TestingFloor.FlushAsync(timeout)` for quit-time drain.
- `TestingFloor.State`, `TestingFloor.DeviceId`, `TestingFloor.ProfileId`, `TestingFloor.Session`.
- `ITelemetryContextProvider` extension point for game-supplied per-event context.
- `TestingFloorSettings` ScriptableObject with Addressables → Resources → in-memory fallback loader.
- Session discovery from `--testing-floor=` CLI arg and `Library/TestingFloor/session-payload.json` sidecar.
- Automatic `tf_session_start` / `tf_session_end` when in a Testing Floor session.
- 256-event bounded ring queue with drop-oldest on overflow and dict pooling.
- Quit flush on `Application.quitting` and `EditorApplication.playModeStateChanged == ExitingPlayMode`.
- Editor: settings inspector and `Tools → Testing Floor → Create Settings Asset` menu.
