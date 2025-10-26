# Suspect Tests in NSerf.CLI.Tests

| Test | Why it looks fake or inconclusive | Evidence |
| --- | --- | --- |
| `RpcClientIntegrationTests.RpcClient_UserEvent_DispatchesSuccessfully` | The only assertion is `Assert.True(true)`, so the test succeeds even if the RPC call never dispatched an event. | @NSerf/NSerf.CLI.Tests/Commands/RpcClientIntegrationTests.cs#178-191 |
| `RpcClientIntegrationTests.RpcClient_Query_DispatchesAndReceivesAck` | No assertion verifies the result; the call returning without throwing is the only signal, so failures that still return (e.g. wrong response content) go unnoticed. | @NSerf/NSerf.CLI.Tests/Commands/RpcClientIntegrationTests.cs#310-333 |
| `RpcClientIntegrationTests.RpcClient_GetCoordinate_ReturnsCoordinateForExistingNode` | Lacks any checks on the returned coordinate, so the test passes even if the RPC handler returns `null` or bogus data. | @NSerf/NSerf.CLI.Tests/Commands/RpcClientIntegrationTests.cs#339-351 |
| `AgentLifecycleIntegrationTests.Agent_RetryJoin_EventuallySucceeds` | Asserts only that the cluster has ≥ 1 member—the local node satisfies this even when retry join never succeeds. | @NSerf/NSerf.CLI.Tests/Commands/AgentLifecycleIntegrationTests.cs#111-142 |
| `CoordinateIntegrationTests.Coordinate_TwoNodes_CalculatesRTT` | Exits early when the remote coordinate is `null`, meaning the primary RTT assertion never runs and failures are silently ignored. | @NSerf/NSerf.CLI.Tests/Commands/CoordinateIntegrationTests.cs#52-82 |
| `RttCommandTests.RttCommand_WithValidNode_ReturnsCoordinate` | Treats command failures as acceptable by asserting on the error output and returning, so the test passes even when the command cannot fetch coordinates. | @NSerf/NSerf.CLI.Tests/Commands/RttCommandTests.cs#22-75 |
