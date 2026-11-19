# SwapMid Roadmap (`v0.2` → `v3.0+`)

SwapMid focuses on being a **correctness-first, single-stack, agent-ready enterprise data exchange and integration control plane** built on **.NET 10 (ABP v10.1)** and **Next.js 16**.

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
| **`v2.0.1`** *(Released)* | **GitHub Open-Source Readiness, Helm/CI Hardening & `v2.0` Documentation** | Fixed Helm web `/build-meta` health probes + per-pod singleton lease env wiring, fixed `release.yml` `RELEASE_SHA` OCI build-arg & CI Helm secret validation, updated `README.md`/`README.zh-CN.md`/`SECURITY.md`/`operations-and-upgrade.md` for `v2.0`, and added `examples/mysql-binlog-deltalake-cdc/` blueprint |
| **`v2.1.0`** *(Released)* | **Delta Lake Time-Travel, Unity Catalog & Iceberg REST Interop + Chaos Suite** | `_delta_log` Time-Travel Inspector (`GET /lakehouse-history`), Unity Catalog REST & Apache Iceberg REST metadata sync (`POST /sync-catalog`), `ChaosResilienceContractTests`, and turnkey `docker-compose.lakehouse-demo.yml` (MySQL 8.4 + Chainguard MinIO) |
| **`v2.2.0`** *(Released)* | **Zero-Infra Live Lakehouse Preview, Data Contract SLA Engine & 8-Tool MCP Copilot** | Bounded Zero-Infra Parquet/JSONL live preview (`GET /lakehouse-preview`), declarative Data Contract & Freshness SLA engine (`POST /evaluate-contract` + `SwapMidCdcDataContractBreached`), MCP Tools 7 & 8 (`propose_pipeline_column_mappings`, `evaluate_cdc_data_contracts`), and Node 24-native CI/Release Actions |
| **`v2.3.0`** *(Released)* | **Native Apache Iceberg v2 Protocol, Serverless Push Webhook CDC & Chaos SLA Benchmarks** | Pure-.NET Apache Iceberg v2 writer/reader (`metadata/v*.metadata.json` + `version-hint.text` + S3 CAS `If-None-Match: *` + `replace` compaction + DuckDB `iceberg_scan()` / Trino SQL + live Parquet preview), fail-closed HMAC-SHA256 `POST /api/v1/cdc-webhooks/{streamId}` Push Webhook CDC source (`CdcSourceType.PushWebhook = 3`), 5-scenario Chaos contract suite + 1,000-row SLA throughput benchmarks (`RPO = 0`), and `197/197` OpenAPI summary coverage |
| **`v2.4.0`** *(Released)* | **Binary Avro OCF Apache Iceberg v2, Push Webhook Auto-Schema & Dead-Letter Replay Ledger** | Zero-dependency pure-.NET Apache Avro 1.11 OCF binary `manifest-list` (`snap-*.avro`) & `manifest-file` (`*-m0.avro`) + Parquet `FieldId 1..11` / `TIMESTAMP_MICROS` (verified live via official **`duckdb/duckdb:latest` `iceberg_scan()`** across `v1`–`v4`), `AppCdcWebhookDeliveries` dead-letter ledger (`<= 64 KiB` only on authenticated `409`/`500`, zero payload on `401`) + Auto-Schema Inference for `RequiredColumns` SLA & `SchemaDriftPolicy` + `/data-integration/cdc` `推送日志` 1-click `重放` UI, Grafana Panels 10–11 & 3 Iceberg/Webhook Prometheus alert rules, and **`199/199` (100.0%)** OpenAPI summary coverage |
| **`v2.5.0`** *(Released)* | **Kafka / Redpanda Debezium CDC, Iceberg v2 Daily Hidden Partitioning, 10-Tool MCP Copilot & Terminology Governance** | Native `CdcSourceType.KafkaDebezium = 4` inbound source (`Confluent.Kafka` consumer with monotonic multi-partition checkpoints `kafka:0=N,1=M`, watermark lag preflight & `swapmid_cdc_kafka_lag_messages` / `SwapMidCdcKafkaLagHigh`, wrapped/unwrapped Debezium & CloudEvents envelopes, auto-schema inference & drift policy), Iceberg v2 native **`day(commit_timestamp_utc)` Hidden Partitioning** (`spec-id: 1`, `r102`/`r508` Avro bounds, UTC day-bucketed Parquet files, same-day partition-aware `REPLACE` compaction, and SigV4 Hive-partition path signing fix — verified live with real **Redpanda** + official **DuckDB `iceberg_scan()`** partition pruning), MCP Tools 9–10 (`diagnose_webhook_deliveries`, `inspect_lakehouse_snapshots`), and automated bilingual terminology & anti-mistranslation CI gate (`check-terminology.mjs`) |
| **`v2.6.0` / `v2.6.1`** *(Released)* | **Iceberg v2 Merge-on-Read Equality Deletes, Schema Registry Avro, Declarative GitOps & Zero-`apt-get` API Image** | Pure-.NET Iceberg v2 **`LakehouseWriteMode.MergeOnReadUpsert = 1`** (`business_key` `FieldId=12`, `eqdel_*.parquet` `content=2`, dual binary Avro OCF `*-m0.avro` + `*-d0.avro`, `operation="overwrite"`, Zero-Infra Equality Delete preview & `REPLACE` compaction GC — verified live via official **`duckdb/duckdb:latest` (`v2.0.0`) `iceberg_scan()`**), pure-.NET **Confluent / Redpanda Schema Registry Wire-Format Binary Avro** decoding, secret-free **`swapmid.io/v1` Declarative GitOps Bundles** (`scripts/swapmidctl.mjs`, Web UI & MCP Tool 11), and zero-`apt-get` minimal API container with `bash /dev/tcp` healthcheck |
| **`v2.7.0`** *(Released)* | **Iceberg v2 WAP Tags & `expireSnapshots` GC, Protobuf Schema Registry, GitOps PR Drift Action & 12-Tool MCP** | Pure-.NET `IcebergV2MaintenanceService` (`POST .../lakehouse-tags` & `POST .../expire-lakehouse-snapshots` with reference-counted orphan GC permanently protecting named `refs` tags, verified live via official **DuckDB v2.0.0 `iceberg_scan()`** across `v6` and tag-protected `v1`), zero-dependency pure-.NET **Confluent / Redpanda Schema Registry Protobuf & JSON** wire decoding (`ProtobufSchemaParser` + `ProtobufWireDatumReader`, verified live against **Redpanda v24.3.6**), `swapmidctl.mjs` fail-closed `${ENV_SECRET:...}` placeholder resolution & `--markdown --exit-code` PR gate + composite GitHub Action `.github/actions/swapmid-gitops-diff/action.yml`, MCP Tool 12 `plan_lakehouse_maintenance`, `/data-integration/cdc` Time-Travel `Refs` & `expireSnapshots` UI, and **`204/204` (100.0%)** OpenAPI summary coverage |
| **`v2.8.0`** *(Released)* | **Iceberg v2 WAP Branch Promotion & Scheduled Maintenance, Cross-Engine Lineage Explorer, 13-Tool MCP & Turnkey Streaming Sandbox** | Pure-.NET Apache Iceberg v2 **WAP `staging` Branch Isolation & Fast-Forward Promotion** (`POST .../promote-lakehouse-branch` with Data Contract SLA pass gate, fast-forward ancestry check & atomic audit tag pinning, verified live via official **DuckDB v2.0.0 `iceberg_scan()`** proving zero unaudited leakage before promote), **Hangfire Scheduled Lakehouse Maintenance Policies** (`cdc-lakehouse-maint:{streamId}` Cron compaction + `expireSnapshots`), **Cross-Engine Data Lineage DAG & Blast-Radius Impact Explorer** (`GET /api/v1/integration-lineage{,/impact}` + interactive 4-stage Console UI + **MCP Tool 13 `analyze_lineage_impact`**), turnkey **`docker-compose.streaming-lakehouse.yml`** (`Redpanda` + `MinIO` + `DuckDB`) + `examples/streaming-lakehouse-iceberg/`, **`207/207` (100.0%)** OpenAPI summary coverage, and CI ephemeral-port collision hardening (`15432`) |
| **`v2.9.0`** *(Released)* | **Iceberg v2 Atomic Rollback & FinOps Metering, CNCF OpenLineage 2.0 + Column Lineage, 14-Tool MCP, Native Multi-Arch Docker & W3C TraceContext** | Pure-.NET Apache Iceberg v2 **Reversible Atomic Time-Travel Rollback** (`POST .../rollback-lakehouse` by version or named tag/branch with `DryRun = true` default & atomic `BackupCurrentAsTagName` pinning, verified live via official **DuckDB v2.0.0 `iceberg_scan()`**), **Lakehouse FinOps Storage Metering** Prometheus gauges (`swapmid_cdc_lakehouse_active_bytes` / `active_files` / `snapshots_count` + Grafana Panel 10), **Column-Level Lineage & CNCF OpenLineage 2.0 (`v2-0-2`) Export + Optional Fail-Closed Webhook Emitter** (`GET /api/v1/integration-lineage/openlineage` + **MCP Tool 14 `export_openlineage_spec`** + interactive UI column chips & JSON download), **`--platform=$BUILDPLATFORM` Native Multi-Arch Dockerfiles**, **End-to-End W3C TraceContext** (`traceparent` -> `X-SwapMid-Trace-Id`), and **`209/209` (100.0%)** OpenAPI summary coverage |
| **`v3.0.0`** *(Current)* | **Iceberg v2 Native Schema Evolution, Turnkey OTLP + Grafana Tempo 3-Pillar Observability, 15-Tool MCP & v3.0 SLA Benchmarks** | Pure-.NET Apache Iceberg v2 **Native `FieldId`-Pinned Schema Evolution** (`POST .../evolve-lakehouse-schema` supporting typed column promotion `string|long|double|boolean|timestamptz`, zero-rewrite renames preserving `FieldId` + `schema.name-mapping.default`, retired-ID-safe drops, automatic Parquet projection across Append/Partitioned/MoR & Schema-Evolution-safe `REPLACE` compaction — verified live via official **DuckDB v2.0.0 `iceberg_scan()`** across `schema-id` `0 -> 1 -> 2`), **Turnkey OpenTelemetry OTLP + Grafana Tempo** (`grafana/tempo:2.7.2` + provisioned Grafana datasource + Panel 12), **MCP Tool 15 `inspect_lakehouse_schema`**, interactive Console `CdcIcebergSchemaSection`, **1,000-row 16-column Schema-Evolved SLA benchmark** (`6,454 rows/s`, `RPO = 0`), and **`210/210` (100.0%)** OpenAPI summary coverage |

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

