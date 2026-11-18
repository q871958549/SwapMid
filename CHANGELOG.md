# Changelog

All notable changes to **SwapMid** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.9.0] - 2026-11-18

### Added
- **Heterogeneous CDC Provider Architecture & SQL Server Change Tracking (`DataIntegration` / `EntityFrameworkCore` / `web/`)**:
  - Extended `CdcStreamDefinition` with `CdcSourceType` (`PostgreSqlLogicalReplication = 0`, `SqlServerChangeTracking = 1`) and `LakehouseCompactMinFiles` (`2..100`), backed by EF Core migration `AddCdcSourceTypeAndLakehouseCompaction`.
  - Implemented `SqlServerCdcCaptureService`: validates `sys.change_tracking_databases`, `sys.change_tracking_tables`, primary keys, and `CHANGE_TRACKING_MIN_VALID_VERSION` retention bounds; performs `Snapshot` / `REPEATABLE READ + HOLDLOCK` consistent baseline exports anchored to `CHANGE_TRACKING_CURRENT_VERSION()`; captures `CHANGETABLE(CHANGES ...)` incremental inserts/updates/deletes strictly bounded by `MaxBatchSize` without splitting a transaction version; evaluates `INFORMATION_SCHEMA.COLUMNS` schema drift; and feeds seamlessly into Message Bus, Data Pipeline, Kafka/ClickHouse/MinIO Parquet, and Relational Table Mirror (`FieldMappings` & `SoftDelete`). Verified live on Windows `(localdb)\MSSQLLocalDB` (`SqlServerCdcLocalDbContractTests`).
- **Object-Store Lakehouse Small-File Compaction & Analytics SQL (`CdcTargetMode.ExternalSink = 2` / `Observability` / `web/`)**:
  - Implemented `CdcObjectStorageCompactionService`: merges `>= minFiles` small Parquet (via `Parquet.Net` RowGroup copy) or JSONL fragment objects referenced in `_swapmid/latest.json` into a consolidated `compact_{runId}.parquet`/`.jsonl` object under SSRF guard, writes an immutable compaction manifest (`isCompaction: true`), atomically updates `_swapmid/latest.json` (`compactionCount`, `lastCompactedAtUtc`), optionally deletes superseded fragments via SigV4 `DELETE`, and emits `swapmid_cdc_lakehouse_compactions_total` + `swapmid_cdc_lakehouse_compacted_files_total` (surfaced on Grafana Panel 9).
  - Added optional per-stream auto-compaction (`LakehouseCompactMinFiles`) on incremental capture, `POST /api/v1/cdc-streams/{id}/compact-lakehouse` (`CompactLakehouseAsync`), and ready-to-run `LakehouseQuerySql` (DuckDB, ClickHouse `s3()`, Spark SQL with sanitized credential placeholders) inside `CdcSinkTemplateDto`.
- **Frontend CDC Console & SSR Hydration Fix (`web/`)**:
  - Updated `/data-integration/cdc` across `zh-Hans`, `zh-Hant`, and `en` to support PostgreSQL & SQL Server Change Tracking source selection, `lakehouseCompactMinFiles` threshold configuration, cross-engine analytics SQL preview (`复制SQL` / `Copy SQL`), and one-click **合并分片** / **Compact** triggering inside `CdcSinkTemplateDialog`.

### Fixed
- **Timezone SSR Hydration Mismatch on `/system/health` (`web/`)**:
  - Added `suppressHydrationWarning` to client-locale formatted `checkedAt` timestamp rendering in `web/src/app/(dashboard)/system/health/client.tsx` (and CDC `lastCapturedAt` table cells), eliminating React minified error `#418` when server (`UTC`) and browser timezones differ.

## [1.8.0] - 2026-11-18

### Added
- **Configurable CDC Field-Level Remapping & Soft-Delete Tombstones (`CdcTargetMode.RelationalTable = 3` / `DataIntegration` / `web/`)**:
  - Extended `CdcStreamDefinition` with `TargetFieldMappingsJson` (explicit `SourceColumn -> TargetColumn` rules, up to 256 pairs), `CdcDeletePolicy.SoftDelete = 2`, `SoftDeleteColumn` (required flag column), and `SoftDeleteTimestampColumn` (optional nullable timestamp column), backed by EF Core migration `AddCdcFieldMappingsAndSoftDelete`.
  - Implemented `CdcRelationalMirrorRowProjector` and `EtlTargetAtomicWriter.ExecuteSoftDeleteChunksAsync`: normal/snapshot/resurrection events clear tombstones (`SoftDeleteColumn = false/0`, `SoftDeleteTimestampColumn = NULL`), out-of-batch `DELETE` events update target tombstone flags safely under both `REPLICA IDENTITY DEFAULT` and `FULL` without overwriting existing columns, and intra-batch `Insert/Update -> Delete` preserves full payload into staging `Upsert` so `NOT NULL` target columns never fail.
  - Added dual-source/target schema inspection & compatibility validator (`CdcRelationalMirrorPreflightService`, `POST /api/v1/cdc-streams/inspect-mirror-schema`, wired into `PreflightAsync` & `ActivateAsync`), modularized the CDC Next.js console (`cdc-form-state.ts`, `cdc-relational-mirror-section.tsx`, `use-cdc-stream-actions.ts`, shared `@/lib/etl-column-mapping.ts`), and added interactive **探测映射 / Inspect** + **Auto Match** controls across `zh-Hans`, `zh-Hant`, and `en`.
