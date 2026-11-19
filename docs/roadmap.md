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

## Next Evolution (`v2.3+` AI-Era Open-Source Competitiveness)

1. **Oracle LogMiner & Serverless Authenticated Webhook CDC Sources**:
   - Extend `ICdcStreamAppService` and `CdcStreamCaptureCoordinator` with `OracleLogMiner` and HMAC-verified serverless push webhook CDC ingestion alongside PostgreSQL `pgoutput`, SQL Server Change Tracking, and MySQL / MariaDB `Binlog`.
2. **Native Apache Iceberg v2 Metadata & Snapshot Manifest Interop**:
   - Emit full Iceberg v2 `metadata.json` / manifest lists alongside Delta Lake `_delta_log` so Trino, Snowflake, and StarRocks can read SwapMid Lakehouse tables natively via Iceberg REST Catalogs without translation layers.
3. **Automated Chaos SLA Latency & RPO Benchmark Reports**:
   - Publish reproducible per-release throughput, WAL lag P99, and crash-recovery RPO=0 benchmark artifacts directly alongside `ChaosResilienceContractTests`.
