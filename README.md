# Testing Floor SDK for Unity

Telemetry and recording-sync primitives for Unity games using Testing Floor.

## Install

Add the package to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.testingfloor.unity-sdk": "https://github.com/capitalisminc/testingfloor-sdk-unity.git"
  }
}
```

For local development:

```json
{
  "dependencies": {
    "com.testingfloor.unity-sdk": "file:../../testingfloor-sdk-unity"
  }
}
```

## Setup

1. In Unity, run **Tools -> Testing Floor -> Create Settings Asset**.
2. Enter your Testing Floor write key.
3. Send an event:

```csharp
using TestingFloor;

TestingFloor.Track("weapon_fire")
    .Set("weapon.id", "plasma_rifle")
    .Set("damage", 42)
    .Send();
```

Flush before quit if needed:

```csharp
await TestingFloor.FlushAsync(TimeSpan.FromSeconds(2));
```

## Context

Register a context provider to add game state to every event:

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

## Sessions

The SDK can pick up a Testing Floor session from either:

- `--testing-floor={json}` command-line argument
- `{projectRoot}/Library/TestingFloor/session-payload.json`

Example payload:

```json
{
  "session_id": "tf_abc123",
  "playtest_id": 123,
  "created_at_unix_ms": 1711234567890
}
```

When a session is present, the SDK sends `tf_session_start` and `tf_session_end`.
If no session is provided, events still use a generated session id.

## Recording QR

Games can opt into a visible recording-sync QR:

```csharp
TestingFloor.SetQrHeartbeatsEnabled(true);
```

The QR appears top-right in the game view and in recordings. It encodes:

```text
tfqr://sync/v1?s=<session_id>&t=<unix_ms>&q=<sequence>
```

Disable it with:

```csharp
TestingFloor.SetQrHeartbeatsEnabled(false);
```

Or return to the settings asset value:

```csharp
TestingFloor.UseConfiguredQrHeartbeats();
```

## Settings

`TestingFloorSettings` is loaded from `Resources/TestingFloorSettings`.

| Field | Default | Purpose |
| --- | --- | --- |
| `enabled` | `true` | Master telemetry switch |
| `enableInEditor` | `false` | Send telemetry in Play Mode |
| `writeKey` | required | Testing Floor write key |
| `endpoint` | `https://dataentry.testingfloor.com` | Collector URL |
| `qrHeartbeatsEnabled` | `false` | Enable QR from settings |
| `qrHeartbeatIntervalSeconds` | `30` | QR payload refresh interval |
| `qrHeartbeatVisibleSeconds` | `0` | `0` means always visible |

## License

MIT. See [LICENSE.md](LICENSE.md).
