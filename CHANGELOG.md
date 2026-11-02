# Changelog

## Unreleased

### Added

- The EditMode `ContractScenarioTests` run the shared SDK scenarios in
  `Tests/Contract/scenarios`, one test per file. `Tests/Contract` now holds the
  whole published SDK contract, including `SDK_CONTRACT.md`, and
  `Tools~/update_contract.py` refreshes it.

## 0.6.0

### Added

- Orthographic cameras send `camera.ortho_width`, the view's width, instead of
  `camera.fov`, which an orthographic camera ignores. The new session state
  `camera.projection` says `orthographic` or `perspective`, and
  `camera.fov_axis` is sent only for a perspective camera. Every camera sample
  also sends `camera.up`, which orients a camera that looks straight down.
- `TestingFloor.SetCameraFocusSource` sends `camera.focus`, the point at the
  center of the view, such as a strategy camera's pivot.
  `ClearCameraFocusSource` removes it.
- A change of orthographic width is movement, reported as
  `movement.ortho_width_delta` in meters and tuned by
  `movementMinPingOrthoWidth` and `movementMinStepOrthoWidth`.

## 0.5.1

### Fixed

- A build that could read the recorder's telemetry directory but not create
  files in it, such as a sandboxed macOS app, failed to register its run and
  then sent no recording telemetry at all: events lacked the recording's UUID
  and no completion marker was sent. Registration is now best effort. Such a
  run still attaches to the recording, watches `closed.json` and sends its
  completion marker. It is missing from the recorder's participant list, and
  the server finds it by the recording UUID on its events. A missing directory
  or an existing `closed.json` still admits no recording events.

## 0.5.0

### Added

- The package tests its payloads against an unedited copy of the Testing Floor
  telemetry contract in `Tests/Contract`. EditMode tests fail when a field name,
  type, record name or limit drifts from it.
- `$tf.editor` is `true` for records from Play Mode in the Editor and `false` in
  builds, so editor runs can be told apart from players' runs.
- `TestingFloor.RegisterEventProcessor` registers a final pass over each event's
  merged properties, before it is queued. `ContextSnapshot` gains `TryGetValue`,
  `Remove` and `Keys` so a processor can read and change them.
- `TestingFloor.SetState` accepts string arrays and integer arrays, like a state
  provider.
- `TestingFloor.SetUserId` and `SetDeviceId` send your game's own ids as
  `$user_id` and `$device_id` instead of the generated ones.
- Editor telemetry follows the desktop app's recordings. A recording can start
  before or during Play, stop, and be followed by another without restarting
  Play. Each recording gets fresh state and object keyframes, and the Play run
  ID stays the same. `TestingFloor.IsCollecting` and
  `TestingFloor.CollectionReason` say whether the SDK is collecting and why not,
  and `TestingFloor.RecordingChanged` reports the active recording, or null when
  it detaches. The SDK writes `<run UUID>.status.json` in the recording's
  telemetry directory with its attachment state.

### Changed

- Play Mode in the Editor sends telemetry while the desktop app records it, even
  when `enableInEditor` is false. The new `enableInEditorWhileRecording`
  setting, on by default, controls this; turn both off for no editor telemetry.
  Outside a recording, the Editor still sends telemetry only with
  `enableInEditor`.
- `$tf.os` is the operating system family (`Windows`, `macOS`, `Linux`, `iOS`,
  `Android`, `tvOS`, `visionOS`, `Web`, `PlayStation`, `Xbox`, `Switch`), not
  Unity's platform name such as `WindowsPlayer` or `OSXEditor`. Other platforms
  still send Unity's name.
- `$tf.locale` is a BCP 47 tag such as `en-US`, not a language name such as
  `English`. When the region is unknown it is a language-only tag, and when the
  language is unknown it is left out.
- `$time` and `$sent_at` have exactly three fractional digits and a `Z` suffix,
  for example `2026-04-19T18:32:00.000Z`, instead of seven digits and `+00:00`.
- `Track` refuses event names that begin with `$tf_`, in any casing. It logs one
  warning per name and counts the event in `DroppedEventCount`. It does not count
  it toward a recording's `dropped_events` or start a keyframe. Earlier versions
  sent such events, which Testing Floor could mistake for its own records.
- A valid `--testing-floor` launch argument now wins over a handoff file without
  a recording lease in the Editor too, as it already did in builds and in the
  Unreal SDK. A handoff file with a lease still wins, even after the lease
  expires. A handoff file written for another editor process no longer hides the
  launch argument.
- The recording completion marker carries the same `$tf` context as other
  records. Before, it could leave out the platform fields when no event had been
  sent yet.
- With the new array overloads, `SetState(key, null)` no longer compiles because
  the type is ambiguous. Use `ClearState(key)` or cast the null.

## 0.4.3

### Fixed

- When the recorder closed a recording mid-session, the SDK queued the
  completion marker first, so a session state change or object removal still
  waiting for the next poll or check went out afterwards without the
  recording's UUID, and the marker reported no loss. The SDK now polls state
  and checks objects, as it does on quit, before it queues the marker. Those
  records carry the recording and take earlier `$tf.seq` values, and one the
  queue refuses counts toward the marker's `dropped_events`.
- An event that a state provider or position callback sends while the SDK
  polls state or checks objects no longer polls state again or closes the
  recording in the middle of that work. A state provider that sent events
  could recurse without end, and a close seen mid-check could discard that
  check's samples.

## 0.4.2

### Fixed

- An object removal in a `$tf_objects` record that the queue refused was lost,
  and Testing Floor ends an object's path only on a removal, so the object
  stayed on the map. Refused removals now wait and go out at the next check,
  with their final position. At most `maxTrackedObjects` wait; any beyond that
  count toward `dropped_events` and trigger the next keyframe.
- Quitting or leaving Play mode now sends the last session state change and
  object movement before the recording completion marker. Before, changes
  after the last once-a-second poll were lost. The SDK repeats the object
  check until removals carried to a later check, and the later lifetimes of an
  id they hold back, are sent. It stops if the queue refuses a record, and
  counts whatever still waits toward `dropped_events`.
- When the queue refused part of a large keyframe, the next keyframe started
  from the same objects, so the last ones were never sent. Live objects from a
  refused record now come first in the next check.
- Consecutive object checks always get increasing `$time`, also when repeated
  checks at quit fall in one millisecond.

## 0.4.1

### Fixed

- A tracked object whose position has a NaN or infinite axis is skipped for
  that check instead of writing invalid JSON that broke the whole request.
- When an object and a same-id replacement were both removed before the next
  check, the replacement's samples joined the old path. Each lifetime of an id
  now ends in its own record.

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
