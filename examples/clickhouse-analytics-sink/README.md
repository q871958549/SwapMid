# ClickHouse Analytics Sink

This example provisions a **ClickHouse (`DataSourceType = 4`)** analytical sink over HTTP `JSONEachRow` with `system.columns` pre-flight schema verification.

## Security Prerequisite

Explicitly allowlist the ClickHouse HTTP endpoint host when connecting to internal cluster services:

```bash
DataIntegration__Sinks__AllowedHosts=clickhouse
DataIntegration__Sinks__AllowedPrivateNetworks=10.0.0.0/8,172.16.0.0/12,192.168.0.0/16
```

## Target DDL & Run Steps

1. Create the analytics table in ClickHouse:

```sql
CREATE TABLE IF NOT EXISTS analytics.fact_orders (
  order_id Int64,
  order_no String,
  total_amount Decimal(18, 2),
  order_status String,
  ordered_at_utc DateTime64(3, 'UTC')
) ENGINE = MergeTree() ORDER BY order_id;
```

2. Create the ClickHouse data source (`POST /api/v1/data-sources`) from [`datasource.json`](./datasource.json).
3. Create the pipeline from [`pipeline.json`](./pipeline.json), run **Schema Pre-flight**, activate the pipeline from the table menu, and execute.
