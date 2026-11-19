# Changelog

All notable changes to **SwapMid** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.8.0] - 2026-11-19

### Added
- **Apache Iceberg v2 WAP Staging Branch Isolation, Fast-Forward Promotion & Scheduled Maintenance Policies**:
  - Added EF Core migration `AddCdcIcebergWapAndMaintenanceSchedule` extending `AppCdcStreams` with `LakehouseWriteBranch`, `LakehouseMaintenanceCron`, `LakehouseExpireRetainLast`, `LakehouseExpireOlderThanMinutes`, `LakehouseDeleteOrphanedFiles`, `LakehouseLastMaintenanceAt`, and `LakehouseLastMaintenanceSummary`.
  - Upgraded `IcebergV2CommitWriter` (`JsonBuilder.cs`) so streams configured with a WAP branch (e.g., `staging`) commit strictly onto `refs[branch]` while keeping `current-snapshot-id` and `refs["main"]` untouched — preventing unaudited writes from leaking to external readers (`DuckDB` / `Trino` / `Spark`).
  - Added `IcebergV2MaintenanceService.Branches.cs` and `POST /api/v1/cdc-streams/{id}/promote-lakehouse-branch` (`[Authorize(CdcStreams.Manage)]`) enforcing Data Contract SLA pass gating (`RequireContractPass = true`), strict fast-forward ancestry validation, atomic optional audit tag pinning (`CreateAuditTagName`), best-effort Catalog sync, and `swapmid_cdc_iceberg_maintenance_total{operation="promote"}` telemetry.
  - Added `HangfireCdcScheduleSynchronizer.SyncMaintenanceAsync` and `RunScheduledLakehouseMaintenanceHangfireJob` (`cdc-lakehouse-maint:{streamId}`) for Cron-driven automated small-file compaction and `expireSnapshots` orphan reclamation, plus full export/diff/apply support in `swapmid.io/v1` GitOps bundles.
- **Cross-Engine Data Lineage Graph, Blast-Radius Impact Explorer & MCP Tool 13**:
  - Added host-layer `SwapMid.HttpApi/Lineage/` (`IntegrationLineageBuilder`, `IntegrationLineageController`) exposing read-only, secret-free `GET /api/v1/integration-lineage` and `GET /api/v1/integration-lineage/impact` connecting `DataSources` -> `DataPipelines` / `CdcStreams` -> `LakehouseTables` / `MessageTopics` -> `MessageSubscriptions` & `ApiGroups` / `ApiRoutes`, with cycle-safe BFS upstream/downstream impact traversal, risk grading (`low`/`medium`/`high`), and pre-change remediation guidance.
  - Added RBAC-gated read-only MCP Tool 13 `analyze_lineage_impact` (`McpLineageImpactHandler`), enriched MCP Tool 12 `plan_lakehouse_maintenance` with pending WAP branch recommendations, and expanded `scripts/e2e-smoke.mjs` to assert live lineage + 13 MCP tools.
  - Delivered interactive `/data-integration/cdc` **`血缘图谱` / `Lineage`** 4-stage topology & blast-radius dialog (`CdcLineageGraphDialog`), WAP branch promotion controls in `CdcIcebergMaintenanceSection`, and WAP/Cron policy controls in `CdcLakehouseSinkSection`, maintaining **100.0% (`207/207`)** `/api/v1` OpenAPI summary coverage across 9 business domains and `2,190` trilingual i18n keys.
- **Turnkey Streaming Lakehouse Sandbox Overlay & Blueprint**:
  - Added [docker/docker-compose.streaming-lakehouse.yml](docker/docker-compose.streaming-lakehouse.yml) (`Redpanda v24.3.6` Kafka + built-in Schema Registry + Chainguard `MinIO` + optional `DuckDB` query profile) and [examples/streaming-lakehouse-iceberg/](examples/streaming-lakehouse-iceberg/) (`README.md`, `swapmid-bundle.yaml`, `query-duckdb.sql`).

### Fixed
- **CI Ephemeral Port Collision Hardening**: Moved the CI `postgres-cdc` logical-replication test container mapping in `.github/workflows/ci.yml` from `55432` (inside Linux ephemeral range `32768–60999`) to `15432` to eliminate transient runner bind collisions.
- **Event-Driven CDC (`PushWebhook` / `KafkaDebezium`) Schema Drift Acceptance**: Updated `POST /api/v1/cdc-streams/{id}/accept-schema` so payload-inferred CDC sources cleanly clear drift alerts, re-evaluate Data Contract SLA status, and re-lock the schema baseline on the next incoming batch instead of attempting SQL `INFORMATION_SCHEMA` introspection.

## [2.7.0] - 2026-11-19