## `v2.1.0`: Delta Lake Time-Travel、Open Catalog 互操作、混沌韧性与安全基线（Completed）

1. **Delta Lake `_delta_log` Time-Travel Inspector（`GET /api/v1/cdc-streams/{id}/lakehouse-history`）**：
   - 服务端解析不可变 `_delta_log/{version:D20}.json` 时间线（`SNAPSHOT` / `STREAMING UPDATE` / `OPTIMIZE`），确定性重构任意历史版本 `VERSION AS OF v` 的活跃 Parquet 分片快照、行数与字节统计，并在控制台输出可执行 DuckDB `read_parquet([...])` 与 Spark `VERSION AS OF v` 检索 SQL。
2. **外部开放目录互操作（Unity Catalog REST & Apache Iceberg REST）**：
   - 基于既有加密 `RestApi` `DataSource` 凭据与 `DataIntegrationSinkEndpointPolicy` SSRF 守卫扩展 `CatalogSinkId`、`CatalogNamespace`、`CatalogTableName` 与状态审计列，支持手动（`POST /api/v1/cdc-streams/{id}/sync-catalog`）与写入/快照/合并后非阻塞 Best-Effort 目录同步（`swapmid_cdc_catalog_syncs_total` Prometheus 指标 + MCP `diagnose_cdc_streams` 诊断字段），目录故障绝不阻断 CDC 位点推进。