- **Object-Store CDC Lakehouse Manifest & Checkpoint Index (`CdcTargetMode.ExternalSink = 2` / `DataIntegration` / `Observability`)**:
  - Implemented `CdcObjectStorageLakehouseWriter`: after each Parquet or JSONL CDC data object PUT succeeds on `ObjectStorage` (`S3` / `MinIO`), idempotently writes an immutable per-batch manifest `{prefix}/{dest}/_swapmid/manifests/{lsnToken}_{runId}.json` (`StartLsn`, `EndLsn`, `OpCounts`, `TableCounts`, `Sha256Hex`, `SchemaBaselineHash`) and a bounded sliding-window checkpoint index `{prefix}/{dest}/_swapmid/latest.json` (max `100` recent files), instrumented by `swapmid_cdc_object_storage_manifests_total{format}`.
  - Surfaced live `_swapmid/latest.json` probe status (`ManifestDirectory`, `ManifestLatestKey`, `LatestManifestJson`) inside `GetSinkTemplateAsync` and `CdcSinkTemplateDialog`, added Grafana Overview Panel 9 (Capture P95 Duration, Schema Drift Rate, and Lakehouse Manifest Rate), and enriched MCP `diagnose_cdc_streams` with monitored tables, poll cadence, mapping counts, soft-delete columns, and `mirrorCompatibility`.

## [1.7.0] - 2026-11-17

### Added
- **CDC Row-Level Relational Table Mirroring (`CdcTargetMode.RelationalTable = 3` / `DataIntegration` / `web/`)**:
  - Added `CdcTargetMode.RelationalTable` and `CdcDeletePolicy` (`DeleteRow` / `Ignore`) across domain, EF Core migration (`AddCdcRelationalMirrorAndSchemaDrift`), and `CdcRelationalMirrorDeliveryAdapter` + `EtlTargetAtomicWriter.ApplyCdcMirrorAsync`.
  - Coalesces ordered WAL & `EXPORT_SNAPSHOT` baseline batches per business primary key (`TargetSinkKeyColumns`), resolves unchanged TOAST sentinels cleanly across batches, coerces temporal/binary values, and atomically executes staging-backed `Upsert` (`ON CONFLICT DO UPDATE` / SQL Server `MERGE WITH (HOLDLOCK)` / MySQL `ON DUPLICATE KEY UPDATE`) plus parameterized `DELETE` inside a single target transaction under writer lease protection (`swapmid_cdc_deliveries_total{target_mode="relational_table"}`).
- **Upstream Schema Drift Detection, Auto-Pause Guardrails & Recovery Notification (`DataIntegration` / `Mcp` / `Notification` / `web/`)**:
  - Extended `CdcStreamDefinition` with `CdcSchemaDriftPolicy` (`Ignore`, `Warn`, `PauseAndAlert`), `SchemaBaselineHash`, `SchemaBaselineJson`, `SchemaDriftDetected`, `SchemaDriftSummary`, and `SchemaDriftDetectedAt`.
  - Implemented `CdcSchemaDriftEvaluator` and `PgOutputReplicationMetadataReader.InspectTableSchemasAsync` (`pg_attribute` catalog inspection) wired into `PreflightAsync`, `ActivateAsync`, `CaptureAsync`, and `RunScheduledCdcCaptureHangfireJob`. When `PauseAndAlert` detects upstream column/type changes, capture aborts safely before advancing LSN, pauses the stream, removes Hangfire polling, emits `swapmid_cdc_schema_drift_events_total{action="paused"}` (and Prometheus alert rule `SwapMidCdcSchemaDriftDetected`), and dispatches notification center alerts (`NotificationService`) with correlation on operator confirmation via `POST /api/v1/cdc-streams/{id}/accept-schema`.
  - Surfaced `targetMode = 3` Relational Table Mirror controls, `SchemaDriftPolicy` / `TargetDeletePolicy` selectors, `Schema 已漂移` badges, preflight drift banners, and `确认Schema` action in `/data-integration/cdc`, alongside enriched schema-drift diagnostics in MCP `diagnose_cdc_streams`.

## [1.6.0] - 2026-11-16