### Added
- **Apache Iceberg v2 WAP Named Tags (`refs`) & Safe `expireSnapshots` Orphan Garbage Collection**:
  - Added pure-.NET `IcebergV2MaintenanceService`, DTOs (`CdcLakehouseMaintenanceDtos.cs`), and endpoints `POST /api/v1/cdc-streams/{id}/lakehouse-tags` and `POST /api/v1/cdc-streams/{id}/expire-lakehouse-snapshots` (`DryRun = true` default, `[Authorize(CdcStreams.Manage)]`).
  - Upgraded `IcebergV2CommitWriter.JsonBuilder.cs` to preserve all non-`main` named `refs` across subsequent CDC writes/compactions, and upgraded `IcebergV2MetadataReader.cs` to expose `Refs` and resolve latest metadata paths (`v{metaSeq}.metadata.json`) for DuckDB/Trino/Spark Time-Travel SQL.
  - Enforced strict reference-counted orphan reclamation: `expireSnapshots` permanently protects `current-snapshot-id`, the last `RetainLast` snapshots, and every snapshot referenced by a named branch/tag (`refs`), physically deleting only unreferenced Parquet/Equality Delete/Avro manifest objects when `DeleteOrphanedFiles = true` — verified live with official **`duckdb/duckdb:latest` (`v2.0.0`) `iceberg_scan()`** across both post-expiration `v6` (`3` live rows) and tag-protected `audit-v1` `v1` (`2` historical rows) after deleting `8/8` orphan objects.
  - Added Prometheus metric `swapmid_cdc_iceberg_maintenance_total{operation,outcome}` and Grafana Panel 10 maintenance series, plus the new `/data-integration/cdc` Time-Travel **`命名分支与审计标签 (Refs)`** / **`打标签`** / **`清理预演`** / **`执行清理`** panel (`CdcIcebergMaintenanceSection`).
- **Confluent / Redpanda Schema Registry Wire-Format Protobuf & JSON Schema Decoding**:
  - Added zero-dependency pure-.NET `ProtobufSchemaParser` and `ProtobufWireDatumReader` supporting `proto2`/`proto3` `message`, nested messages, `enum`, `oneof`, packed/unpacked `repeated`, `map<K,V>`, and Confluent ZigZag Varint message-index arrays (`0x00` shortcut & multi-index nested paths).
  - Upgraded `ConfluentSchemaRegistryClient` to transparently dispatch `AVRO`, `PROTOBUF`, and `JSON` Schema Registry wire payloads (`0x00` + 4-byte BE `schemaId`) — verified live against real **Redpanda v24.3.6** built-in Schema Registry (`schemaType: "PROTOBUF"`).
- **GitOps PR Drift Gate Action, Environment Secret Placeholder Resolution & MCP Tool 12**:
  - Upgraded [scripts/swapmidctl.mjs](scripts/swapmidctl.mjs) with fail-closed `${ENV_SECRET:NAME}` placeholder resolution (`--resolve-env-secrets`, never echoing secrets), PR-ready Markdown output (`--markdown`), and CI exit-code signaling (`--exit-code`: `0` clean / `2` drift), covered by [scripts/swapmidctl.test.mjs](scripts/swapmidctl.test.mjs).
  - Added turnkey composite GitHub Action [.github/actions/swapmid-gitops-diff/action.yml](.github/actions/swapmid-gitops-diff/action.yml) outputting PR drift summaries to `GITHUB_STEP_SUMMARY`.
  - Added RBAC-gated read-only MCP Tool 12 `plan_lakehouse_maintenance` (`McpLakehouseMaintenanceHandler`), expanded `scripts/e2e-smoke.mjs` to assert 12 MCP tools, and maintained **100.0% (`204/204`)** `/api/v1` OpenAPI summary coverage and `2,162` trilingual i18n keys.

## [2.6.1] - 2026-11-19

### Fixed
- **Zero-`apt-get` Production API Container Image & Dependency-Free Healthcheck**: Removed the runtime `apt-get install curl` layer from `src/SwapMid.HttpApi.Host/Dockerfile` and switched the `api` healthcheck in `docker/docker-compose.yml` to a zero-dependency `bash` `/dev/tcp/127.0.0.1/8080` HTTP `200` probe, eliminating upstream `archive.ubuntu.com` build outages across multi-arch (`amd64`/`arm64`) builds while reducing runtime container attack surface.

## [2.6.0] - 2026-11-19

### Added
- **Apache Iceberg v2 Merge-on-Read Equality Deletes (`LakehouseWriteMode.MergeOnReadUpsert = 1`)**:
  - Added `LakehouseWriteMode` (`AppendChangelog = 0`, `MergeOnReadUpsert = 1`), EF Core migration `AddCdcLakehouseWriteMode` (`AppCdcStreams.LakehouseWriteMode`), and `business_key` (`FieldId = 12`, `string`, optional) across Iceberg v2 Parquet schemas (`FieldId 1..12`) and `schema.name-mapping.default`.
  - Extended `ObjectStorageEtlSinkWriter`, `IcebergAvroManifestBuilder`, `IcebergV2CommitWriter.AvroStorage.cs`, and `CdcObjectStorageLakehouseWriter.MergeOnRead.cs` to collapse intra-batch business keys, emit 1-column equality-delete Parquet files (`eqdel_{runId:N}.parquet`, `content = 2`, `equality_ids = [12]`), dual binary Apache Avro 1.11 OCF manifests (`*-m0.avro` data + `*-d0.avro` deletes, `manifest_file.content = 1`), and atomic `operation = "overwrite"` commits (`swapmid_cdc_iceberg_equality_deletes_total`).
  - Upgraded `CdcLakehouseDataPreviewService.EqualityDeletes.cs` to apply Iceberg v2 Equality Delete filtering natively in pure-.NET Zero-Infra preview, and upgraded `CdcObjectStorageCompactionService` / `CdcParquetCompactionMergeHelper` to materialize deduplicated live rows during `REPLACE` compaction while garbage-collecting superseded equality-delete files — verified live with official **`duckdb/duckdb:latest` (`v2.0.0`) `iceberg_scan()`** across `v1` (historical), `v2` (`OVERWRITE` MoR), and `v3` (`REPLACE` compacted).
