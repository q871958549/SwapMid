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
2. Create the CDC stream in `/data-integration/cdc` (or `POST /api/v1/cdc-streams`) using [`cdc-stream.json`](./cdc-stream.json) with the resolved `sourceId` (or set `targetMode: 2` with `targetSinkId`, `targetSinkDestination`, and optional `targetSinkKeyColumns` such as `"order_id"` to stream ordered CDC batches straight into `Kafka`, `ClickHouse`, or `S3/MinIO` Parquet).
3. For `targetMode: 2`, open row action **Sink Template** (`GET /api/v1/cdc-streams/{id}/sink-template`) to inspect the CDC envelope column types, partition key expression, sample payload JSON, and keyed ClickHouse `ReplacingMergeTree` DDL (or click **Create Table** / `POST /api/v1/cdc-streams/{id}/provision-sink`) before starting capture.
4. Click **Pre-flight** (`POST /api/v1/cdc-streams/{id}/preflight`) to inspect `wal_level`, Publication/Slot readiness, retained `walLagBytes`, live upstream schema drift status, and per-table `REPLICA IDENTITY` modes, and automatically provision `swapmid_orders_pub` / `swapmid_orders_slot` (or enable `initialSnapshotEnabled: true` / run **Initial Snapshot** via `POST /api/v1/cdc-streams/{id}/snapshot` to export baseline rows before WAL streaming).
5. You can also set `targetMode: 3` (**Relational Table Mirror**) targeting a relational `DataSource` (`targetSinkId`), destination table (`targetSinkDestination`, e.g. `public.etl_demo_orders_mirror`), required business `targetSinkKeyColumns`, and `targetDeletePolicy` (`0 = DeleteRow`, `1 = Ignore`) to materialize `INSERT`/`UPDATE`/`DELETE` row-for-row into PostgreSQL / SQL Server / MySQL tables.
6. Set `schemaDriftPolicy: 2` (`PauseAndAlert`) to freeze the monitored table's catalog fingerprint (`pg_attribute`) on activation/first capture and automatically pause the stream + alert operators if upstream DDL changes occur; click **Accept Schema** (`POST /api/v1/cdc-streams/{id}/accept-schema`) after updating downstream targets to confirm the new baseline.
7. Click **Activate** (`PUT /api/v1/cdc-streams/{id}/activate`) to register periodic Hangfire capture, or click **Capture Now** (`POST /api/v1/cdc-streams/{id}/capture`) to process WAL batches immediately and inspect CloudEvents deliveries under `/message-bus/records` (supporting multi-select set-based replay via `POST /api/v1/message-records/batch-replay`), relational mirror tables, or written Parquet objects / Kafka messages / ClickHouse rows. When decommissioning or pausing long-term, use **Drop Slot** (`POST /api/v1/cdc-streams/{id}/drop-slot`) to safely remove idle upstream replication slots and release retained WAL.
