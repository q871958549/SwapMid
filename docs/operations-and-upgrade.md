# SwapMid Operations & Upgrade Runbook (`v1.0 LTS` → `v2.0+`)

This runbook documents production security hardening, backup/restore procedures, zero-downtime upgrade choreography (Docker Compose & Kubernetes Helm), **Target Receipt v1** DBA reconciliation, heterogeneous CDC (`pgoutput`, SQL Server Change Tracking, MySQL Binlog), Apache Delta Lake (`_delta_log`), and closed-loop observability for **SwapMid `v1.0 LTS` through `v2.0+`**.

---

## 1. Pre-Production Security Checklist

Before exposing SwapMid to corporate networks or partner traffic, verify all seven controls:

1. **Rotate Seeded Admin Credentials**:
   - Sign in as `admin` immediately after initial `DbMigrator` bootstrap and change the default password (`1q2w3E*`), or disable password login in favor of External OIDC SSO.
2. **Unique Cryptographic Secrets**:
   - Replace `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD`, `OPENIDDICT_PFX_PASS`, `STRING_ENCRYPTION_PASSPHRASE` (`StringEncryption:DefaultPassPhrase` encrypts stored data-source credentials at rest), and `NEXT_SERVER_ACTIONS_ENCRYPTION_KEY` (32-byte base64 key shared across all `web` replicas).
3. **Strict TLS & Cookie Hardening**:
   - Serve both Web BFF and API Host behind TLS 1.2+/1.3 termination.
   - Set `REQUIRE_HTTPS_METADATA=true` (`AuthServer:RequireHttpsMetadata=true`) and `COOKIE_SECURE=true`.
4. **Fail-Closed Machine Surfaces**:
   - Keep `API_GATEWAY_ENABLED=false` unless partner API forwarding is actively used; when enabled, configure explicit `API_GATEWAY_ALLOWED_HOSTS` (DNS-rebinding-safe SSRF allowlist) and per-client ACL + rate-limit policies (`PUT /api/v1/third-party-clients/{clientId}/policy`).
   - Keep `MCP_ENABLED=false` unless controlled AI operator tooling (`POST /api/mcp`) is explicitly required.
5. **Outbound Webhook & External Sink SSRF Allowlists**:
   - Restrict `MessageBus:HttpDelivery:AllowedHosts`, `NotificationCenter:Webhook:AllowedHosts`, and `DataIntegration:Sinks:AllowedHosts` (ClickHouse / S3 / MinIO / Kafka) to approved receiver domains; never allow RFC1918 or link-local metadata CIDR ranges unless explicitly scoped in `AllowedPrivateNetworks`.
6. **Least-Privilege External Database Accounts**:
   - Provision read-only accounts for ETL source connectors (`SELECT` only).
   - For **Target Receipt v1** targets, grant only table DML + receipt table `SELECT, INSERT` (never `CONTROL`, `ALTER`, or `DELETE` on `__swapmid_etl_receipt_v1`).
7. **Alertmanager Webhook Token**:
   - Set a high-entropy `NotificationCenter:AlertWebhookToken` (`X-SwapMid-Alert-Token` or `?token=...`) so unauthenticated callers receive `401 Unauthorized`.

---

## 2. Backup & Disaster Recovery

### What to Back Up
1. **PostgreSQL (`swapmid` database)** — authoritative system of record for organization tree, data sources (AES-encrypted `Config`), pipelines, execution fences, message topics/subscriptions/records, Hangfire schedules, OpenIddict clients, and audit logs.
   ```bash
   pg_dump -Fc -h <pg-host> -U swapmid -d swapmid -f swapmid-$(date +%Y%m%d-%H%M).dump
   ```
2. **Encryption Passphrase & Signing Certificate**:
   - Store `STRING_ENCRYPTION_PASSPHRASE` and the OpenIddict signing certificate (`.pfx` + passphrase) in an external KMS / Vault. Without `STRING_ENCRYPTION_PASSPHRASE`, encrypted data-source secrets in `SwapMidDataSources` cannot be decrypted after restore.
3. **Redis**:
   - Ephemeral BFF session store and distributed single-flight/coordination leases. If lost, active browser sessions gracefully re-authenticate (`307 -> /login`); no durable integration state is lost.

---

## 3. Zero-Downtime Upgrade Procedure

SwapMid enforces backward-compatible additive schema migrations across `v1.x` and `v2.x` releases:

### Kubernetes (Helm `charts/swapmid/`)
1. Review release notes in [`CHANGELOG.md`](../CHANGELOG.md).
2. Run Helm upgrade:
   ```bash
   helm upgrade swapmid ./charts/swapmid \
     --namespace swapmid \
     -f prod-values.yaml \
     --wait --timeout 10m
   ```
   - The `migrator` Job runs automatically as a `pre-install,pre-upgrade` Helm hook (`weight: -5`) before `api` or `web` pods roll.
   - `PodDisruptionBudget` (`minAvailable: 1`) and `/health/ready` + `/api/ready` readiness gates ensure zero dropped requests during rolling pod replacement.

### Docker Compose (`docker/docker-compose.yml`)
1. Update `IMAGE_TAG` in `docker/.env`.
2. Run migrations first, then roll `api` and `web`:
   ```bash
   docker compose -f docker/docker-compose.yml --env-file docker/.env run --rm migrator
   docker compose -f docker/docker-compose.yml --env-file docker/.env up -d --no-deps api web
   ```

---

## 4. Target Receipt v1 & `OutcomeUnknown` Runbook

When a network partition or host crash interrupts an ETL run **after** target transaction commit begins, SwapMid transitions the execution to `OutcomeUnknown` (`status = 5`) and **quarantines** the pipeline against automatic or manual retries to prevent silent duplicate writes:

1. **For `*-receipt-v1` Pipelines (`append-receipt-v1`, `truncate-receipt-v1`, `upsert-receipt-v1`)**:
   - Provision DDL scripts live in `scripts/data-integration/target-receipt-v1/{postgresql,sqlserver}.sql`.
   - Open **Data Integration → Execution History**, inspect the quarantined `RunId`, and trigger **Verify Target Receipt** (`POST /api/v1/pipeline-executions/{id}/verify-target-receipt`).
   - SwapMid queries `__swapmid_etl_receipt_v1` using cryptographic `coordination_hash` verification to prove whether the atomic batch committed (`Succeeded`) or rolled back (`Failed`), automatically lifting quarantine once converged.
2. **For Legacy Plain Modes (`append`, `truncate`, `upsert` without receipt)**:
   - Have a DBA inspect target table row counts / watermarks against the immutable execution snapshot (`TargetSnapshot`), then record the operator resolution via **Resolve Outcome** (`POST /api/v1/pipeline-executions/{id}/resolve-outcome`).

---

## 5. External OIDC Identity Federation

To enable enterprise SSO (Keycloak, Microsoft Entra ID, Okta, Casdoor, GitLab):

1. Register a Confidential OIDC Client in your IdP with Redirect URI:
   `https://<your-web-domain>/api/auth/oidc/callback`
2. Set environment variables on `api` (or `externalOidc.*` + `secret.externalOidcClientSecret` in Helm):
   ```env
   EXTERNAL_OIDC_ENABLED=true
   EXTERNAL_OIDC_DISPLAY_NAME="Enterprise SSO"
   EXTERNAL_OIDC_AUTHORITY="https://idp.example.com/realms/corp"
   EXTERNAL_OIDC_CLIENT_ID="swapmid-console"
   EXTERNAL_OIDC_CLIENT_SECRET="<confidential-secret>"
   EXTERNAL_OIDC_REQUIRE_HTTPS=true
   ```
3. Next.js BFF automatically renders **Sign in with Enterprise SSO** on `/login`, performs S256 PKCE + HMAC-sealed `state` validation, exchanges the authorization code server-to-server via OpenIddict `grant_type=external_oidc`, and stores tokens strictly in Redis.

---

## 6. Turnkey Observability & Closed-Loop Alerting

Launch the bundled Prometheus + Alertmanager + Grafana stack alongside Docker Compose:

```bash
GRAFANA_ADMIN_PASSWORD="ChangeMe_Grafana!2026" \
  docker compose \
  -f docker/docker-compose.yml \
  -f docker/docker-compose.observability.yml \
  --env-file docker/.env up -d
```

- **Prometheus** (`:19090`): Scrapes `api:8080/metrics` and evaluates `docker/observability/prometheus/alert-rules.yml`.
- **Alertmanager** (`:19093`): Routes firing/resolved alerts back into SwapMid's `/api/v1/notification/webhooks/alertmanager`, dispatching multi-channel notifications (SMTP Email / Slack / Feishu / DingTalk / WeCom).
- **Grafana** (`:13300`): Auto-provisions the **SwapMid — Three Integration Engines Overview** dashboard (`docker/observability/grafana/dashboards/swapmid-overview.json`).