- **Confluent / Redpanda Schema Registry Wire-Format Binary Avro Decoding for Kafka Debezium CDC**:
  - Extended `KafkaSinkConfig` (`KafkaSinkConfig.cs` / `KafkaSinkConfigContract.cs`) with optional SSRF-guarded `schemaRegistryUrl`, `schemaRegistryUsername`, and redacted `schemaRegistryPassword`.
  - Added zero-dependency pure-.NET `AvroBinaryDatumReader` (supporting records, named references, unions, arrays, maps, enums, fixed, decimal, and date logical types) and bounded-cache `ConfluentSchemaRegistryClient` (`0x00` + 4-byte big-endian `schemaId`, `/subjects` preflight probe returning `slotPlugin = "debezium-avro+json"`, `/schemas/ids/{id}` fetch, `swapmid_cdc_schema_registry_fetches_total{outcome}`).
  - Upgraded `KafkaDebeziumCdcCaptureService` to consume raw `byte[]` Kafka frames and transparently decode both Confluent Binary Avro and JSON Debezium envelopes — verified live against real **Redpanda v24.3.6** built-in Schema Registry.
- **Declarative GitOps Integration Bundles (`swapmid.io/v1`), `swapmidctl` CLI & MCP Tool 11**:
  - Added host-layer `SwapMid.HttpApi/GitOps/` (`IntegrationBundleController`, `IntegrationBundleOrchestrator`, zero-dependency `IntegrationBundleYamlCodec`) exposing `GET /api/v1/integration-bundles/export`, `POST /api/v1/integration-bundles/diff`, and `POST /api/v1/integration-bundles/apply` (`DryRun = true` default, zero plaintext secrets exported, new pipelines/CDC streams safely initialized in `Draft`, preserving zero cross-domain coupling in `SwapMid.Application`).
  - Added zero-dependency Node CLI [scripts/swapmidctl.mjs](scripts/swapmidctl.mjs) (`export` / `diff` / `apply`), MCP Tool 11 `export_integration_bundle` (`McpIntegrationBundleHandler`), and updated [scripts/e2e-smoke.mjs](scripts/e2e-smoke.mjs) to verify all 11 MCP tools and GitOps bundle export/diff.
  - Upgraded `/data-integration/cdc` (`MoR Upsert` selector/badges, `EqDel` counters in Time-Travel dialog, and one-click `GitOps` Export/Diff/Dry-Run/Apply dialog) and `/data-integration/sources` (Kafka Schema Registry fields) across `zh-Hans`, `zh-Hant`, and `en` (`2,151` trilingual keys, **`202/202` 100.0%** `/api/v1` OpenAPI summary coverage), plus Grafana Panel 10 equality-delete & Schema Registry series.
- **Container Build Resilience**:
  - Added optional `APT_MIRROR` build arg and `Acquire::Retries=5` in `src/SwapMid.HttpApi.Host/Dockerfile` and `docker/docker-compose.yml` to support reliable builds behind restricted HTTP proxies without altering default global Ubuntu sources.

## [2.5.0] - 2026-11-19

### Added
- **Apache Kafka / Redpanda Debezium Envelope Inbound CDC Source (`CdcSourceType.KafkaDebezium = 4`)**:
  - Added `KafkaDebeziumEnvelopeParser` and `KafkaDebeziumCdcCaptureService` (using `Confluent.Kafka` via `KafkaSinkConfigContract.ToConsumerConfig`) binding to `DataSourceType.Kafka`, mapping `PublicationName` to Kafka Topic and `SlotName` to Consumer Group. Supports wrapped & unwrapped Debezium JSON envelopes, CloudEvents CDC JSON, flat business JSON, and tombstone records.
  - Added monotonic multi-partition checkpoint tracking (`kafka:0=N,1=M`), live broker watermark lag preflight (`walLevel = "kafka_debezium"`, `swapmid_cdc_kafka_lag_messages` Prometheus gauge, `SwapMidCdcKafkaLagHigh` alert rule, Grafana Panel 10 series), automatic schema inference (`SchemaBaselineJson`), `SchemaDriftPolicy`, Data Contract SLA enforcement, and delivery to all SwapMid downstream targets.
