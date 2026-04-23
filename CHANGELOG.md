# Changelog

All notable changes to this package are documented here. Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [SemVer](https://semver.org/).

## Unreleased

### Added

- Runtime QR heartbeat opt-in/out API via `TestingFloor.SetQrHeartbeatsEnabled(...)`.
- Runtime QR heartbeat color override via `TestingFloor.SetQrHeartbeatInverted(...)`.
- Documented telemetry QR payload format: `tfqr://sync/v1?s=<session_id>&t=<unix_ms>&q=<sequence>`.
- Low-allocation telemetry JSON writer for the runtime send path.

### Changed

- QR heartbeat codes now rotate every 15 seconds by default.
- QR heartbeat timing controls are presented as advanced settings in the custom inspector.

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
