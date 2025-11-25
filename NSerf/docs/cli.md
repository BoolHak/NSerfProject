# CLI

## Purpose

The CLI project (`NSerf.CLI`) provides a command-line interface around the Serf agent and RPC endpoints.

It is responsible for:

- Starting and managing the agent process (`agent` command).
- Providing user-facing commands (`members`, `join`, `leave`, `event`, `query`, `force-leave`, `tags`, etc.).
- Parsing CLI flags and merging them with file-based configuration.

---

## AgentCommand

`AgentCommand` is the main entry point for running the agent from the CLI.

### Command

- Name: `agent`
- Description: `Start the Serf agent`

The command is created via `AgentCommand.Create(CancellationToken)` and uses `System.CommandLine` to define options and bind them to an internal `ExecuteAsync` method.

### Key Options

Core options:

- `--node` – node name (default is hostname if omitted).
- `--bind` – bind address for Serf/memberlist gossip (default `0.0.0.0:7946`).
- `--advertise` – advertise address (IP:port) shared with other nodes.
- `--rpc-addr` – RPC bind address (default `127.0.0.1:7373`).
- `--rpc-auth` – RPC authentication token.
- `--encrypt` – encryption key for gossip (base64, 16 bytes).
- `--join` – single address to join after the agent has started.
- `--replay` – replay user events on join when using `--join`.
- `--tag` – node tag (`key=value`, repeatable; multiple values allowed per token).
- `--config-file` – path to a JSON config file or directory of `.json` files.
- `--event-handler` – event handler specification (repeatable); passed into `AgentConfig.EventHandlers`.

Lighthouse discovery options:

- `--lighthouse-base-url` – Lighthouse service base URL.
- `--lighthouse-cluster-id` – cluster id for Lighthouse.
- `--lighthouse-private-key` – private key (base64 ECDSA PKCS#8).
- `--lighthouse-aes-key` – AES key (base64, 32 bytes).
- `--lighthouse-timeout-seconds` – HTTP timeout (default 30 seconds).
- `--lighthouse-version-name` – version name for partitioning nodes.
- `--lighthouse-version-number` – version number (> 0) for partitioning.
- `--lighthouse-start-join` – use Lighthouse for initial start join.
- `--lighthouse-retry-join` – use Lighthouse for retry join.

### Configuration Merge

When `agent` runs, configuration is merged in this order:

1. `AgentConfig.Default()` – built-in defaults.
2. File config (optional) – loaded via `ConfigLoader` if `--config-file` is supplied.
3. CLI config – values explicitly set by flags.

Details:

- A CLI-only `AgentConfig` (`cliConfig`) is created, starting with replay semantics and empty strings for several fields (to mark them as not set unless overridden).
- Only values explicitly provided via CLI flags are copied into `cliConfig`:
  - `nodeName`, `bindAddr` (if different from default), `advertiseAddr`.
  - `encryptKey`, `rpcAddr` (if different from default), `rpcAuth`.
  - `tags` (parsed via a local `ParseTags` helper).
  - `eventHandlers` (`--event-handler`).
  - Lighthouse flags and overrides (start/retry join, version name/number).
- If a config path is provided:
  - If it is a directory: `ConfigLoader.LoadFromDirectoryAsync()` merges all `*.json` files in lexical order.
  - If it is a file: `ConfigLoader.LoadFromFileAsync()` loads a single JSON file.
  - The file config is merged into defaults with `AgentConfig.Merge(defaults, fileConfig)`.
- Finally, CLI config is merged on top: `finalConfig = AgentConfig.Merge(finalConfig, cliConfig)`.

After merge:

- `finalConfig.NodeName` is set to `Environment.MachineName` if still empty.
- If Lighthouse join is enabled, a `ILighthouseClient` is built using a small DI container; CLI flags override or fall back to values from `finalConfig`.

---

## Agent Lifecycle from the CLI

Once `finalConfig` (and optionally `lighthouseClient`) are prepared:

1. A `SerfAgent` is created: `new SerfAgent(finalConfig, logger: null, lighthouseClient: lighthouseClient)`.
2. A `SignalHandler` is instantiated to listen for OS signals.
3. On `SIGHUP`, the handler:
   - Reloads configuration from the same `--config-file` path (if provided).
   - Merges loaded config with `cliConfig`.
   - Calls `agent.UpdateEventHandlers(reloaded.EventHandlers)`.
   - Prints how many handlers were reloaded.
4. `agent.StartAsync(shutdownToken)` is awaited, starting Serf, RPC, and background tasks.
5. If `--join` was specified:
   - Calls `agent.Serf!.JoinAsync([joinAddr], replay)`.
   - Prints either success or an error and returns non-zero on failure.
6. The CLI prints:
   - Bind address and RPC endpoint (from `finalConfig`).
   - A hint that Ctrl+C shuts down the agent.
7. The command then waits on `Task.Delay(Timeout.Infinite, shutdownToken)` until the shutdown token is cancelled (e.g. Ctrl+C or external cancellation).

Shutdown paths:

- **Graceful (OperationCanceledException)** – prints "Shutting down...", calls `agent.ShutdownAsync()` and `agent.DisposeAsync()`, returns 0.
- **Error** – prints "Agent error: ..." to stderr, calls `ShutdownAsync` and `DisposeAsync`, returns 1.

---

## Other Commands

The CLI also includes commands that talk to the agent over RPC. Common examples (see `NSerf.CLI/Commands`):

- `members` – list cluster members and their status.
- `join` – ask the cluster to join one or more nodes (distinct from `agent --join`, which joins during agent startup).
- `leave` – gracefully leave the cluster.
- `force-leave` – forcibly remove a node.
- `event` – send a user event.
- `query` – issue a query and collect responses.
- `tags` – view or update node tags.

Each of these commands typically:

- Parses options (for example `--rpc-addr`, timeouts, payload formats).
- Connects to the agent RPC server configured by `--rpc-addr` (or its default).
- Issues the corresponding RPC command to the running `SerfAgent`.

Refer to the source under `NSerf.CLI/Commands` for exact options and behavior of each command.
