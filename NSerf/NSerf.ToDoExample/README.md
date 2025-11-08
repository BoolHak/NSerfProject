# NSerf Todo Example - PostgreSQL with Embedded NSerf Agent

This example demonstrates **NSerf service discovery for non-.NET dependencies** by running the **NSerf.CLI agent** **inside** the PostgreSQL container.

## Architecture

```
┌──────────────────────────────────────────────────────┐
│  NSerf Gossip Cluster                                │
│                                                      │
│  ┌─────────────┐    ┌─────────────┐                │
│  │ Todo API 1  │    │ Todo API 2  │                │
│  │ (NSerf lib) │◄──►│ (NSerf lib) │                │
│  └─────────────┘    └─────────────┘                │
│         │                   │                        │
│         └───────┬───────────┘                        │
│                 │ Gossip                             │
│                 ▼                                    │
│  ┌────────────────────────────┐                     │
│  │  PostgreSQL Container      │                     │
│  │  ┌──────────────────────┐  │                     │
│  │  │  NSerf.CLI Agent     │  │                     │
│  │  │  (advertises DB)     │  │                     │
│  │  └──────────────────────┘  │                     │
│  │  ┌──────────────────────┐  │                     │
│  │  │  PostgreSQL Server   │  │                     │
│  │  └──────────────────────┘  │                     │
│  └────────────────────────────┘                     │
└──────────────────────────────────────────────────────┘
```

## Key Difference from Traditional Approach

### ❌ Traditional (Sidecar Pattern)
```yaml
services:
  postgres:
    image: postgres
  postgres-serf:  # Separate container
    image: hashicorp/serf
```
- 2 containers per database
- Network complexity
- Separate lifecycle management
- Uses HashiCorp's Serf (Go binary)

### ✅ NSerf Approach (Agent Inside Container)
```yaml
services:
  postgres:
    build: postgres.Dockerfile  # NSerf.CLI agent INSIDE
```
- 1 container
- Simpler networking
- Single lifecycle
- Agent dies with database (proper health signal)
- **Uses our own NSerf.CLI agent (.NET)**

## How It Works

### 1. PostgreSQL Container with NSerf.CLI Agent

The `postgres.Dockerfile` builds a PostgreSQL image with our **NSerf.CLI agent**:

```dockerfile
# Build NSerf.CLI
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
COPY ../NSerf/ NSerf/
COPY ../NSerf.CLI/ NSerf.CLI/
RUN dotnet publish NSerf.CLI/NSerf.CLI.csproj -o /app/publish

# PostgreSQL with .NET runtime + NSerf agent
FROM postgres:16-alpine
RUN apk add --no-cache icu-libs && \
    # Install .NET runtime
    wget https://dot.net/v1/dotnet-install.sh && \
    ./dotnet-install.sh --channel 8.0 --runtime aspnetcore

COPY --from=build /app/publish /usr/local/nserf/
```

### 2. Startup Script

The `postgres-entrypoint.sh` starts both NSerf.CLI and PostgreSQL:

```bash
# Start NSerf agent in background
dotnet /usr/local/nserf/NSerf.CLI.dll agent \
  --node-name=postgres-node \
  --bind-addr=0.0.0.0:7946 \
  --tag service:postgres=true \
  --tag port:postgres=5432 \
  --tag username=postgres &

# Start PostgreSQL
docker-entrypoint.sh postgres &
```

### 3. API Discovery

The Todo API uses NSerf library to discover PostgreSQL:

```csharp
// API has NSerf embedded
var instances = _registry.GetHealthyInstances("postgres");
var connectionString = $"Host={instance.Host};Port={instance.Port};...";
```

## Running the Example

### Start Everything

```bash
cd NSerf.ToDoExample
docker-compose up --build
```

You'll see:
```
postgres    | 🚀 Starting NSerf agent inside PostgreSQL container...
postgres    | ✅ NSerf agent started with PID 42
postgres    | 🐘 Starting PostgreSQL...
todo-api-1  | 🚀 Starting NSerf node: todo-api-...
todo-api-1  | 📡 Service event: InstanceRegistered - postgres/postgres-node:postgres
todo-api-1  | ✅ Discovered PostgreSQL at postgres:5432 via NSerf gossip
```

### Test the API

```bash
# Create a todo
curl -X POST http://localhost:8080/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title": "Test NSerf", "description": "PostgreSQL discovered via gossip!"}'

# Get all todos
curl http://localhost:8080/api/todos

# Update todo
curl -X PUT http://localhost:8080/api/todos/1 \
  -H "Content-Type: application/json" \
  -d '{"isCompleted": true}'
```

