# S3 / MinIO Apache Parquet Lakehouse Sink

This example provisions an **Object Storage (`DataSourceType = 5`)** sink writing schema-on-write Apache Parquet files with deterministic `RunId`-keyed object paths (`<prefix>/<destination>/run_<runId>.parquet`).

## Security Prerequisite

Because SwapMid enforces fail-closed SSRF boundaries on external analytical sinks, explicitly allowlist the MinIO/S3 endpoint host (and private network CIDR when self-hosted inside Docker or VPC):

```bash
DataIntegration__Sinks__AllowedHosts=minio
DataIntegration__Sinks__AllowedPrivateNetworks=10.0.0.0/8,172.16.0.0/12,192.168.0.0/16
```

## Steps

1. Create the object storage data source (`POST /api/v1/data-sources` or via `/data-integration/sources` UI) using [`datasource.json`](./datasource.json), and click **Test Connection**.
2. Create the pipeline using [`pipeline.json`](./pipeline.json), run **Schema Pre-flight** (`POST /api/v1/data-pipelines/preflight-schema`) to verify projected Parquet columns, activate the pipeline via row action **Activate**, and trigger execution.