3. **混沌故障注入契约与开箱湖仓沙箱（`docs/benchmarks/resilience-and-chaos.md`）**：
   - 交付 `ChaosResilienceContractTests`（Mid-Flight Crash + `Target Receipt v1` 无重放、Delta Lake `412` CAS 竞态 + `swapmidRunId` 幂等重放、Catalog `503` 雪崩零阻断校验）并修复只读 `DataSource` 实体的 `AsNoTracking` 并发戳竞态；新增一键 `docker/docker-compose.lakehouse-demo.yml`（MySQL 8.4 `ROW`+`FULL` + Chainguard MinIO），零开放 Dependabot 安全告警与 CI Actions 全升级。

---

## `v2.2.0`: 零计算集群 Lakehouse 数据预览、数据契约 SLA 与 AI MCP Copilot（Completed）

1. **Zero-Infra Lakehouse 实时样本数据预览（`GET /api/v1/cdc-streams/{id}/lakehouse-preview`）**：
   - 基于 `Parquet.Net` 与权威 `_delta_log` / `_swapmid/latest.json` 路径解析，在不部署外部 Spark/Trino/DuckDB 计算集群的前提下，直接在管理台 Time-Travel 弹窗内按任意历史版本 `VERSION AS OF v` 解码展示 Parquet / JSONL 样本数据行（强制 SSRF 守卫、单文件 4 MiB / 最多 3 分片 / 50 行上限与已清理分片优雅降级提示），并同步修正 `DeltaLake` 自动压缩默认保留历史分片语义。