- **Apache Iceberg v2 Native Daily Hidden Partitioning (`LakehousePartitionStrategy.DailyCommitUtc = 1` -> `day(commit_timestamp_utc)`)**:
  - Added `LakehousePartitionStrategy` (`None = 0`, `DailyCommitUtc = 1`) and EF Core migration `AddCdcIcebergPartitionStrategy` (`AppCdcStreams.LakehousePartitionStrategy`).
  - Extended `IcebergAvroManifestBuilder`, `IcebergV2CommitWriter`, `IcebergV2MetadataReader`, and `CdcObjectStorageLakehouseWriter.Partitioned.cs` to emit Iceberg v2 `partition-specs` (`spec-id: 1`, `transform: "day"`, `source-id: 8`, `field-id: 1000`), binary Avro `r102` (`commit_timestamp_utc_day`) per data file, binary Avro `r508` little-endian `int32` `lower_bound`/`upper_bound` partition summaries in `manifest-list`, and UTC day-bucketed Parquet paths (`data/commit_day=YYYY-MM-DD/run_*.parquet`).
  - Upgraded `CdcObjectStorageCompactionService` to perform partition-aware same-day `REPLACE` compaction (merging only fragments within the same UTC day while leaving prior days intact), verified live against real **Redpanda** + **MinIO** + official **`duckdb/duckdb:latest` `iceberg_scan()`** (`WHERE commit_timestamp_utc >= TIMESTAMPTZ` partition pruning and historical `v1` Time-Travel).
- **AI-Native MCP Copilot Expansion to 10 Tools & Frontend Console Upgrades**:
  - Added RBAC-gated read-only MCP Tool 9 `diagnose_webhook_deliveries` (`McpWebhookDeliveryDiagnosticsHandler`) and Tool 10 `inspect_lakehouse_snapshots` (`McpLakehouseSnapshotHandler`) on `POST /api/mcp`, and updated `scripts/e2e-smoke.mjs` to verify all 10 tools.
  - Upgraded `/data-integration/cdc` (extracting `CdcLakehouseSinkSection` to enforce `<= 500` line hygiene) with `Kafka Debezium` source forms/badges and `day(ts)` / `day(commit_timestamp_utc)` partition badges & selectors across `zh-Hans`, `zh-Hant`, and `en` (`2,123` trilingual keys, `199/199` 100.0% `/api/v1` OpenAPI summary coverage).
- **Flagship Bilingual Front-Door & Automated Terminology / Anti-Mistranslation CI Gate**:
  - Standardized SwapMid's brand positioning across `README.md`, `README.zh-CN.md`, `AGENTS.md`, `charts/swapmid/Chart.yaml`, `docs/`, and trilingual backend/frontend catalogs as an **Enterprise Data Exchange & Integration Platform (企业级数据交换与集成中台)**, eliminating all "middleware / 中间件" and "data swap" mistranslations.
  - Added `web/scripts/check-terminology.mjs` and `web/scripts/check-terminology.test.mjs` wired into `pnpm check:i18n` / `pnpm lint` and GitHub Actions CI to permanently prevent terminology drift or Chinglish regressions.

### Fixed
- **AWS SigV4 Double-Encoding on Hive Partition Paths (`AwsSigV4Signer.EncodeCanonicalUriPath`)**: Unescaped pre-encoded URI path segments (`Uri.UnescapeDataString`) prior to `AwsUrlEncode` so Hive-style partition directories containing `=` (`data/commit_day=YYYY-MM-DD/...`) sign identically against MinIO and AWS S3 without double-encoding `%3D` into `%253D`.

## [2.4.0] - 2026-11-19

### Added
- **True Binary Apache Avro 1.11 OCF Manifests for Apache Iceberg v2 & Parquet `FieldId` / `TIMESTAMP_MICROS`**:
  - Implemented zero-dependency, pure-.NET Apache Avro 1.11 Object Container File codec (`IcebergAvroOcfCodec`) and Iceberg v2 `manifest-list` / `manifest-file` encoder/decoder (`IcebergAvroManifestBuilder`, `IcebergV2CommitWriter.AvroStorage.cs`), emitting binary `metadata/snap-{snapshotId}-1-{runId:N}.avro` (`manifest_file`) and `metadata/{runId:N}-s{seq}-m0.avro` (`manifest_entry`) alongside `schema.name-mapping.default` in `v*.metadata.json`.
  - Upgraded `ObjectStorageEtlSinkWriter` and `CdcParquetCompactionMergeHelper` to write Parquet Thrift column `FieldId = 1..11` and `INT64 TIMESTAMP(MICROS, isAdjustedToUTC=true)` (`DateTimeFormat.DateAndTimeMicros`), preserving field IDs across `REPLACE` compaction merges.
  - Upgraded `IcebergV2MetadataReader` to read binary `.avro` manifest chains natively (with automatic JSON fallback for `v2.3.0` legacy tables) and verified live end-to-end interoperability with official **`duckdb/duckdb:latest` `iceberg_scan()`** across both compacted `v3`/`v4` and historical Time-Travel `v1` snapshots.
- **Push Webhook CDC Auto-Schema Inference & Governed Delivery / Dead-Letter Replay Ledger**:
  - Added `CdcWebhookDeliveryRecord` entity (`AppCdcWebhookDeliveries`) with EF Core migration `AddCdcWebhookDeliveryLedger` (`Cascade` FK + composite index on `(StreamId, ReceivedAtUtc)`). Security invariant: persists bounded `<= 64 KiB` `StoredPayloadJson` **only** for HMAC-authenticated `Inactive (409)` / `Failed (500)` dead-letters and stores `null` on `401 Unauthorized` (preventing storage amplification) and `200 Accepted` (zero payload bloat).
  - Added `CdcWebhookSchemaInferenceHelper` to automatically infer and persist `SchemaBaselineJson` from Push Webhook batches (enabling Data Contract `RequiredColumns` SLA enforcement automatically on serverless webhook streams) and enforce `SchemaDriftPolicy` (`Warn` / `PauseAndAlert` with dead-letter retention for 1-click recovery).
  - Exposed `GET /api/v1/cdc-webhooks/{streamId}/deliveries` (`CdcStreams.Default`) and `POST /api/v1/cdc-webhooks/{streamId}/deliveries/{deliveryId}/replay` (`CdcStreams.Manage`) with Prometheus `outcome="replayed"` telemetry and a new `/data-integration/cdc` **`推送日志` (`Push Log`)** dialog supporting 1-click dead-letter replay (`2,111` trilingual i18n keys, `0` CJK in `en.json`).
