# Changelog

All notable changes to this package are documented here. Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [SemVer](https://semver.org/).

## Unreleased

### Changed

- The runtime sender now bundles up to 50 events into a single `/v1/batch` request with a 0.25 s flush window (both tunable on the settings asset). Previously every queued event was its own HTTP POST, which got expensive once movement events started flowing. The collector wire format is unchanged — just more events per request. Per-event 32 KB and per-batch 1 MB collector caps are honored: oversized events are skipped with a warning, and any events that would push the body past the cap stay in the queue for the next batch.

### Added

- `TestingFloor.SetPositionSource(...)` and `TestingFloor.SetCameraSource(...)` / `UseMainCamera()` register the player transform and camera so every event automatically carries `player.position.*`, `camera.position.*`, `camera.euler.*`, `camera.fov`, and `viewport.width`/`height`. Property keys match the names the Testing Floor heatmap and session viewer already consume.
- Built-in debounced `player_moved` event with `movement.reason` of `start` / `stop` / `ping`, plus `movement.distance` (meters) and `movement.duration` (seconds) on `ping` and `stop` so segment length and run length are queryable directly. Toggle with `TestingFloorSettings.movementTrackingEnabled` or `TestingFloor.SetMovementTrackingEnabled(...)`. Thresholds (`movementMinPingDistance`, `movementMinPingIntervalSeconds`, `movementStartGraceSeconds`, `movementStopGraceSeconds`, `movementMinStep`) are tunable on the settings asset; defaults target ~2 path samples per second.
- Runtime QR heartbeat opt-in/out API via `TestingFloor.SetQrHeartbeatsEnabled(...)`.
- Runtime QR heartbeat color override via `TestingFloor.SetQrHeartbeatInverted(...)`.
- Documented telemetry QR payload format: `tfqr://sync/v1?s=<session_id>&t=<unix_ms>&q=<sequence>`.
- Low-allocation telemetry JSON writer for the runtime send path.

### Changed

- QR heartbeat codes now rotate every 15 seconds by default.
- QR heartbeat timing controls are presented as advanced settings in the custom inspector.

### Added

- Typed `Set(string, string[])` and `Set(string, int[])` overloads on `EventBuilder` and `ContextSnapshot` for tag-style array properties.
- `Set(string, Guid)` overload that serializes the GUID as a JSON string.
- `SetIfPresent` overloads for `int?`, `long?`, `float?`, `double?`, and `bool?` that skip when the value is `null`.

### Removed

- `Set(string, object)` overload on `EventBuilder` and `ContextSnapshot`. Property values must use a typed overload (`string`, `long`, `double`, `bool`, `string[]`, `int[]`) so they serialize as the correct JSON token instead of silently stringifying.

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