2. **数据契约与新鲜度 SLA 引擎（`POST /api/v1/cdc-streams/{id}/evaluate-contract`）**：
   - 新增迁移 `AddCdcDataContractSla`（`MaxFreshnessLagSeconds`、`MaxAllowedWalLagBytes`、`RequiredColumns` 及违约审计字段）与 `CdcDataContractEvaluator`，支持新鲜度延迟（80% 预警 / 超限违约）、必填业务列核验、上游 Schema 漂移与 WAL 字节预算校验，联动去重恢复告警、`swapmid_cdc_contract_evaluations_total` / `swapmid_cdc_contract_violations_total` Prometheus 指标与 `SwapMidCdcDataContractBreached` 告警规则。
3. **受控 AI MCP Copilot 8 工具扩展与 CI Node 24 零注解收口**：
   - 在 `POST /api/mcp` 新增工具 7 `propose_pipeline_column_mappings`（基于只读零行预检推导精确/归一化列映射、候选主键/水位线及 Draft 就绪配置）与工具 8 `evaluate_cdc_data_contracts`（SRE 契约态势巡检与修复建议）；升级 CI/Release Actions 至 Node 24 原生主版本并消除 Dockerfile BuildKit 误报注解。

---

## `v2.3.0` – `v2.4.0`: 纯 .NET 二进制 Avro Iceberg v2、无服务器 Push Webhook 死信重放与可观测闭环（Completed）

1. **纯 .NET 零 JVM 二进制 Apache Avro 1.11 OCF Iceberg v2 互操作（经官方 DuckDB `iceberg_scan()` 实机验证）**：
   - 新增 `IcebergAvroOcfCodec`、`IcebergAvroManifestBuilder` 与 `IcebergV2CommitWriter.AvroStorage.cs`，以零外部依赖直接生成 Iceberg v2 规范二进制 `manifest-list`（`snap-*.avro`）与 `manifest-file`（`*-m0.avro`），同步写入 `schema.name-mapping.default` 并在 `ObjectStorageEtlSinkWriter` / `CdcParquetCompactionMergeHelper` 中保留 Parquet Thrift `FieldId = 1..11` 与 `TIMESTAMP_MICROS (UTC)`；在隔离 Docker 实测中经官方 `duckdb/duckdb:latest` 容器直连 MinIO `iceberg_scan()` 一次通过 `v1`、`v3`（`REPLACE` 压缩）与 `v4` 查询。
2. **Push Webhook CDC 自动 Schema 推导与受控死信重放台账**：
   - 新增迁移 `AddCdcWebhookDeliveryLedger`（`AppCdcWebhookDeliveries`）与 `CdcWebhookSchemaInferenceHelper`：自动从首次推送批次推导 `SchemaBaselineJson` 以驱动 `RequiredColumns` 数据契约与 `SchemaDriftPolicy`（`Warn`/`PauseAndAlert`）；严格落实安全不变式（仅对已通过 HMAC 验签的 `409`/`500` 暂存 `<= 64 KiB` 载荷，`401` 伪造请求绝不落盘），开放 `GET /api/v1/cdc-webhooks/{streamId}/deliveries` 与 `POST .../replay` 接口并在 `/data-integration/cdc` 提供 `推送日志` 一键 `重放` 弹窗。