### Added
- **Consistent Initial CDC Snapshot + WAL LSN Handshake (`DataIntegration` / `EntityFrameworkCore` / `web/`)**:
  - Extended `CdcStreamDefinition` (`InitialSnapshotEnabled`, `SnapshotStatus`, `SnapshotCompletedAt`, `SnapshotRowCount`, EF Core migration `AddCdcInitialSnapshot`) and implemented `PgOutputCdcSnapshotReader` using PostgreSQL `LogicalSlotSnapshotInitMode.Export` (`SET TRANSACTION SNAPSHOT` under `REPEATABLE READ`), guaranteeing gapless & duplicate-free baseline table export seamlessly anchored to `slot.ConsistentPoint` before WAL streaming begins.
  - Added automatic initial snapshot execution on first capture when enabled, explicit `POST /api/v1/cdc-streams/{id}/snapshot` (`RunSnapshotAsync`), per-chunk deterministic `RunId` & `event_id` deduplication, and `/data-integration/cdc` UI switch + snapshot status badge + `全量快照` / `Initial Snapshot` row action.
- **Keyed ClickHouse `ReplacingMergeTree` Templates & One-Click Provisioning (`DataIntegration` / `web/`)**:
  - Upgraded `CdcExternalSinkTemplateBuilder.BuildClickHouseDdl` to emit `ENGINE = ReplacingMergeTree(commit_timestamp_utc)` keyed on `JSONExtractRaw(coalesce(after_json, before_json, '{}'), '<key>')` when `targetSinkKeyColumns` is configured, and added `POST /api/v1/cdc-streams/{id}/provision-sink` (`ProvisionExternalSinkAsync`) paired with the UI `一键建表` / `Create Table` button inside `CdcSinkTemplateDialog`.
- **AI MCP CDC Diagnostics, Grafana CDC Dashboard & CI Logical Replication Gate (`Mcp` / `docker/observability/` / `.github/workflows/`)**:
  - Added RBAC-gated `diagnose_cdc_streams` MCP tool (`SwapMid.DataIntegration.CdcStreams.Default`, redacted secrets, optional live WAL lag preflight via `McpCdcDiagnosticsHandler`).
  - Added Prometheus-backed Grafana CDC panels (`swapmid_cdc_captures_total`, `swapmid_cdc_events_captured_total`, `swapmid_cdc_deliveries_total`, `swapmid_cdc_wal_lag_bytes`) in `docker/observability/grafana/dashboards/swapmid-overview.json`.
  - Activated PostgreSQL 18 `wal_level=logical` container + `SWAPMID_RUN_POSTGRES_CDC_CONTRACT_TESTS=1` in `.github/workflows/ci.yml`.

## [1.5.0] - 2026-11-16

### Added
- **Deterministic Kafka Partition Key Ordering & External Sink DDL Templates (`DataIntegration` / `web/`)**:
  - Added `TargetSinkKeyColumns` (`CdcStreamConsts.MaxTargetSinkKeyColumnsLength = 256`, max `16` columns, EF Core migration `AddCdcSinkKeyColumns`) and `EtlSourceBatch.PartitionKeys`, enabling per-event Kafka partition keys across both CDC external sink streams (`CdcExternalSinkDeliveryAdapter` -> `schema.table:k1=v1|k2=v2`) and ETL transform `keyColumns` (`KafkaEtlSinkWriter.ResolveMessageKey`).
  - Added `CdcExternalSinkTemplateBuilder` and `GET /api/v1/cdc-streams/{id}/sink-template` (`ICdcStreamAppService.GetSinkTemplateAsync`), generating ClickHouse `ReplacingMergeTree` DDL (respecting `targetSinkKeyColumns` / schema metadata and escaping identifiers), live target-table existence probing, CDC envelope schema columns, and sample payload previews, paired with the interactive `CdcSinkTemplateDialog` (`查看模板` / `Sink Template`) in `/data-integration/cdc`.
- **Set-Based `MessageBus` Batch Replay & Prometheus CDC Alerting (`MessageBus` / `docker/observability/` / `web/`)**:
  - Added `IMessageRecordAppService.BatchReplayAsync` (`POST /api/v1/message-records/batch-replay`) using `BatchOperationExecutor.LoadAsync` + `UpdateManyAsync` + `IMessageTopicRepository.GetExistingIdsNoTrackingAsync` (bounded to `BatchOperationLimits.MaxIdsPerRequest = 2000`), and switched `/message-bus/records` bulk replay to single-request chunked `submitBatchIds('message-records/batch-replay', ids)`.
  - Added Prometheus alert rules `SwapMidCdcCaptureFailures` and `SwapMidCdcWalLagHigh` (`> 256 MiB` sustained retained WAL) in `docker/observability/prometheus/alert-rules.yml`.