- **Closed-Loop Lakehouse & Push Webhook Observability**:
  - Added Prometheus alert rules `SwapMidCdcIcebergCommitFailures`, `SwapMidCdcWebhookAuthFailuresSpike`, and `SwapMidCdcWebhookDeliveryFailures` in [docker/observability/prometheus/alert-rules.yml](docker/observability/prometheus/alert-rules.yml).
  - Added Grafana Overview **Panel 10** (Apache Iceberg v2 Commits & Push Webhook Ingress) and **Panel 11** (Data Contract SLA, Catalog Syncs & Zero-Infra Previews) in [docker/observability/grafana/dashboards/swapmid-overview.json](docker/observability/grafana/dashboards/swapmid-overview.json).
  - Expanded OpenAPI `/api/v1` summary coverage to **100.0% (199/199 endpoints)** across 9 domains.

## [2.3.0] - 2026-11-19

### Added
- **Native Apache Iceberg v2 Table Protocol (`LakehouseTableFormat.ApacheIceberg = 2`)**:
  - Added pure-.NET, zero-JVM `IcebergV2CommitWriter` (`IcebergV2CommitWriter.cs` + `IcebergV2CommitWriter.JsonBuilder.cs`) writing spec-compliant Iceberg v2 `metadata/v{seq}.metadata.json`, companion `metadata/snap-{snapshotId}-manifest.json`, and `metadata/version-hint.text` with S3 `If-None-Match: *` CAS optimistic concurrency (`412` retry + `swapmid-run-id` idempotent crash replay) and atomic `replace` compaction snapshots preserving historical Parquet fragments.
  - Added `IcebergV2MetadataReader` integrated into `DeltaLakeHistoryReader` and `CdcLakehouseDataPreviewService` for point-in-time Iceberg v2 Time-Travel (`v1..vN`), Zero-Infra live Parquet row preview, executable DuckDB `iceberg_scan(...)` / Trino `FOR VERSION AS OF` / Spark SQL templates, Prometheus counter `swapmid_cdc_iceberg_commits_total{operation,outcome}`, and `metadata-location` + `swapmid.iceberg_snapshot_id` pass-through in `ExternalLakehouseCatalogSyncService`.
- **Serverless Authenticated Push Webhook CDC Source (`CdcSourceType.PushWebhook = 3`)**:
  - Added fail-closed `POST /api/v1/cdc-webhooks/{streamId}` (`CdcWebhookIngressController` + `CdcWebhookIngressService`) verifying `X-SwapMid-Signature: sha256=<hex>` in constant time (`CryptographicOperations.FixedTimeEquals`) against encrypted `webhookSecret`/`hmacSecret` stored in the bound `RestApi` DataSource (zero plaintext secrets on `CdcStreamDefinition`), bounded `<= 2 MiB` body streaming, `X-SwapMid-Delivery-Id` idempotency, monotonic `webhook:{seq}` checkpoints, optional `schema.table` filtering, full downstream fanout (MessageBus, Kafka, ClickHouse, Relational Mirror, Delta Lake, and Apache Iceberg v2), Prometheus counter `swapmid_cdc_webhook_ingests_total{outcome}`, and MCP `webhookIngressPath` discovery.
- **Automated Chaos SLA Throughput & RPO=0 Benchmark Suite**:
  - Extended `ChaosResilienceContractTests` with Scenario 4 (Iceberg v2 `412` CAS race + `swapmid-run-id` crash replay) and Scenario 5 (Push Webhook forged HMAC `401` rejection + valid signed replay -> Iceberg v2 commit).
  - Added `ChaosSlaThroughputBenchmarkTests` measuring end-to-end 1,000-row Parquet + Delta Lake / Apache Iceberg v2 commit throughput, P50/P95/P99 latency, Time-Travel replay, Data Contract SLA compliance, and out-of-order replay idempotency (`RPO = 0`), recorded in [docs/benchmarks/resilience-and-chaos.md](docs/benchmarks/resilience-and-chaos.md).
- **Frontend CDC Console & OpenAPI Coverage**:
  - Updated `/data-integration/cdc` forms, table badges (`Push Webhook`, `Iceberg v2`, `Iceberg · vN`), and Iceberg v2 Time-Travel + live Parquet preview dialogs across `zh-Hans`, `zh-Hant`, and `en` (`2,087` i18n keys, `0` CJK in `en.json`).
  - Expanded OpenAPI `/api/v1` summary coverage to **100.0% (197/197 endpoints)** across 9 domains (`CdcWebhookIngress` categorized under Data Integration).