### View Swagger

http://localhost:8080/swagger

### Check Cluster Membership

```bash
# Exec into PostgreSQL container
docker exec -it todo-postgres sh

# Check NSerf members
dotnet /usr/local/nserf/NSerf.CLI.dll members

# Output:
# postgres-node    172.20.0.2:7946    alive   service:postgres=true,port:postgres=5432
# todo-api-...     172.20.0.3:7946    alive   service:todo-api=true
# todo-api-...     172.20.0.4:7946    alive   service:todo-api=true
```

## Why This Matters

### Traditional Approach Problems

```yaml
# Hardcoded connection string
ConnectionStrings:
  Default: "Host=postgres;Port=5432;..."
```

**Issues:**
- ❌ Hardcoded hostname
- ❌ No dynamic discovery
- ❌ Manual updates when DB moves
- ❌ No health awareness
- ❌ Requires DNS/service mesh

### NSerf Approach Benefits

```csharp
// Dynamic discovery
var instances = _registry.GetHealthyInstances("postgres");
```

**Advantages:**
- ✅ Zero configuration
- ✅ Automatic discovery
- ✅ Health-aware routing
- ✅ Works across any network
- ✅ No external dependencies
- ✅ Fast failure detection (gossip)
- ✅ Metadata propagation (credentials, config)

## Use Cases

This pattern works for **any** non-.NET service:

### Databases
```dockerfile
FROM postgres:16-alpine
# Add Serf agent
```

### Caches
```dockerfile
FROM redis:7-alpine
# Add Serf agent
```

### Message Queues
```dockerfile
FROM rabbitmq:3-alpine
# Add Serf agent
```

### Search Engines
```dockerfile
FROM elasticsearch:8.11.0
# Add Serf agent
```

## Production Considerations

### 1. Health Checks

The Serf agent monitors the service:

```yaml
healthcheck:
  test: ["CMD-SHELL", "pg_isready && serf members"]
```

If PostgreSQL dies, Serf agent dies too → automatic deregistration!

### 2. Secure Credentials

Don't pass passwords via Serf tags:

```bash
# ❌ Bad
-tag password=secret123

# ✅ Good
-tag username=postgres
# Password from environment variable in API
```

### 3. Multi-Region Setup

```bash
serf agent \
  -node=postgres-us-east-1 \
  -tag service:postgres=true \
  -tag region=us-east \
  -tag port:postgres=5432
```

### 4. Read Replicas

```bash
# Primary
serf agent -tag service:postgres=true -tag role=primary

# Replica
serf agent -tag service:postgres=true -tag role=replica
```

Then in your API:
```csharp
// Write to primary
var primary = _registry.GetHealthyInstances("postgres")
    .First(i => i.Metadata["role"] == "primary");

// Read from replicas
var replicas = _registry.GetHealthyInstances("postgres")
    .Where(i => i.Metadata["role"] == "replica");
```

## Scaling

### Scale the API

```bash
docker-compose up --scale todo-api=5
```

All instances automatically discover PostgreSQL via gossip!

### Scale PostgreSQL (Read Replicas)

```yaml
postgres-replica:
  build:
    dockerfile: postgres.Dockerfile
  environment:
    SERF_NODE_NAME: postgres-replica-1
    SERF_SEED: postgres:7946
```

## Troubleshooting

### API can't find PostgreSQL

```bash
# Check if PostgreSQL's Serf agent is running
docker exec -it todo-postgres serf members

# Check API logs
docker logs todo-api-1
```

### Serf agent not starting in PostgreSQL

```bash
# Check PostgreSQL logs
docker logs todo-postgres

# Should see:
# 🚀 Starting Serf agent inside PostgreSQL container...
# ✅ Serf agent started with PID 42
```

### Connection refused

```bash
# Ensure PostgreSQL is healthy
docker-compose ps
docker exec -it todo-postgres pg_isready -U postgres
```

## Comparison with Other Solutions

| Solution | Complexity | External Deps | Dynamic | Health-Aware |
|----------|-----------|---------------|---------|--------------|
| **Hardcoded** | Low | None | ❌ | ❌ |
| **DNS** | Medium | DNS server | Limited | ❌ |
| **Consul** | High | Consul cluster | ✅ | ✅ |
| **Kubernetes** | High | K8s | ✅ | ✅ |
| **NSerf** | **Low** | **None** | **✅** | **✅** |

## Next Steps

- Add Redis cache with Serf agent
- Implement read replica discovery
- Add circuit breaker with health-based routing
- Deploy to production with monitoring
