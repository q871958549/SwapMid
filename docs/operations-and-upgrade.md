# SwapMid Operations & Upgrade Runbook (`v1.0 LTS`)

This runbook documents production security hardening, backup/restore procedures, zero-downtime upgrade choreography (Docker Compose & Kubernetes Helm), **Target Receipt v1** DBA reconciliation, and closed-loop observability for **SwapMid `v1.0 LTS`**.

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

SwapMid enforces backward-compatible additive schema migrations across `v1.x` releases:

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