### Fixed
- **Out-of-Order Lakehouse Replay Deduplication**: Hardened `CdcObjectStorageLakehouseWriter.DeliverWithManifestAsync` to detect already-committed deterministic `RunId` entries in `latest.json` before issuing duplicate Delta Lake or Iceberg v2 commits on out-of-order webhook/CDC replays.
- **GHCR OCI Helm Push Retry Resilience**: Added a 3-attempt backoff retry loop around `helm push` in `.github/workflows/release.yml` to absorb transient GHCR OCI blob propagation races.

## [2.2.0] - 2026-11-19

### Added
- **Zero-Infra Instant Lakehouse Data Preview (`GET /api/v1/cdc-streams/{id}/lakehouse-preview`)**: Added `CdcLakehouseDataPreviewService` to fetch and decode Apache Parquet (via `Parquet.Net`) and JSONL fragments directly from S3/MinIO for any Delta Lake Time-Travel version (`?version=v`) or `_swapmid/latest.json` snapshot, enforced by `DataIntegrationSinkEndpointPolicy` SSRF guards, authoritative metadata key resolution, `4 MiB`/file read budgets, and `50`-row caps, rendered inside the `/data-integration/cdc` Time-Travel dialog (`swapmid_cdc_lakehouse_previews_total`).
- **Data Contract & Freshness SLA Engine**: Added `CdcContractStatus` (`Unconfigured`, `Healthy`, `Warning`, `Breached`), EF Core migration `AddCdcDataContractSla` (`MaxFreshnessLagSeconds`, `MaxAllowedWalLagBytes`, `RequiredColumns`, contract evaluation audit state), `CdcDataContractEvaluator`, `POST /api/v1/cdc-streams/{id}/evaluate-contract`, deduplicated breach/recovery notifications, Prometheus metrics (`swapmid_cdc_contract_evaluations_total`, `swapmid_cdc_contract_violations_total`), alert rule `SwapMidCdcDataContractBreached`, and `/data-integration/cdc` SLA badges & form controls.
- **AI-Native MCP Copilot Expansion (`POST /api/mcp`, 8 Tools)**: Added RBAC-gated Tool 7 `propose_pipeline_column_mappings` (`McpPipelineCopilotHandler`, deterministic exact & normalized `snake_case`/`camelCase` mapping proposals + candidate `keyColumns`/`watermarkColumn` + Draft-ready `transformConfig`) and Tool 8 `evaluate_cdc_data_contracts` (`McpDataContractHandler`), and updated `scripts/e2e-smoke.mjs` to assert all 8 MCP tools.

### Changed & Fixed
- **Delta Lake Time-Travel Auto-Compaction Preservation**: Updated `CdcObjectStorageCompactionService.TryAutoCompactAsync` to preserve historical Parquet files when `LakehouseTableFormat == DeltaLake` (`deleteCompactedFiles: false`), ensuring `VERSION AS OF v` historical Time-Travel previews remain readable across automatic `OPTIMIZE` runs, while handling physically deleted/vacuumed fragments gracefully (`MissingFileCount` diagnostic summary).
- **CI / Release Actions Node 24 & Dockerfile Hygiene**: Upgraded GitHub Actions in `.github/workflows/{ci,release}.yml` to Node 24-native majors (`actions/checkout@v7`, `actions/setup-node@v7`, `actions/setup-dotnet@v6`, `docker/setup-qemu-action@v4`, `docker/setup-buildx-action@v4`) and renamed non-secret boolean build arg `OPENIDDICT_PFX_RUNTIME_SECRET` -> `OPENIDDICT_PFX_MOUNT_AT_RUNTIME` in `src/SwapMid.HttpApi.Host/Dockerfile` to eliminate BuildKit `SecretsUsedInArgOrEnv` warnings.
- Expanded OpenAPI `/api/v1` summary coverage to **100.0% (196/196 endpoints)**.

## [2.1.0] - 2026-11-19

### Added
- **Delta Lake `_delta_log` Time-Travel Inspector**: Added `DeltaLakeHistoryReader` and `GET /api/v1/cdc-streams/{id}/lakehouse-history` (optional `?version=v`) to parse `_delta_log/{version:D20}.json` commit history, reconstruct point-in-time active Parquet snapshot sets (`VERSION AS OF v`), and generate executable DuckDB `read_parquet([...])` and Spark SQL queries inside the new `/data-integration/cdc` Time-Travel dialog.
- **External Open Catalog Interop (Unity Catalog REST & Apache Iceberg REST)**: Added `LakehouseCatalogType` (`None`, `UnityCatalogRest`, `IcebergRest`), EF Core migration `AddCdcLakehouseCatalogSync` (`CatalogSinkId`, `CatalogNamespace`, `CatalogTableName`, sync audit metadata), SSRF-guarded `ExternalLakehouseCatalogSyncService`, manual `POST /api/v1/cdc-streams/{id}/sync-catalog`, best-effort post-snapshot/capture/compaction catalog sync, Prometheus metric `swapmid_cdc_catalog_syncs_total{catalog_type,outcome}`, and MCP `diagnose_cdc_streams` catalog fields.
- **Chaos & Fault-Injection Resilience Suite**: Added `ChaosResilienceContractTests` and [docs/benchmarks/resilience-and-chaos.md](docs/benchmarks/resilience-and-chaos.md) verifying mid-flight `OutcomeUnknown` crash recovery via `Target Receipt v1`, Delta Lake `412` optimistic CAS retry with `swapmidRunId` idempotency, and external catalog `503` outage isolation without blocking CDC `CheckpointLsn` durability.
- **Turnkey Lakehouse Sandbox Overlay**: Added [docker/docker-compose.lakehouse-demo.yml](docker/docker-compose.lakehouse-demo.yml) (`mysql:8.4` `ROW`+`FULL` binlog + `swapmid_cdc.orders` seed + Chainguard `minio` / `minio-client` bucket bootstrap) and validated it in CI.

