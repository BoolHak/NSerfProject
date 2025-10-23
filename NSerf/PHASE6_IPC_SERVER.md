# Phase 6: IPC/RPC Server - Detailed Test Specification

**Timeline:** Weeks 6-7 | **Tests:** 60 | **Focus:** Server-side RPC handling

---

## Files to Create
```
NSerf/NSerf/Agent/
├── AgentIpc.cs (~600 lines)
├── IpcClient.cs (~200 lines)
├── IpcEventStream.cs (~150 lines)
├── IpcLogStream.cs (~80 lines)
└── IpcQueryResponseStream.cs (~100 lines)

NSerfTests/Agent/
├── IpcServerTests.cs (6)
├── IpcHandshakeTests.cs (5)
├── IpcAuthTests.cs (4)
├── IpcMembershipTests.cs (8)
├── IpcJoinLeaveTests.cs (6)
├── IpcEventTests.cs (5)
├── IpcKeyTests.cs (8)
├── IpcQueryTests.cs (8)
├── IpcStreamTests.cs (6)
├── IpcStopTests.cs (2)
└── IpcMiscTests.cs (2)
```

---

## Test Groups

### 6.1 Server Lifecycle (6 tests)
1. Create initializes listener
2. Start accepts connections
3. Shutdown stops accepting
4. Shutdown closes existing clients
5. Shutdown idempotent
6. Multiple clients concurrent

### 6.2 Handshake (5 tests)
1. Valid version succeeds
2. Unsupported version fails
3. Duplicate handshake fails
4. Command before handshake fails
5. Client version stored

### 6.3 Authentication (4 tests)
1. Valid key succeeds
2. Invalid key fails
3. Command without auth fails
4. Auth not required works

### 6.4-6.11 Command Handlers (43 tests)
- Members/MembersFiltered (8)
- Join/Leave/ForceLeave (6)
- UserEvent (5)
- Keys (Install/Use/Remove/List) (8)
- Query/Respond (8)
- Stream/Monitor/Stop (8)
- Stats/GetCoordinate (2)

---

## Key Implementation

```csharp
public class AgentIpc : IDisposable
{
    private readonly Agent _agent;
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, IpcClient> _clients;
    
    public async Task StartAsync()
    {
        _listener.Start();
        _ = Task.Run(ListenLoop);
    }
    
    private async Task ListenLoop()
    {
        while (!_stopped)
        {
            var tcpClient = await _listener.AcceptTcpClientAsync();
            var ipcClient = new IpcClient(tcpClient, _agent);
            _clients[ipcClient.Id] = ipcClient;
            _ = Task.Run(() => HandleClient(ipcClient));
        }
    }
    
    private async Task HandleClient(IpcClient client)
    {
        while (!client.IsClosed)
        {
            var header = await client.ReadRequestHeaderAsync();
            await HandleRequest(client, header);
        }
    }
    
    private async Task HandleRequest(IpcClient client, RequestHeader header)
    {
        switch (header.Command)
        {
            case "handshake": await HandleHandshake(client, header.Seq); break;
            case "auth": await HandleAuth(client, header.Seq); break;
            case "members": await HandleMembers(client, header.Seq); break;
            // ... all other commands
        }
    }
}
```

---

## Go Reference
- `serf/cmd/serf/command/agent/ipc.go`
- `serf/cmd/serf/command/agent/rpc_client_test.go`
