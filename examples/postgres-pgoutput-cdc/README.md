# PostgreSQL Logical Replication (`pgoutput`) CDC

This example provisions a **PostgreSQL Logical Replication (`pgoutput`) CDC Stream** that captures `INSERT` / `UPDATE` / `DELETE` WAL changes from PostgreSQL using `NpgsqlLogicalReplicationConnection`, publishes CloudEvents v1.0 events to SwapMid's Message Bus (`integration.cdc.events`), and advances the replication slot `ConfirmedFlushLsn` only after downstream delivery succeeds.

## PostgreSQL Prerequisites

Configure the source PostgreSQL instance with logical replication enabled and a role granted `REPLICATION` (or superuser) privilege plus `SELECT` on monitored tables:

```sql
ALTER SYSTEM SET wal_level = logical;
ALTER SYSTEM SET max_replication_slots = 4;
ALTER SYSTEM SET max_wal_senders = 4;
-- Restart PostgreSQL for wal_level=logical to take effect.
SHOW wal_level; -- Expected: logical
```

Ensure monitored tables have an appropriate `REPLICA IDENTITY` (`DEFAULT` for primary-keyed tables, or `FULL` to emit full old tuples on `UPDATE`/`DELETE`).

## Steps

1. Verify or create the PostgreSQL source (`DataSourceType = 0`, provider `PostgreSql`) using [`datasource.json`](./datasource.json), and click **Test Connection**.
2. Create the CDC stream in `/data-integration/cdc` (or `POST /api/v1/cdc-streams`) using [`cdc-stream.json`](./cdc-stream.json) with the resolved `sourceId`.
3. Click **Pre-flight** (`POST /api/v1/cdc-streams/{id}/preflight`) to confirm `wal_level = logical` and automatically provision `swapmid_orders_pub` / `swapmid_orders_slot` (or create them manually beforehand when least-privilege mode is preferred).
4. Click **Activate** (`PUT /api/v1/cdc-streams/{id}/activate`) to register periodic Hangfire capture, or click **Capture Now** (`POST /api/v1/cdc-streams/{id}/capture`) to process WAL batches immediately and inspect CloudEvents deliveries under `/message-bus/records`.