### Fixed
- Prevented EF Core `SaveChangesAsync` from bumping `DataSource.ConcurrencyStamp` during read-only `GetActivationForUpdateAsync` / `ClaimAsync` pipeline coordination (`AsNoTracking`), eliminating spurious `SwapMid:ETL:025` concurrent-modification errors on subsequent `TargetReceiptV1` executions.
- Switched `docker-compose.lakehouse-demo.yml` object-storage images from deprecated Docker Hub `minio/*` tags to public Chainguard images (`cgr.dev/chainguard/minio:latest` and `cgr.dev/chainguard/minio-client:latest`).
- Upgraded `moment` (`2.30.1` -> `2.31.0`) in `src/SwapMid.HttpApi.Host` lockfiles and upgraded GitHub Actions workflow actions (`pnpm/action-setup@v6`, `azure/setup-helm@v5`, `docker/login-action@v4`, `docker/metadata-action@v6`, `docker/build-push-action@v7`).
- Expanded OpenAPI `/api/v1` summary coverage to **100.0% (194/194 endpoints)**.

## [2.0.1] - 2026-11-18

### Fixed & Added (Open-Source Release & Kubernetes Readiness)
- **Helm Chart Pod Health Probes, Singleton Lease Envs & CI Secret Validation (`charts/swapmid/`, `.github/workflows/`)**:
  - Updated `charts/swapmid/templates/web-deployment.yaml` liveness and readiness probes from non-existent `/api/health` and `/api/ready` paths to SwapMid's live `/build-meta` probe, injected per-pod `POD_NAME` + `WEB_SINGLETON_LEASE_KEY` + `WEB_SINGLETON_LEASE_TTL_MS` + `SWAPMID_API_READY_FALLBACK_URL` required by `scripts/start-production.mjs`, and avoided multi-replica lease contention in Kubernetes.
  - Updated `.github/workflows/ci.yml` and `.github/workflows/release.yml` to supply `--set secret.existingSecret=ci-placeholder` for `helm lint` / `helm template` validation while keeping fail-closed `required` secret enforcement in `charts/swapmid/templates/secret.yaml`.
  - Fixed `.github/workflows/release.yml` OCI build-arg from unused `APP_RELEASE` to `RELEASE_SHA=${{ github.sha }}` so published GHCR container images carry accurate `org.opencontainers.image.revision` metadata.
- **Public Documentation, Security Policy & Turnkey MySQL Binlog -> Delta Lake Blueprint**:
  - Updated `README.md`, `README.zh-CN.md`, `SECURITY.md` (`2.0.x` supported baseline + private advisory URL), `.github/ISSUE_TEMPLATE/config.yml`, `.github/PULL_REQUEST_TEMPLATE.md`, and `docs/operations-and-upgrade.md` (Section `7.7` covering MySQL/MariaDB `ROW`+`FULL` binlog & Delta Lake `_delta_log` operations).
  - Added runnable example blueprint [`examples/mysql-binlog-deltalake-cdc/`](./examples/mysql-binlog-deltalake-cdc/) and indexed it in [`examples/README.md`](./examples/README.md).

## [2.0.0] - 2026-11-18

### Added
- **MySQL / MariaDB Row-Based Binlog CDC Capture Engine (`CdcSourceType.MySqlBinlog = 2`)**:
  - Added `MySqlCdc 4.0.1` (paired with `MySqlConnector 2.6.1`, zero NuGetAudit warnings) and implemented `MySqlBinlogCdcCaptureService` (split cleanly across partial classes under `src/SwapMid.Application/DataIntegration/`).
  - Performs server pre-flight validation (`log_bin=ON`, `binlog_format=ROW`, `binlog_row_image`, purged binlog detection, and byte-accurate `WalLagBytes`), consistent baseline snapshots (`FLUSH TABLES WITH READ LOCK` + `REPEATABLE READ` + `START TRANSACTION WITH CONSISTENT SNAPSHOT` + `SHOW BINARY LOG STATUS`/`SHOW MASTER STATUS`, with `LOCK TABLES READ` fallback), transaction-atomic bounded incremental reads (`RotateEvent`, `TableMapEvent`, `WriteRowsEvent`, `UpdateRowsEvent`, `DeleteRowsEvent`, `XidEvent`, `QueryEvent("COMMIT")`), `INFORMATION_SCHEMA.COLUMNS` schema drift evaluation, and full integration across `CdcStreamCaptureCoordinator`, `CdcRelationalMirrorPreflightService`, and `McpCdcDiagnosticsHandler`.
  - Added `MySqlBinlogCdcHelperTests`, `MySqlBinlogCdcContractTests` (`SWAPMID_MYSQL_TEST_CONNECTION`), and a live `mysql:8.4` `--binlog-format=ROW --binlog-row-image=FULL` service step in `.github/workflows/ci.yml`.