## [1.4.0] - 2026-11-16

### Added
- **Direct Analytical & Event-Stream CDC Sink Delivery (`CdcTargetMode.ExternalSink = 2`)**:
  - Extended `CdcStreamDefinition` (`TargetSinkId`, `TargetSinkDestination`, EF Core migration `AddCdcExternalSinkAndWalGovernance`) so captured PostgreSQL `pgoutput` WAL batches stream directly to configured `Kafka`, `ClickHouse`, or `ObjectStorage` (`S3/MinIO` Parquet / JSONL) sinks via `CdcExternalSinkDeliveryAdapter` and pluggable `IEtlSinkWriter` implementations while keeping SSRF guards, deterministic `RunId` idempotency, and post-delivery `ConfirmedFlushLsn` advancement.
  - Updated `/data-integration/cdc` (`CdcFormDialog`, `CdcStreamsClient`) to create, edit, and display External Sink CDC streams alongside Message Bus and Data Pipeline targets.
- **`REPLICA IDENTITY` Diffs, WAL Lag Governance & Replication Slot Drop (`DataIntegration` / `web/`)**:
  - Added `CdcProtocolHelpers.ComputeChangedColumns` (`changedColumns` in `integration.cdc.events` JSON payloads and `changed_columns` in External Sink rows, excluding `__toast_unchanged__` markers).
  - Upgraded `PgOutputReplicationMetadataReader` and `CdcPreflightResultDto` to report `WalLagBytes` (`pg_wal_lsn_diff`) and per-table `TableReplicaIdentities` (`default` / `full` / `index` / `nothing`), exposed in the UI Pre-flight dialog (`WalLagBytes` formatting & per-table `StatusBadge` chips) and Prometheus telemetry (`swapmid_cdc_wal_lag_bytes`, `swapmid_cdc_deliveries_total`).
  - Added `POST /api/v1/cdc-streams/{id}/drop-slot` (`ICdcStreamAppService.DropSlotAsync`) and the paused-stream UI `释放槽位` / `Drop Slot` confirmation workflow to safely release idle upstream replication slots and stop WAL retention.

### Fixed
- **Concurrent `MessageBus` Delivery Tracking Contention**:
  - Added no-tracking topic lookups (`IMessageTopicRepository.FindNoTrackingAsync` / `FindByNameNoTrackingAsync`) across `MessageDeliveryBackgroundJob`, `MessageBusIntegrationEventPublisher`, `PlatformMessagePublisher`, and `MessageRecordAppService`, eliminating optimistic concurrency exceptions (`MessageTopic Modified`) under concurrent delivery jobs.

## [1.3.0] - 2026-11-16

### Added
- **Lightweight PostgreSQL Logical Replication (`pgoutput`) CDC (`DataIntegration` / `MessageBus` / `web/`)**:
  - Added `CdcStreamDefinition` (`AppCdcStreams` EF migration `AddCdcStreams`), `CdcStreamStatus` (`Draft` / `Active` / `Paused` / `Error`), `CdcTargetMode` (`MessageBusTopic` / `DataPipeline`), and host-only RBAC permissions `SwapMid.DataIntegration.CdcStreams.*` (`GET/POST/PUT/DELETE/preflight/activate/pause/capture/checkpoint` on `/api/v1/cdc-streams`).
  - Implemented zero-dependency `pgoutput` logical replication reader & WAL capture engine (`PgOutputWalStreamReader`, `PgOutputCdcCaptureService`, `CdcStreamCaptureCoordinator`) using `Npgsql 10` `LogicalReplicationConnection`, strict PostgreSQL identifier & LSN validation (`CdcProtocolHelpers`), per-stream distributed lock exclusivity, and post-delivery `ConfirmedFlushLsn` checkpoint advancement.
  - Wired downstream delivery to either seeded Message Bus topic `integration.cdc.events` (`IIntegrationEventPublisher` CloudEvents v1.0, plus demo RabbitMQ subscription `demo-cdc-events-rmq`) or triggered downstream `DataPipeline` runs, paired with Hangfire recurring capture scheduling (`HangfireCdcScheduleSynchronizer`, `RunScheduledCdcCaptureHangfireJob`).
  - Added Prometheus CDC telemetry (`swapmid_cdc_events_captured_total`, `swapmid_cdc_captures_total`, `swapmid_cdc_capture_duration_seconds`) and `HealthCheckCatalog` evaluation (`PostgreSQL CDC 捕获失败率`).
  - Built `/data-integration/cdc` management console (`ListPageShell`, `FilterBar`, `CdcFormDialog`, `CdcPreflightDialog`, `CdcCheckpointDialog`, lifecycle activation/pause/capture-now actions, and trilingual `zh-Hans`, `zh-Hant`, `en` localization) plus turnkey recipe [`examples/postgres-pgoutput-cdc/`](./examples/postgres-pgoutput-cdc/).

