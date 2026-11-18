# MySQL Binlog CDC -> Apache Delta Lake (`_delta_log`)

This example provisions a **MySQL / MariaDB Row-Based Binlog CDC Stream (`CdcSourceType = 2`)** streaming consistent baseline snapshots and transactional `INSERT` / `UPDATE` / `DELETE` row events into an **Object Storage (`Parquet`)** sink backed by native **Apache Delta Lake (`LakehouseTableFormat = 1`)** `_delta_log/` transaction logs and automatic small-file `OPTIMIZE` compaction.

## Server & Security Prerequisites

Configure the upstream MySQL 8.x server with row-based full-image binary logging:

```ini
[mysqld]
server-id = 1
log-bin = mysql-bin
binlog-format = ROW
binlog-row-image = FULL
```

Grant the source account `REPLICATION SLAVE`, `REPLICATION CLIENT`, and `SELECT` on monitored tables (plus `RELOAD` for `FLUSH TABLES WITH READ LOCK` consistent snapshot, or `LOCK TABLES` as automatic fallback), and allowlist your MySQL/MinIO hosts in SwapMid's fail-closed SSRF boundary:

```bash
DataIntegration__Sinks__AllowedHosts=mysql,minio
DataIntegration__Sinks__AllowedPrivateNetworks=10.0.0.0/8,172.16.0.0/12,192.168.0.0/16
```

## Steps

1. Create the MySQL data source (`datasource-mysql.json`) and MinIO Object Storage Parquet sink (`sink-minio.json`) in `/data-integration/sources` (or via `POST /api/v1/data-sources`), then click **Test Connection** on both.
2. Create the CDC stream in `/data-integration/cdc` (or `POST /api/v1/cdc-streams`) using `cdc-stream.json` (`sourceType: 2`, `lakehouseTableFormat: 1`, `lakehouseCompactMinFiles: 2`).
3. Run **Pre-flight** (`POST /api/v1/cdc-streams/{id}/preflight`) to verify `binlog_format=ROW`, `binlog_row_image=FULL`, primary keys, and current binlog file/position.
4. Trigger **Initial Snapshot** (`POST /api/v1/cdc-streams/{id}/snapshot`) or **Activate** (`PUT /api/v1/cdc-streams/{id}/activate`): SwapMid writes the initial Parquet part file and commits `_delta_log/00000000000000000000.json` (`protocol` v1/v2, `metaData`, `add`, `commitInfo`) via S3 `If-None-Match: *`.
5. Mutate rows in `swapmid_cdc.orders`; subsequent incremental binlog batches commit `00000000000000000001.json` (`STREAMING UPDATE`) and automatically trigger `OPTIMIZE` compaction (`remove` + `add` with `dataChange: false`) once uncompacted files reach `lakehouseCompactMinFiles`.
6. Open **Sink Template** (`GET /api/v1/cdc-streams/{id}/sink-template`) to inspect live `Delta Lake vN` version badges, `_delta_log` paths, and copy executable **DuckDB (`delta_scan`) / Apache Spark / Polars** SQL snippets.
