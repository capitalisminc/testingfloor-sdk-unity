# Testing Floor SDK for Unity

Add gameplay events and recording sync to a Unity game. Start with one useful
event; add movement, maps, or external-capture QR sync when you need them.
Requires Unity 6 and a Testing Floor game write key. Licensed under MIT.

## Install and send your first event

1. In **Window -> Package Manager**, select **+ -> Add package from git URL**:
   `https://github.com/capitalisminc/testingfloor-sdk-unity.git`.
2. Select **Tools -> Testing Floor -> Create Settings Asset**.
3. Paste your game's write key into the asset. Keep real keys out of source control.
4. Editor telemetry defaults to **Collect While Recording**. Enable **Collect During Ordinary Editor Play** only when you also want telemetry outside recordings.

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

A context provider adds small values that describe each moment, such as level,
health and game clock, to every event. Keep it inexpensive. Event properties
win when they use the same key as context.

```csharp
public sealed class MomentContext : ITelemetryContextProvider {
    public void FillSnapshot(ref ContextSnapshot snapshot) {
        snapshot.Set("game_level.id", "tutorial");
        snapshot.Set("player.health", 80);
    }
}

// During your game's initialization:
TestingFloor.RegisterContextProvider(new MomentContext());
```

Use SDK APIs and providers on Unity's main thread. Clear a provider with
`RegisterContextProvider(null)` when its owner is torn down.

## Session state

Values that stay true until they change, such as settings, device, build,
inventory and outfit, belong in session state. The SDK sends them once and then
only when they change, so events stay small. A state provider uses the same
interface as a context provider:

```csharp
public sealed class SettingsState : ITelemetryContextProvider {
    public void FillSnapshot(ref ContextSnapshot snapshot) {
        snapshot.Set("pref.quality", QualitySettings.names[QualitySettings.GetQualityLevel()]);
        snapshot.Set("inventory.items", inventory.ItemIds);
    }
}

TestingFloor.RegisterStateProvider(new SettingsState());
TestingFloor.SetState("difficulty", "hard");   // or set one value directly
TestingFloor.ClearState("difficulty");
```

The SDK reads state before each event and once a second, also while gameplay is
paused. When a value changed, it queues a `$tf_state` record with only the
changed keys before the event. A removed key is sent as `null`. A keyframe
repeats every value when the play session starts, when a recording starts,
every 5 minutes and after any record is dropped. Testing Floor applies the
state at each moment to the events, so you can filter by these values as
before. The SDK also sends `viewport.width`, `viewport.height` and
`camera.fov_axis` as state.

Keep values that row-level analytics filter by, such as `game_level.id` and the
game clock, in the context provider. A game without a state provider keeps all
of its context on every event.

## Tracked objects

Track NPCs, props, vehicles, pickups and projectiles to draw their paths on the
recording map. Add **Testing Floor/Tracked Object** to a prefab and set its
kind, or register objects from code:

```csharp
var guard = TestingFloor.TrackObject("guard", transform, "captain_zed");
guard.SetState("behavior", "searching");
guard.Remove("knocked_out");

// Or read the position yourself; return null when the object is gone.
TestingFloor.TrackObject("rocket", () => rocket != null ? rocket.position : (Vector3?)null);
```

Without an id the SDK assigns `kind#n`. Give the id your events already use for
the object, so events and its path line up. The SDK checks objects 4 times a
second in unscaled time and sends an object only after it moves at least
0.25 m. Removing an object sends its final position and the reason. The SDK
removes an object itself with `destroyed` when its GameObject is destroyed,
`gone` when the position function returns null, and `disabled` when the
component is disabled. Object state values are strings, integers, doubles or
booleans; a value named `label` becomes the display name.

Samples are batched into `$tf_objects` records that copy the `game_level.*`
values of the most recent event. At most 256 objects are live; further
registrations are ignored with one warning. While collection is disabled,
`TrackObject` returns a handle that does nothing, and a component registers
once collection starts. `objectSampleRateHz` (1–10) and `maxTrackedObjects`
are in the settings asset.

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
forward direction, and vertical FOV; viewport size is sent as session state.
Positions use native meters.
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

The desktop app passes `--testing-floor={"recording_uuid":"..."}` or writes
`Library/TestingFloor/recording-payload.json`. It owns the handoff and its cleanup.
The SDK watches for changes during Play, including while paused. Start a recording
before or after Play, stop it, and start another without restarting the game.
Each recording receives fresh state and object keyframes while the Play run ID
stays the same.

`enableInEditor` defaults to false; `enableInEditorWhileRecording` defaults to true.
Set both false for no editor telemetry, or set `enableInEditor` true for ordinary
editor telemetry too. The master switch, force-disable, automation restrictions
and collector configuration still apply. `TestingFloor.IsCollecting` and
`TestingFloor.CollectionReason` expose the effective policy; game wrappers should
query them instead of caching an editor-off decision or choosing a permanent null
client. Subscribe to `TestingFloor.RecordingChanged` to invalidate recording-specific
caches or update external error-reporting tags. It receives the active recording on
attach/resume and null on detach/suspension, before new attachment keyframes. Also
synchronize once after subscribing, since a recording may already be attached.
Register context/state providers and tracked objects even while dormant;
the SDK retains handles without sampling them until collection becomes active.

Recording-only collection requires an active desktop app lease. The additive
handoff fields are `lease_version: 1`, `telemetry_requested: true`, and
`lease_expires_at_unix_ms`, alongside `recording_uuid` and `telemetry_directory`.
The app renews the lease while recording; a fresh lease remains valid even when
the original recording was created more than twelve hours earlier. The legacy
age limit still applies to static, expired and unsupported handoffs.
An expired lease suspends association;
renewal sends new keyframes and retains an interruption flag. A closed recording
cannot reopen in the same Play run. Static legacy handoffs retain their existing
behavior when ordinary editor collection is enabled, but do not override editor-off
policy. Do not construct handoffs manually for normal desktop app use.

The SDK registers `<run UUID>.session` before admitting recording events and writes
`<run UUID>.status.json` with attachment state, reason, capabilities and freshness.
Recorder QR suppression follows the valid recording handoff even when telemetry
is disabled. Local attachment is not a warehouse delivery acknowledgement. Completion markers
are ordered behind accepted data and report drop counts and interruptions for that
recording alone. Accepted batches can finish uploading after recording stops.

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
events including requests in flight. State and object records count as events
here. Each record carries `$tf.seq`, which counts every record of a play
session from 1, so Testing Floor can tell when records are missing. Queue overflow drops the newest event.
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
[CHANGELOG.md](CHANGELOG.md) documents the changes in each release.

MIT. See [LICENSE.md](LICENSE.md) and [ThirdPartyNotices.md](ThirdPartyNotices.md).
