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
    "com.unity.ugui": "2.0.0",
    "com.unity.modules.unitywebrequest": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0"
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

`Tests/Contract` is an unedited copy of the published
[Testing Floor SDK contract](https://github.com/capitalisminc/testingfloor-sdk-contract),
which every Testing Floor SDK follows:

- `telemetry_contract.json` holds wire field names, types and limits. The EditMode
  contract tests compare the SDK's payloads with it.
- `SDK_CONTRACT.md` describes the shared behavior.
- `scenarios/` checks that behavior. The EditMode `ContractScenarioTests` run each
  file through the SDK's recording-lifecycle test hook, with a fake clock, a
  temporary telemetry directory and a fake collector; `scenarios/README.md`
  describes the format.

`Tests/contract-source.json` records the published commit the copy came from.
Refresh the copy with `python3 "Tools~/update_contract.py"`, which keeps existing
`.meta` files and creates them for new files, then run the EditMode and PlayMode
tests. `--check` confirms the copy still matches its recorded commit, and
`--from <folder>` tries an unpublished change. Never edit the copy to make a test
pass. Fix the SDK, or have the contract changed where it is published and copy it
again.

When changing identity, fields, defaults, limits, retry policy, or units, change
the published contract first, with a scenario that shows it, then update the SDK
and its observable regression tests. Bump the package and runtime version
together. Document breaking changes without compatibility aliases.
