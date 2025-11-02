# YARP Service Discovery with Serf – Implementation Plan

## Goals
- **Proper service discovery**: multiple logical services behind YARP using Serf membership/tags.
- **Per-service routing**: host- or path-based routes per service.
- **Health checks**: active health probing per service/destination.
- **Event-driven updates**: instant updates on join/leave/fail/update with timer fallback.
- **Weights/versions**: enable canary/weighted routing from tags.
- **Backwards compatible**: if no service tags present, keep today’s single-cluster behavior.

## Scope (What changes)
- **Enhance SerfServiceDiscoveryProvider** to:
  - Build a ServiceCatalog from Serf members/tags.
  - Generate YARP `ClusterConfig` and `RouteConfig` per service.
  - Diff current vs new config; push only when changed.
  - React to member join/leave/fail (and tag updates if surfaced), with periodic reconcile.
- **Add docs + sample** to register services with tags.
- **Add minimal tests** for catalog building and config generation.

## Service Registration Contract (via Serf tags)
- **Required**
  - `service`: logical service name (e.g., `orders`, `payments`).
  - `http-port`: destination port, e.g., `5000`.
- **Optional**
  - `scheme`: `http` (default) or `https`.
  - `route-path`: path match for routing, e.g., `/orders/{**catch-all}`.
  - `host`: host-based routing, e.g., `orders.local`.
  - `health-path`: path for health checks, default `/health`.
  - `weight`: integer (default `1`).
  - `version`: label for canary/segments, e.g., `stable`, `canary`, `v2`.
  - `region`: informational / future policies.
  - `instance`: stable destination key; defaults to `ip:port`.

Notes:
- Provide at least one routing hint: `host` or `route-path`.
- If neither provided across all services, fallback to single-service config with catch-all route.

## ServiceCatalog Model
- **ServiceInstance**
  - Address: `scheme://[ip]:port`
  - Key: `instance` tag or `ip:port`
  - Meta: `version`, `region`, `weight`, `health-path`, etc.
- **ServiceCatalog**: `Dictionary<string serviceName, List<ServiceInstance>>`
- **Build Rules**
  - Consider only `Alive` members.
  - Must have `service` and `http-port`.
  - Group by `service`.
  - Sort destinations deterministically by key to keep stable diffs.

## YARP Mapping
- **Clusters**: one per service
  - `ClusterId = service:{name}`
  - `Destinations`: `{ key -> DestinationConfig { Address, Metadata? } }`
  - `LoadBalancingPolicy = RoundRobin`
  - `HealthCheck.Active`: enable, `Interval=2s`, `Timeout=1s`, `Path = health-path|/health`
  - Optional: attach `weight` via `DestinationConfig.Metadata["Weight"]` (for future custom policy), or duplicate destinations by weight as a first-pass approach.
- **Routes**: one per service
  - `RouteId = route:{name}`
  - `ClusterId = service:{name}`
  - If `host` set: `Match.Hosts = [host]`
  - Else if `route-path` set: `Match.Path = route-path`
  - Else: only allow this when there is exactly one service; set `/{**catch-all}`

## Update Triggers
- **Event-driven**: on `MemberEvent` (Join/Leave/Failed). If a `MemberUpdate` (tags change) exists, treat the same.
- **Timer fallback**: keep reconciliation timer (e.g., 30s) to self-heal missed updates.

## Config Diffing
- Extract current set: sorted tuples `(service, key, address, weight, health-path, route criteria)`
- Extract new set from catalog.
- If counts differ or any tuple differs, rebuild YARP config and push.

## Backward Compatibility
- If no member exposes `service` tag:
  - Preserve current behavior: single `backend-cluster` + catch-all route with `service=backend` selection.

## Example Tags
- Orders (host-based):
```
service=orders
http-port=5000
host=orders.local
health-path=/healthz
instance=orders-1
```
- Payments (path-based):
```
service=payments
http-port=6000
route-path=/payments/{**catch-all}
scheme=http
instance=payments-2
```
- Web with canary:
```
service=web
http-port=8080
route-path=/{**catch-all}
version=stable
weight=10
```
```
service=web
http-port=8081
route-path=/{**catch-all}
version=canary
weight=1
```

## Minimal Test Plan
- **CatalogBuilderTests**
  - Builds catalog from mixed members (Alive/Left/Failed) → filters to Alive.
  - Groups by service; uses instance tag as key or falls back to ip:port.
  - Deterministic ordering.
- **ConfigGenerationTests**
  - Per-service clusters + routes for host-based and path-based services.
  - Health path defaults and overrides.
  - Weights (either via metadata presence or duplication strategy).
  - Fallback to single-backend when no service tags exist.
- **DiffingTests**
  - No-op when nothing changes.
  - Push when a destination is added/removed/changed.

## Rollout Steps
- **Phase 1**: Introduce ServiceCatalog + config generation, guarded by internal flag; default ON.
- **Phase 2**: Event-driven updates (include tag-change if supported), timer reconcile tuning.
- **Phase 3**: Weights/canary first-pass (duplication strategy), optional custom policy later.
- **Phase 4**: Documentation + sample app registrations + scripts.
- **Phase 5**: Tests and stabilization.

## Risks & Mitigations
- **Tag updates not surfaced as events**: rely on reconcile timer; consider periodic forced diff.
- **IPv6 formatting issues**: already handled by address builder (`[ip]`).
- **Route conflicts**: document that services must choose distinct host or path; we will not auto-resolve conflicts.
- **Weighted routing policy**: initial duplication strategy; consider YARP custom policy in a later iteration.

## Work Items
- **Design & Docs**
  - Define and document tag schema (this file).
  - README with examples and join commands.
- **Code**
  - Implement catalog builder and diff logic.
  - Refactor provider to generate per-service clusters/routes.
  - Health checks per service.
  - Event + timer updates.
  - Backward-compatible fallback.
- **Tests**
  - Unit tests for catalog and config generation.
  - Light integration test if feasible.

## Acceptance Criteria
- Multiple services registered with tags appear as distinct clusters/routes in YARP.
- Health checks hit per-destination health paths.
- Join/leave/update reflect within seconds (event-driven) or within 30s (timer).
- No change → no YARP reload.
- No `service` tags → legacy single-backend mode.
