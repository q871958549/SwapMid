# PostgreSQL Logical Replication (`pgoutput`) CDC

This example provisions a **PostgreSQL Logical Replication (`pgoutput`) CDC Stream** that captures `INSERT` / `UPDATE` / `DELETE` WAL changes from PostgreSQL using `NpgsqlLogicalReplicationConnection`, publishes CloudEvents v1.0 events (`data`, `old`, and `changedColumns` diff array) to SwapMid's Message Bus (`integration.cdc.events`) or streams change batches directly to external analytical/event sinks (`Kafka`, `ClickHouse`, or `S3/MinIO` Parquet via `CdcTargetMode = 2`), and advances the replication slot `ConfirmedFlushLsn` only after downstream delivery succeeds.

## PostgreSQL Prerequisites

Configure the source PostgreSQL instance with logical replication enabled and a role granted `REPLICATION` (or superuser) privilege plus `SELECT` on monitored tables:

```sql
ALTER SYSTEM SET wal_level = logical;
ALTER SYSTEM SET max_replication_slots = 4;
ALTER SYSTEM SET max_wal_senders = 4;
-- Restart PostgreSQL for wal_level=logical to take effect.
SHOW wal_level; -- Expected: logical
```

Ensure monitored tables have an appropriate `REPLICA IDENTITY` (`DEFAULT` for primary-keyed insert/new-row capture, or `FULL` via `ALTER TABLE public.etl_demo_source_orders REPLICA IDENTITY FULL;` to capture full `old` tuples and `changedColumns` diffs on `UPDATE`/`DELETE`).

## Steps

1. Verify or create the PostgreSQL source (`DataSourceType = 0`, provider `PostgreSql`) using [`datasource.json`](./datasource.json), and click **Test Connection**.
2. Create the CDC stream in `/data-integration/cdc` (or `POST /api/v1/cdc-streams`) using [`cdc-stream.json`](./cdc-stream.json) with the resolved `sourceId` (or set `targetMode: 2` with `targetSinkId` / `targetSinkDestination` to stream CDC batches straight into `Kafka`, `ClickHouse`, or `S3/MinIO` Parquet).
3. Click **Pre-flight** (`POST /api/v1/cdc-streams/{id}/preflight`) to inspect `wal_level`, Publication/Slot readiness, retained `walLagBytes`, and per-table `REPLICA IDENTITY` modes, and automatically provision `swapmid_orders_pub` / `swapmid_orders_slot` (or create them manually beforehand when least-privilege mode is preferred).
4. Click **Activate** (`PUT /api/v1/cdc-streams/{id}/activate`) to register periodic Hangfire capture, or click **Capture Now** (`POST /api/v1/cdc-streams/{id}/capture`) to process WAL batches immediately and inspect CloudEvents deliveries under `/message-bus/records` (or written Parquet objects / Kafka messages / ClickHouse rows). When decommissioning or pausing long-term, use **Drop Slot** (`POST /api/v1/cdc-streams/{id}/drop-slot`) to safely remove idle upstream replication slots and release retained WAL.
