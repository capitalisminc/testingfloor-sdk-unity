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

The SDK adds its own context to every record under `$tf`: SDK version, Unity
version, `editor` (true in Play Mode in the Editor), `os` as the operating system
family such as `Windows` or `macOS`, `locale` as a BCP 47 tag such as `en-US`,
`app_version` from `Application.version` (left out when empty), device model and
screen size. Times are UTC with exactly milliseconds, such as
`2026-04-19T18:32:00.000Z`.

Event names beginning with `$tf_` are reserved for Testing Floor's own records.
`Send()` refuses them, returns false and logs one warning per name; the refused
event counts toward `DroppedEventCount`.

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

An event processor runs last, after context, player and camera values and the
event's own properties are merged. It can read, change or remove any of them
before the event is queued, for example to convert your game's coordinate frame:

```csharp
public sealed class ToCentimeters : ITelemetryContextProvider {
    public void FillSnapshot(ref ContextSnapshot snapshot) {
        foreach (var key in snapshot.Keys) {
            if (key.StartsWith("player.position.") && snapshot.TryGetValue(key, out var value)) {
                snapshot.Set(key, System.Convert.ToDouble(value) * 100);
            }
        }
        snapshot.Remove("debug.overlay");
    }
}

TestingFloor.RegisterEventProcessor(new ToCentimeters());
```

