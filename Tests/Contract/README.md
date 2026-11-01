# Telemetry contract

`telemetry_contract.json` is a verbatim copy of the Testing Floor telemetry
contract. It names every wire field, record, SDK property and limit that the
collector, the SDKs and Testing Floor agree on. The EditMode tests in
`Tests/Editor/TelemetryContractTests.cs` compare what this SDK sends with it.

Do not edit the copy here. To refresh it:

1. Replace `telemetry_contract.json` with the current published contract,
   unchanged. Keep its `.meta` file.
2. Run the EditMode tests.
3. If a test fails, the SDK and the contract disagree. Change the SDK to match,
   or, if the contract is wrong, get it fixed in the contract and copy it again.
