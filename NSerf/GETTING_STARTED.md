# Getting Started with NSerf Development

This guide helps you get started with developing NSerf, the C# port of HashiCorp Serf.

---

## 🎯 Quick Start (5 Minutes)

### 1. Prerequisites
```bash
# Install .NET 8 SDK
# Download from: https://dotnet.microsoft.com/download/dotnet/8.0

# Verify installation
dotnet --version
# Should show: 8.0.x or higher
```

### 2. Clone and Build
```bash
cd c:\Users\bilel\Desktop\SerfPort\NSerf

# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test
```

### 3. Open in IDE
```bash
# Visual Studio
start NSerf.sln

# VS Code
code .
```

---

## 📚 Understanding the Codebase

### Project Layout
```
NSerf/
├── NSerf/                  # Main library
│   ├── Memberlist/         # Low-level cluster membership
│   │   ├── Protocol/       # SWIM protocol
│   │   ├── Transport/      # Network layer
│   │   ├── Security/       # Encryption
│   │   └── ...
│   └── Serf/              # High-level orchestration
│       ├── Events/        # Event system
│       ├── Queries/       # Query system
│       └── ...
└── NSerfTests/            # All tests
```

### Key Concepts

#### 1. Memberlist (Low Level)
- **Purpose**: Cluster membership and failure detection
- **Protocol**: SWIM (Scalable Weakly-consistent Infection-style Membership)
- **Key Files**: `Memberlist.cs`, `NodeState.cs`, `Transport.cs`

#### 2. Serf (High Level)
- **Purpose**: Service orchestration and discovery
- **Built on**: Memberlist
- **Key Files**: `Serf.cs`, `Event.cs`, `Query.cs`

---

## 🔨 Your First Contribution

### Choose a Task

**Option A: Port a Simple File**
Start with utility files:
- `util.go` → `NetworkUtils.cs`
- `label.go` → `LabelManager.cs`

**Option B: Write Tests**
Help test existing code:
- Add unit tests for ported components
- Create integration test scenarios

**Option C: Fix a TODO**
Search for `// TODO:` comments in the codebase

### Example: Port util.go

#### Step 1: Find the Go Source
```bash
# Located at: ../memberlist/util.go
```

#### Step 2: Create C# File
```csharp
// File: NSerf/Memberlist/Common/NetworkUtils.cs

// Ported from: github.com/hashicorp/memberlist/util.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Common;

/// <summary>
/// Network utility functions for Memberlist.
/// </summary>
public static class NetworkUtils
{
    /// <summary>
    /// Joins a host and port into an address string.
    /// </summary>
    public static string JoinHostPort(string host, int port)
    {
        // Handle IPv6 addresses
        if (host.Contains(':'))
        {
            return $"[{host}]:{port}";
        }
        return $"{host}:{port}";
    }
}
```

#### Step 3: Write Tests
```csharp
// File: NSerfTests/Memberlist/Common/NetworkUtilsTests.cs

using NSerf.Memberlist.Common;
using Xunit;

namespace NSerfTests.Memberlist.Common;

public class NetworkUtilsTests
{
    [Theory]
    [InlineData("192.168.1.1", 8080, "192.168.1.1:8080")]
    [InlineData("::1", 8080, "[::1]:8080")]
    [InlineData("example.com", 443, "example.com:443")]
    public void JoinHostPort_ShouldFormatCorrectly(
        string host, int port, string expected)
    {
        // Act
        var result = NetworkUtils.JoinHostPort(host, port);

        // Assert
        Assert.Equal(expected, result);
    }
}
```

#### Step 4: Run Tests
```bash
dotnet test --filter "NetworkUtilsTests"
```

#### Step 5: Commit and Push
```bash
git checkout -b port/network-utils
git add .
git commit -m "Port: util.go to NetworkUtils.cs"
git push origin port/network-utils
```

---

## 🧪 Testing Your Changes

### Run Specific Tests
```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "NetworkUtilsTests"

# Run specific test method
dotnet test --filter "JoinHostPort_ShouldFormatCorrectly"

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"
```

### Debug Tests in Visual Studio
1. Open Test Explorer (Test → Test Explorer)
2. Right-click test → Debug
3. Set breakpoints in code

### Debug Tests in VS Code
1. Add to `.vscode/launch.json`:
```json
{
    "name": ".NET Test",
    "type": "coreclr",
    "request": "launch",
    "program": "dotnet",
    "args": ["test"],
    "cwd": "${workspaceFolder}",
    "console": "internalConsole",
    "stopAtEntry": false
}
```

---

## 🔍 Common Porting Patterns

### Pattern 1: Simple Struct → Class
```go
// Go
type Node struct {
    Name string
    Addr net.IP
    Port uint16
}
```

```csharp
// C#
public class Node
{
    public string Name { get; set; } = string.Empty;
    public IPAddress Addr { get; set; } = IPAddress.None;
    public ushort Port { get; set; }
}
```

### Pattern 2: Method → Method
```go
// Go
func (m *Memberlist) NumMembers() int {
    m.nodeLock.RLock()
    defer m.nodeLock.RUnlock()
    return len(m.nodes)
}
```