It runs for `$tf_player_moved` and `$tf_player_teleported` too, but not for session
state or tracked-object records. Those records copy `game_level.*` from the
processed event.

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
TestingFloor.SetState("party.members", new[] { "ana", "kai" });
TestingFloor.ClearState("difficulty");
```

`SetState` accepts strings, integers, doubles, booleans, string arrays and
integer arrays, like a provider, and its value wins over the provider's for the
same key. Arrays are copied.

The SDK reads state before each event and once a second, also while gameplay is
paused. When a value changed, it queues a `$tf_state` record with only the
changed keys before the event. A removed key is sent as `null`. A keyframe
repeats every value when the play session starts, when a recording starts,
every 5 minutes and after any record is dropped. Testing Floor applies the
state at each moment to the events, so you can filter by these values as
before. The SDK also sends `viewport.width`, `viewport.height`,
`camera.projection` and `camera.fov_axis` as state.

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

The SDK creates these ids itself and attaches them to every event, so you don't need to set an identity in code.

| On the wire | SDK property | In Testing Floor |
| --- | --- | --- |
| `$user_id` | `TestingFloor.ProfileId` | Player ID |
| `$device_id` | `TestingFloor.DeviceId` | Player ID, only when the event has no `$user_id` |
| `$session_id` | `TestingFloor.PlaySessionId` | Run |

- `ProfileId` is a random id kept in `{Application.persistentDataPath}/synced/profile_id`. It stays the same across launches on one machine. To keep one Player ID for a player across machines, include that file in your cloud save sync.
- `DeviceId` is a random id kept in `PlayerPrefs`. Testing Floor uses it as the Player ID only when `ProfileId` is missing, for example when the SDK can't write the profile file.
- If your game has its own player or account id, call `TestingFloor.SetUserId(id)` on each launch to send it as `$user_id` instead. `SetDeviceId(id)` does the same for `$device_id`. Pass `null` to go back to the generated id. The SDK does not save these ids.
- `PlaySessionId` changes on every launch and every time you enter Play Mode in the Editor. Each value is one run in Testing Floor. A recording from the Testing Floor recorder can span several runs, for example when you stop and restart Play Mode.

`TestingFloor.PlayerId` returns the Player ID that Testing Floor shows for this player. Put it on a feedback or debug screen so testers can tell you their ID, then assign that ID to the tester in Testing Floor. Their runs then show under their name.

```csharp
playerIdLabel.text = $"Player ID: {TestingFloor.PlayerId}";
```

## Player, camera, and movement

```csharp
TestingFloor.SetPositionSource(playerTransform);
TestingFloor.UseMainCamera();
```

Every event includes available player/camera positions, camera rotation,
forward and up directions, and vertical FOV; viewport size is sent as session
state. Positions use native meters.
A function returning `Vector3?` can replace the player transform; return `null`
when unavailable. Use `SetCameraSource(Func<Camera>)` for a custom camera.

An orthographic camera sends `camera.ortho_width`, the view's width
(`2 × orthographicSize × aspect`), instead of a FOV, and `camera.projection` is
`orthographic`. If the camera orbits or follows a point, such as the pivot of a
strategy or sim camera, give the SDK that point. Testing Floor centers the
camera's view on the map there:

```csharp
TestingFloor.SetCameraFocusSource(cameraPivot);
```

`SetPositionSource` is for the character the player plays. If the player directs
many characters instead, don't point it at a selected one: leave it unset and
track each character with `TrackObject`.

Movement tracking is on by default and samples player or camera-only activity
once a position or camera source is set. `$tf_player_moved` reports `start`, `ping`,
and `stop`, with nonzero distance, camera distance, rotation, FOV-change,
orthographic width change, and duration metrics. It sends up to ten samples a
second while the player moves, so a half-second jump draws as an arc on the
map. Raise **Movement Min Ping Interval Seconds** in the settings asset to send
fewer, for example in a slow-moving game, or turn movement off there or with
`SetMovementTrackingEnabled(false)`. Timing uses unscaled time during pause.

Clear sources with `ClearPositionSource()`, `ClearCameraSource()` and
`ClearCameraFocusSource()`. Runtime
movement overrides can be reset with `UseConfiguredMovementTracking()`.

## Teleports

```csharp
using var teleport = TestingFloor.BeginTeleport("respawn");
await MovePlayerAsync(destination);
```

For an instantaneous move, call `RecordTeleport(start, end, "respawn")` after
moving. A scope suspends movement sampling, emits one `$tf_player_teleported` event,
and resets the destination baseline. Call `teleport.Cancel()` to cancel without
an event. Positions and scalar distances are meters.

## Recording sync

The SDK creates a fresh `PlaySessionId` on each game launch or editor Play run.
`TestingFloor.Recording?.RecordingUuid` identifies a recording that can span
several Play runs. No synthetic session bookend events are emitted.

The desktop app passes `--testing-floor={"recording_uuid":"..."}` or writes
`Library/TestingFloor/recording-payload.json`. It owns the handoff and its cleanup.
A handoff file with a recording lease wins over the launch argument, even after
the lease expires. A valid launch argument wins over an older handoff file
without a lease.
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
Registration is best effort: a sandboxed build that cannot write to the directory
still attaches, watches `closed.json` and sends its completion marker.
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

See [Tests/Contract/SDK_CONTRACT.md](Tests/Contract/SDK_CONTRACT.md) for behavior
every Testing Floor SDK shares, such as the
[Unreal SDK](https://github.com/capitalisminc/testingfloor-sdk-unreal). It is a copy
of the published [SDK contract](https://github.com/capitalisminc/testingfloor-sdk-contract).
See
[CONTRIBUTING.md](CONTRIBUTING.md) for package tests and local development.
[CHANGELOG.md](CHANGELOG.md) documents the changes in each release.

MIT. See [LICENSE.md](LICENSE.md) and [ThirdPartyNotices.md](ThirdPartyNotices.md).


### Immutable builds and recording-only collection

Supply the same immutable build ID to the SDK, build upload and map publication. A readable version label may be reused; an artifact identity must change when the built artifact or output coordinate space changes. Collection still defaults to always in built games. Recording-only mode waits for an active recording that requests telemetry; discovery continues while waiting, and pauses or expired leases stop collection. Editor policy still applies.

Set `buildId` and `recordingOnly` on the TestingFloorSettings asset. Its optional `coordinateTransform` has `enabled`, `axes`, `scale`, and `offset` fields. For example, signed axes `(2,-1,3)`, scale `2`, offset `(10,20,30)` map native `(x,y,z)` to `(2y+10,-2x+20,2z+30)`. The settings inspector exposes these controls; set them before the client starts.

Axes must be a signed permutation of 1–3; scale must be positive and finite; offsets must be finite. Invalid configuration disables collection. Configuration is captured when the client starts. Object positions quantize in native units before conversion, and movement thresholds run before conversion. Directions only permute/sign axes; converted events omit native Euler fields and retain forward/up vectors. Arbitrary properties are not guessed, and the event processor runs after conversion. Map bounds, axes and units must describe the configured output space.

Automatic movement and teleport events now use `$tf_player_moved` and `$tf_player_teleported`. Update filters that used their previous automatic names. Games may still send their own `player_moved` and `player_teleported`; the public tracking API rejects reserved `$tf_` names.

Built games use the launch argument's telemetry directory to read the desktop app's renewable `recording-payload.json`. The file must name the same recording UUID. After observing a renewable lease, a missing or unreadable file retains that lease only until expiry; it cannot revive the static launch hint. Paused leases keep renewing with `telemetry_requested:false`. Recording-only mode requires a desktop app version that publishes this file; older desktop apps cannot activate that mode in built games.