## [1.2.1] - 2026-11-15

### Added
- **Data Integration UI Lifecycle Closure & Reference Blueprints (`web/` / `examples/`)**:
  - Added pipeline lifecycle `Status` column (`StatusBadge`: `Draft` / `Active` / `Paused` / `Archived`) and RBAC-gated **Activate** (`PUT /api/v1/data-pipelines/{id}/activate`) / **Pause** (`PUT /api/v1/data-pipelines/{id}/pause`) actions in `/data-integration/pipelines`, disabling `Execute` on non-active pipelines until activated.
  - Added row-level one-click **Test Connection** probing in `/data-integration/sources` (`POST /api/v1/data-sources/{id}/test-connection`, reusing stored encrypted secrets safely when connection scope is unchanged).
  - Rendered **Target Delivery & Source Snapshot** summary inside normal `/data-integration/executions` detail dialogs so operators can directly inspect target provider, endpoint, bucket/database, destination prefix/topic/table, resource group, and write mode without leaving execution history.
  - Added turnkey reference examples [`examples/s3-parquet-lakehouse/`](./examples/s3-parquet-lakehouse/), [`examples/clickhouse-analytics-sink/`](./examples/clickhouse-analytics-sink/), and [`examples/kafka-event-stream-sink/`](./examples/kafka-event-stream-sink/).

## [1.2.0] - 2026-11-15

### Added
- **Analytical & Event-Stream ETL Sinks (`DataIntegration` / `HttpApi.Host` / `web/`)**:
  - Extended `DataSourceType` (`Kafka = 3`, `ClickHouse = 4`, `ObjectStorage = 5`) and `ConnectorCapabilityCatalog` (`clickhouse`, `s3-parquet`, `kafka`) with explicit write-mode & receipt capability declarations.
  - Added strongly-typed, secret-redacted configuration contracts (`ClickHouseSinkConfigContract`, `ObjectStorageSinkConfigContract`, `KafkaSinkConfigContract`), `targetResourceGroup` single-writer fencing identity, AWS Signature Version 4 request signing (`AwsSigV4Signer`), fail-closed SSRF / private-network guards (`DataIntegrationSinkEndpointPolicy` + `DataIntegration:Sinks:AllowedHosts` / `AllowedPrivateNetworks`), and live connection testing (`DataIntegrationSinkProbeService`).
  - Implemented pluggable bounded-memory `IEtlSinkWriter` adapters (`ClickHouseEtlSinkWriter` via HTTP `FORMAT JSONEachRow` / `TRUNCATE`, `ObjectStorageEtlSinkWriter` writing temp-file Apache Parquet RowGroups via `Parquet.Net` or `jsonl` with `StreamContent` SigV4 uploads, and `KafkaEtlSinkWriter` via `Confluent.Kafka` async batch producer with trace/run correlation headers).
  - Upgraded schema pre-flight (`DataPipelineSchemaPreflightAppService`) to inspect `ClickHouse` `system.columns` and generate projected schema-on-write targets for `ObjectStorage` and `Kafka`, alongside fail-closed mode/destination validation in `EtlTransformConfig`, `DataPipelineActivationCoordinator`, and `McpController`.
  - Upgraded the Next.js Data Sources and Pipelines UI with structured `ClickHouse`, `Object Storage (S3/MinIO)`, and `Kafka` editors (preserving redacted secrets and scope-change secret prompts), live connection testing, target-aware write-mode/destination constraints, and full `zh-Hans`, `zh-Hant`, and `en` i18n parity.
  - Exposed fail-closed `DataIntegration__Sinks__AllowedHosts` / `AllowedPrivateNetworks` configuration across `docker/docker-compose.yml`, `docker/.env.example`, and `charts/swapmid/`.

## [1.1.0] - 2026-11-15

### Added
- **Visual ETL Field Mapper & First-Class Column Mapping (`DataIntegration` / `web/`)**:
  - Added first-class `columnMappings` (`EtlColumnMapping` source-to-target column projection, up to 256 pairs, case-insensitive unique `targetColumn` validation, and conditional `EtlReceiptHashes.ComputeExecutionContract` TLV binding preserving full backwards compatibility when omitted) across `EtlTransformConfig`, `EtlPipelineRunner`, and `EtlSchemaCompatibilityEvaluator`.
  - Upgraded `DataPipelineSchemaPreflightAppService` to validate explicit column mappings (`MappedSourceMissing`, `MappedTargetMissing`, required `NOT NULL` target coverage, and `Upsert` key/watermark alignment against mapped targets).
  - Added interactive `PipelineFieldMapperPanel` inside the Next.js pipeline editor (`Schema Pre-flight` workflow) featuring one-click **Auto Match** (exact case-insensitive + normalized identifier matching), per-column Source `Select` pickers, type-family mismatch / missing-column / required-unmapped diagnostics, **Clear**, and full trilingual localization (`zh-Hans`, `zh-Hant`, `en`).

