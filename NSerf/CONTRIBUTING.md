# Contributing to NSerf

Thank you for your interest in contributing to NSerf! This document provides guidelines for porting code from the Go implementation to C#.

---

## 🎯 Project Goals

1. **Complete feature parity** with HashiCorp Serf (Go version)
2. **Protocol compatibility** - can interoperate with Go version
3. **Idiomatic C#** - leverage .NET features and patterns
4. **High quality** - well-tested, performant, maintainable

---

## 🏗️ Development Setup

### Prerequisites
- **.NET 8 SDK** or later
- **Visual Studio 2022** or **VS Code** with C# extension
- **Git** for version control

### Getting Started
```bash
# Clone the repository
git clone https://github.com/your-org/NSerf.git
cd NSerf

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test
```

---

## 📁 Project Structure

```
NSerf/
├── NSerf/              # Main library code
│   ├── Memberlist/     # Port memberlist Go files here
│   └── Serf/           # Port serf Go files here
└── NSerfTests/         # All tests
    ├── Memberlist/     # Memberlist tests
    └── Serf/           # Serf tests
```

---

## 🔄 Porting Process

### Step 1: Choose a File to Port
- Check `PROJECT.md` for current milestone
- Pick an unchecked file from the current milestone
- Create a branch: `git checkout -b port/filename`

### Step 2: Create the C# File
```csharp
// Example: Porting state.go to NodeState.cs

// Header comment with reference
// Ported from: github.com/hashicorp/memberlist/state.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.State;

/// <summary>
/// Represents the state type of a node in the cluster.
/// </summary>
public enum NodeStateType
{
    Alive = 0,
    Suspect = 1,
    Dead = 2,
    Left = 3
}
```

### Step 3: Port the Logic
Follow the patterns in this guide (see below)

### Step 4: Write Tests
```csharp
// Create corresponding test file in NSerfTests
// Example: NSerfTests/Memberlist/State/NodeStateTests.cs

public class NodeStateTests
{
    [Fact]
    public void NodeState_ShouldTransitionFromAliveToSuspect()
    {
        // Arrange
        var node = new NodeState
        {
            State = NodeStateType.Alive
        };

        // Act
        node.State = NodeStateType.Suspect;

        // Assert
        Assert.Equal(NodeStateType.Suspect, node.State);
    }
}
```

### Step 5: Submit for Review
```bash
# Commit your changes
git add .
git commit -m "Port: state.go to NodeState.cs"

# Push and create PR
git push origin port/filename
```

---

## 🔨 Go to C# Translation Patterns

### Basic Types

```go
// Go
type NodeState struct {
    Name        string
    Addr        net.IP
    Port        uint16
    Incarnation uint32
    State       NodeStateType
}
```

```csharp
// C#
public class NodeState
{
    public string Name { get; set; } = string.Empty;
    public IPAddress Addr { get; set; } = IPAddress.None;
    public ushort Port { get; set; }
    public uint Incarnation { get; set; }
    public NodeStateType State { get; set; }
}
```

### Interfaces

```go
// Go
type Delegate interface {
    NodeMeta(limit int) []byte
    NotifyMsg([]byte)
}
```

```csharp
// C#
public interface IDelegate
{
    byte[] NodeMeta(int limit);
    void NotifyMsg(ReadOnlySpan<byte> message);
}
```

### Goroutines → Tasks

```go
// Go
go func() {
    doWork()
}()
```

```csharp
// C#
_ = Task.Run(async () =>
{
    await DoWorkAsync();
});
```

### Channels → System.Threading.Channels

```go
// Go
ch := make(chan Event, 100)
ch <- event
event := <-ch
```

```csharp
// C#
var channel = Channel.CreateBounded<Event>(100);
await channel.Writer.WriteAsync(evt);
var evt = await channel.Reader.ReadAsync();
```

### Select → Channel Operations

```go
// Go
select {
case msg := <-ch1:
    handleMsg(msg)
case <-ch2:
    handleSignal()
case <-time.After(timeout):
    handleTimeout()
}
```

```csharp
// C# (using Task.WhenAny with channels)
var timeoutCts = new CancellationTokenSource(timeout);
var readTask = channel.Reader.ReadAsync(timeoutCts.Token).AsTask();

try
{
    var msg = await readTask;
    HandleMsg(msg);
}
catch (OperationCanceledException)
{
    HandleTimeout();
}
```