## 7. PostgreSQL Logical Replication (`pgoutput`) CDC Operations (`v1.3.0+`)

1. **PostgreSQL Server Configuration**:
   - Enable `wal_level = logical`, `max_replication_slots >= 4`, and `max_wal_senders >= 4` (restart PostgreSQL when changing `wal_level`).
   - Grant `REPLICATION` (or DBA-precreated Publication + Slot ownership) and `SELECT` on monitored tables to the SwapMid DataSource user.
2. **Pre-flight & Least-Privilege Provisioning**:
   - Use **Pre-flight** (`/data-integration/cdc` or `POST /api/v1/cdc-streams/{id}/preflight`) to verify `wal_level = logical`, Publication/Slot existence, per-table `REPLICA IDENTITY` mode (`full` / `index` / `default` / `nothing`), retained WAL lag (`walLagBytes`), and current WAL position before activation.
   - Disable `autoCreatePublicationAndSlot` in locked-down environments after DBA pre-creation (`CREATE PUBLICATION ... FOR TABLE ...` and `SELECT pg_create_logical_replication_slot(..., 'pgoutput')`).
3. **Replication Slot Retention & Disk Monitoring**:
   - Monitor PostgreSQL inactive replication slots (`SELECT slot_name, active, confirmed_flush_lsn, pg_size_pretty(pg_wal_lsn_diff(pg_current_wal_lsn(), restart_lsn)) AS retained_wal FROM pg_replication_slots;`) alongside Prometheus metrics (`swapmid_cdc_captures_total`, `swapmid_cdc_wal_lag_bytes`, `swapmid_cdc_deliveries_total`), alert rules (`SwapMidCdcCaptureFailures`, `SwapMidCdcWalLagHigh`), and System Health item **PostgreSQL CDC 捕获失败率**.
   - Always **Pause** retired CDC streams and run **Drop Slot** (`POST /api/v1/cdc-streams/{id}/drop-slot` or row menu **释放槽位**) to release unused upstream PostgreSQL replication slots so retained WAL cannot exhaust disk space.
4. **External Sink Key Ordering, Initial Snapshot & ClickHouse DDL Provisioning (`v1.5.0+` / `v1.6.0+`)**:
   - When routing CDC changes to Kafka (`CdcTargetMode.ExternalSink = 2`), configure comma-separated **Partition / Sort Key Columns** (`targetSinkKeyColumns`, e.g. `order_id`) so events sharing an entity primary key deterministically land on the same Kafka partition (`schema.table:k1=v1|...`).
   - For ClickHouse (`ReplacingMergeTree` when keys are configured, otherwise `MergeTree`) and analytical sinks, open row menu **查看模板** / **Sink Template** (`GET /api/v1/cdc-streams/{id}/sink-template`) to check live target table readiness, copy the recommended DDL, or click **一键建表** (`POST /api/v1/cdc-streams/{id}/provision-sink`) to initialize the table automatically.
   - To include existing historical table rows before streaming WAL changes, enable **Initial Snapshot** (`initialSnapshotEnabled: true`) on stream creation (uses PostgreSQL `EXPORT_SNAPSHOT` + `REPEATABLE READ` at slot creation) or trigger **全量快照** (`POST /api/v1/cdc-streams/{id}/snapshot`).