### Fixed
- **Observability System Health PromQL Evaluation (`SystemManagement`)**:
  - Fixed `HealthCheckCatalog` (`数据管道执行失败率`) label mismatch from `outcome!="succeeded"` to `result!="succeeded"` matching `PlatformMetrics.EtlExecutionResultsTotal`.
  - Added `or vector(0)` zero-numerator fallbacks on `HTTP 接口 5xx 错误率` and `消息总线投递失败率` so healthy active services with zero errors evaluate as `Healthy (0%)` instead of `Unknown` / false `Critical`.

## [1.0.1] - 2026-11-15

### Fixed
- **Alertmanager Closed-Loop Token Fallback (`Notification` / `docker/observability/`)**:
  - Updated `AlertWebhookAppService` and `NotificationSettingsAppService` so `NotificationCenter:AlertWebhookToken` (used by `docker/docker-compose.observability.yml`) works seamlessly as a fallback when no database setting override exists, while database settings continue to take precedence.
- **External OIDC Custom Flow Registration, BFF Public Origin & Split-Horizon Discovery (`OpenIddict` / `web/` / `docker/` / `charts/`)**:
  - Registered `OpenIddictServerBuilder.AllowCustomFlow(ExternalOidcTokenExtensionGrant.ExtensionGrantName)` in `SwapMidHttpApiHostModule` so `/connect/token` (`grant_type=external_oidc`) passes OpenIddict core grant validation instead of failing with `ID2032 (unsupported_grant_type)`, and added startup container assertions in `HostStartupSmokeTests`.
  - Added reverse-proxy-safe `resolveOidcPublicOrigin` (`X-Forwarded-Proto` / `X-Forwarded-Host` / `Host`) and consistent `isSecureCookieEnabled` handling across `/api/auth/oidc/start` and `/api/auth/oidc/callback`.
  - Wired `EXTERNAL_OIDC_AUTHORIZATION_ENDPOINT` (`AuthServer:ExternalOidc:AuthorizationEndpoint` / `externalOidc.authorizationEndpoint`) across Docker Compose and Helm manifests for split-horizon container/Kubernetes deployments.
  - Surfaced localized OIDC failure banners (`?sso_error=...`) and preserved deep-link `?redirect=...` targets across Enterprise SSO login flows (`zh-Hans`, `zh-Hant`, `en`).

### Changed
- **Open-Source Release & Cloud-Native Distribution Polish**:
  - Updated `SECURITY.md` supported versions table for the `1.0.x` LTS release line.
  - Aligned `charts/swapmid/values.yaml` container image repositories with GHCR owner conventions (`q871958549/swapmid-*`), added Helm Chart linting & observability Compose validation to `.github/workflows/ci.yml`, and added automated Helm OCI publishing (`oci://ghcr.io/<owner>/charts`) to `.github/workflows/release.yml`.
  - Documented `v1.1+` post-LTS ecosystem evolution priorities in `docs/roadmap.md`.

## [1.0.0] - 2026-11-15

### Added
- **Optional External OIDC Identity Federation (`AccountManagement` / `OpenIddict` / `web/`)**:
  - Added public OIDC discovery metadata endpoint (`GET /api/v1/auth-providers` via `AuthProviderController`, never exposing `ClientSecret`) and server-to-server OpenIddict extension grant (`grant_type=external_oidc` via `ExternalOidcTokenExtensionGrant` + `DefaultExternalOidcCodeExchanger`) validating ID tokens against OIDC Discovery JWKS.
  - Added Next.js BFF S256 PKCE + HMAC-sealed state initiation and callback routes (`/api/auth/oidc/start`, `/api/auth/oidc/callback`) and conditional enterprise SSO sign-in button on `/login` across `zh-Hans`, `zh-Hant`, and `en`.
- **Turnkey Observability Suite (`docker/observability/`)**:
  - Added `docker/docker-compose.observability.yml` overlay, Prometheus scrape & 3-engine alert rule pack (`docker/observability/prometheus/`), closed-loop Alertmanager webhook routing into SwapMid (`docker/observability/alertmanager/alertmanager.yml`), and auto-provisioned Grafana overview dashboard (`docker/observability/grafana/dashboards/swapmid-overview.json`).
- **v1.0 LTS Contract Freeze & Operations Runbook**:
  - Added `OpenApiV1CompatibilityContractTests` locking core `/api/v1/*` paths and HTTP verbs across `DataIntegration`, `MessageBus`, `ApiManagement`, `AccountManagement`, and `NotificationCenter`.
  - Authored production hardening, backup/restore, zero-downtime Compose/Helm upgrade, and `OutcomeUnknown` Target Receipt v1 DBA runbook in `docs/operations-and-upgrade.md`.