3. **Lakehouse & Push Webhook 闭环可观测与 100% 契约治理**：
   - 新增 3 条 Prometheus 告警规则（`SwapMidCdcIcebergCommitFailures`、`SwapMidCdcWebhookAuthFailuresSpike`、`SwapMidCdcWebhookDeliveryFailures`）与 Grafana 总览面板 Panel 10 / Panel 11，OpenAPI 摘要覆盖率保持 `100.0%`（`199/199` 接口 / `9` 业务域）。

---

## `v2.5.0`: Kafka / Redpanda Debezium CDC、Iceberg v2 按日隐藏分区、10 工具 MCP 与术语门禁（Completed）

1. **Apache Kafka / Redpanda Debezium 信封流 CDC 源（`CdcSourceType.KafkaDebezium = 4`）**：
   - 新增 `KafkaDebeziumEnvelopeParser` 与 `KafkaDebeziumCdcCaptureService`，绑定 `DataSourceType.Kafka`，以 `PublicationName` 指定源 Topic、`SlotName` 指定 Consumer Group，兼容标准 Debezium Wrapped/Unwrapped JSON、CloudEvents CDC JSON、扁平业务 JSON 及 Tombstone 墓碑事件；实现多分区单调位点 `kafka:0=N,1=M`、分区水位线滞留预检与 Prometheus 指标 `swapmid_cdc_kafka_lag_messages` / `SwapMidCdcKafkaLagHigh`，并无缝接入自动 Schema 推导、`SchemaDriftPolicy`、数据契约 SLA 与全部下游目标（MessageBus / ClickHouse / 关系表镜像 / Delta Lake / 二进制 Avro Iceberg v2）。
2. **Apache Iceberg v2 原生按日隐藏分区（`LakehousePartitionStrategy.DailyCommitUtc = 1` -> `day(commit_timestamp_utc)`）**：
   - 扩展 `AddCdcIcebergPartitionStrategy` 迁移与 `IcebergAvroManifestBuilder` / `IcebergV2CommitWriter` / `CdcObjectStorageLakehouseWriter.Partitioned.cs`，输出符合 Iceberg v2 规范的 `partition-specs`（`spec-id: 1`, `transform: "day"`, `source-id: 8`, `field-id: 1000`）、二进制 Avro `r102` 分区元组与 `r508` 小端 `int32` 分区上下界，Parquet 分片按 `data/commit_day=YYYY-MM-DD/` 归档，并升级 `CdcObjectStorageCompactionService` 实现同日分区感知 `REPLACE` 压缩；同步修复 `AwsSigV4Signer` 对 Hive 分区路径 `=` 字符的双重编码缺陷，经真实 Redpanda + MinIO + 官方 **`duckdb/duckdb:latest` `iceberg_scan()`** 实机验证分区裁剪与历史版本时间旅行。
3. **AI 原生 MCP Copilot 扩展至 10 工具与自动化术语防乱翻译门禁**：
   - 新增 RBAC 只读诊断工具 Tool 9 `diagnose_webhook_deliveries` 与 Tool 10 `inspect_lakehouse_snapshots`；重构中英文门面文档为规范的 **企业级数据交换与集成中台（Enterprise Data Exchange & Integration Platform）** 定位，并内置 CI 术语与防乱翻译守卫脚本 `web/scripts/check-terminology.mjs` 根治中台/中间件混淆与机翻问题。

---

## `v2.6.0`: Iceberg v2 Merge-on-Read Equality Deletes、Schema Registry Avro 与声明式 GitOps（Completed）

1. **Apache Iceberg v2 Merge-on-Read Equality Deletes（`LakehouseWriteMode.MergeOnReadUpsert = 1`）**：
   - 新增迁移 `AddCdcLakehouseWriteMode`，将 Iceberg v2 Parquet 列扩展至 `FieldId 1..12`（引入 `business_key` 顶层列），并在 `ObjectStorageEtlSinkWriter`、`IcebergAvroManifestBuilder`、`IcebergV2CommitWriter.AvroStorage.cs` 与 `CdcObjectStorageLakehouseWriter.MergeOnRead.cs` 中实现批内主键折叠、单列 `eqdel_*.parquet`（`content=2`, `equality_ids=[12]`）及双二进制 Avro 1.11 OCF 清单（`*-m0.avro` 数据 + `*-d0.avro` 删除）原子 `overwrite` 提交。
   - 同步升级零计算集群预览（`CdcLakehouseDataPreviewService.EqualityDeletes.cs`）与 `REPLACE` 压缩器（合并有效行并回收历史 Equality Delete 文件），在隔离 Docker 中经官方 **`duckdb/duckdb:latest` (`v2.0.0`) `iceberg_scan()`** 实机验证 `v1` 历史时间旅行、`v2` Merge-on-Read 精准去重与 `v3` `REPLACE` 物化压缩（`RPO = 0`）。