```csharp
// C#
public int NumMembers()
{
    _nodeLock.EnterReadLock();
    try
    {
        return _nodes.Count;
    }
    finally
    {
        _nodeLock.ExitReadLock();
    }
}
```

### Pattern 3: Error Handling
```go
// Go
func validate(name string) error {
    if name == "" {
        return errors.New("name is required")
    }
    return nil
}
```

```csharp
// C#
public void Validate(string name)
{
    if (string.IsNullOrEmpty(name))
    {
        throw new ArgumentException("Name is required", nameof(name));
    }
}
```

---

## 🔧 Development Tools

### Recommended VS Extensions
- **C# Dev Kit** (Microsoft)
- **EditorConfig** (EditorConfig)
- **GitLens** (GitKraken)
- **.NET Core Test Explorer** (Jun Han)

### Recommended VS Code Extensions
```json
{
    "recommendations": [
        "ms-dotnettools.csharp",
        "ms-dotnettools.csdevkit",
        "editorconfig.editorconfig",
        "eamodio.gitlens"
    ]
}
```

### Code Formatting
```bash
# Format all files
dotnet format

# Check formatting (CI)
dotnet format --verify-no-changes
```

---

## 📖 Learning Resources

### Understanding Serf
1. Read original docs: `../serf/README.md`
2. Read memberlist docs: `../memberlist/README.md`
3. Watch: [HashiCorp Serf Overview](https://www.hashicorp.com/resources/serf)

### Learning C# Async
- [Async/Await Best Practices](https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/)

### Learning System.Threading.Channels
- [An Introduction to Channels](https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/)

### Performance
- [High-performance networking in C#](https://devblogs.microsoft.com/dotnet/system-io-pipelines-high-performance-io-in-net/)
- [Span<T> Guide](https://learn.microsoft.com/en-us/archive/msdn-magazine/2018/january/csharp-all-about-span-exploring-a-new-net-mainstay)

---

## 🎓 Porting Workflow

### Daily Routine
```bash
# 1. Start of day - sync with main
git checkout main
git pull origin main

# 2. Create feature branch
git checkout -b port/your-feature

# 3. Port code + write tests
# ... development work ...

# 4. Test your changes
dotnet test

# 5. Format code
dotnet format

# 6. Commit and push
git add .
git commit -m "Port: description"
git push origin port/your-feature

# 7. Create PR and request review
```

### Code Review Process
1. **Self Review**: Check your own PR first
2. **Automated Checks**: Wait for CI to pass
3. **Peer Review**: Respond to comments
4. **Merge**: Once approved, merge to main

---

## 🐛 Troubleshooting

### Build Errors

**Error**: `The type or namespace name 'X' could not be found`
- **Solution**: Add missing `using` statement or install NuGet package

**Error**: `Nullable reference types` warning
- **Solution**: Initialize properties or mark as nullable:
```csharp
public string Name { get; set; } = string.Empty;  // Not null
public string? Name { get; set; }  // Nullable
```

### Test Failures

**Error**: `System.Net.Sockets.SocketException: Address already in use`
- **Solution**: Use port 0 to let OS assign available port

**Error**: `Task was canceled`
- **Solution**: Increase timeout or check `CancellationToken` handling

### Performance Issues

**Problem**: High memory allocation
- **Solution**: Use `Span<T>`, `ArrayPool<T>`, or `MemoryPool<T>`

**Problem**: CPU usage high
- **Solution**: Profile with dotTrace or Visual Studio Profiler

---

## 📊 Tracking Progress

### Update Checkpoints
When you complete a task:
1. Open `PROJECT.md`
2. Find relevant checkpoint
3. Change `[ ]` to `[x]`
4. Commit: `git commit -m "Checkpoint: completed NetworkUtils"`

### Weekly Updates
Every week, update `ROADMAP.md`:
- Current progress percentage
- Blockers or risks
- Next week's focus

---

## 🤝 Getting Help

### Documentation
- **PROJECT.md**: Overall plan and milestones
- **ROADMAP.md**: Timeline and progress
- **CONTRIBUTING.md**: Detailed porting guidelines
- **ARCHITECTURE.md**: System design (coming soon)

### Communication
- Daily standup: Share progress and blockers
- Code reviews: Ask questions in PR comments
- Team chat: Quick questions and discussions

### Reference Material
- Go source: `../memberlist/` and `../serf/`
- Go tests: Great examples of expected behavior
- This documentation: Keep referring back!

---

## ✅ Checklist for First Day

- [ ] .NET 8 SDK installed
- [ ] Solution builds successfully
- [ ] All tests run
- [ ] IDE configured (VS or VS Code)
- [ ] Read PROJECT.md and ROADMAP.md
- [ ] Understand Memberlist vs Serf
- [ ] Picked first task
- [ ] Created feature branch
- [ ] Ready to code!

---

## 🚀 Next Steps

1. **Explore the codebase**: Browse existing ported files
2. **Run the tests**: See what's working
3. **Pick a task**: Start with something small
4. **Ask questions**: Don't hesitate to ask for help
5. **Have fun**: You're building something awesome!

---

**Welcome to the team! Let's build NSerf together! 🎉**