## [0.5.0] - 2026-11-15

### Added
- **Connector Capability Registry & Pipeline Schema Pre-flight (`DataIntegration`)**:
  - Added `ConnectorCapabilityCatalog` (`GET /api/v1/data-pipelines/capabilities`) declaring source/target capabilities, atomic upsert support, and strict-TLS Target Receipt v1 eligibility across `postgresql`, `sqlserver`, `mysql`, `rest-api`, and `file`.
  - Added `DataPipelineSchemaPreflightAppService` (`POST /api/v1/data-pipelines/preflight-schema`) performing zero-row read-only `CommandBehavior.SchemaOnly` source projection probing and multi-dialect target catalog inspection (`PostgreSQL`, `SQL Server`, `MySQL`, `SQLite`) to detect unmapped source columns, missing required `NOT NULL` target columns, nullability narrowing, type-family mismatches, and `Upsert` key/watermark violations before activation.
  - Added interactive **Schema Pre-flight** diagnostics panel (`pipeline-schema-preflight.tsx`) in the Next.js pipeline editor across `zh-Hans`, `zh-Hant`, and `en`.
- **Controlled Read-Only / Draft-Only MCP Server (`Mcp`)**:
  - Added opt-in Model Context Protocol JSON-RPC 2.0 endpoint (`POST /api/mcp`, default `Mcp:Enabled=false`) enforcing authentication, per-tool platform RBAC filtering/authorization, credential redaction, and draft-only pipeline authoring (`list_datasources`, `preflight_pipeline_schema`, `diagnose_failed_executions`, `diagnose_dead_letters`, `draft_data_pipeline`).
- **Cloud-Native Helm Chart & Reference Examples**:
  - Added official Helm v3 chart (`charts/swapmid/`) with `pre-install,pre-upgrade` `migrator` Job hook, `api` & `web` Deployments, `HorizontalPodAutoscaler`, `PodDisruptionBudget`, optional `Ingress` and Prometheus `ServiceMonitor`, and fail-closed security defaults.
  - Added runnable integration templates in `examples/` (`postgres-incremental-upsert`, `cloudevents-webhook-fanout`, `partner-api-gateway`).

## [0.3.0] - 2026-11-15

### Added
- **Incremental Atomic `Upsert` & `Upsert-Receipt-V1` (`DataIntegration`)**:
  - Extended `EtlTransformConfig` with `EtlWriteMode.Upsert`, `keyColumns` (`1..16` unique column identifiers), and optional `watermarkColumn`, together with `EtlTargetUpsertPreflight` (non-null unique/PK index verification + enabled `UPDATE` trigger guard) and pre-transaction staging `NULL`/duplicate-key verification (`EtlTargetUpsertWriter`).
  - Added dialect-native atomic batch upsert across **PostgreSQL** (`INSERT ... AS "etl_target" ... ON CONFLICT (...) DO UPDATE`), **SQL Server** (`MERGE ... WITH (HOLDLOCK)`), and **MySQL** (`INSERT ... ON DUPLICATE KEY UPDATE`), plus **Target Receipt v1** support (`upsert-receipt-v1`, `write_mode = 3`) and full `zh-Hans` / `zh-Hant` / `en` pipeline form support.
- **Production-Hardened Managed API Gateway & Client Governance (`ApiManagement`)**:
  - Upgraded `GatewayController` (`/api/gateway/{**path}`) with dual-principal authorization (`SwapMid.ApiManagement.ApiRoutes` OR OpenIddict `external-integration` client tokens), DNS-rebinding-safe outbound SSRF connection guard (`ApiGateway:AllowedHosts` + `ApiGateway:AllowedPrivateNetworks`), inbound credential stripping (`Authorization` / `Cookie`), bounded payload streaming, and zero-ambient-transaction forwarding (`[UnitOfWork(IsDisabled = true)]`).
  - Added per-client route-group ACLs and per-minute sliding-window rate limits (`429 Too Many Requests` + `Retry-After`) persisted in OpenIddict application properties (`PUT /api/v1/third-party-clients/{clientId}/policy`) with a dedicated Gateway Access Policy dialog in `web/`.
  - Added asynchronous gateway event ingress (`POST /api/gateway/events/{topicName}` -> `202 Accepted` into `MessageBus` with deterministic `Idempotency-Key` deduplication) and Prometheus gateway metrics (`swapmid_gateway_requests_total`, `swapmid_gateway_upstream_duration_seconds`).

## [0.2.0] - 2026-11-15

