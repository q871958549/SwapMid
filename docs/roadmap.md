# SwapMid Roadmap (`v0.2` → `v2.0+`)

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
| **`v1.0.0 LTS`** *(Released LTS)* | **Enterprise OIDC, Observability Pack & LTS Freeze** | Optional external OIDC SSO federation, turnkey Prometheus + Grafana dashboards + Alertmanager rule pack (`docker/observability/`), API v1 compatibility guard, and production upgrade runbook |
| **`v1.1.0`** *(Released)* | **Visual ETL Field Mapper & Observability Polish** | First-class `columnMappings` streaming projection & Target Receipt v1 contract binding, schema pre-flight explicit mapping checks, interactive `PipelineFieldMapperPanel` (`Auto Match` + type-family diagnostics), and PromQL system-health evaluation fixes |
| **`v1.2.0`** *(Released)* | **Analytical & Event-Stream Sinks (`Kafka` / `ClickHouse` / `S3 Parquet`)** | Pluggable bounded-memory `IEtlSinkWriter` adapters (`ClickHouse` HTTP JSONEachRow, `S3/MinIO` Parquet.Net & JSONL with SigV4, `Kafka` batch producer), SSRF-guarded connection probes, `system.columns` & schema-on-write preflight, and trilingual UI editors |
| **`v1.2.1`** *(Released)* | **UI Lifecycle Closure & Reference Blueprints** | Pipeline `StatusBadge` (`Draft`/`Active`/`Paused`/`Archived`), UI Activate/Pause actions, per-row `Test Connection` probe, `ExecutionTargetSummary` in execution history, and `s3-parquet-lakehouse` / `clickhouse-analytics-sink` / `kafka-event-stream-sink` recipes |
| **`v1.3.0`** *(Released)* | **PostgreSQL Logical Replication (`pgoutput`) CDC** | Native `Npgsql` `pgoutput` WAL reader with strict identifier validation, pre-flight `wal_level=logical` / Publication / Slot provisioning, post-delivery `ConfirmedFlushLsn` advancement, Hangfire polling, CloudEvents (`integration.cdc.events`) & pipeline fanout, and `/data-integration/cdc` console |
| **`v1.4.0`** *(Released)* | **CDC-to-Sink Streaming, `REPLICA IDENTITY` Diffs & Operational Hardening** | Direct `CdcTargetMode.ExternalSink` streaming (`Kafka` / `ClickHouse` / `S3 Parquet`), `changedColumns` old/new diff computation, pre-flight `TableReplicaIdentities` + `WalLagBytes` inspection, safe `drop-slot` WAL retention release, `swapmid_cdc_wal_lag_bytes` telemetry, and no-tracking concurrent `MessageBus` delivery hardening |
| **`v1.5.0`** *(Released)* | **Deterministic Kafka Key Ordering, Sink DDL Templates & Batch Replay** | Per-event Kafka partition keys (`TargetSinkKeyColumns` + `EtlSourceBatch.PartitionKeys`), one-click ClickHouse DDL & CDC schema template dialog (`GET /api/v1/cdc-streams/{id}/sink-template`), set-based `POST /api/v1/message-records/batch-replay`, and Prometheus `SwapMidCdcCaptureFailures` / `SwapMidCdcWalLagHigh` alert rules |
| **`v1.6.0`** *(Released)* | **Consistent CDC Initial Snapshot, Keyed ClickHouse Auto-DDL, Grafana CDC & MCP** | PostgreSQL `EXPORT_SNAPSHOT` + `REPEATABLE READ` baseline snapshot (`InitialSnapshotEnabled` + `/snapshot`), keyed ClickHouse `ReplacingMergeTree` DDL & `/provision-sink`, MCP `diagnose_cdc_streams` tool, Grafana CDC dashboard panels, and active CI PG 18 `wal_level=logical` contracts |
| **`v1.7.0`** *(Released)* | **CDC Relational Row-Level Mirror (`Upsert`/`Delete`) & Schema Drift Guardrails** | Direct `CdcTargetMode.RelationalTable` materialization across PostgreSQL / SQL Server / MySQL (`TargetDeletePolicy`, `EXPORT_SNAPSHOT` + staging `Upsert` + atomic `Delete`), upstream DDL `SchemaDriftPolicy` (`Ignore` / `Warn` / `PauseAndAlert`) with Hangfire circuit-breaking, `NotificationService` alerts, `accept-schema` recovery, and `SwapMidCdcSchemaDriftDetected` alerting |
| **`v1.8.0`** *(Released)* | **CDC Field Remapping, Soft-Delete Tombstones & Object-Store Lakehouse Manifests** | Explicit `TargetFieldMappings` projection + `CdcDeletePolicy.SoftDelete` tombstones with intra-batch `NOT NULL` safety & resurrection clearing, dual-source `inspect-mirror-schema` preflight & interactive visual mapper UI, immutable S3/MinIO `_swapmid/manifests/*.json` + bounded `_swapmid/latest.json` Lakehouse checkpoint index (`swapmid_cdc_object_storage_manifests_total`), and Grafana Panel 9 CDC P95 observability |
| **`v1.9.0`** *(Released)* | **SQL Server Change Tracking CDC & Lakehouse Small-File Compaction** | Heterogeneous `CdcSourceType` (`SqlServerChangeTracking = 1`) via native `CHANGETABLE(CHANGES ...)` + retention/drift checks, `CdcObjectStorageCompactionService` (Parquet/JSONL small-file consolidation + auto-threshold + `/compact-lakehouse`), DuckDB/ClickHouse/Spark analytics SQL, Grafana Panel 9 compaction series, and React `#418` hydration fix |
| **`v1.9.1`** *(Released)* | **Security & Runtime Polish, 100% OpenAPI Summaries & SQL Server CI** | Next.js `16.4.6` security upgrade, zero-404 UI runtime (`ITablePreferenceAppService` cloud table persistence, `/api/v1/nav-usage` logging, `204` default avatar fallback), 100% `/api/v1` XML `<summary>` OpenAPI coverage, and Linux SQL Server 2022 Change Tracking CI gate |
| **`v2.0.0`** *(Released)* | **MySQL Row-Based Binlog CDC & Apache Delta Lake (`_delta_log`) Protocol** | Native `CdcSourceType.MySqlBinlog = 2` (`MySqlCdc 4.0.1` ROW + FULL binlog capture, consistent FTWRL/LOCK TABLES snapshot, schema drift, `mysql:8.4` CI gate), `LakehouseTableFormat.DeltaLake = 1` (`_delta_log/{version:D20}.json` v1/v2 commits, S3 `If-None-Match: *` CAS, `OPTIMIZE` `remove`+`add` compaction, DuckDB `delta_scan()` / Spark / Polars SQL), and trilingual frontend controls |
| **`v2.0.1`** *(Current)* | **GitHub Open-Source Readiness, Helm/CI Hardening & `v2.0` Documentation** | Fixed Helm web `/build-meta` health probes + per-pod singleton lease env wiring, fixed `release.yml` `RELEASE_SHA` OCI build-arg & CI Helm secret validation, updated `README.md`/`README.zh-CN.md`/`SECURITY.md`/`operations-and-upgrade.md` for `v2.0`, and added `examples/mysql-binlog-deltalake-cdc/` blueprint |

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

