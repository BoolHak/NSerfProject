# Fake Test Remediation Plan

This document lists every **fake** test identified in the current C# port and describes the concrete behaviors that must be verified. Each entry references the canonical Go implementation under `serf/cmd/serf/command/agent` (or the closest upstream owner) to ensure parity.

---

## AgentEventHandlerTests.cs
- **Agent_RegisterHandler_AddsHandler**  
  - *Go reference*: `agent/event_handler_test.go::TestScriptEventHandler`  
  - *What to cover*: After calling `RegisterEventHandler`, trigger a `MemberEvent` and assert that the registered handler receives it (e.g., inspect a capturing handler’s queue length or payload contents).

- **Agent_RegisterMultipleHandlers_AllSupported**  
  - *Go reference*: `agent/event_handler_test.go::TestEventScriptInvoke` (multiple filters)  
  - *What to cover*: Register two distinct handlers, raise a broadcast event, and assert both handlers receive the dispatch in the order expected.

- **Agent_RegisterDuplicateHandler_OnlyAddedOnce**  
  - *Go reference*: `agent/event_handler_test.go::TestEventScriptInvoke` (no duplicate invocation)  
  - *What to cover*: Register the same handler twice, emit an event, and prove it fires once (e.g., check handler invocation count == 1 or registry snapshot count == 1).

- **Agent_DeregisterHandler_RemovesHandler**  
  - *Go reference*: `agent/event_handler_test.go::TestScriptEventHandler` (implicit deregister in tests)  
  - *What to cover*: Register a handler, deregister it, emit an event, and confirm the handler receives nothing.

- **Agent_EventLoop_DispatchesToAllHandlers**  
  - *Go reference*: `agent/event_handler_test.go::TestScriptEventHandler`  
  - *What to cover*: Start the agent with multiple handlers, emit a Serf event (e.g., via `SerfEventChannel`), and verify that each handler’s receive buffer contains the dispatched event.

- **Agent_HandlerException_DoesNotStopLoop**  
  - *Go reference*: `agent/event_handler_test.go::TestScriptEventHandler` (script failure still allows more events)  
  - *What to cover*: Register one handler that throws and another that records events; after injecting a faulting event, emit another event and assert the healthy handler still receives it.

## AgentLifecycleTests.cs
- **Agent_Leave_InitiatesGracefulShutdown**  
  - *Go reference*: `agent/rpc_client_test.go::TestRPCClientLeave`  
  - *What to cover*: After `LeaveAsync`, poll `Serf.State()` until it transitions to `SerfState.SerfLeft` and confirm the local member advertises `left` status.

- **Agent_ShutdownBeforeLeave_Works**  
  - *Go reference*: `agent/rpc_client_test.go::TestRPCClientShutdown` (indirect)  
  - *What to cover*: Call `ShutdownAsync` before `LeaveAsync` and assert `_serf == null`, `_disposed == true`, and no background goroutines remain (e.g., membership list empty).

- **Agent_ShutdownBeforeStart_Works**  
  - *Go reference*: `agent/agent.go::Shutdown` (allowed pre-start)  
  - *What to cover*: Invoke `ShutdownAsync` on a freshly constructed agent and verify it is idempotent (no new tasks created, `Serf` still null, no log spam).

## AgentKeyringTests.cs
- **Agent_LoadKeyringFile_OnCreate**  
  - *Go reference*: `agent/rpc_client_test.go::TestRPCClient_Keys`  
  - *What to cover*: Provide a serialized keyring file, start the agent, and confirm the `KeyManager` contains every key from disk.

- **Agent_InstallKey_UpdatesKeyringFile**  
  - *Go reference*: `agent/rpc_client_test.go::TestRPCClient_Keys`  
  - *What to cover*: Call `InstallKeyAsync`, then inspect both the keyring file and `KeyManager` state to ensure the new key appears and is counted once.

- **Agent_RemoveKey_UpdatesKeyringFile**  
  - *Go reference*: `agent/rpc_client_test.go::TestRPCClient_Keys`  
  - *What to cover*: Remove a non-primary key, verify it disappears from `KeyManager` and persisted keyring, and ensure primary key removal returns the correct error.

