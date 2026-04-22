# Testing Floor SDK for Unity

Add lightweight telemetry and recording sync to Unity games tested with Testing Floor.

## Install

Add the package to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.testingfloor.unity-sdk": "https://github.com/capitalisminc/testingfloor-sdk-unity.git"
  }
}
```

## Setup

1. In Unity, run **Tools -> Testing Floor -> Create Settings Asset**.
2. Paste your Testing Floor write key.
3. Track the moments you care about.

```csharp
using TestingFloor;

TestingFloor.Track("weapon_fire")
    .Set("weapon.id", "plasma_rifle")
    .Set("damage", 42)
    .Send();
```

That is enough for basic telemetry. Testing Floor will associate events with the current playtest session when the game is launched from Testing Floor.

## Recording Sync

For recordings made outside the Testing Floor recorder, enable the sync QR:

```csharp
TestingFloor.SetQrHeartbeatsEnabled(true);
```

The QR appears in the top-right of the game view and helps Testing Floor line up video with telemetry.

You can also enable it in `TestingFloorSettings` with `qrHeartbeatsEnabled`.

## Game Context

Use a context provider for state that should appear on every event:

```csharp
public sealed class MyGameContextProvider : ITelemetryContextProvider {
    public void FillSnapshot(ref ContextSnapshot snapshot) {
        snapshot.Set("level.id", Level.Current?.Id);
        snapshot.Set("player.hp_percent", Player.Current?.HpPercent ?? 0);
    }
}

TestingFloor.RegisterContextProvider(new MyGameContextProvider());
```

Event-specific `.Set(...)` values win over context values with the same key.

## Useful Calls

```csharp
await TestingFloor.FlushAsync(TimeSpan.FromSeconds(2));

TestingFloor.SetQrHeartbeatsEnabled(false);
TestingFloor.UseConfiguredQrHeartbeats();
```

## Advanced

`TestingFloorSettings` lives at `Assets/Resources/TestingFloorSettings.asset`.

Most games only need `writeKey` and optionally `qrHeartbeatsEnabled`. The default endpoint is already set for Testing Floor.

Testing Floor launchers can pass session data through either:

- `--testing-floor={json}`
- `{projectRoot}/Library/TestingFloor/session-payload.json`

For local SDK development, use a file dependency:

```json
{
  "dependencies": {
    "com.testingfloor.unity-sdk": "file:../../testingfloor-sdk-unity"
  }
}
```

## License

MIT. See [LICENSE.md](LICENSE.md).