### `v1.1.0` — Visual ETL Field Mapper, Column Projection & Observability Polish
- **First-Class `ColumnMappings` & Streaming Projection (`DataIntegration`)**:
  - Added validated `columnMappings` (`SourceColumn` -> `TargetColumn`, max 256 pairs, case-insensitive unique `targetColumn`) across `EtlTransformConfig`, `EtlPipelineRunner` ordinal projection, `EtlReceiptHashes.ComputeExecutionContract`, and `EtlSchemaCompatibilityEvaluator`.
  - Upgraded `DataPipelineSchemaPreflightAppService` (`MappedSourceMissing`, `MappedTargetMissing`, required `NOT NULL` target coverage, and `Upsert` key/watermark validation against mapped targets).
- **Interactive Field Mapper UI (`web/`)**:
  - Added `PipelineFieldMapperPanel` inside the Next.js pipeline editor (`Schema Pre-flight` workflow) featuring one-click `Auto Match` (case-insensitive exact + normalized identifier matching), per-column Source `Select` pickers, type-family mismatch badges, and full `zh-Hans` / `zh-Hant` / `en` localization.
- **Observability System Health PromQL Accuracy (`SystemManagement`)**:
  - Fixed `HealthCheckCatalog` (`result!="succeeded"` label matching `PlatformMetrics.EtlExecutionResultsTotal`) and added `or vector(0)` zero-numerator fallbacks on error-rate gauges.

### `v1.2.0` — Analytical & Event-Stream Sinks (`Kafka` / `ClickHouse` / `S3 Parquet`)
- **Connector Contracts & Fail-Closed Security Boundaries**:
  - Extended `DataSourceType` (`Kafka`, `ClickHouse`, `ObjectStorage`) and `ConnectorCapabilityCatalog` with capability negotiation, credential redaction, `targetResourceGroup` fencing identity, live connection probing, and `DataIntegration:Sinks:AllowedHosts` / `AllowedPrivateNetworks` DNS-rebinding-safe SSRF protection.