- **Agent_ListKeys_ReturnsAllKeys**  
  - *Go reference*: `agent/rpc_client_test.go::TestRPCClient_Keys`  
  - *What to cover*: Install multiple keys, call the C# list API, and confirm the tuple `(keys, numServers)` mirrors the Go RPC output.

## AgentMdnsTests.cs
- **AgentMdns_Constructor_SetsServiceName**, **AgentMdns_Start_DoesNotThrow**, **AgentMdns_DiscoverPeers_RespectsTimeout**, **AgentMdns_CustomDomain_Accepted**  
  - *Go reference*: `command/command_test.go::TestCommandRun_mDNS`  
  - *What to cover*: Spin up two agents with mDNS enabled, wait for discovery, and assert both see each other. Add positive cases for custom domains and timeouts, plus negative cases for stopped discoverer.

## SerfAgentVerificationTests.cs
- **Agent_RegisterHandler_RebuildsHandlerList**  
  - *Go reference*: `agent/event_handler_test.go::TestEventScriptHandler`  
  - *What to cover*: After registering/deregistering handlers, inspect the snapshot (or send user events) to confirm the backing array is rebuilt without stale entries.

- **Agent_DeregisterHandler_RemovesHandler**  
  - *Go reference*: `agent/event_handler_test.go::TestEventScriptHandler`  
  - *What to cover*: Deregister a handler, emit an event, and assert only active handlers receive it.

## ConfigReloadTests.cs
- **ConfigReload_UpdatesLogLevel**, **ConfigReload_PreservesConnection**, **ConfigReload_InvalidConfig_Rejected**, **ConfigReload_UpdatesEventScripts**, **ConfigReload_WithoutRestart_AgentContinues**  
  - *Go reference*: `agent/command.go::handleReload`  
  - *What to cover*: Exercise the reload entrypoint: start a long-lived agent, call reload with new config payloads, assert log level changes, scripts hot-swap, invalid config returns an error, and RPC connections remain intact.

## CircularLogWriterTests.cs
- **CircularLogWriter_BuffersLogs**  
  - *Go reference*: `agent/log_writer_test.go::TestLogWriter`  
  - *What to cover*: Fill the buffer, register a handler, verify the backlog (minus evicted entries) is replayed before live writes continue.

## GatedWriterTests.cs
- **GatedWriter_BuffersUntilFlush**, **GatedWriter_PassesThroughAfterFlush**, **GatedWriter_Reset_ClearsBuffer**, **GatedWriter_Dispose_FlushesBuffer**, **GatedWriter_ThreadSafe_ConcurrentWritesAsync**  
  - *Go reference*: `agent/gated_writer_test.go::TestGatedWriter`  
  - *What to cover*: Reproduce Go’s gating behavior—ensure pre-flush writes are buffered, flush empties queue, reset clears backlog, dispose flushes once, and concurrent writers emit ordered output without data races.

## LogWriterTests.cs
- **LogWriter_FiltersBasedOnLevel**, **LogWriter_AllowsUnprefixedLines**, **LogWriter_LevelFiltering_CountsCorrectly**, **LogWriter_ThreadSafe_ConcurrentWritesAsync**  
  - *Go reference*: `agent/log_writer_test.go::TestLogWriter`  
  - *What to cover*: Validate regex-based filtering, backlog retention, and thread safety; compare handler outputs to expectations from Go tests (e.g., ensure old entries drop when buffer full, prefix filtering works).

## ScriptEventHandlerTests.cs
- **ScriptEventHandler_UpdateScripts_HotReloadsOnNextEvent**, **ScriptEventHandler_FilterMatching_OnlyExecutesMatchingScripts**, **ScriptEventHandler_MultipleScripts_AllExecute**  
  - *Go reference*: `agent/event_handler_test.go::TestEventScriptHandler` & `TestEventScriptInvoke`  
  - *What to cover*: Attach file-backed scripts, fire events, and assert only matching scripts execute. After updating scripts, confirm the next event runs the new script set.

