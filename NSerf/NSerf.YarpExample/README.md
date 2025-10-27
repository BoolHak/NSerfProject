# NSerf YARP Integration Example

This example demonstrates **production-ready dynamic service discovery and load balancing** using NSerf with Microsoft YARP (Yet Another Reverse Proxy).

## 🎯 What This Demonstrates

- ✅ **Dynamic Service Discovery** - YARP automatically discovers backend services via Serf cluster membership
- ✅ **Automatic Load Balancing** - Round-robin distribution across healthy backends
- ✅ **Health Monitoring** - YARP actively monitors backend health and removes failed nodes
- ✅ **Zero-Configuration Scaling** - Add/remove backends without restarting the proxy
- ✅ **Encrypted Cluster Communication** - AES-256-GCM encryption for secure gossip protocol

## 🏗️ Architecture

```
                                ┌──────────────────┐
                                │                  │
                           ┌────┤  YARP Proxy      │
                           │    │  (Port 8080)     │
                           │    └──────────────────┘
                           │             │
                           │    ┌────────┴────────┐
Client Requests ───────────┤    │  NSerf Cluster  │
                           │    └────────┬────────┘
                           │             │
                           │    ┌────────┴────────┐
                           │    │                 │
                           │    │  Service        │
                           │    │  Discovery      │
                           │    │                 │
                           │    └────────┬────────┘
                           │             │
                           └──────┬──────┴──────┬──────────┐
                                  │             │          │
                          ┌───────┴───────┐     │          │
                          │  Backend-1    │     │          │
                          │  (Port 5001)  │     │          │
                          └───────────────┘     │          │
                                        ┌───────┴───────┐  │
                                        │  Backend-2    │  │
                                        │  (Port 5002)  │  │
                                        └───────────────┘  │
                                                  ┌────────┴────────┐
                                                  │  Backend-3      │
                                                  │  (Port 5003)    │
                                                  └─────────────────┘
```

## 🚀 Running the Example

### Option 1: Docker Compose (Recommended)

```bash
cd NSerf.YarpExample
docker-compose up --build
```

This starts:
- **1 YARP Proxy** on `http://localhost:8080`
- **3 Backend Services** on ports 5001-5003
- **Serf Cluster** for service discovery (ports 7950-7953)
- **Encrypted gossip** with shared AES-256 key
- **Persistent snapshots** in Docker volumes

### Option 2: Manual Local Execution

Terminal 1 - First Backend:
```bash
cd NSerf.BackendService
dotnet run -- backend-1 5001 7951
```

Terminal 2 - Second Backend:
```bash
cd NSerf.BackendService
dotnet run -- backend-2 5002 7952 localhost:7951
```

Terminal 3 - Third Backend:
```bash
cd NSerf.BackendService
dotnet run -- backend-3 5003 7953 localhost:7951
```

Terminal 4 - YARP Proxy:
```bash
cd NSerf.YarpExample
dotnet run -- yarp-proxy 8080 7950 localhost:7951
```

## 📊 Testing the Setup

### 1. Check Proxy Status

```bash
# Verify proxy is healthy
curl http://localhost:8080/proxy/health

# View discovered backends
curl http://localhost:8080/proxy/members | jq
```

### 2. Test Load Balancing

```bash
# Make multiple requests - notice they round-robin across backends
for i in {1..10}; do
  curl http://localhost:8080/api/info | jq '.instance'
done
```

You should see requests distributed across `backend-1`, `backend-2`, and `backend-3`.

### 3. Test Dynamic Service Discovery

**Add a new backend:**
```bash
# Terminal 5 - New backend joins automatically
cd NSerf.BackendService
dotnet run -- backend-4 5004 7954 localhost:7951
```

Wait 5 seconds for discovery, then:
```bash
# New backend is automatically included in load balancing!
for i in {1..10}; do
  curl http://localhost:8080/api/info | jq '.instance'
done
```

**Simulate backend failure:**
```bash
# Stop backend-2
docker-compose stop backend-2

# Requests automatically route around failed node
for i in {1..10}; do
  curl http://localhost:8080/api/info | jq '.instance'
done
```

**Restore the backend:**
```bash
# Restart backend-2
docker-compose start backend-2

# It automatically rejoins and receives traffic
```

### 4. Test API Endpoints

```bash
# Get backend info
curl http://localhost:8080/api/info | jq

# Simulate work (notice different backends process requests)
curl http://localhost:8080/api/work/job-123 | jq
curl http://localhost:8080/api/work/job-456 | jq

# View cluster from backend perspective
curl http://localhost:5001/api/cluster | jq
```

## 🔧 How It Works

### YARP Proxy (`NSerf.YarpExample`)