2. **Confluent / Redpanda Schema Registry 二进制 Avro 线协议解码（Kafka Debezium CDC）**：
   - 为 `KafkaSinkConfig` 新增受 `DataIntegrationSinkEndpointPolicy` SSRF 守卫保护的可选 `schemaRegistryUrl` 与脱敏 Basic 凭据；实现零依赖纯 .NET `AvroBinaryDatumReader` 与有界缓存 `ConfluentSchemaRegistryClient`（`0x00` + 4 字节大端 `schemaId`，`/subjects` 预检返回 `debezium-avro+json`，`swapmid_cdc_schema_registry_fetches_total`），实机接入真实 **Redpanda v24.3.6** 内置 Schema Registry 完成实时二进制 Avro 事件解码。
3. **零明文密钥 `swapmid.io/v1` 声明式 GitOps 集成清单、`swapmidctl` CLI 与 MCP Tool 11**：
   - 在宿主层 `SwapMid.HttpApi/GitOps/` 实现 `GET /api/v1/integration-bundles/export`、`POST .../diff` 与 `POST .../apply`（默认 `DryRun = true`、零明文密钥导出、新建管道/CDC 流默认落入 `Draft` 且零破坏 `SwapMid.Application` 跨域依赖零耦合基线）；同步交付零依赖 Node CLI [scripts/swapmidctl.mjs](../scripts/swapmidctl.mjs)、MCP Tool 11 `export_integration_bundle` 与 `/data-integration/cdc` **`GitOps`** 一键导出/对比/预演/应用弹窗，OpenAPI 摘要覆盖率达 **`202/202`（100.0%）**。

---

## `v2.7.0`: Iceberg v2 WAP 命名标签与 `expireSnapshots` 回收、Protobuf Schema Registry 与 GitOps PR 漂移门禁（Completed）

1. **Apache Iceberg v2 WAP 命名标签（`refs`）与严格引用计数 `expireSnapshots` 孤儿清理**：
   - 新增纯 .NET `IcebergV2MaintenanceService`、`POST /api/v1/cdc-streams/{id}/lakehouse-tags` 与 `POST /api/v1/cdc-streams/{id}/expire-lakehouse-snapshots`（默认 `DryRun = true`）；升级 `IcebergV2CommitWriter.JsonBuilder.cs` 在后续 CDC 写入/压缩时自动保留全部非 `main` 命名标签；`expireSnapshots` 永久保护当前快照、最近 `RetainLast` 个快照及任意命名分支/标签所指向的历史快照，仅清理严格无引用的 Parquet/Equality Delete/Avro 清单对象，经官方 **`duckdb/duckdb:latest` (`v2.0.0`) `iceberg_scan()`** 实机验证清理 `8/8` 孤儿对象后 `v6`（3 行）与受 `audit-v1` 保护的 `v1`（2 行）均可完整读取。
2. **Confluent / Redpanda Schema Registry 二进制 Protobuf 与 JSON 线协议解码**：
   - 新增零依赖纯 .NET `ProtobufSchemaParser` 与 `ProtobufWireDatumReader`，完整解析 `proto2`/`proto3` 嵌套 `message`、`enum`、`oneof`、`repeated`（含 packed）、`map<K,V>` 与 Confluent ZigZag Varint 消息索引数组；升级 `ConfluentSchemaRegistryClient` 统一分发 `AVRO`/`PROTOBUF`/`JSON`，实机接入真实 **Redpanda v24.3.6** 内置 Schema Registry 完成二进制 Protobuf Debezium 实时捕获。