5. **Relational Table Mirror, Field Mapping, Soft-Delete & Lakehouse Manifests (`v1.7.0+` / `v1.8.0+`)**:
   - Use `CdcTargetMode.RelationalTable = 3` (with single monitored source table + target `RelationalDatabase` `targetSinkId` + target table `targetSinkDestination` + business `targetSinkKeyColumns` + `targetDeletePolicy`) to materialize `EXPORT_SNAPSHOT` + WAL `INSERT`/`UPDATE`/`DELETE` batches directly into PostgreSQL, SQL Server, or MySQL target tables via atomic staging `Upsert` + `DELETE` under database writer leases. Use `targetFieldMappings` (`POST /api/v1/cdc-streams/inspect-mirror-schema` / **探测映射**) to remap or subset columns, and select `targetDeletePolicy = 2` (`SoftDelete`) with `softDeleteColumn` (e.g. `is_deleted`) and optional nullable `softDeleteTimestampColumn` (e.g. `deleted_at_utc`) to retain soft-deleted rows and clear tombstones automatically on re-insertion.
   - For `CdcTargetMode.ExternalSink = 2` targeting `ObjectStorage` (`S3` / `MinIO`), SwapMid writes immutable per-batch Lakehouse manifests to `{prefix}/{dest}/_swapmid/manifests/{lsnToken}_{runId}.json` alongside a bounded sliding checkpoint index (`_swapmid/latest.json`, max 100 files), increments `swapmid_cdc_object_storage_manifests_total`, and exposes live checkpoint status in **查看模板** / **Sink Template**.
   - Configure `schemaDriftPolicy` (`Ignore = 0`, `Warn = 1`, `PauseAndAlert = 2`). Under `PauseAndAlert`, upstream `ALTER TABLE` column/type changes halt capture before advancing `ConfirmedFlushLsn`, pause the stream + Hangfire schedule, raise `SwapMid:ETL:036` + `NotificationService` alerts, and emit `swapmid_cdc_schema_drift_events_total` (`SwapMidCdcSchemaDriftDetected`). Once downstream schemas are aligned, run **确认Schema** / `POST /api/v1/cdc-streams/{id}/accept-schema` to lock the new schema baseline and send a correlated recovery notice before re-activating.
6. **SQL Server Change Tracking CDC & Lakehouse Small-File Compaction (`v1.9.0+`)**:
   - Create `sourceType = 1` (`SqlServerChangeTracking`) CDC streams against SQL Server databases with database-level (`ALTER DATABASE ... SET CHANGE_TRACKING = ON`) and table-level (`ALTER TABLE ... ENABLE CHANGE_TRACKING`) tracking enabled on primary-keyed tables (optionally enabling `ALLOW_SNAPSHOT_ISOLATION ON` for lock-free baseline snapshots). No PostgreSQL `Publication` or `Replication Slot` is required. SwapMid validates `CHANGE_TRACKING_MIN_VALID_VERSION` on every capture and alerts operators via `CdcReplicationPreconditionFailed` if checkpoint retention expires.
   - For `CdcTargetMode.ExternalSink = 2` targeting `ObjectStorage` (`S3` / `MinIO`), set optional `lakehouseCompactMinFiles` (`2..100`) to automatically consolidate small Parquet/JSONL objects into `compact_{runId}.*` files and clean up superseded fragments, or trigger manual compaction via **查看模板 -> 合并分片** (`POST /api/v1/cdc-streams/{id}/compact-lakehouse`). Monitor `swapmid_cdc_lakehouse_compactions_total` and copy ready-to-run DuckDB / ClickHouse `s3()` / Spark SQL queries directly from `LakehouseQuerySql` inside **Sink Template**.
7. **MySQL / MariaDB Row-Based Binlog CDC & Apache Delta Lake `_delta_log` Protocol (`v2.0.0+`)**:
   - Configure the upstream MySQL 8.x / MariaDB server with `--server-id>=1 --log-bin=mysql-bin --binlog-format=ROW --binlog-row-image=FULL`. Grant the SwapMid DataSource account `REPLICATION SLAVE`, `REPLICATION CLIENT` (or `BINLOG MONITOR` on MariaDB), and `SELECT` on monitored tables (plus `RELOAD` / `LOCK TABLES` for `FLUSH TABLES WITH READ LOCK` consistent baseline snapshots; SwapMid automatically falls back to `LOCK TABLES ... READ` when `RELOAD` is withheld).
   - Create `sourceType = 2` (`MySqlBinlog`) streams pointing at `{database}.{table}` names (no Publication/Slot required). Pre-flight validates binary log settings, purged-binlog watermark reachability, per-table primary keys, and byte-level lag (`walLagBytes`).
   - For Apache Parquet object-storage Lakehouse targets (`CdcTargetMode.ExternalSink = 2`, `ObjectStorage`), set `lakehouseTableFormat = 1` (`DeltaLake`) to write standard Delta Protocol v1/v2 transaction log commits under `{prefix}/{dest}/_delta_log/{version:D20}.json` (`commitInfo`, `protocol`, `metaData`, `add`, and atomic `OPTIMIZE` compaction `remove` + `add` actions) protected by S3 `If-None-Match: *` optimistic concurrency control and idempotent `swapmidRunId` replay. Inspect live version badges (`Delta Lake vN`), `_delta_log/` directories, and executable DuckDB (`delta_scan('s3://...')`) / Apache Spark (`format("delta")`) / Polars (`pl.scan_delta(...)`) queries inside **Sink Template**.
