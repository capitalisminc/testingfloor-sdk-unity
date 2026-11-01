# Contributing

Keep public source, documentation, examples, and discussions free of private
project information and credentials. Use synthetic fixtures. See [AGENTS.md](AGENTS.md).

## Run package tests

Use a minimal Unity 6 host project. Add this package as a local dependency in
`Packages/manifest.json` and mark it testable:

```json
{
  "dependencies": {
    "com.testingfloor.unity-sdk": "file:../../testingfloor-sdk-unity",
    "com.unity.test-framework": "1.6.0",
    "com.unity.ugui": "2.0.0"
  },
  "testables": ["com.testingfloor.unity-sdk"]
}
```

Use the Test Runner for **both EditMode and PlayMode**. EditMode covers the
inspector and the telemetry contract; PlayMode covers runtime payloads, delivery,
movement, teleports, and QR behavior. From the command line, run each platform separately:

```sh
Unity -batchmode -nographics -projectPath /path/to/SdkTestHost \
  -runTests -testPlatform EditMode -testResults editmode.xml -logFile editmode.log
Unity -batchmode -nographics -projectPath /path/to/SdkTestHost \
  -runTests -testPlatform PlayMode -testResults playmode.xml -logFile playmode.log
```

Automation does not send live telemetry. Queue tests use injected mock senders;
QR decoding uses the test-only ZXing dependency. CI checks package metadata;
licensed Unity test runs must be performed before release.

Follow Unity's [package layout](https://docs.unity3d.com/6000.3/Documentation/Manual/cus-layout.html)
and [package test guidance](https://docs.unity3d.com/6000.3/Documentation/Manual/cus-tests.html).
Keep `.meta` files with every imported source or document. Runtime transport and
serialization types stay internal; expose only the API game integrations need.

## Change the SDK contract

Wire field names, types and limits come from the Testing Floor telemetry
contract. This package keeps an unedited copy at
[Tests/Contract/telemetry_contract.json](Tests/Contract/telemetry_contract.json),
and the EditMode contract tests compare the SDK's payloads with it. Do not edit
the copy to make a test pass; [Tests/Contract/README.md](Tests/Contract/README.md)
explains how to refresh it.

Keep [SDK_CONTRACT.md](SDK_CONTRACT.md) identical to the public Unreal SDK copy.
When changing identity, fields, defaults, limits, retry policy, or units, update
both implementations and their observable regression tests. Bump the package and
runtime version together. Document breaking changes without compatibility aliases.
