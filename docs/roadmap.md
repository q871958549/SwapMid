# SwapMid Roadmap (`v0.2` → `v1.0 LTS`)

SwapMid focuses on being a **correctness-first, single-stack, agent-ready enterprise data swap and integration control plane** built on **.NET 10 (ABP v10.1)** and **Next.js 16**.

Every milestone adheres to five non-negotiable engineering invariants:
1. **Zero Compiler & Lint Warnings** (`TreatWarningsAsErrors=true`, `eslint --max-warnings=0`).
2. **Strict Domain Decoupling** (`domain-dependency-baseline.txt` — zero cross-domain cycles; cross-engine orchestration flows strictly through `SwapMid.SharedKernel` contracts and transactional outbox jobs).
3. **Deterministic Execution & Explicit Reconciliation** (`RunId` idempotency + `Target Receipt v1` + explicit `OutcomeUnknown` resolution).
4. **Zero-Token Browser BFF Boundary** (`HttpOnly` encrypted cookies + `/api/proxy/*` server-side token injection + outbound SSRF guards).
5. **100% Trilingual Parity** (`zh-Hans`, `zh-Hant`, `en` across backend and frontend).

---

## Milestone Overview

| Release | Theme | Key Capabilities |
| --- | --- | --- |
| **`v0.1.0`** *(Released)* | **Core Foundation** | Three integration engines (`DataIntegration`, `MessageBus`, `ApiManagement`), `Target Receipt v1`, Hangfire scheduling, 5-invariant impersonation guard, live DB schema dictionary, Docker Compose + live E2E smoke suite |
| **`v0.2.0`** *(Released)* | **Composable Event Mesh & CloudEvents 1.0** | Transactional Outbox bridge (`ETL → MessageBus`), CNCF CloudEvents v1.0 webhook headers, structured subscription filter rules (glob + JSONPath), PostgreSQL 18 CI contract service, multi-arch GHCR release workflow |
| **`v0.3.0`** *(Released)* | **Incremental Upsert & Production API Gateway** | Staging-backed atomic `Upsert` (`ON CONFLICT` / `MERGE` / `ON DUPLICATE KEY`) + `Upsert-Receipt-V1`, `external-integration` client gateway authorization, SSRF upstream guard, route-group ACLs, sliding-window rate limiting (`429`), and gateway webhook ingress |
| **`v0.5.0`** *(Released)* | **Connector SDK, Controlled MCP & Cloud-Native** | Connector capability registry & source-target schema preflight inspector, opt-in read-only/draft-only MCP server (`/api/mcp`), official Kubernetes Helm chart (`charts/swapmid/`), and turnkey `examples/` recipes |
| **`v1.0.0 LTS`** *(Current LTS)* | **Enterprise OIDC, Observability Pack & LTS Freeze** | Optional external OIDC SSO federation, turnkey Prometheus + Grafana dashboards + Alertmanager rule pack (`docker/observability/`), API v1 compatibility guard, and production upgrade runbook |

---

## Detailed Milestone Scope

### `v0.2.0` — Composable Event Mesh, CloudEvents 1.0 & Release Automation
- **Cross-Engine Transactional Outbox (`ETL → MessageBus`)**:
  - Atomic scheduling of terminal pipeline execution events (`Succeeded`, `Failed`, `Cancelled`, `OutcomeUnknown`) inside `DataPipelineExecutionCoordinator`'s terminal `UnitOfWork` via `AbpBackgroundJobs`.
  - Deterministic event IDs guarantee idempotent publishing to `integration.pipeline.events` without holding coordinator locks or coupling `DataIntegration` directly to `MessageBus`.
- **CloudEvents v1.0 & Structured Subscription Filters**:
  - Standard `ce-specversion`, `ce-id`, `ce-source`, `ce-type`, and `ce-time` headers alongside HMAC-SHA256 `X-SwapMid-Signature`.
  - Subscription `FilterRule` support for `subject:<glob>` wildcards, `$.path=value` JSON property matching, and multi-clause `AND` composition.
- **CI & Container Distribution**:
  - Live `postgres:18-alpine` service container in CI running all migration, advisory lock fence, and atomic transaction contract tests.
  - Automated multi-arch (`linux/amd64`, `linux/arm64`) GHCR image publishing on `v*` tags (`.github/workflows/release.yml`).

### `v0.3.0` — Incremental Upsert Pipelines & Production-Hardened Gateway
- **Atomic `Upsert` Mode**:
  - `mode: "upsert" | "upsert-receipt-v1"` with validated `keyColumns` and optional `watermarkColumn` across PostgreSQL, SQL Server, and MySQL, preserving lossless staging validation and `Target Receipt v1`.
- **Gateway Security, ACL & Traffic Governance**:
  - Dual-principal gateway authorization (`SwapMid.ApiManagement.ApiRoutes` + OpenIddict `external-integration` client tokens), SSRF/private-network upstream protection, per-client route-group ACLs, per-minute rate limiting, gateway latency/QPS Prometheus metrics, and asynchronous webhook ingress to `MessageBus`.

### `v0.5.0` — Pluggable Connector Contracts, Agent-Ready MCP & Helm
- **Schema Pre-flight & Connector Metadata**:
  - Static source-query to target-table column compatibility verification before pipeline activation.
- **Controlled MCP Endpoint (`/api/mcp`)**:
  - Fail-closed by default (`Mcp:Enabled=false`); exposes read-only schema inspection, execution/dead-letter diagnostics, and validated `Draft` pipeline generation with mandatory human approval before activation.
- **Kubernetes & Recipes**:
  - Production Helm chart (`charts/swapmid/`) and runnable `examples/` integration scenarios.

### `v1.0.0 LTS` — External OIDC SSO, Turnkey Observability & LTS Stability
- **External OIDC Federation**:
  - Optional enterprise IdP sign-in (Keycloak, Microsoft Entra ID, Casdoor, GitLab) integrated with SwapMid's `HttpOnly` BFF session store.
- **Observability & Operations**:
  - Pre-built Grafana dashboards and Prometheus/Alertmanager configurations in `docker/observability/`, API v1 backward-compatibility contract tests, and `docs/operations-and-upgrade.md`.
