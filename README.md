# Testing Floor SDK for Unity

Add gameplay events and recording sync to a Unity game. Start with one useful
event; add movement, maps, or external-capture QR sync when you need them.
Requires Unity 6 and a Testing Floor game write key. Licensed under MIT.

## Install and send your first event

1. In **Window -> Package Manager**, select **+ -> Add package from git URL**:
   `https://github.com/capitalisminc/testingfloor-sdk-unity.git`.
2. Select **Tools -> Testing Floor -> Create Settings Asset**.
3. Paste your game's write key into the asset. Keep real keys out of source control.
4. Enable **Enable In Editor** while checking the integration in Play mode.

```csharp
using TestingFloorSdk;

TestingFloor.Track("level_started")
    .Set("game_level.id", "tutorial")
    .Send();
```

Trigger the event and check the game's telemetry status page in Testing Floor.
Editor collection is off by default, and automated SDK tests never send live
telemetry. A missing settings asset or write key disables sends.

## Events and shared context

`Track` creates a single-use builder. `Send()` returns whether the event entered
the in-memory queue; it does not confirm server delivery. Create a new builder
for each event. Sending or changing an already-sent builder throws.

```csharp
bool queued = TestingFloor.Track("weapon_fire")
    .Set("weapon.id", "plasma_rifle")
    .Set("damage", 42)
    .SetIfPresent("target.id", optionalTargetId)
    .Send();
```

`Set` accepts strings, signed integers, finite floats/doubles, booleans, GUIDs,
string arrays, and integer arrays. Arrays are copied. Null values, empty keys,
reserved `$` keys, and nonfinite numbers are omitted. Convert enums or custom
values explicitly; nested objects are unsupported. `SetVector(prefix, vector)`
adds `.x`, `.y`, and `.z` fields.

Register shared state once; keep the provider inexpensive. Event properties
win when they use the same key as context.

```csharp
public sealed class GameContext : ITelemetryContextProvider {
    public void FillSnapshot(ref ContextSnapshot snapshot) {
        snapshot.Set("game_level.id", "tutorial");
        snapshot.Set("difficulty", "normal");
    }
}

// During your game's initialization:
TestingFloor.RegisterContextProvider(new GameContext());
```

Use SDK APIs and context providers on Unity's main thread. Clear the provider
with `RegisterContextProvider(null)` when its owner is torn down.

## Player ID and Runs

The SDK creates these ids itself and attaches them to every event, so you don't set an identity in code.

| On the wire | SDK property | In Testing Floor |
| --- | --- | --- |
| `$user_id` | `TestingFloor.ProfileId` | Player ID |
| `$device_id` | `TestingFloor.DeviceId` | Player ID, only when the event has no `$user_id` |
| `$session_id` | `TestingFloor.PlaySessionId` | Run |

- `ProfileId` is a random id kept in `{Application.persistentDataPath}/synced/profile_id`. It stays the same across launches on one machine. To keep one Player ID for a player across machines, include that file in your cloud save sync.
- `DeviceId` is a random id kept in `PlayerPrefs`. Testing Floor uses it as the Player ID only when `ProfileId` is missing, for example when the SDK can't write the profile file.
- `PlaySessionId` changes on every launch and every time you enter Play Mode in the Editor. Each value is one run in Testing Floor. A recording from the Testing Floor recorder can span several runs, for example when you stop and restart Play Mode.

`TestingFloor.PlayerId` returns the Player ID that Testing Floor shows for this player. Put it on a feedback or debug screen so testers can tell you their ID, then assign that ID to the tester in Testing Floor. Their runs then show under their name.

```csharp
playerIdLabel.text = $"Player ID: {TestingFloor.PlayerId}";
```

## Player, camera, and movement

```csharp
TestingFloor.SetPositionSource(playerTransform);
TestingFloor.UseMainCamera();
TestingFloor.SetMovementTrackingEnabled(true);
```