## SignalHandlerTests.cs
- **SignalHandler_RegisterCallback_InvokesOnSignal**, **SignalHandler_MultipleCallbacks_AllInvoked**, **SignalHandler_CallbackException_DoesNotStopOthers**, **SignalHandler_Dispose_CleansUpHandlers**  
  - *Go reference*: `agent/command.go::handleSignals`  
  - *What to cover*: Use real OS signals (or platform abstractions) to ensure registered callbacks trigger, multiple callbacks all run, exceptions don’t prevent subsequent callbacks, and dispose removes handlers so future signals are ignored.

## SignalHandlingIntegrationTests.cs
- **SignalHandler_SIGINT_TriggersGracefulShutdown**, **SignalHandler_SIGTERM_TriggersConfiguredShutdown**, **SignalHandler_SIGHUP_TriggersConfigReload**, **SignalHandler_DoubleSignal_ForcesShutdown**  
  - *Go reference*: `agent/command.go::handleSignals` (integration behavior)  
  - *What to cover*: Run `AgentCommand` as the Go CLI does, send real signals, and assert the corresponding branch executes (graceful leave, leave-on-term, reload invocation, forced shutdown after second signal).

## RpcStreamingTests.cs
- **RpcServer_MonitorCommand_Exists**, **RpcServer_StreamCommand_Exists**, **CircularLogWriter_Integration_WithAgentAsync**, **RpcEventHandler_ReceivesBacklog**  
  - *Go reference*: `agent/rpc_client_test.go::TestRPCClientMonitor`, `::TestRPCClientStream`, `::TestRPCClientStream_Member`  
  - *What to cover*: Execute the actual monitor/stream RPCs, assert log output and backlog events arrive, and confirm stream handlers receive replayed history plus live updates.

## CoordinateIntegrationTests.cs
- **Coordinate_TwoNodes_CalculatesRTT**  
  - *Go references*: `agent/rpc_client_test.go::TestRPCClientGetCoordinate`, `command/rtt_test.go::TestRTTCommand_Run`  
  - *What to cover*: Join two agents, wait for coordinate gossip, and assert both coordinates exist and `DistanceTo` returns a plausible RTT. Handle the timeout case explicitly (e.g., mark inconclusive vs. failure).

## RttCommandTests.cs
- **RttCommand_WithValidNode_ReturnsCoordinate**  
  - *Go reference*: `command/rtt_test.go::TestRTTCommand_Run`  
  - *What to cover*: Execute the RTT command end-to-end, assert the command exits 0, output includes the expected node line with computed RTT, and ensure it fails if coordinates never materialize (no silent skips).

## MemberCommandExtendedTests.cs
- **Members_FilterByStatus_Alive**, **Members_FilterByName**, **Members_FilterByTags**  
  - *Go references*: `agent/rpc_client_test.go::TestRPCClientMembersFiltered`, `command/members_test.go::TestMembersCommandRun_statusFilter`  
  - *What to cover*: Stand up a multi-node cluster, invoke the CLI or RPC filters, and validate only the expected members appear (and excluded members are absent). Include failure cases (e.g., filtering by a non-existent tag).

## ConfigReloadTests.cs (CLI)
- **ConfigReload_UpdatesLogLevel**, **ConfigReload_PreservesConnection**, **ConfigReload_InvalidConfig_Rejected**, **ConfigReload_UpdatesEventScripts**, **ConfigReload_WithoutRestart_AgentContinues**  
  - *Go reference*: `agent/command.go::handleReload`  
  - *What to cover*: Drive the reload path by writing an updated config file, sending SIGHUP (or reload RPC), and asserting log level changes, event scripts refresh, invalid configs emit errors, and RPC clients remain connected.

## AgentMdnsTests.cs (additional fake cases)
- Covered earlier; ensure discovery and timeout behavior are asserted rather than skipped.

## AgentKeyringTests.cs (additional fake cases)
- Covered earlier; ensure key operations mutate both in-memory and persisted key stores exactly as Go tests expect.

---

Focus on making each rewritten test observable—exercise the same side-effects and assertions present in the Go suite so regressions cannot hide behind "no exception" passes.
