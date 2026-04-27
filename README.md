# Testing Floor SDK for Unity

Add Testing Floor telemetry and recording sync to a Unity game.

## Install

In Unity:

1. Open **Window -> Package Manager**.
2. Click **+**.
3. Choose **Add package from git URL...**.
4. Paste:

```text
https://github.com/capitalisminc/testingfloor-sdk-unity.git
```

Then run **Tools -> Testing Floor -> Create Settings Asset** and paste your Testing Floor write key.

## Track Events

```csharp
using TestingFloor;

TestingFloor.Track("weapon_fire")
    .Set("weapon.id", "plasma_rifle")
    .Set("damage", 42)
    .Send();
```

`Set` accepts `string`, `long` (and `int`), `double` (and `float`), `bool`, `string[]`, `int[]`, and `Guid`. For optional values, `SetIfPresent` accepts `string`, `int?`, `long?`, `float?`, `double?`, and `bool?` and skips when null. Other types — enums, `decimal`, `DateTime`, custom structs — are a compile error; convert at the call site (e.g. `weapon.ToString()`, `(long)timestamp.Ticks`) so the type sent over the wire is explicit.

Add game state that should be attached to every event:

```csharp
public sealed class MyGameContextProvider : ITelemetryContextProvider {
    public void FillSnapshot(ref ContextSnapshot snapshot) {
        snapshot.Set("level.id", Level.Current?.Id);
        snapshot.Set("player.hp_percent", Player.Current?.HpPercent ?? 0.0);
    }
}

public static class TestingFloorBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() {
        TestingFloor.RegisterContextProvider(new MyGameContextProvider());
    }
}
```

## Player & Camera Context

Hand the SDK a player transform and a camera, and every event will automatically carry position, camera pose, camera FOV, and viewport size — no per-call wiring:

```csharp
TestingFloor.SetPositionSource(playerTransform);
TestingFloor.UseMainCamera();
```

These attach `player.position.{x,y,z}`, `camera.position.{x,y,z}`, `camera.euler.{x,y,z}`, `camera.fov`, `viewport.width`, and `viewport.height` to every event. If you don't have a `Transform` handy, pass a `Func<Vector3?>` instead; return `null` to omit the player keys from that event. Same for the camera with `SetCameraSource(Func<Camera>)`. Clear with `ClearPositionSource()` / `ClearCameraSource()`.

## Movement Events

Once a position source is registered, you can opt into a debounced `player_moved` event that won't blast one event per frame:

```csharp
TestingFloor.SetMovementTrackingEnabled(true);
```

The event carries:

- `movement.reason` — `start` (player just began moving), `ping` (still moving — emitted on a debounce), or `stop` (movement ended)
- `movement.distance` — meters covered in the segment this event closes (segment-since-last-ping for `ping`, full run for `stop`)
- `movement.duration` — seconds elapsed in the same segment

Defaults are tuned for a 1-unit-per-meter humanoid character at roughly 2 path samples per second. Tune them on the settings asset if your scale or pacing is different:

- `movementMinPingDistance` (default `0.5`)
- `movementMinPingIntervalSeconds` (default `0.5`)
- `movementStartGraceSeconds` (default `0.05`)
- `movementStopGraceSeconds` (default `0.25`)
- `movementMinStep` (default `0.005`) — sub-step jitter ignored

You can also flip `movementTrackingEnabled` on the settings asset to make the project's default opt-in. `TestingFloor.UseConfiguredMovementTracking()` reverts the runtime override back to the asset's value.

## Recording Sync

If testers record with OBS, browser capture, or another external recorder, enable the visible sync QR before recording starts:

```csharp
TestingFloor.SetQrHeartbeatsEnabled(true);
```

Turn it off when the recording flow ends:

```csharp
TestingFloor.SetQrHeartbeatsEnabled(false);
```

The QR appears in the top-right of the game view and helps Testing Floor line up the video with telemetry. If testers use the Testing Floor recorder, you usually do not need to enable it manually.

When enabled, the QR changes every 15 seconds by default. Timing controls exist for unusual capture setups, but most projects should leave them alone.

The QR is inverted by default. Use normal black-on-white rendering if your recorder or scanner prefers it:

```csharp
TestingFloor.SetQrHeartbeatInverted(false);
```

You can also set `qrHeartbeatsEnabled` and `qrHeartbeatInverted` in `TestingFloorSettings` for builds where the QR should always follow the asset setting.

## Performance

- `Track(...).Send()` is lightweight and queues events in memory.
- Calling `.Set(...)` stores event properties; avoid large objects or per-frame spam.
- Context providers run for every event, so keep them cheap.
- Network work happens when the SDK flushes queued events. The sender bundles up to 50 events per HTTP request with a 0.25 s flush window. Tune `batchMaxEvents` and `batchMaxFlushIntervalSeconds` on the settings asset; set the interval to `0` to send each event in its own request.
- The QR overlay is disabled by default. When enabled, it renders cheaply but regenerates the QR texture on its refresh interval.

## Common Calls

```csharp
await TestingFloor.FlushAsync(TimeSpan.FromSeconds(2));

TestingFloor.SetPositionSource(playerTransform);
TestingFloor.UseMainCamera();
TestingFloor.SetMovementTrackingEnabled(true);

TestingFloor.SetQrHeartbeatsEnabled(true);
TestingFloor.SetQrHeartbeatsEnabled(false);
TestingFloor.UseConfiguredQrHeartbeats();
TestingFloor.SetQrHeartbeatInverted(false);
TestingFloor.UseConfiguredQrHeartbeatColors();
```

## Advanced

`TestingFloorSettings` lives at `Assets/Resources/TestingFloorSettings.asset`. Most games only need `writeKey` and optionally `qrHeartbeatsEnabled` / `qrHeartbeatInverted`.

QR heartbeat timing is an advanced sync setting. The default interval is 15 seconds, and tuning it is not usually recommended unless Testing Floor support asks you to adjust it.

Testing Floor launchers can pass session data with `--testing-floor={json}` or `{projectRoot}/Library/TestingFloor/session-payload.json`.

For local SDK development, add this to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.testingfloor.unity-sdk": "file:../../testingfloor-sdk-unity"
  }
}
```

## License

MIT. See [LICENSE.md](LICENSE.md).