3. **GitOps PR 漂移门禁 Action、`${ENV_SECRET:...}` 密钥占位符解析与 12 工具 MCP**：
   - 升级 [scripts/swapmidctl.mjs](../scripts/swapmidctl.mjs) 支持 `--resolve-env-secrets`（缺失即失败且绝不回显明文）、`--markdown` 与 `--exit-code`（无漂移 `0` / 存在漂移 `2`），交付可复用组合 Action [.github/actions/swapmid-gitops-diff/action.yml](../.github/actions/swapmid-gitops-diff/action.yml)；新增只读 MCP Tool 12 `plan_lakehouse_maintenance` 与 `/data-integration/cdc` Time-Travel **`命名分支与审计标签 (Refs)`** / **`打标签`** / **`清理预演`** / **`执行清理`** 控制台，保持 **`204/204`（100.0%）** OpenAPI 摘要覆盖与 `2,162` 条三语键零漂移。

---

## `v2.8.0`: Iceberg v2 WAP 分支快进发布与定时维护、跨引擎数据血缘影响分析与流式湖仓沙箱（Completed）

1. **Apache Iceberg v2 WAP 隔离分支写入、快进发布与 Hangfire 定时维护策略**：
   - 新增迁移 `AddCdcIcebergWapAndMaintenanceSchedule`（`LakehouseWriteBranch`、`LakehouseMaintenanceCron`、`LakehouseExpireRetainLast`、`LakehouseExpireOlderThanMinutes`、`LakehouseDeleteOrphanedFiles` 与最近维护审计列），同步纳入 `swapmid.io/v1` GitOps 清单。
   - 升级 `IcebergV2CommitWriter` 支持将流式变更隔离写入 `refs[staging]` 而不推进 `main` 与 `current-snapshot-id`，确保下游 DuckDB/Trino/Spark 读取端零脏数据泄漏；交付 `POST /api/v1/cdc-streams/{id}/promote-lakehouse-branch`（强制校验数据契约 SLA 合规与祖先链快进关系，支持原子绑定审计标签）及 `RunScheduledLakehouseMaintenanceHangfireJob` 定时压缩/快照过期回收。
2. **跨引擎数据血缘图谱、变更影响面分析（Blast Radius）与 13 工具 MCP**：
   - 在宿主层 `SwapMid.HttpApi/Lineage/` 交付 `GET /api/v1/integration-lineage` 与 `GET /api/v1/integration-lineage/impact`，串联 `DataSource -> DataPipeline / CdcStream -> LakehouseTable / MessageTopic -> MessageSubscription & ApiGroup / ApiRoute`，提供上下游 BFS 传播路径、风险分级与变更前核查建议。
   - 新增只读 MCP Tool 13 `analyze_lineage_impact` 与 `/data-integration/cdc` 交互式 4 列**`血缘图谱`**与影响面分析弹窗，保持 **`207/207`（100.0%）** OpenAPI 摘要覆盖与 `2,190` 条三语键零漂移。
3. **Turnkey 流式湖仓沙箱（`docker-compose.streaming-lakehouse.yml`）与 CI 端口加固**：
   - 新增 [docker/docker-compose.streaming-lakehouse.yml](../docker/docker-compose.streaming-lakehouse.yml)（Redpanda v24.3.6 + Schema Registry + MinIO + DuckDB query profile）与 [examples/streaming-lakehouse-iceberg/](../examples/streaming-lakehouse-iceberg/) 60 秒可复现蓝图；将 `.github/workflows/ci.yml` `postgres-cdc` 端口切换至 `15432` 根治临时端口冲突。

---

## Next Evolution (`v2.9+` AI-Era Open-Source Competitiveness)

1. **OpenLineage Standard Event Emission & Column-Level Lineage Traceability**:
   - Emit CNCF OpenLineage (`START`/`COMPLETE`/`FAIL`) run events for ETL pipelines, CDC snapshots, and Iceberg v2 WAP promotions to external lineage hubs (Marquez / DataHub / OpenMetadata), and extend SwapMid's Lineage Explorer with column-level mapping projections (`columnMappings` & `targetFieldMappings`).
2. **Iceberg v2 Branch Cherry-Pick / Rollback-to-Snapshot & Multi-Tenant Cost Metering**:
   - Support one-click atomic `rollback-to-snapshot` / `rollback-to-tag` on Apache Iceberg v2 tables plus per-stream object-storage byte & snapshot growth telemetry for FinOps governance.
3. **Zero-Trust Webhook / Gateway Mutual TLS & OpenTelemetry Distributed Tracing (`OTLP`)**:
   - Export end-to-end W3C TraceContext spans across BFF -> Control Plane -> ETL/CDC -> Outbox -> Webhook/API Gateway for Jaeger / Grafana Tempo correlation.