### Added
- **Cross-Engine Transactional Outbox (`DataIntegration` -> `MessageBus`)**:
  - Defined zero-domain-coupling integration event contracts (`IIntegrationEventPublisher`, `IntegrationEventEnvelope`, RFC 4122 v5 `DeterministicEventId`) in `SwapMid.SharedKernel.IntegrationEvents`.
  - Wired `PipelineTerminalEventScheduler` into `DataPipelineExecutionCoordinator` to atomically enqueue `PipelineTerminalEventDispatchJob` inside the terminal `UnitOfWork`, and added distributed-lock-guarded `TransactionalOutboxRelayHostedService` to drain `IBackgroundJobStore` alongside Hangfire, publishing normalized `pipeline.execution.{succeeded|failed|cancelled|outcome_unknown}` events to `integration.pipeline.events` with deterministic replay idempotency.
- **CNCF CloudEvents v1.0 & Structured Subscription Filtering (`MessageBus`)**:
  - Upgraded `HttpMessageDeliveryClient` to emit standard CloudEvents v1.0 binary-mode headers (`ce-specversion`, `ce-id`, `ce-source`, `ce-type`, `ce-subject`, `ce-time`) alongside `X-SwapMid-*` and `HMAC-SHA256` signatures.
  - Added `MessageSubscriptionFilterEvaluator` supporting plain substring compatibility, `subject:<glob>` wildcards (`*` / `?`), bounded `$.jsonPath=value` / `$.jsonPath!=value` scalar comparisons, and `;`-delimited `AND` composition.
- **PostgreSQL 18 Live Contract CI & Multi-Arch GHCR Release Pipeline**:
  - Upgraded `.github/workflows/ci.yml` with a `postgres:18-alpine` service container and enabled `SWAPMID_RUN_POSTGRES_CONTRACT_TESTS=1` + `SWAPMID_POSTGRES_TEST_CONNECTION` so advisory fence, transaction, and migration contracts execute live on every push/PR.
  - Added `.github/workflows/release.yml` to build and push multi-architecture (`linux/amd64`, `linux/arm64`) OCI images (`swapmid-migrator`, `swapmid-api`, `swapmid-web`) to GHCR and publish GitHub Releases on `v*` tags.
  - Expanded `scripts/e2e-smoke.mjs` to verify automatic `ETL -> Outbox -> MessageBus -> RabbitMQ` delivery end-to-end, and published the long-term roadmap (`docs/roadmap.md`).

## [0.1.0] - 2026-11-15

### Added
- **Data Integration Engine (`DataIntegration`)**:
  - Multi-source connectors for **PostgreSQL**, **SQL Server**, **MySQL**, **REST APIs**, and **CSV / Excel** files.
  - Batch streaming pipeline execution with Cron scheduling (`IDataPipelineScheduleSynchronizer`), distributed execution coordination, and transactional **Target Receipt v1** (`__swapmid_etl_receipt_v1`).
  - Ready-to-apply DBA receipt DDL scripts in `scripts/data-integration/target-receipt-v1/`.
  - Out-of-the-box local PostgreSQL demo orders ETL seed (`demo-orders-pg-to-pg`) via `SwapMidDemoDataSeedContributor`.
- **Durable Message Bus (`MessageBus`)**:
  - Topic and subscription routing with persistent delivery attempt tracking, exponential backoff retries, and manual dead-letter replay.
  - Multi-channel dispatch across **HTTP Webhook** (`HMAC-SHA256` `X-SwapMid-Signature` + outbound SSRF guard), **RabbitMQ**, and generic **BotWebhook** (Generic JSON, Slack, Feishu/Lark, DingTalk, WeCom).
- **API Management & Gateway (`ApiManagement`)**:
  - Dynamic API groups and route forwarding (`/gateway/{groupPrefix}/{routePath}`), interactive request test console, and **OpenIddict** `client_credentials` third-party application management (`external-integration` scope).
- **Zero-Browser-Token Next.js 16 BFF Console (`web/`)**:
  - Server-side OAuth2 session storage in Redis behind `HttpOnly` `swapmid_sid` cookies, distributed single-flight token refresh leases, per-tab identity generation fencing (`409 SESSION_IDENTITY_CHANGED`), and bounded upstream proxy fetches.
  - Full `zh-Hans`, `zh-Hant`, and `en` internationalization parity across frontend and backend.
- **Enterprise Governance & Developer Experience**:
  - Unified Hangfire/Cron job scheduler, RBAC with 5-invariant `ImpersonationGuardService`, multi-enterprise organization directory, two-tier data dictionaries, code rules, audit logs, native system health dashboard, Prometheus Alertmanager webhook receiver, and live OpenAPI + EF Core schema explorer.
  - Production-hardened multi-stage Dockerfiles (`DbMigrator`, `HttpApi.Host`, `web`), `docker/docker-compose.yml`, local dev scripts (`scripts/dev.ps1`, `scripts/dev.sh`), and zero-warning GitHub Actions CI.
