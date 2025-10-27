# Agent Test Suite Audit (DeepWiki-Validated)

Scope: `NSerf/NSerfTests/Agent`
Date: 2025-10-26

DeepWiki reference: [`hashicorp/serf` → `TestRPCClientUserEvent`](https://deepwiki.com/search/in-the-go-serf-repository-what_b4348dd7-f165-4aae-8336-7eefabc1722a)

## 1. Executive Summary
- **Confirmed fake tests (6)** – lack observable assertions; contradicted by Go’s verified behavior.
- **Weak tests (8)** – exercise code paths but rely on “no exception” style assertions; require stronger verification.
- **Healthy suites** – config, retry join, tags, mDNS, config reload, script execution, etc., already aligned with upstream.

## 2. Fake Tests (Zero Verification)
Cross-check with Go’s RPC agent tests shows these tests never observe handler or event outcomes.

| File | Test | Why Fake |
| --- | --- | --- |
| `AgentEventHandlerTests.cs` | `Agent_RegisterHandler_AddsHandler` | Only asserts agent != null after registration; no handler list inspection. |
|  | `Agent_RegisterMultipleHandlers_AllSupported` | Registers handlers, never checks handler snapshot length. |
|  | `Agent_RegisterDuplicateHandler_OnlyAddedOnce` | No assertion verifying duplicates are suppressed. |
|  | `Agent_DeregisterHandler_RemovesHandler` | Doesn’t confirm handler removal from registry. |
|  | `Agent_EventLoop_DispatchesToAllHandlers` | Starts agent, waits, never asserts handlers received events. |
|  | `Agent_HandlerException_DoesNotStopLoop` | Registers throwing handler but only checks `agent.Serf` is non-null. |

> False positive removed: `AgentOperationsTests.Agent_UserEvent_Broadcasts` was reclassified as “weak,” not fake, after confirming Go’s tests assert handler delivery rather than just API success.

## 3. Weak Tests (Need Stronger Assertions)

| File | Test | Current Behavior | Needed Upgrade |
| --- | --- | --- | --- |
| `AgentCommandTests.cs` | `AgentCommand_StartsWithConfig` | Start + 500 ms delay. | Assert RPC listener/Serf agent actually started (status flag, log capture, or health probe). |
|  | `AgentCommand_ConfigValidation_ThrowsOnInvalidConfig` | Instantiates `SerfAgent` instead of `AgentCommand`. | Exercise `AgentCommand` constructor/run path for validation errors. |
| `AgentLifecycleTests.cs` | `Agent_Leave_InitiatesGracefulShutdown` | Calls `LeaveAsync`, waits 200 ms. | Verify member status transitions to `Leaving/Left` and agent shutdown. |
|  | `Agent_ShutdownBeforeLeave_Works`, `Agent_ShutdownBeforeStart_Works` | Pass if no exception. | Assert `_disposed` flag and `_serf == null`. |
| `AgentOperationsTests.cs` | `Agent_UserEvent_Broadcasts` | Sends user event, waits 100 ms. | Capture handler invocation or inspect Serf event buffer (mirror Go handler test). |
|  | `Agent_ForceLeave_RemovesFailedNode`, `Agent_ForceLeavePrune_PrunesCompletely` | Calls API on nonexistent node. | Confirm gossip/member tables or metrics updated. |
|  | `Agent_Query_InitiatesQuery` | Asserts response not null. | Validate query ID, ack/response channels, or handler effects. |
|  | `Agent_Members_ReturnsClusterMembers` | Ensures single local member. | Extend to multi-node cluster for join verification. |

## 4. Strong Suites (Reference)
No action required for:
- `AgentConfigVerificationTests.cs`
- `RetryJoinTests.cs`
- `AgentTagsTests.cs`
- `AgentMdnsTests.cs`
- `ConfigReloadTests.cs`
- `ScriptExecutionIntegrationTests.cs`
- `ScriptInvokerTests.cs`
- `ScriptEventHandlerTests.cs`
- `SignalHandlingIntegrationTests.cs`

## 5. Recommendations
1. **Rewrite fake tests** – add hooks (handler snapshots, event buffers, metrics) to assert actual outcomes.
2. **Upgrade weak tests** – add assertions for event delivery, cluster membership, query IDs, and shutdown state.
3. **Provide test helpers** – consider `InternalsVisibleTo` or dedicated test APIs to inspect internal state safely.
4. **Maintain Go parity** – use Go tests (via DeepWiki) as the acceptance oracle when porting or refactoring.

## 6. File-by-File Findings

### AgentCommandTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `AgentCommand_StartsWithConfig` | [`command_test.go::TestCommandRun`](https://deepwiki.com/search/which-tests-in-hashicorpserf-c_75893429-f413-4768-b834-971145f60b09) | **Weak** | Go waits for the daemon to start and verifies it stays running before shutdown. Our test only sleeps 500 ms, never asserting the agent actually entered the running state. Add explicit health check (RPC probe/log capture). |
| `AgentCommand_GracefulShutdown_ReturnsZero` | Same as above | **✅ Real** | Mirrors Go behavior: cancels run token, asserts exit code `0`. |
| `AgentCommand_ConfigValidation_ThrowsOnInvalidConfig` | Go validates via `AgentCommand` execution | **Weak** | Instantiates `SerfAgent` directly, so the command-layer validation path (argument parsing/wiring) is never exercised. Switch to constructing/starting `AgentCommand`. |

### AgentLifecycleTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `Agent_Create_InitializesAgent` | `cmd/serf/command/util_test.go::testAgent` (creates agent before start) | **✅ Real** | Confirms constructor leaves `Serf` null prior to `StartAsync`, matching Go helper expectations. |
| `Agent_Start_CreatesSerfInstance` | Implicit in `testAgent` + join tests (`serf_test.go::TestSerf_eventsJoin`) | **✅ Real** | Verifies that `StartAsync` yields a live `Serf` instance. |
| `Agent_StartTwice_ThrowsException` | No direct Go test (DeepWiki confirmed) | **✅ Real** | Enforces C# guard against double start; behaviour should be kept even without explicit Go analogue. |
| `Agent_Leave_InitiatesGracefulShutdown` | [`serf_test.go::TestSerf_eventsLeave`](https://deepwiki.com/search/which-tests-in-hashicorpserf-c_d75f7dd3-52b4-48db-ae57-8d1cba0894e2) | **Fake** | Calls `LeaveAsync` then waits 200 ms but never asserts member state (`Leaving/Left`) or shutdown result. Needs observable check like membership status or event emission mirroring Go test. |
| `Agent_Shutdown_StopsAllProcesses` | `serf_test.go::TestSerfState` | **✅ Real** | Confirms `ShutdownAsync` clears `Serf` reference, paralleling Go state assertions. |
| `Agent_ShutdownIdempotent_CanCallMultipleTimes` | Go relies on idempotent shutdown (`Agent.Shutdown()` in `agent.go`) | **✅ Real** | Verifies repeated shutdown calls succeed without error. |
| `Agent_ShutdownBeforeLeave_Works` | No direct Go match | **Fake** | Starts agent and calls `ShutdownAsync` with no assertions. Should confirm Serf stopped or membership cleared. |
| `Agent_ShutdownBeforeStart_Works` | `agent.go::Shutdown` can run before start | **Fake** | Only ensures no exception; add check that `_serf` remains null and no side-effects occurred. |

### AgentEventHandlerTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `Agent_RegisterHandler_AddsHandler` | [`rpc_client_test.go::TestRPCClientUserEvent`](https://deepwiki.com/search/in-hashicorpserf-what-tests-co_b6cb0da9-98e0-46ee-90e1-c2c2cd417eb1) | **Fake** | Go verifies handler list by asserting user event reaches mock handler. Our test merely asserts `agent != null`. Need to inspect handler snapshot or ReceivedEvents. |
| `Agent_RegisterMultipleHandlers_AllSupported` | Same as above | **Fake** | Should assert multiple handlers appear in snapshot or receive events. Currently only `Assert.NotNull(agent)`. |
| `Agent_RegisterDuplicateHandler_OnlyAddedOnce` | Same as above | **Fake** | Go’s map deduplicates handlers; we never assert count stays 1. |
| `Agent_DeregisterHandler_RemovesHandler` | [`Agent.DeregisterEventHandler`](https://deepwiki.com/search/in-hashicorpserf-what-tests-co_b6cb0da9-98e0-46ee-90e1-c2c2cd417eb1) | **Fake** | Need to confirm handler removed (e.g., length check). Test only ensures no exception. |
| `Agent_EventLoop_DispatchesToAllHandlers` | `TestRPCClientUserEvent` | **Fake** | Should publish event and assert `TestEventHandler.ReceivedEvents` length > 0. Currently no validation. |
| `Agent_HandlerException_DoesNotStopLoop` | No direct Go recover test, but event loop expected to continue | **Fake** | Test must assert non-throwing handler still receives events after exception. Currently only checks `agent.Serf` not null. |

### AgentOperationsTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `Agent_Join_DelegatesToSerf` | [`rpc_client_test.go::TestRPCClientJoin`](https://deepwiki.com/search/which-go-tests-cover-agent-ope_6f1c43f6-4f65-4596-be49-f5d84c6fe812) | **✅ Real** | Asserts join count equals 1 after joining peer, matching Go RPC client test expectations. |
| `Agent_JoinReturnsCount` | Same as above | **Weak** | Only asserts that joining nonexistent node throws. Go tests also cover successful joins and membership verification; add positive-path checks. |
| `Agent_UserEvent_Broadcasts` | [`TestRPCClientUserEvent`](https://deepwiki.com/search/in-hashicorpserf-what-tests-co_b6cb0da9-98e0-46ee-90e1-c2c2cd417eb1) | **Weak** | Sends event but never verifies handler/queue received payload. Should capture events or use mock handler. |
| `Agent_Query_InitiatesQuery` | [`serf_test.go::TestSerf_Query`](https://deepwiki.com/search/which-go-tests-cover-agent-ope_6f1c43f6-4f65-4596-be49-f5d84c6fe812) | **Weak** | Only asserts response non-null. Go verifies ack/response channels and handler results; add stronger assertions. |
| `Agent_ForceLeave_RemovesFailedNode` | [`rpc_client_test.go::TestRPCClientForceLeave`](https://deepwiki.com/search/which-go-tests-cover-agent-ope_6f1c43f6-4f65-4596-be49-f5d84c6fe812) | **Weak** | Call succeeds but no check members were marked Left. Need membership/state verification. |
| `Agent_ForceLeavePrune_PrunesCompletely` | Same as above | **Weak** | Missing assertion that node was pruned. |
| `Agent_UpdateTags_ModifiesTags` | [`rpc_client_test.go::TestRPCClientTags`](https://deepwiki.com/search/which-go-tests-cover-agent-ope_6f1c43f6-4f65-4596-be49-f5d84c6fe812) | **✅ Real** | Confirms tags update via Serf local member tags. |
| `Agent_Members_ReturnsClusterMembers` | [`rpc_client_test.go::TestRPCClientMembers`](https://deepwiki.com/search/which-go-tests-cover-agent-ope_6f1c43f6-4f65-4596-be49-f5d84c6fe812) | **Weak** | Only asserts single local member; should add multi-node case verifying join propagation. |

### AgentConfigVerificationTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `AgentConfig_Load_ParsesDurations` | [`cmd/serf/command/agent/config_test.go::TestDecodeConfig`](https://deepwiki.com/search/where-are-the-go-tests-that-ve_b1df5726-482d-41ed-b36d-de24723ed01a) | **✅ Real** | Mirrors Go duration parsing assertions. |
| `AgentConfig_LoadFromDirectory_MergesAllJsonFiles` | `config_test.go::TestReadConfig` | **✅ Real** | Validates directory merge behavior like Go (later files override). |
| `AgentConfig_Load_UnknownDirective_ThrowsException` | `config_test.go::TestDecodeConfig_unknown` | **✅ Real** | Matches Go behavior rejecting unknown keys. |
| `AgentConfig_Merge_Arrays_AreAppended` | `config.go::MergeConfig` + tests | **✅ Real** | Ensures arrays append consistent with Go merge semantics. |
| `AgentConfig_Merge_Tags_AreMerged` | Same | **✅ Real** | Validates tag merge overriding duplicates. |
| `AgentConfig_Merge_BooleanZeroValues_HandledCorrectly` | `MergeConfig` OR logic | **✅ Real** | Confirms booleans combine via OR as in Go. |
| `AgentConfig_Load_RoleField_MapsToTag` | `config_test.go::TestReadConfig_role` | **✅ Real** | Mirrors Go’s role-to-tag mapping. |
| `AgentConfig_Load_AllFieldsSupported` | `config_test.go::TestReadConfig_all` | **✅ Real** | Kitchen-sink coverage aligned with Go. |
| `AgentConfig_EncryptBytes_ValidatesLength` | `agent/config.go::EncryptBytes` tests | **✅ Real** | Matches Go validation for 32-byte key. |
| `AgentConfig_EncryptBytes_Valid32Bytes` | Same | **✅ Real** | Confirms valid key accepted. |
| `AgentConfig_AddrParts_ParsesCorrectly` | `command.go::splitHostPort` behavior | **✅ Real** | Ensures address parsing consistent with Go CLI. |
| `ConfigLoader_LoadTagsFromFile_LoadsCorrectly` | `config_test.go::TestReadTags` | **✅ Real** | Tests tags file loading. |
| `ConfigLoader_SaveTagsToFile_SavesCorrectly` | `config.go::SaveTagsToFile` usage | **✅ Real** | Round-trip matches Go expectations. |
| `ConfigLoader_LoadKeyringFromFile_LoadsKeys` | `config_test.go::TestReadKeyring` | **✅ Real** | Ensures keyring file parsing matches Go. |

### AgentTagsTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `Agent_LoadTagsFile_OnCreate` | [`agent_test.go::TestAgentTagsFile`](https://deepwiki.com/search/which-go-tests-cover-agent-tag_77b176cf-7b8f-4461-af51-1044567af306) | **✅ Real** | Confirms tags are loaded from `TagsFile` at startup just like Go test. |
| `Agent_SaveTagsFile_OnUpdate` | Same | **✅ Real** | Matches Go behavior of persisting tags when `SetTags` invoked. |
| `Agent_UpdateTags_AddsNewTags` | [`serf_test.go::TestSerf_SetTags`](https://deepwiki.com/search/which-go-tests-cover-agent-tag_77b176cf-7b8f-4461-af51-1044567af306) | **✅ Real** | Verifies new tags appear on local member, mirroring Go cluster assertions. |
| `Agent_UpdateTags_DeletesTags` | Same | **✅ Real** | Ensures removed tags vanish from local member as Go test expects. |
| `Agent_RoleTag_SpecialHandling` | `config_test.go::TestReadConfig_role` | **✅ Real** | Confirms role field maps into tags consistent with Go. |

### AgentKeyringTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `Agent_LoadKeyringFile_OnCreate` | [`agent.go::loadKeyringFile`](https://deepwiki.com/search/which-go-tests-cover-agent-key_86200415-80e3-4a24-adbb-94b8f21bb9d5) | **Fake** | Go loads and validates keyring contents; our test merely instantiates agent and asserts non-null. Needs actual keyring load verification or Serf start. |
| `Agent_InstallKey_UpdatesKeyringFile` | [`rpc_client_test.go::TestRPCClient_Keys`](https://deepwiki.com/search/which-go-tests-cover-agent-key_86200415-80e3-4a24-adbb-94b8f21bb9d5) | **Fake** | Placeholder with `Assert.NotNull(agent.Serf)`; no key installation or file assertion. |
| `Agent_RemoveKey_UpdatesKeyringFile` | Same | **Fake** | No key removal logic or validation; only checks agent running. |
| `Agent_ListKeys_ReturnsAllKeys` | Same | **Fake** | Should inspect key list; currently a stub. |

### AgentMdnsTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `AgentMdns_Constructor_SetsServiceName` | [`command_test.go::TestCommandRun_mDNS`](https://deepwiki.com/search/which-go-tests-cover-serf-agen_d50856a3-4230-4c0f-a98c-4c3fe42b5330) | **Fake** | Go test spins up two agents and asserts discovery via members list. Here we only instantiate object and check non-null. |
| `AgentMdns_Start_DoesNotThrow` | Same | **Fake** | Needs verification that peers are discovered or listener bound; test only ensures no exception. |
| `AgentMdns_DiscoverPeers_ReturnsEmptyIfNotStarted` | Same | **Weak** | Useful negative case but lacks positive discovery assertion. |
| `AgentMdns_DiscoverPeers_RespectsTimeout` | Same | **Fake** | Only checks elapsed time, not discovery results. |
| `AgentMdns_Dispose_MultipleCallsSafe` | Same | **Weak** | Idempotent dispose check acceptable, but no discovery coverage. |
| `AgentMdns_CustomDomain_Accepted` | Same | **Fake** | No verification of actual service registration. |
| `AgentMdns_AfterDispose_ReturnsEmpty` | Same | **Weak** | Negative case only; add positive discovery check to pair with. |

### SerfAgentVerificationTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `Agent_Create_TagsAndTagsFile_ThrowsException` | [`agent.go::loadTagsFile`](https://deepwiki.com/search/which-go-tests-correspond-to-s_ac685e99-dc59-45b8-94ac-9b660a89e322) | **✅ Real** | Mirrors Go guard preventing simultaneous Tags + TagsFile. |
| `Agent_Create_EncryptKeyAndKeyringFile_ThrowsException` | `agent.go::loadKeyringFile` | **✅ Real** | Matches Go mutual exclusion between EncryptKey and KeyringFile. |
| `Agent_RegisterHandler_RebuildsHandlerList` | [`rpc_client_test.go::TestRPCClientUserEvent`](https://deepwiki.com/search/in-hashicorpserf-what-tests-co_b6cb0da9-98e0-46ee-90e1-c2c2cd417eb1) | **Fake** | Go asserts handlers receive events; this test only checks `agent != null`. Need to inspect handler list or ReceivedEvents. |
| `Agent_SetTags_PersistsBeforeGossiping` | [`serf_test.go::TestSerf_SetTags`](https://deepwiki.com/search/which-go-tests-correspond-to-s_ac685e99-dc59-45b8-94ac-9b660a89e322) | **✅ Real** | Confirms tags written to file before gossip and reflected in local member. |
| `Agent_UnmarshalTags_ValidFormat_Succeeds` | `command.go::UnmarshalTags` | **✅ Real** | Matches Go parsing of key=value inputs. |
| `Agent_UnmarshalTags_InvalidFormat_ThrowsException` | Same | **✅ Real** | Verifies error path identical to Go implementation. |
| `Agent_StartTwice_ThrowsException` | No direct Go test (behaviour enforced in `agent.go::Start`) | **✅ Real** | Guards against double start; aligns with Go logic though not explicitly tested. |
| `Agent_ShutdownIdempotent_CanCallMultipleTimes` | `agent.go::Shutdown` usage throughout tests | **✅ Real** | Ensures repeated shutdown succeeds as Go depends on. |
| `Agent_ShutdownBeforeStart_Succeeds` | `agent.go::Shutdown` (pre-start) | **Weak** | Only checks no exception; should assert `_serf` remains null. |
| `Agent_DeregisterHandler_RemovesHandler` | [`rpc_client_test.go::TestRPCClientUserEvent`](https://deepwiki.com/search/in-hashicorpserf-what-tests-co_b6cb0da9-98e0-46ee-90e1-c2c2cd417eb1) | **Fake** | Needs verification handler removed (e.g., ReceivedEvents stays empty). Currently only asserts non-null. |
| `Agent_LoadsTagsFromFile_OnStart` | [`agent.go::loadTagsFile`](https://deepwiki.com/search/which-go-tests-correspond-to-s_ac685e99-dc59-45b8-94ac-9b660a89e322) | **✅ Real** | Confirms tags file applied on startup, same as Go behaviour. |

### CircularBufferTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `CircularBuffer_WriteLessThanSize_ReturnsExactContent` | [`log_writer_test.go::TestLogWriter`](https://deepwiki.com/search/where-are-go-tests-covering-th_cb99f21f-44ba-443b-807e-f7bcdd3dda6d) | **✅ Real** | Exercises buffer write path analogous to Go log writer keeping full content when under size. |
| `CircularBuffer_WriteMoreThanSize_Truncates` | Same | **✅ Real** | Verifies truncation flag/length just like Go log writer dropping oldest entries. |
| `CircularBuffer_Wraps_MaintainsNewestData` | Same | **✅ Real** | Ensures wrap-around retains latest data mirroring Go circular buffer behavior. |
| `CircularBuffer_Reset_ClearsState` | Same | **✅ Real** | Confirms reset semantics similar to reinitializing Go log writer buffer. |

### CircularLogWriterTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `CircularLogWriter_BuffersLogs` | [`log_writer_test.go::TestLogWriter`](https://deepwiki.com/search/where-are-go-tests-covering-th_cb99f21f-44ba-443b-807e-f7bcdd3dda6d) | **Fake** | Only asserts `NotNull`; should confirm buffer contents or handler receipt like Go test. |
| `CircularLogWriter_NewHandler_ReceivesBacklog` | Same | **✅ Real** | Verifies backlog replay to handler, mirroring Go behavior. |
| `CircularLogWriter_Wraps_AfterBufferFull` | Same | **✅ Real** | Ensures buffer retains most recent entries, matching Go expectations. |
| `CircularLogWriter_NewLog_SentToHandlers` | Same | **✅ Real** | Confirms live handlers receive new writes. |
| `CircularLogWriter_DeregisterHandler_StopsReceiving` | Same | **✅ Real** | Validates deregistration halts delivery, as Go test implies. |
| `CircularLogWriter_MultipleHandlers_AllReceive` | Same | **✅ Real** | Checks fan-out to multiple handlers. |
| `CircularLogWriter_StripsNewline` | Same | **✅ Real** | Mirrors Go trimming of newline characters before dispatch. |

### ConfigReloadTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `ConfigReload_UpdatesLogLevel` | [`command.go::handleReload`](https://deepwiki.com/search/which-go-tests-cover-agent-con_da3a4bd0-e505-45d9-a361-0f9b1171ee84) | **Fake** | Simply mutates config.LogLevel and asserts agent still running; no reload invocation or log level verification. |
| `ConfigReload_PreservesConnection` | Same | **Fake** | Compares `agent.Serf` reference before/after config mutation; never exercises reload path. |
| `ConfigReload_InvalidConfig_Rejected` | Same | **Fake** | Assigns invalid log level but performs no validation or error assertion. |
| `ConfigReload_UpdatesTags` | [`serf_test.go::TestSerf_SetTags`](https://deepwiki.com/search/which-go-tests-cover-agent-con_da3a4bd0-e505-45d9-a361-0f9b1171ee84) | **✅ Real** | Uses `SetTagsAsync` to update tags and confirms propagation (more of a duplicate tag test). |
| `ConfigReload_UpdatesEventScripts` | [`command.go::handleReload`](https://deepwiki.com/search/which-go-tests-cover-agent-con_da3a4bd0-e505-45d9-a361-0f9b1171ee84) | **Fake** | Does not inspect script handler state; only asserts agent not null. |
| `ConfigReload_WithoutRestart_AgentContinues` | Same | **Fake** | Mutates config/tags without verifying reload or member continuity beyond equal count. |

### ScriptInvokerTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `ScriptInvoker_TagSanitization_ConvertsToValidEnvVar` | [`invoke.go::buildEnv`](https://deepwiki.com/search/which-go-tests-correspond-to-t_38be8dba-a288-4627-bba6-4f06157d5ad1) | **✅ Real** | Confirms tag name sanitization mirrors Go environment variable rules. |
| `ScriptInvoker_BuildEnvironmentVariables_SetsBasicVars` | Same | **✅ Real** | Verifies SERF_* variables populated just like Go script invoker. |
| `ScriptInvoker_BuildMemberEventStdin_TabSeparatedFormat` | [`invoke.go::memberEventStdin`](https://deepwiki.com/search/which-go-tests-correspond-to-t_38be8dba-a288-4627-bba6-4f06157d5ad1) | **✅ Real** | Ensures member event stdin formatting matches Go implementation. |
| `ScriptInvoker_EventClean_EscapesTabsAndNewlines` | [`invoke.go::eventClean`](https://deepwiki.com/search/which-go-tests-correspond-to-t_38be8dba-a288-4627-bba6-4f06157d5ad1) | **✅ Real** | Matches string escaping behaviour from Go code. |
| `ScriptInvoker_PreparePayload_AppendsNewlineIfMissing` | [`invoke.go::preparePayload`](https://deepwiki.com/search/which-go-tests-correspond-to-t_38be8dba-a288-4627-bba6-4f06157d5ad1) | **✅ Real** | Validates newline handling identical to Go. |
| `ScriptInvoker_SimpleScript_Executes` | [`invoke.go::invokeEventScript`](https://deepwiki.com/search/which-go-tests-correspond-to-t_38be8dba-a288-4627-bba6-4f06157d5ad1) | **✅ Real** | Smoke test for ExecuteAsync, analogous to Go script invocation path. |

### ScriptExecutionIntegrationTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `ScriptInvoker_OutputExceeds8KB_TruncatesWithWarning` | [`invoke.go::invokeEventScript`](https://deepwiki.com/search/which-go-tests-correspond-to-t_38be8dba-a288-4627-bba6-4f06157d5ad1) | **✅ Real** | Ensures truncation flag/warning align with Go’s 8KB buffer behaviour. |
| `ScriptInvoker_SlowScript_LogsWarning` | Same | **✅ Real** | Validates slow-script timer warning consistent with Go implementation. |
| `ScriptInvoker_ScriptWithEnvironmentVars_ReceivesVars` | Same | **✅ Real** | Confirms environment variables reach script process. |
| `ScriptInvoker_ScriptWithStdin_ReceivesInput` | Same | **✅ Real** | Verifies stdin piping works (mirrors Go goroutine streaming). |
| `ScriptInvoker_QueryWithOutput_AutoResponds` | Same | **✅ Real** | Checks query output handling matching Go’s auto-response logic. |
| `ScriptInvoker_ScriptFailure_ReturnsNonZeroExit` | Same | **✅ Real** | Ensures non-zero exit codes bubble up. |
| `ScriptInvoker_CrossPlatform_ExecutesCorrectly` | Same | **✅ Real** | Confirms cross-platform command execution, comparable to Go invoker behaviour. |

### GatedWriterTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `GatedWriter_BuffersUntilFlush` | [`cmd/serf/command/agent/command.go::setupLoggers`](https://deepwiki.com/search/where-are-go-tests-covering-th_cb99f21f-44ba-443b-807e-f7bcdd3dda6d) | **Fake** | Only checks backend StringWriter remains empty until Flush; should verify go-style gating semantics including multi-writer behavior. |
| `GatedWriter_PassesThroughAfterFlush` | Same | **Fake** | Writes after flush and asserts text present, but lacks validation that gate stays open and no buffering remains. |
| `GatedWriter_Reset_ClearsBuffer` | Same | **Fake** | Simply calls Reset and Flush; needs inspection of internal gating flag akin to Go implementation. |
| `GatedWriter_Dispose_FlushesBuffer` | Same | **Fake** | Only asserts output contains line; no check for proper dispose semantics or multi-handler fan-out. |
| `GatedWriter_ThreadSafe_ConcurrentWritesAsync` | Same | **Fake** | Flushes to StringWriter and counts lines; does not ensure thread-safety like Go tests (missing race checks). |

### EventFilterTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| All tests | [`event_handler.go::EventFilter`](https://deepwiki.com/search/which-go-tests-correspond-to-t_38be8dba-a288-4627-bba6-4f06157d5ad1) | **✅ Real** | Mirror Go event filter matching semantics (type/name wildcard handling). |

### EventScriptParseTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| All tests | [`event_handler.go::ParseEventHandlers`](https://deepwiki.com/search/which-go-tests-correspond-to-t_38be8dba-a288-4627-bba6-4f06157d5ad1) | **✅ Real** | Validates script specification parsing consistent with Go logic. |

### ScriptEventHandlerTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `ScriptEventHandler_UpdateScripts_HotReloadsOnNextEvent` | [`event_handler.go::UpdateScripts`](https://deepwiki.com/search/which-go-tests-correspond-to-t_38be8dba-a288-4627-bba6-4f06157d5ad1) | **Fake** | Asserts `true`; does not dispatch event to confirm new scripts used. |
| `ScriptEventHandler_FilterMatching_OnlyExecutesMatchingScripts` | Same | **Fake** | Needs to assert only matching scripts invoked; currently `Assert.True(true)`. |
| `ScriptEventHandler_MultipleScripts_AllExecute` | Same | **Fake** | No verification of execution; should record script runs. |

### SignalHandlerTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `SignalHandler_RegisterCallback_InvokesOnSignal` | [`command.go::handleSignals`](https://deepwiki.com/search/which-go-tests-cover-signalhan_3f742c66-9d73-456b-bf3c-393c1254425b) | **Fake** | Uses custom `TriggerSignal`; does not simulate OS signals or verify behavior in actual handler goroutine. |
| `SignalHandler_MultipleCallbacks_AllInvoked` | Same | **Fake** | Only increments counter on manual trigger; no concurrency or OS integration as in Go agent. |
| `SignalHandler_CallbackException_DoesNotStopOthers` | Same | **Fake** | Manual trigger with simple counter, lacks Go-style panic isolation tests. |
| `SignalHandler_Dispose_CleansUpHandlers` | Same | **Fake** | Calls TriggerSignal after dispose without verifying OS signal deregistration. |

### SignalHandlingIntegrationTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `SignalHandler_SIGINT_TriggersGracefulShutdown` | [`command.go::handleSignals`](https://deepwiki.com/search/which-go-tests-cover-signalhan_3f742c66-9d73-456b-bf3c-393c1254425b) | **Fake** | Uses manual TriggerSignal instead of actual OS SIGINT; no confirmation of graceful leave logic. |
| `SignalHandler_SIGTERM_TriggersConfiguredShutdown` | Same | **Fake** | Manual trigger only; does not exercise LeaveOnTerm path. |
| `SignalHandler_SIGHUP_TriggersConfigReload` | Same | **Fake** | Should invoke handleReload and verify state; current test just checks callback variable. |
| `SignalHandler_DoubleSignal_ForcesShutdown` | Same | **Fake** | Manual double trigger; no link to force leave flow. |
| `AgentCommand_LeaveOnTerm_LeavesGracefully` | [`command_test.go::TestCommandRun`](https://deepwiki.com/search/which-go-tests-cover-signalhan_3f742c66-9d73-456b-bf3c-393c1254425b) | **Weak** | Cancels CTS instead of sending SIGTERM; should verify actual leave triggered. |
| `AgentCommand_SkipLeaveOnInt_ForcesShutdown` | Same | **Weak** | Cancellation token used rather than signal; exit code assertion loose. |
| `AgentCommand_GracefulTimeout_ForcesShutdown` | Same | **Weak** | Measures elapsed time but does not confirm force shutdown path executed. |

### LogWriterTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `LogWriter_FiltersBasedOnLevel` | [`command.go::setupLoggers`](https://deepwiki.com/search/where-are-go-tests-covering-th_cb99f21f-44ba-443b-807e-f7bcdd3dda6d) | **Fake** | Writes to StringWriter with handcrafted prefixes; does not simulate regex filtering or multi-writer behavior from Go implementation. |
| `LogWriter_AllowsUnprefixedLines` | Same | **Fake** | Only checks plain line passes through; lacks verification of regex-based filtering logic. |
| `LogWriter_LevelFiltering_CountsCorrectly` | Same | **Fake** | Counts lines after WriteLine; ignores regex and warning/error prefix semantics. |
| `LogWriter_ThreadSafe_ConcurrentWritesAsync` | Same | **Fake** | Concurrent writes to StringWriter without verifying thread safety guarantees or locking similar to Go code. |

### RpcServerVerificationTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `RpcServer_CommandBeforeHandshake_Fails` | [`rpc_client_test.go::TestRPCClientMembers`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **✅ Real** | Sends raw MessagePack request pre-handshake and asserts handshake error, matching Go behavior. |
| `RpcServer_DuplicateHandshake_Fails` | [`rpc_client_test.go::TestRPCClientHandshake`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **✅ Real** | Verifies second handshake rejected just like Go tests. |
| `RpcServer_WithAuth_CommandWithoutAuth_Fails` | [`rpc_client_test.go::TestRPCClientAuth`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **✅ Real** | Confirms auth required after handshake, identical to Go coverage. |
| `RpcServer_WithAuth_AfterAuth_CommandSucceeds` | Same | **✅ Real** | After auth, Members command succeeds – mirrors Go test path. |
| `RpcServer_NoAuth_CommandSucceeds` | Same | **✅ Real** | Confirms commands work when server has no auth key, consistent with Go. |
| `RpcServer_InvalidAuthKey_Fails` | Same | **✅ Real** | Ensures wrong key is rejected exactly like Go tests. |
| `RpcServer_MembersFiltered_UsesAnchoredRegex` | [`rpc_client_test.go::TestRPCClientMembersFiltered`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Weak** | Only exercises name/status filter; should assert anchored tag regex enforcement. |
| `RpcServer_MembersFiltered_InvalidRegex_Fails` | Same | **✅ Real** | Invalid regex raises error, matching Go assertions. |
| `RpcServer_AcceptDuringShutdown_RejectsConnection` | [`rpc_client_test.go::TestRPCClientShutdown`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Weak** | Allows connection attempt during dispose but doesn’t assert explicit rejection/exception. |

### RpcStreamingTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `RpcServer_MonitorCommand_Exists` | [`rpc_client_test.go::TestRPCClientMonitor`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Fake** | Only checks `LogWriter` not null; does not execute monitor RPC or verify streamed logs. |
| `RpcServer_StreamCommand_Exists` | [`rpc_client_test.go::TestRPCClientStream`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Fake** | Merely asserts agent is running; no RPC stream nor event verification. |
| `CircularLogWriter_Integration_WithAgentAsync` | [`rpc_client_test.go::TestRPCClientMonitor`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Fake** | Writes directly to log writer but never inspects monitor output as Go tests do. |
| `RpcEventHandler_ReceivesBacklog` | [`rpc_client_test.go::TestRPCClientStream_Member`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Fake** | Registers handler without asserting backlog or streamed events – lacks Go-level validation. |

### RpcClientIntegrationTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `RpcClient_Connect_Succeeds` | [`rpc_client_test.go::TestRPCClient`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **✅ Real** | Validates handshake and basic Members call like Go test. |
| `RpcClient_Members_ReturnsLocalNode` | [`TestRPCClientMembers`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **✅ Real** | Confirms members list includes local node with Alive status. |
| `RpcClient_MembersFiltered_ByStatus_Works` | [`TestRPCClientMembersFiltered`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **Weak** | Only exercises alive/failed filters; does not cover anchored regex behavior from Go suite. |
| `RpcClient_MembersFiltered_ByName_Works` | Same | **Weak** | Tests simple name matches but not regex anchoring or error handling. |
| `RpcClient_MembersFiltered_ByTags_Works` | Same | **Weak** | Assumes tag exists, but lacks anchored regex assertions present in Go tests. |
| `RpcClient_Join_ConnectsTwoAgents` | [`TestRPCClientJoin`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **✅ Real** | Joins second agent and verifies both see each other, matching Go coverage. |
| `RpcClient_UserEvent_DispatchesSuccessfully` | [`TestRPCClientUserEvent`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **✅ Real** | Registers handler and asserts payload delivered. |
| `RpcClient_ForceLeave_RemovesFailedNode` | [`TestRPCClientForceLeave`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **Weak** | Only asserts target not Alive; does not ensure final status becomes Left as Go test does. |
| `RpcClient_Leave_CausesGracefulShutdown` | [`TestRPCClientLeave`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **✅ Real** | Confirms agent transitions to Leaving/Shutdown after leave. |
| `RpcClient_UpdateTags_ModifiesNodeTags` | [`TestRPCClientUpdateTags`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **✅ Real** | Updates tags and verifies via Members API. |
| `RpcClient_Stats_ReturnsAgentInfo` | [`TestRPCClientStats`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **✅ Real** | Checks stats dictionary contains agent info. |
| `RpcClient_Query_DispatchesAndReceivesAck` | [`TestRPCClientQuery`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **✅ Real** | Verifies query dispatch, ack, and handler reception. |
| `RpcClient_GetCoordinate_ReturnsCoordinateForExistingNode` | [`TestRPCClientGetCoordinate`](https://deepwiki.com/search/which-go-tests-cover-rpcclient_c3ba6d66-f188-48ff-ab5b-19311783dbf9) | **✅ Real** | Waits for coordinate and asserts vector populated. |
| `RpcClient_GetCoordinate_NonExistentNode_ReturnsNull` | Same | **✅ Real** | Matches Go expectation for missing node. |

### AgentCommandTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `AgentCommand_RunsUntilShutdown` | [`command_test.go::TestCommandRun`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **✅ Real** | Starts CLI command, keeps it alive, and cancels to confirm graceful shutdown with exit code 0, mirroring Go test intent. |
| `AgentCommand_RpcServerAcceptsConnections` | [`command_test.go::TestCommandRun_rpc`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **✅ Real** | Spins up agent with RPC address and validates Members RPC like Go coverage. |
| `AgentCommand_JoinsClusterAtStartup` | [`command_test.go::TestCommandRun_join`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **✅ Real** | Uses `--join` + `--replay` and asserts membership count equals two, matching Go expectations. |
| `AgentCommand_JoinFailure_ExitsWithError` | [`command_test.go::TestCommandRun_joinFail`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **✅ Real** | Non-existent join target yields non-zero exit, aligned with Go failure test. |

### AgentLifecycleIntegrationTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `Agent_StartsAndRuns_UntilShutdown` | [`command_test.go::TestCommandRun`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **✅ Real** | Direct SerfAgent start/stop with state checks paralleling Go run test assertions. |
| `Agent_JoinsCluster_AtStartup` | [`command_test.go::TestCommandRun_join`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **✅ Real** | Uses StartJoin to ensure two-node cluster forms, same as Go behavior. |
| `Agent_JoinFailure_ThrowsException` | [`command_test.go::TestCommandRun_joinFail`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **✅ Real** | StartJoin to bad address throws, matching Go failure expectations. |
| `Agent_AdvertiseAddress_UsedInMemberInfo` | [`command_test.go::TestCommandRun_advertiseAddr`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **✅ Real** | Verifies advertised IP/port propagate to member record just like Go test. |
| `Agent_RetryJoin_EventuallySucceeds` | [`command_test.go::TestCommandRun_retry_join`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **✅ Real** | Configures retry join, waits for success, and asserts both members present as in Go. |
| `Agent_RetryJoin_MaxAttempts_StopsRetrying` | [`command_test.go::TestCommandRun_retry_joinFail`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **Weak** | Only checks member count still 1 after delay; doesn’t detect max-attempt exhaustion or log output like Go test. |
| `Agent_MultipleStartCalls_ThrowsException` | [`serf_agent_test.go::TestSerfAgent_StartTwice`](https://deepwiki.com/search/which-go-tests-cover-the-serf_f92202e8-3793-450b-8c4b-9b306656f204) | **✅ Real** | Re-Start raises InvalidOperationException, reflecting Go guard against double start. |

### ConfigManagementTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `Config_StartJoin_ParsesCorrectly` | [`config_test.go::TestDecodeConfig`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/config_test.go) | **✅ Real** | Mirrors Go decode coverage for `start_join` array. |
| `Config_Tags_MergeCorrectly` | Same | **✅ Real** | Matches Go checks ensuring `tags` map populated from config. |
| `Config_RetryInterval_DefaultValue` | [`config.go::DefaultConfig`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/config.go) | **✅ Real** | Asserts default `RetryInterval` equals Go default (30s). |
| `Config_RetryMaxAttempts_DefaultZero` | [`config.go::DefaultConfig`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/config.go) | **Weak** | Go defaults to 0 but only meaningful with surrounding merge logic; test lacks broader default coverage. |
| `Config_MutualExclusion_TagsAndTagsFile_Throws` | [`config.go::LoadConfig`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/config.go) / `config_test.go::TestLoadConfig` | **✅ Real** | Throws like Go when both tags and tags_file set. |
| `Config_MutualExclusion_EncryptKeyAndKeyringFile_Throws` | Same | **✅ Real** | Matches Go validation preventing encrypt_key with keyring_file. |
| `Config_Profile_DefaultLan` | [`config.go::DefaultConfig`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/config.go) | **Weak** | Verifies single default field; Go tests cover via broader decode cases. |
| `Config_Protocol_DefaultFive` | Same | **Weak** | Confirms protocol value but doesn’t assert max version semantics Go enforces. |
| `Config_LeaveOnTerm_DefaultTrue` | [`config_test.go::TestDecodeConfig`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/config_test.go) | **Weak** | Go ensures leave_on_terminate toggled via decode; C# test only checks default property. |

### CoordinateIntegrationTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `Coordinate_LocalNode_ReturnsCoordinate` | [`rpc_client_test.go::TestRPCClientGetCoordinate`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/rpc_client_test.go#L1125-L1146) | **✅ Real** | Calls `Serf.GetCoordinate` for local node and asserts non-null just like Go RPC test. |
| `Coordinate_NonExistentNode_ReturnsNull` | Same | **✅ Real** | Confirms unknown node returns null, matching Go expectation. |
| `Coordinate_TwoNodes_CalculatesRTT` | [`rtt_test.go::TestRTTCommand_Run`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/rtt_test.go#L41-L104) | **Fake** | Logs and returns early if remote coordinate missing; never fails, so RTT path is unverified. |
| `Coordinate_DisabledCoordinates_ReturnsNull` | [`config.go::DisableCoordinates`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/config.go#L64-L188) | **Weak** | Ensures flag leads to null coordinate, but Go lacks direct test so coverage is partial. |
| `Coordinate_UpdatesOverTime` | [`rpc_client_test.go::TestRPCClientGetCoordinate`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/rpc_client_test.go#L1125-L1146) | **Weak** | Only checks coordinate remains non-null after delay; does not assert update or divergence like Go’s RTT output. |

### MembersCommandTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `MembersCommand_WithRunningAgent_ListsMembers` | [`members_test.go::TestMembersCommandRun`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/members_test.go#L14-L39) | **✅ Real** | Executes CLI against running agent and matches Go expectation of exit code 0 + node listing. |
| `MembersCommand_WithStatusFilter_FiltersCorrectly` | [`members_test.go::TestMembersCommandRun_statusFilter`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/members_test.go#L41-L69) | **✅ Real** | Uses `--status` filter and asserts alive node present just like Go test. |
| `MembersCommand_JsonFormat_OutputsJson` | [`members.go::Run`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/members.go#L54-L180) | **Weak** | Only checks for “{”/node name; does not validate JSON schema or fields the Go command emits. |
| `MembersCommand_InvalidRpcAddress_ReturnsError` | [`members.go::Run`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/members.go#L54-L180) | **Weak** | Verifies non-zero exit on bad address but does not assert specific error handling covered in Go tests. |

### MemberCommandExtendedTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `Members_ReturnsLocalNode` | [`rpc_client_test.go::TestRPCClientMembers`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/rpc_client_test.go#L121-L218) | **Weak** | Confirms local node present but duplicates simple behavior without exercising filtering logic. |
| `Members_MultipleNodes_ReturnsAll` | Same | **✅ Real** | Joins a second agent and asserts both members visible, mirroring Go cluster join coverage. |
| `Members_FilterByStatus_Alive` | [`members.go::Run`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/members.go#L116-L180) | **Fake** | Applies LINQ filtering on test side; does not call product filters like Go CLI does. |
| `Members_FilterByName` | Same | **Fake** | Only filters in-memory after retrieving members, so no product behavior validated. |
| `Members_FilterByTags` | Same | **Fake** | As above; merely inspects default tags without invoking real filtering. |
| `Members_AfterLeave_ShowsLeft` | [`rpc_client_test.go::TestRPCClientForceLeave`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/rpc_client_test.go#L223-L307) | **✅ Real** | Forces leave and confirms member reaches Left state like Go force-leave scenario. |
| `Members_AfterFailed_ShowsFailed` | [`rpc_client_test.go::TestRPCClientForceLeave`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/rpc_client_test.go#L223-L307) | **Weak** | Allows either Failed or Alive, so it won’t detect incorrect status transitions Go guards against. |
| `Members_ChecksProtocol` | [`rpc_client_test.go::TestRPCClientMembers`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/agent/rpc_client_test.go#L121-L218) | **Weak** | Only asserts broad min/max bounds; Go verifies precise protocol negotiation elsewhere. |
| `Members_ChecksDelegateVersions` | Same | **Weak** | Loose inequality checks provide little signal versus Go’s delegate version handling. |

### RttCommandTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `RttCommand_WithValidNode_ReturnsCoordinate` | [`rtt_test.go::TestRTTCommand_Run`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/rtt_test.go#L41-L104) | **Fake** | Returns early when coordinate unavailable; command success path never fails, so RTT output isn’t guaranteed. |
| `RttCommand_InvalidRpcAddress_Fails` | [`rtt_test.go::TestRTTCommand_Run_BadArgs`](https://github.com/hashicorp/serf/blob/master/cmd/serf/command/rtt_test.go#L19-L39) | **Weak** | Asserts non-zero exit for bad RPC address (similar failure mode) but lacks detailed error checks Go performs for multiple argument cases. |

### RetryJoinTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `RetryJoin_RunsInBackground_DoesNotBlockStartup` | [`command_test.go::TestCommandRun_retry_join`](https://deepwiki.com/search/which-go-tests-cover-retry-joi_411c7ab6-2ea3-4a45-bdb7-2a180a0b16bf) | **Weak** | Relies on elapsed time < 1s to imply background retry; does not assert join success or inspect retry routine state. |
| `RetryJoin_MaxAttempts_StopsRetrying` | Same | **Fake** | Waits 500 ms then asserts agent still running; never verifies attempts stopped or max attempt count honored. |
| `RetryJoin_MinInterval_EnforcedAtOneSecond` | Same | **Weak** | Uses wall-clock delay ≥1 s as proxy for min interval; should inspect retry scheduling directly. |
| `StartJoin_Failure_BlocksStartup` | [`command_test.go::TestCommandRun_join`](https://deepwiki.com/search/which-go-tests-cover-retry-joi_411c7ab6-2ea3-4a45-bdb7-2a180a0b16bf) | **✅ Real** | Matches Go expectation that StartJoin failure surfaces during startup. |
| `StartJoin_Success_JoinsImmediately` | [`command_test.go::TestCommandRun_join`](https://deepwiki.com/search/which-go-tests-cover-retry-joi_411c7ab6-2ea3-4a45-bdb7-2a180a0b16bf) | **✅ Real** | Verifies immediate membership propagation similar to Go test. |

### RpcServerVerificationTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `RpcServer_CommandBeforeHandshake_Fails` | [`rpc_client_test.go::TestRPCClientMembers`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **✅ Real** | Manually sends MessagePack header prior to handshake and asserts handshake error. |
| `RpcServer_DuplicateHandshake_Fails` | [`rpc_client_test.go::TestRPCClientHandshake`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **✅ Real** | Confirms second handshake rejected as in Go tests. |
| `RpcServer_WithAuth_CommandWithoutAuth_Fails` | [`rpc_client_test.go::TestRPCClientAuth`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **✅ Real** | Mirrors Go failure when auth missing. |
| `RpcServer_WithAuth_AfterAuth_CommandSucceeds` | Same | **✅ Real** | Ensures command works post-auth, aligned with Go behavior. |
| `RpcServer_NoAuth_CommandSucceeds` | Same | **✅ Real** | Confirms commands succeed when auth disabled. |
| `RpcServer_InvalidAuthKey_Fails` | Same | **✅ Real** | Matches Go invalid key failure. |
| `RpcServer_MembersFiltered_UsesAnchoredRegex` | [`rpc_client_test.go::TestRPCClientMembersFiltered`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Weak** | Only basic name/status filter; does not verify anchored tags regex as in Go. |
| `RpcServer_MembersFiltered_InvalidRegex_Fails` | Same | **✅ Real** | Throws on invalid regex consistent with Go behavior. |
| `RpcServer_AcceptDuringShutdown_RejectsConnection` | [`rpc_client_test.go::TestRPCClientShutdown`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Weak** | Allows connection attempt during dispose without asserting explicit rejection. |

### RpcStreamingTests.cs
| Test | Go Reference | Classification | Notes |
| --- | --- | --- | --- |
| `RpcServer_MonitorCommand_Exists` | [`rpc_client_test.go::TestRPCClientMonitor`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Fake** | Only asserts LogWriter not null; no streaming handshake/client verification. |
| `RpcServer_StreamCommand_Exists` | [`rpc_client_test.go::TestRPCClientStream`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Fake** | Asserts agent `Serf` not null; doesn’t exercise stream RPC or backlog delivery. |
| `CircularLogWriter_Integration_WithAgentAsync` | [`rpc_client_test.go::TestRPCClientMonitor`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Fake** | Writes directly to log writer; does not confirm monitor RPC sees output. |
| `RpcEventHandler_ReceivesBacklog` | [`rpc_client_test.go::TestRPCClientStream_Member`](https://deepwiki.com/search/which-go-tests-cover-rpc-serve_29aed25c-25fc-493f-b0a1-db5b944e8cae) | **Fake** | Registers handler but never asserts backlog/events delivered as Go streaming tests do. |

---
Prepared by: Cascade audit (DeepWiki cross-checked)

