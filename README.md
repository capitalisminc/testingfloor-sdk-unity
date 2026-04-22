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

Add game state that should be attached to every event:

```csharp
public sealed class MyGameContextProvider : ITelemetryContextProvider {
    public void FillSnapshot(ref ContextSnapshot snapshot) {
        snapshot.Set("level.id", Level.Current?.Id);
        snapshot.Set("player.hp_percent", Player.Current?.HpPercent ?? 0);
    }
}

public static class TestingFloorBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() {
        TestingFloor.RegisterContextProvider(new MyGameContextProvider());
    }
}
```

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

You can also set `qrHeartbeatsEnabled` in `TestingFloorSettings` for builds where the QR should always follow the asset setting.

## Performance

- `Track(...).Send()` is lightweight and queues events in memory.
- Calling `.Set(...)` stores event properties; avoid large objects or per-frame spam.
- Context providers run for every event, so keep them cheap.
- Network work happens when the SDK flushes queued events.
- The QR overlay is disabled by default. When enabled, it renders cheaply but regenerates the QR texture on its refresh interval.

## Common Calls

```csharp
await TestingFloor.FlushAsync(TimeSpan.FromSeconds(2));

TestingFloor.SetQrHeartbeatsEnabled(true);
TestingFloor.SetQrHeartbeatsEnabled(false);
TestingFloor.UseConfiguredQrHeartbeats();
```

## Advanced

`TestingFloorSettings` lives at `Assets/Resources/TestingFloorSettings.asset`. Most games only need `writeKey` and optionally `qrHeartbeatsEnabled`.

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