- **Bounded-Memory Streaming `IEtlSinkWriter` Adapters & UI**:
  - Added `ClickHouseEtlSinkWriter` (`FORMAT JSONEachRow`, `append` / `truncate`), `ObjectStorageEtlSinkWriter` (streaming `Parquet.Net` RowGroups & `jsonl` with SigV4 `StreamContent` PUT), and `KafkaEtlSinkWriter` (`Confluent.Kafka` async batch producer), paired with `system.columns` / schema-on-write preflight and structured Next.js forms across `zh-Hans`, `zh-Hant`, and `en`.

---

### `v1.9.0` — SQL Server Change Tracking CDC & Lakehouse Small-File Compaction
- **Heterogeneous CDC Provider Abstraction (`CdcSourceType.SqlServerChangeTracking = 1`)**:
  - Extended `CdcStreamDefinition` with `SourceType` and `LakehouseCompactMinFiles` (`AddCdcSourceTypeAndLakehouseCompaction`), and implemented `SqlServerCdcCaptureService` (`CHANGETABLE(CHANGES ...)`, `CHANGE_TRACKING_MIN_VALID_VERSION` retention guard, `Snapshot` / `REPEATABLE READ + HOLDLOCK` baseline, `INFORMATION_SCHEMA.COLUMNS` schema drift evaluation) feeding the shared `CdcStreamDeliveryDispatcher` across all targets. Verified live on Windows `(localdb)\MSSQLLocalDB`.
- **Object-Store Lakehouse Small-File Compaction & Cross-Engine Analytics SQL**:
  - Implemented `CdcObjectStorageCompactionService` (Parquet `RowGroup` merge + JSONL consolidation, immutable compaction manifest, atomic `_swapmid/latest.json` swap, optional superseded fragment cleanup, auto-compaction trigger, and `POST /api/v1/cdc-streams/{id}/compact-lakehouse`), `LakehouseQuerySql` generation (`DuckDB`, `ClickHouse s3()`, `Spark`), Prometheus compaction metrics, Grafana Panel 9 compaction series, and React `#418` hydration fixes.

### `v1.9.1` — Security & Runtime Polish, 100% OpenAPI Summaries & SQL Server CI
- **Next.js `16.4.6` Security Baseline & Zero Console 404s (`web/` / `Layout` / `Account`)**:
  - Upgraded `next` and `eslint-config-next` cleanly from `16.4.3` to `16.4.6` (resolving first-build `SecurityFatalError` advisories while preserving `minimatch`/`brace-expansion` supply-chain contracts).
  - Implemented user-scoped cloud table preferences (`ITablePreferenceAppService`, `TablePreferenceController` `/api/v1/table-preference`), structured navigation usage telemetry (`NavUsageController` `/api/v1/nav-usage`), anonymous `GET /api/v1/account-management/password-policy` (`AccountSettingsController`), permission-decoupled `GET /api/v1/account-users/{employee-options,department-options}` (`AccountUserController`), and `204 No Content` empty-avatar responses in `AvatarController`, eliminating all background `404`/`400` noise across SSR and authenticated pages.
- **100% `/api/v1` OpenAPI Summary Coverage & Cross-Platform SQL Server CDC CI**:
  - Added accurate XML `<summary>` annotations across all remaining `SwapMid.HttpApi` controllers (`100%` coverage in `check:api-summary`), refreshed `docs/api/summary-coverage-baseline.json`, and enabled `SWAPMID_SQLSERVER_TEST_CONNECTION` + SQL Server 2022 container execution for `SqlServerCdcLocalDbContractTests` in `.github/workflows/ci.yml`.

---

## Next Evolution (`v2.1+` Post-2.0 Backlog)

1. **Oracle LogMiner & Cloud Serverless CDC Sources**:
   - Extend `ICdcStreamAppService` and `CdcStreamCaptureCoordinator` with `OracleLogMiner` alongside PostgreSQL `pgoutput`, SQL Server Change Tracking, and MySQL / MariaDB `Binlog`.
2. **External Hive Metastore / Unity Catalog / REST Iceberg Synchronization**:
   - Optionally register SwapMid Delta Lake (`_delta_log/*.json`) and `_swapmid/manifests/*.json` table commits directly with an external REST Iceberg / Hive Metastore / Unity Catalog service.
