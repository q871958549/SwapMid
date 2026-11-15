# Changelog

All notable changes to **SwapMid** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