### Mutexes → Locks

```go
// Go
var mu sync.RWMutex

mu.RLock()
// read operations
mu.RUnlock()

mu.Lock()
// write operations
mu.Unlock()
```

```csharp
// C# - For async code, use SemaphoreSlim
private readonly SemaphoreSlim _lock = new(1, 1);

await _lock.WaitAsync();
try
{
    // critical section
}
finally
{
    _lock.Release();
}

// C# - For sync code, use ReaderWriterLockSlim
private readonly ReaderWriterLockSlim _rwLock = new();

_rwLock.EnterReadLock();
try
{
    // read operations
}
finally
{
    _rwLock.ExitReadLock();
}
```

### Atomic Operations

```go
// Go
atomic.AddUint32(&counter, 1)
atomic.LoadUint32(&counter)
atomic.StoreUint32(&counter, 0)
atomic.CompareAndSwapUint32(&counter, old, new)
```

```csharp
// C#
Interlocked.Increment(ref counter);
Interlocked.Read(ref counter);
Interlocked.Exchange(ref counter, 0);
Interlocked.CompareExchange(ref counter, newVal, oldVal);
```

### Error Handling

```go
// Go
func DoSomething() error {
    if err := validate(); err != nil {
        return fmt.Errorf("validation failed: %w", err)
    }
    return nil
}
```

```csharp
// C#
public void DoSomething()
{
    try
    {
        Validate();
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("Validation failed", ex);
    }
}
```

### Defer → try-finally / using

```go
// Go
func process() {
    mu.Lock()
    defer mu.Unlock()
    // do work
}
```

```csharp
// C#
public async Task ProcessAsync()
{
    await _lock.WaitAsync();
    try
    {
        // do work
    }
    finally
    {
        _lock.Release();
    }
}

// Or with IDisposable
public void Process()
{
    using var resource = AcquireResource();
    // do work
}
```

### Time Operations

```go
// Go
time.Now()
time.Sleep(time.Second)
time.After(timeout)
timer := time.NewTimer(duration)
```

```csharp
// C#
DateTimeOffset.UtcNow
await Task.Delay(TimeSpan.FromSeconds(1));
using var cts = new CancellationTokenSource(timeout);
using var timer = new Timer(callback, state, dueTime, period);
```

---

## 📝 Coding Standards

### Naming Conventions
- **Classes/Interfaces**: PascalCase (`NodeState`, `IDelegate`)
- **Methods**: PascalCase (`GetMembers()`, `SendMessage()`)
- **Properties**: PascalCase (`NodeName`, `IsAlive`)
- **Private fields**: _camelCase (`_nodeMap`, `_sequenceNum`)
- **Local variables**: camelCase (`nodeState`, `messageCount`)
- **Constants**: PascalCase (`DefaultPort`, `MaxMessageSize`)

### File Organization
```csharp
// 1. Using directives
using System;
using System.Net;

// 2. Namespace
namespace NSerf.Memberlist;

// 3. Class/interface
public class Memberlist : IDisposable
{
    // 4. Constants
    private const int DefaultPort = 7946;
    
    // 5. Fields
    private readonly ITransport _transport;
    
    // 6. Constructors
    public Memberlist(Config config)
    {
    }
    
    // 7. Properties
    public int MemberCount { get; private set; }
    
    // 8. Public methods
    public void Join(string[] existing)
    {
    }
    
    // 9. Private methods
    private void SendPing()
    {
    }
    
    // 10. IDisposable
    public void Dispose()
    {
    }
}
```

### Comments
```csharp
/// <summary>
/// Creates a new instance of Memberlist with the provided configuration.
/// </summary>
/// <param name="config">The configuration to use.</param>
/// <returns>A new Memberlist instance.</returns>
/// <exception cref="ArgumentNullException">Thrown when config is null.</exception>
public static Memberlist Create(Config config)
{
    // Implementation
}
```

### Async/Await
- Use `async`/`await` for I/O operations
- Suffix async methods with `Async`
- Accept `CancellationToken` parameters
- Use `ConfigureAwait(false)` in library code

```csharp
public async Task<bool> SendAsync(
    byte[] message, 
    CancellationToken cancellationToken = default)
{
    await _transport.WriteAsync(message, cancellationToken)
        .ConfigureAwait(false);
    return true;
}
```

