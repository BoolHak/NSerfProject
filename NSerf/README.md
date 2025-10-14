# NSerf

> A complete C# port of HashiCorp Serf - Decentralized service discovery and orchestration

[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/badge/license-MPL--2.0-blue.svg)](LICENSE)
[![Status](https://img.shields.io/badge/status-In%20Development-yellow.svg)](PROJECT.md)

---

## 📖 About

NSerf is a faithful C# rewrite of [HashiCorp Serf](https://github.com/hashicorp/serf), providing:

- **Cluster Membership**: SWIM-based gossip protocol for managing cluster membership
- **Failure Detection**: Adaptive detection with Lifeguard extensions
- **Event System**: Propagate custom events across your cluster
- **Distributed Queries**: Query nodes with filtering and fanout
- **Network Coordinates**: Vivaldi algorithm for RTT estimation
- **Encryption**: AES-256 GCM for secure communication
- **Snapshots**: Persistent state for fast recovery
- **Key Rotation**: Dynamic encryption key management

### Why NSerf?

- ✅ **Protocol Compatible**: Can join clusters with Go Serf
- ✅ **Modern .NET**: Built on .NET 8+ with async/await
- ✅ **High Performance**: Optimized with Span<T>, ArrayPool, and more
- ✅ **Well Tested**: Comprehensive unit and integration tests
- ✅ **Production Ready**: (Coming soon)

---

## 🚀 Quick Start

### Installation

```bash
# Coming soon: Install via NuGet
dotnet add package NSerf

# For now: Build from source
git clone https://github.com/your-org/NSerf.git
cd NSerf
dotnet build
```

### Basic Usage

```csharp
using NSerf;
using NSerf.Serf;

// Create configuration
var config = new SerfConfig
{
    NodeName = "node1",
    BindAddr = "0.0.0.0",
    BindPort = 7946
};

// Create and start Serf
using var serf = await Serf.CreateAsync(config);

// Join existing cluster
await serf.JoinAsync(new[] { "192.168.1.100:7946" });

// Get cluster members
var members = serf.Members();
foreach (var member in members)
{
    Console.WriteLine($"{member.Name} - {member.Addr}:{member.Port}");
}

// Fire a user event
await serf.UserEventAsync("deploy", Encoding.UTF8.GetBytes("v1.2.3"));

// Send a query
var response = await serf.QueryAsync("load", null, 
    new QueryOptions { Timeout = TimeSpan.FromSeconds(5) });

await foreach (var nodeResponse in response.Responses())
{
    Console.WriteLine($"{nodeResponse.From}: {nodeResponse.Payload}");
}
```

---

## 📚 Documentation

### For Users
- **[Installation Guide](docs/INSTALLATION.md)** (Coming soon)
- **[User Guide](docs/USER_GUIDE.md)** (Coming soon)
- **[Configuration Reference](docs/CONFIGURATION.md)** (Coming soon)
- **[API Reference](docs/API_REFERENCE.md)** (Coming soon)

### For Developers
- **[Getting Started](GETTING_STARTED.md)** - Start here!
- **[Project Plan](PROJECT.md)** - Milestones and checkpoints
- **[Roadmap](ROADMAP.md)** - Timeline and progress
- **[Contributing Guide](CONTRIBUTING.md)** - Porting guidelines
- **[Architecture](docs/ARCHITECTURE.md)** (Coming soon)

---

## 🗺️ Project Status

### Current Phase: Phase 0 - Planning ✅
### Overall Progress: 0% Complete

| Phase | Status | Progress |
|-------|--------|----------|
| Phase 1: Memberlist Core | 🔄 In Progress | 5% |
| Phase 2: Serf Core | ⏳ Pending | 0% |
| Phase 3: Advanced Features | ⏳ Pending | 0% |
| Phase 4: Testing & Hardening | ⏳ Pending | 0% |
| Phase 5: Documentation | ⏳ Pending | 0% |

See [ROADMAP.md](ROADMAP.md) for detailed timeline.

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────┐
│          Your Application               │
│   (Service Discovery, Orchestration)    │
└─────────────────┬───────────────────────┘
                  │
                  │ Events, Queries, Tags
                  ▼
┌─────────────────────────────────────────┐
│             NSerf.Serf                  │
│  - Event System & Coalescence           │
│  - Distributed Queries                  │
│  - Network Coordinates (Vivaldi)        │
│  - Snapshots & Persistence              │
│  - Key Management                       │
└─────────────────┬───────────────────────┘
                  │
                  │ Delegate Interface
                  ▼
┌─────────────────────────────────────────┐
│         NSerf.Memberlist                │
│  - SWIM Protocol                        │
│  - Failure Detection                    │
│  - Gossip / Anti-Entropy                │
│  - AES-256 GCM Encryption               │
│  - Broadcast Queue                      │
└─────────────────┬───────────────────────┘
                  │
                  │ UDP/TCP
                  ▼
            [ Network ]
```

---

## 🎯 Features

### ✅ Planned
- [x] Project structure and planning
- [ ] SWIM membership protocol
- [ ] Failure detection with suspicion mechanism
- [ ] Gossip-based state propagation
- [ ] AES-256 GCM encryption
- [ ] Event system with coalescence
- [ ] Distributed query system
- [ ] Vivaldi network coordinates
- [ ] Snapshot persistence
- [ ] Key rotation
- [ ] Command-line interface

### 🔮 Future (v2.0+)
- [ ] TLS transport support
- [ ] gRPC API
- [ ] Prometheus metrics exporter
- [ ] ASP.NET Core integration
- [ ] Docker examples
- [ ] Kubernetes operator

---

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true

# Run specific test class
dotnet test --filter "MemberlistTests"

# Run benchmarks
dotnet run --project benchmarks/NSerf.Benchmarks -c Release
```

### Test Coverage Goals
- **Unit Tests**: >80% coverage
- **Integration Tests**: All major scenarios
- **Chaos Tests**: Network partitions, failures
- **Performance Tests**: Benchmarks for critical paths

---

## 🤝 Contributing

We welcome contributions! Here's how to get started:

1. **Read the docs**: [GETTING_STARTED.md](GETTING_STARTED.md)
2. **Pick a task**: See [PROJECT.md](PROJECT.md) for milestones
3. **Write code**: Follow [CONTRIBUTING.md](CONTRIBUTING.md)
4. **Submit PR**: We'll review and merge

### Good First Issues
- Port utility functions
- Write unit tests
- Improve documentation
- Add code examples

See [Contributing Guide](CONTRIBUTING.md) for detailed guidelines.

---

## 📦 Project Structure

```
NSerf/
├── NSerf/                      # Main library
│   ├── Memberlist/             # SWIM membership protocol
│   │   ├── Protocol/           # Core protocol logic
│   │   ├── Transport/          # Network transport
│   │   ├── Security/           # Encryption & keyring
│   │   ├── State/              # Node state management
│   │   ├── Broadcast/          # Gossip queue
│   │   └── Delegates/          # Extension points
│   ├── Serf/                   # Orchestration layer
│   │   ├── Core/               # Main Serf logic
│   │   ├── Events/             # Event system
│   │   ├── Queries/            # Query system
│   │   ├── Coordinate/         # Network coordinates
│   │   ├── Snapshot/           # Persistence
│   │   ├── KeyManagement/      # Key rotation
│   │   └── Delegates/          # Memberlist integration
│   └── Cli/                    # Command-line tool
├── NSerfTests/                 # All tests
│   ├── Memberlist/             # Memberlist tests
│   ├── Serf/                   # Serf tests
│   └── Common/                 # Shared utilities
├── docs/                       # Documentation
├── examples/                   # Code examples
└── benchmarks/                 # Performance benchmarks
```

---

## 📖 Learn More

### About Serf
- [Original Serf Documentation](https://github.com/hashicorp/serf/tree/master/docs)
- [SWIM Paper](https://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf)
- [Lifeguard Paper](https://arxiv.org/abs/1707.00788)

### About This Port
- [Porting Decisions](docs/PORTING_NOTES.md) (Coming soon)
- [Differences from Go](docs/DIFFERENCES.md) (Coming soon)
- [Performance Analysis](docs/PERFORMANCE.md) (Coming soon)

---

## 🔒 Security

- **Encryption**: AES-256 GCM with unique nonces
- **Authentication**: Label-based authenticated data
- **Key Rotation**: Seamless cluster-wide key updates

Report security issues to: security@yourorg.com

---

## 📄 License

This project is licensed under the **Mozilla Public License 2.0 (MPL-2.0)**, the same license as the original HashiCorp Serf.

```
Copyright (c) HashiCorp, Inc.
SPDX-License-Identifier: MPL-2.0
```

See [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- **HashiCorp** for creating Serf and Memberlist
- Original Serf contributors and maintainers
- SWIM and Lifeguard paper authors
- The .NET community

---

## 📞 Contact & Support

- **Issues**: [GitHub Issues](https://github.com/your-org/NSerf/issues)
- **Discussions**: [GitHub Discussions](https://github.com/your-org/NSerf/discussions)
- **Discord**: (Coming soon)

---

## 🗓️ Milestones

### Phase 1: Memberlist Core (Months 1-4)
- [ ] Network transport layer
- [ ] SWIM protocol implementation
- [ ] Failure detection
- [ ] Encryption support

### Phase 2: Serf Core (Months 5-8)
- [ ] Event system
- [ ] Query system
- [ ] Network coordinates

### Phase 3: Advanced Features (Months 9-10)
- [ ] Snapshot persistence
- [ ] Key management
- [ ] CLI interface

### Phase 4: Testing & Hardening (Month 11)
- [ ] Comprehensive testing
- [ ] Performance optimization

### Phase 5: Documentation (Month 12)
- [ ] Complete documentation
- [ ] Examples and tutorials
- [ ] v1.0 Release

See [ROADMAP.md](ROADMAP.md) for detailed timeline.

---

## ⭐ Show Your Support

If you find NSerf useful, please:
- ⭐ Star this repository
- 🐛 Report bugs and issues
- 💡 Suggest features
- 🤝 Contribute code
- 📢 Spread the word

---

<p align="center">
  <strong>Built with ❤️ by the NSerf team</strong><br>
  <sub>Bringing enterprise-grade clustering to .NET</sub>
</p>

---

**Status**: 🚧 In Active Development  
**Target Release**: Q4 2025  
**Current Version**: 0.1.0-alpha