1. **Joins Serf Cluster** with role `proxy`
2. **SerfServiceDiscoveryProvider** polls cluster every 5 seconds
3. **Discovers Backend Services** tagged with `service=backend`
4. **Updates YARP Configuration** dynamically with discovered destinations
5. **YARP Routes Traffic** using round-robin load balancing
6. **Health Checks** remove unhealthy backends automatically

Key Code: `SerfServiceDiscoveryProvider.cs`
```csharp
var members = _agent.Serf.Members()
    .Where(m => m.Status == MemberStatus.Alive)
    .Where(m => m.Tags["service"] == "backend")
    .ToList();

// Create YARP destinations from Serf members
var destinations = members.Select(m => new DestinationConfig
{
    Address = $"http://{m.Addr}:{m.Tags["http-port"]}"
}).ToDictionary(d => Guid.NewGuid().ToString(), d => d);
```

### Backend Services (`NSerf.BackendService`)

1. **Registers with Serf** using tags:
   - `service=backend`
   - `http-port=5000`
   - `version=1.0`
2. **Exposes Health Endpoint** at `/health`
3. **Provides Sample APIs** at `/api/info`, `/api/work`, `/api/cluster`

## 🔐 Security & Persistence Features

### Encrypted Cluster Communication

All nodes use **AES-256-GCM encryption** for secure gossip protocol communication:

```yaml
environment:
  - SERF_ENCRYPT_KEY=sSKDkyfVAKUMnWj2l0nuJBU1arxZ9pe6Q7hjH8nESbc=
```

**Key Features:**
- ✅ **AES-256-GCM** - Good encryption
- ✅ **Authenticated** - Prevents message tampering
- ✅ **Cluster-wide** - All nodes must share the same key
- ✅ **Protects gossip** - Member updates, user events, and queries

**Generate Your Own Key:**
```bash
# Linux/Mac
./generate-key.sh

# Windows
.\generate-key.ps1

# Or use OpenSSL
openssl rand -base64 32
```

**Without the correct key, nodes cannot join the cluster!**

## 🎯 Production Use Cases

### 1. Microservices API Gateway
Replace static YARP configuration with dynamic Serf discovery:
- Services auto-register on startup
- Gateway discovers and routes automatically
- No manual configuration updates needed

### 2. Multi-Region Load Balancing
Use Serf tags for region-aware routing:
```csharp
options.Tags["service"] = "api";
options.Tags["region"] = "us-east-1";
options.Tags["zone"] = "1a";
```

### 3. Canary Deployments
Use version tags for traffic shaping:
```csharp
options.Tags["version"] = "2.0";
options.Tags["canary"] = "true";
```

Filter in YARP provider:
```csharp
var stableBackends = members.Where(m => m.Tags.GetValueOrDefault("canary") != "true");
var canaryBackends = members.Where(m => m.Tags.GetValueOrDefault("canary") == "true");
// Route 90% to stable, 10% to canary
```

### 4. Service Mesh
Combine with mTLS and circuit breakers:
```csharp
new ClusterConfig
{
    LoadBalancingPolicy = "RoundRobin",
    HealthCheck = { /* ... */ },
    HttpClient = new HttpClientConfig
    {
        MaxConnectionsPerServer = 100,
        DangerousAcceptAnyServerCertificate = false
    }
};
```

## 📁 Port Mappings

| Service | HTTP Port | Serf Port | Description |
|---------|-----------|-----------|-------------|
| YARP Proxy | 8080 | 7950 | Reverse proxy with service discovery |
| Backend-1 | 5001 | 7951 | Backend service (bootstrap node) |
| Backend-2 | 5002 | 7952 | Backend service |
| Backend-3 | 5003 | 7953 | Backend service |

## 🔍 Monitoring

### View YARP Metrics
```bash
curl http://localhost:8080/proxy/members | jq '.backends'
```

### View Backend Cluster Info
```bash
curl http://localhost:5001/api/cluster | jq
```

### Monitor Docker Logs
```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f yarp-proxy
docker-compose logs -f backend-1
```

## 🛑 Cleanup

```bash
docker-compose down
```

## 📚 Learn More

- [YARP Documentation](https://microsoft.github.io/reverse-proxy/)
- [NSerf Main README](../../README.md)
- [Serf Website](https://www.serf.io/)
- [Service Discovery Patterns](https://microservices.io/patterns/server-side-discovery.html)

## 🤝 Why This Matters

This example showcases **real production value**:
- No external service discovery (Consul, Eureka, etc.)
- Built-in gossip protocol for membership
- YARP handles HTTP routing and load balancing
- Complete solution in < 500 lines of code
- Enterprise-grade, production-ready patterns