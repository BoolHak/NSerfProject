# Suspect Test Investigation (DeepWiki Corroborated)

This document records what the Go reference suite actually verifies and how the current C# ports fall short. Use it to prioritize fixes.

## 1. `RpcClientIntegrationTests.RpcClient_UserEvent_DispatchesSuccessfully`
- **Observed in C#**: Only calls `UserEventAsync` then asserts `Assert.True(true)`, so the test always passes.@NSerf/NSerf.CLI.Tests/Commands/RpcClientIntegrationTests.cs#178-191
- **Go expectation**: `TestRPCClientUserEvent` registers a mock handler and asserts that it receives the `"deploy"` event with payload `"foo"`.
- **Verdict**: **Fake test.** Missing handler verification.
- **Fix**: Register a mock handler in the fixture and assert receipt of the correct event payload.

## 2. `RpcClientIntegrationTests.RpcClient_Query_DispatchesAndReceivesAck`
- **Observed in C#**: Invokes `QueryAsync` but performs no assertions on acks or responses.@NSerf/NSerf.CLI.Tests/Commands/RpcClientIntegrationTests.cs#310-333
- **Go expectation**: `TestRPCClientQuery` asserts query handler receipt, checks ack channel for the local node, and verifies the response payload.
- **Verdict**: **Fake test.** Provides zero coverage.
- **Fix**: Capture the returned sequence, await ack/response streams, and assert payloads just like the Go test.

## 3. `RpcClientIntegrationTests.RpcClient_GetCoordinate_ReturnsCoordinateForExistingNode`
- **Observed in C#**: Calls `GetCoordinateAsync` and ignores the result entirely.@NSerf/NSerf.CLI.Tests/Commands/RpcClientIntegrationTests.cs#339-351
- **Go expectation**: Asserts non-nil coordinate for existing node and nil for nonexistent node.
- **Verdict**: **Fake test.** No validation of handler behavior.
- **Fix**: Assert the existing node returns a coordinate and explicitly test the `null` case.

## 4. `AgentLifecycleIntegrationTests.Agent_RetryJoin_EventuallySucceeds`
- **Observed in C#**: Verifies only that member count is `>= 1`, satisfied by the local node even if retry join never hits.@NSerf/NSerf.CLI.Tests/Commands/AgentLifecycleIntegrationTests.cs#111-142
- **Go expectation**: CLI test asserts cluster membership count is exactly two after retry join succeeds.
- **Verdict**: **Fake test.** Cannot detect retry-join failure.
- **Fix**: Assert two unique members with retry instrumentation (or fail when still single-node after timeout).

## 5. `CoordinateIntegrationTests.Coordinate_TwoNodes_CalculatesRTT`
- **Observed in C#**: Returns early when the remote coordinate is `null`, skipping RTT assertion.@NSerf/NSerf.CLI.Tests/Commands/CoordinateIntegrationTests.cs#52-82
- **Go expectation**: Similar eventual-consistency skip exists; the Go suite tolerates missing coordinates when gossip hasn't converged yet.
- **Verdict**: **Weak but acceptable.** Not a false positive—the skip mirrors upstream behavior.
- **Improvement**: Log a skip or add retry loop so failures are visible.

## 6. `RttCommandTests.RttCommand_WithValidNode_ReturnsCoordinate`
- **Observed in C#**: Treats command failure as acceptable, returning early after detecting an error message.@NSerf/NSerf.CLI.Tests/Commands/RttCommandTests.cs#22-75
- **Go expectation**: The exact CLI test isn’t present in the repo snapshot; upstream behavior is unclear.
- **Verdict**: **Inconclusive.** Probably should assert success but needs authoritative reference.
- **Fix**: Once upstream coverage is located, align with expected success criteria (likely ensure RTT output prints when coordinates available).

---
**Next actions**
1. Patch the four confirmed fake tests to match Go assertions.
2. Enhance coordinate/RTT tests to report when conditions cause early exit.
3. Track down missing Go RTT CLI test (or recreate coverage) to remove ambiguity.
