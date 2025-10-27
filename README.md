# NSerf

NSerf is a full, from-scratch port of [HashiCorp Serf](https://www.serf.io/) to modern C#. The project mirrors Serf's decentralized cluster membership, failure detection, and event dissemination model while embracing idiomatic .NET patterns for concurrency, async I/O, and tooling. The codebase targets .NET 8+ and is currently in **beta** while the team polishes APIs and round-trips real-world workloads.

## Key Differences from the Go Implementation

While the behaviour and surface area match Serf's reference implementation, a few platform-specific choices differ:

- **Serialization** relies on the high-performance [MessagePack-CSharp](https://github.com/neuecc/MessagePack-CSharp) stack instead of Go's native MessagePack bindings, keeping message layouts identical to the original protocol.@NSerf/NSerf/Serf/Messages.cs#1-80
- **Compression** uses the built-in .NET `System.IO.Compression.GZipStream` for gossip payload compression, replacing the Go zlib adapter while preserving wire compatibility.@NSerf/NSerf/Memberlist/Common/CompressionUtils.cs#1-44
- **Async orchestration** embraces task-based patterns and the C# transaction-style locking helpers introduced during the port, matching Go's channel semantics without blocking threads.

## Repository Layout

```
NSerf/
├─ NSerf.sln                   # Solution entry point
├─ NSerf/                      # Core library (agent, memberlist, Serf runtime)
│  ├─ Agent/                   # CLI agent runtime, RPC server, script handlers
│  ├─ Client/                  # RPC client, request/response contracts
│  ├─ Memberlist/              # Gossip, failure detection, transport stack
│  ├─ Serf/                    # Cluster state machine, event managers, helpers
│  └─ ...
├─ NSerf.CLI/                  # dotnet CLI facade mirroring `serf` command
├─ NSerf.CLI.Tests/            # End-to-end and command-level test harnesses
├─ NSerfTests/                 # Comprehensive unit, integration, and verification tests
└─ documentation *.md          # Test plans, remediation reports, design notes
```

### Highlights by Area

- **Agent** – Implements the long-running daemon (`serf agent`) including configuration loading, RPC hosting, script invocation, and signal handling.
- **Memberlist** – Full port of HashiCorp's SWIM-based memberlist, including gossip broadcasting, indirect pinging, and encryption support.
- **Serf** – Cluster coordination, state machine transitions, Lamport clocks, and query/event processing all live here.
- **Client** – Typed RPC requests/responses and ergonomic helpers for building management tooling.
- **CLI** – A drop-in `serf` CLI replacement built on `System.CommandLine`, sharing the same RPC surface and defaults as the Go binary.

## Getting Started

1. **Prerequisites**
   - .NET SDK 8.0 (or newer)

2. **Restore and build**
   ```powershell
   dotnet restore
   dotnet build NSerf.sln
   ```

3. **Run the agent locally**
   ```powershell
   dotnet run --project NSerf.CLI -- agent
   ```

4. **Invoke commands against a running agent**
   ```powershell
   dotnet run --project NSerf.CLI -- members
   dotnet run --project NSerf.CLI -- query "ping" --payload "hello"
   ```

## Testing

The solution ships with an extensive test suite that mirrors HashiCorp's verification matrix, covering state machines, gossip flows, RPC security, script execution, and CLI orchestration.

```powershell
dotnet test NSerf.sln
```

## Project Status

NSerf is feature-complete relative to Serf 1.6.x but remains in **beta** while the team onboards additional users, tightens compatibility, and stabilizes the public API surface. Expect minor breaking changes as interoperability edge cases are addressed.

## Licensing

All source files retain the original MPL-2.0 license notices from HashiCorp Serf. The port is distributed under the same MPL-2.0 terms.