---

## 🧪 Testing Guidelines

### Test Structure
```csharp
public class MemberlistTests
{
    [Fact]
    public async Task Join_WithValidNodes_ShouldSucceed()
    {
        // Arrange
        var config = new Config { Name = "node1" };
        using var memberlist = await Memberlist.CreateAsync(config);
        
        // Act
        var result = await memberlist.JoinAsync(new[] { "192.168.1.100" });
        
        // Assert
        Assert.True(result > 0);
    }
}
```

### Test Categories
- **Unit Tests**: Test individual methods/classes in isolation
- **Integration Tests**: Test multiple components together
- **Scenario Tests**: Test complete end-to-end workflows

### Mocking
```csharp
// Use Moq for mocking interfaces
var mockTransport = new Mock<ITransport>();
mockTransport
    .Setup(t => t.WriteAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(true);
```

### Test Naming
- Format: `MethodName_Scenario_ExpectedResult`
- Examples:
  - `Join_WithInvalidAddress_ThrowsException`
  - `SendMessage_WhenEncrypted_ReturnsEncryptedBytes`

---

## 🔍 Code Review Checklist

### Before Submitting PR
- [ ] Code compiles without warnings
- [ ] All tests pass
- [ ] New tests added for new functionality
- [ ] XML documentation on public APIs
- [ ] No commented-out code
- [ ] No debug logging left in
- [ ] Thread-safety considered
- [ ] Performance implications considered
- [ ] Follows coding standards

### Reviewer Checklist
- [ ] Correctly ports Go logic
- [ ] Idiomatic C#
- [ ] Thread-safe
- [ ] Well tested
- [ ] Documented
- [ ] No performance regressions

---

## 🚀 Performance Considerations

### Use Modern .NET Features
```csharp
// Use Span<T> for stack allocation
Span<byte> buffer = stackalloc byte[256];

// Use ArrayPool<T> for large buffers
var buffer = ArrayPool<byte>.Shared.Rent(4096);
try
{
    // use buffer
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}

// Use ValueTask<T> for hot paths
public ValueTask<int> ReadAsync(Memory<byte> buffer)
{
    if (_cached)
        return new ValueTask<int>(_cachedValue);
    return ReadAsyncSlow(buffer);
}
```

### Avoid Allocations
```csharp
// Bad: Boxing
object value = 42;

// Good: Generic
T value = GetValue<T>();

// Bad: LINQ in hot path
var result = list.Where(x => x.IsActive).ToList();

// Good: For loop
var result = new List<T>();
for (int i = 0; i < list.Count; i++)
{
    if (list[i].IsActive)
        result.Add(list[i]);
}
```

---

## 📊 Benchmarking

Use BenchmarkDotNet for performance testing:

```csharp
[MemoryDiagnoser]
public class MessageBenchmarks
{
    private byte[] _buffer;
    
    [GlobalSetup]
    public void Setup()
    {
        _buffer = new byte[1024];
    }
    
    [Benchmark]
    public void SerializeMessage()
    {
        var msg = new PingMessage { SeqNo = 1, Node = "test" };
        MessagePackSerializer.Serialize(msg);
    }
}
```

---

## 🐛 Debugging Tips

### Logging
```csharp
// Use Microsoft.Extensions.Logging
private readonly ILogger<Memberlist> _logger;

_logger.LogDebug("Sending ping to {Node}", node.Name);
_logger.LogWarning("Failed to contact {Node}, attempt {Attempt}", 
    node.Name, attemptCount);
_logger.LogError(ex, "Error processing message");
```

### Diagnostics
```csharp
// Use System.Diagnostics for metrics
private static readonly Counter<long> _messagesReceived = 
    Meter.CreateCounter<long>("nserf.messages.received");

_messagesReceived.Add(1, new KeyValuePair<string, object?>("type", "ping"));
```

---

## 📞 Getting Help

- Check `PROJECT.md` for overall plan
- Review `ROADMAP.md` for timeline
- Look at existing ported files for patterns
- Ask in team chat/meetings
- Reference Go source code for clarification

---

## 📄 License

This project is licensed under MPL-2.0, same as the original Go implementation.

When porting code, maintain the original copyright notice:
```csharp
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0
```

---

**Happy Porting! 🚀**