Every event includes available player/camera positions, camera rotation,
forward direction, vertical FOV, and viewport size. Positions use native meters.
A function returning `Vector3?` can replace the player transform; return `null`
when unavailable. Use `SetCameraSource(Func<Camera>)` for a custom camera.

Movement is opt-in and samples player or camera-only activity. `player_moved`
reports `start`, `ping`, and `stop`, with nonzero distance, camera distance,
rotation, FOV-change, and duration metrics. Defaults target about two path
samples per second. Configure thresholds in the settings asset rather than
sending movement events every frame. Timing uses unscaled time during pause.

Clear sources with `ClearPositionSource()` / `ClearCameraSource()`. Runtime
movement overrides can be reset with `UseConfiguredMovementTracking()`.

## Teleports

```csharp
using var teleport = TestingFloor.BeginTeleport("respawn");
await MovePlayerAsync(destination);
```

For an instantaneous move, call `RecordTeleport(start, end, "respawn")` after
moving. A scope suspends movement sampling, emits one `player_teleported` event,
and resets the destination baseline. Call `teleport.Cancel()` to cancel without
an event. Positions and scalar distances are meters.

## Recording sync

The SDK creates a fresh `PlaySessionId` on each game launch or editor Play run.
`TestingFloor.Recording?.RecordingUuid` identifies a recording that can span
several Play runs. No synthetic session bookend events are emitted.

The desktop recorder passes `--testing-floor={"recording_uuid":"..."}` or writes
`Library/TestingFloor/recording-payload.json`. The SDK reads the sidecar at each
Play start; the recorder owns its cleanup. Do not construct handoffs manually
for normal recorder use.

For OBS or other external capture, show the built-in marker while recording:

```csharp
TestingFloor.SetQrHeartbeatsEnabled(true);
// When external capture stops:
TestingFloor.SetQrHeartbeatsEnabled(false);
```

The marker uses an inverted QR, refreshed every 15 seconds. Recorder handoffs
normally suppress it. `qrHeartbeatsEnabledWithRecorderSession` or a `--forceqr`
launch argument can override that suppression. `SetQrHeartbeatInverted(false)`
selects black-on-white rendering. Reset runtime overrides with
`UseConfiguredQrHeartbeats()` / `UseConfiguredQrHeartbeatColors()`.

## Delivery, diagnostics, and shutdown

Defaults are 50 events per batch, a 0.25-second flush window, and 4,096 pending
events including requests in flight. Queue overflow drops the newest event.
Network errors, HTTP 408/429, and server errors retry in order with stable event
IDs and exponential backoff. HTTP 401/403 reject the play session; other invalid
client requests are dropped. Oversized events are dropped before sending.

`TestingFloor.State`, `PendingEventCount`, and `DroppedEventCount` expose local
status. Inspect the game's telemetry status page to confirm receipt.

```csharp
await TestingFloor.FlushAsync(TimeSpan.FromSeconds(2));
```

The SDK requests a two-second flush on quit. A flush is best effort and waits
at most five seconds; it respects retry backoff. There is no disk spool. Crashes,
overflow, rejected keys, and shutdown deadlines can lose events.

Advanced settings live in `Assets/Resources/TestingFloorSettings.asset`, including
`queueMaxEvents`, `batchMaxEvents`, `batchMaxFlushIntervalSeconds`, and movement
thresholds. Most games need only the write key and activity sources.

## Contract, development, and license

See [SDK_CONTRACT.md](SDK_CONTRACT.md) for behavior shared with the
[Unreal SDK](https://github.com/capitalisminc/testingfloor-sdk-unreal), and
[CONTRIBUTING.md](CONTRIBUTING.md) for package tests and local development.
[CHANGELOG.md](CHANGELOG.md) documents breaking changes in 0.2.0.

MIT. See [LICENSE.md](LICENSE.md) and [ThirdPartyNotices.md](ThirdPartyNotices.md).