- **Native Apache Delta Lake `_delta_log/` Transaction Log Protocol (`LakehouseTableFormat.DeltaLake = 1`)**:
  - Added `LakehouseTableFormat` (`SwapMidManifest = 0`, `DeltaLake = 1`) to `CdcStreamDefinition` (widened `CheckpointLsn` to `128` characters for `{binlogFile}:{position}`, extracted `CdcStreamDefinition.Configuration.cs`, and generated EF Core migration `AddCdcMySqlBinlogAndDeltaLake`).
  - Implemented `DeltaLakeCommitWriter` emitting native Delta Lake Protocol v1/v2 NDJSON commits (`_delta_log/{version:D20}.json` with `commitInfo`, `protocol`, `metaData`, `add`, and `OPTIMIZE` compaction `remove` + `add` actions with `dataChange: false`), enforced via S3 `If-None-Match: *` optimistic concurrency control and idempotent `swapmidRunId` replay.
  - Integrated Delta Lake `OPTIMIZE` commits into `CdcObjectStorageCompactionService` (with merge helpers extracted to `CdcParquetCompactionMergeHelper.cs`) and surfaced `DeltaLogDirectory`, `LatestDeltaVersion`, DuckDB `delta_scan()`, Spark `format("delta")` / `DESCRIBE HISTORY`, and Polars `pl.scan_delta()` templates in `CdcExternalSinkTemplateBuilder`.
- **Frontend CDC Console (`web/src/app/(dashboard)/data-integration/cdc/`) & Trilingual Localization**:
  - Extended source-type auto-detection (`MySql`/`MariaDb` -> `MySqlBinlog`), `LakehouseTableFormat` selector (`SwapMid Manifest` / `Delta Lake`), checkpoint/template badges (`Delta Lake vN`, `_delta_log` path, `delta_scan()` SQL) across `zh-Hans`, `zh-Hant`, and `en` catalogs (`0` CJK characters in `en.json`), and Vitest coverage in `client.test.tsx`.

## [1.9.1] - 2026-11-18

### Added
- **Cloud Table-Preference Sync & Navigation Telemetry (`Layout` / `SwapMid.HttpApi` / `web/`)**:
  - Added `ITablePreferenceAppService`, `TablePreferenceAppService` (backed by user-level `AbpSettings` `PreferenceTableDensity` & `PreferenceTableViews` with `128 KiB` / `200`-table bounds and `SwapMid:LAY:002` guard, requiring zero EF migrations), `TablePreferenceController` (`GET /api/v1/table-preference`, `PUT /api/v1/table-preference/table`, `PUT /api/v1/table-preference/density`), and `TablePreferenceAppServiceTests`.
  - Added `[Authorize] POST /api/v1/nav-usage` (`NavUsageController`) logging sanitized structured domain/group/href navigation usage telemetry (`204 No Content`).
- **100% `/api/v1` OpenAPI `<summary>` Coverage & SQL Server 2022 Linux CI Gate (`SwapMid.HttpApi` / `.github/workflows/ci.yml`)**:
  - Documented all remaining `27` controllers in `SwapMid.HttpApi` with accurate XML `<summary>` annotations and tightened `docs/api/summary-coverage-baseline.json` to `0` missing summaries (`100.0%` coverage).
  - Extended `SqlServerCdcLocalDbContractTests` with `SqlServerContractFactAttribute` (`SWAPMID_SQLSERVER_TEST_CONNECTION` support alongside Windows LocalDB) and provisioned an `mcr.microsoft.com/mssql/server:2022-latest` CI step in `.github/workflows/ci.yml`.

### Changed & Fixed
- **Next.js `16.4.6` Security Upgrade (`web/`)**:
  - Upgraded `next` and `eslint-config-next` from `16.4.3` to `16.4.6`, resolving the `SecurityFatalError` build warning while maintaining all `security-dependency-contract` invariants.
- **Zero-404 Default Avatar Fallback & Public Password Policy Endpoint (`Account/AvatarController.cs`, `AccountSettingsController.cs`)**:
  - Returned `204 No Content` from `AvatarController.GetAsync` when a user has not uploaded a custom avatar so browser `<img>` elements fall back cleanly to Radix `<AvatarFallback>` initials with zero HTTP `404` network/console errors.
  - Added anonymous `GET /api/v1/account-management/password-policy` (`AccountPasswordPolicyDto` / `IAccountSettingsAppService.GetPasswordPolicyAsync`, exposing live password complexity rules without leaking `defaultPassword`), eliminating SSR and UI `404` errors across `/login`, `/profile`, `/account-management/users`, and the change-password dialog.
  - Added `[Authorize(SwapMidPermissions.AccountManagement.Users.Default)]` read-only option projections `GET /api/v1/account-users/employee-options` & `GET /api/v1/account-users/department-options` (`AccountUserController` / `AccountUserEmployeeBindingResolver`), resolving pre-existing `400 Bad Request` route collisions on `/account-management/users` while keeping Account/Enterprise permissions decoupled.

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
