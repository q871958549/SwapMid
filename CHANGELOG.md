# Changelog

All notable changes to **SwapMid** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
