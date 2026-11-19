# Streaming Lakehouse Blueprint (Redpanda + Schema Registry + Apache Iceberg v2 MoR + WAP + DuckDB)

Reproduce SwapMid's end-to-end **zero-JVM streaming lakehouse** workflow locally in under 60 seconds:
- **Redpanda v24.3.6** (Kafka + built-in Confluent Schema Registry for Avro / Protobuf / JSON)
- **MinIO** object storage storing native Apache Iceberg v2 (`metadata/v*.metadata.json`, binary Avro OCF manifests, Parquet `FieldId`, Equality Deletes, `day(commit_timestamp_utc)` hidden partitions)
- **Write-Audit-Publish (WAP) `staging` Branch** -> Zero-Infra Preview & Data Contract Gate -> Fast-Forward Promote to `main`
- **Official DuckDB `iceberg_scan()`** verification

## 1. Boot the Sandbox Stack

```bash
cp docker/.env.example docker/.env
docker compose \
  -f docker/docker-compose.yml \
  -f docker/docker-compose.streaming-lakehouse.yml \
  --env-file docker/.env up -d
```

- SwapMid Console: `http://localhost:13000`
- SwapMid API / OpenAPI: `http://localhost:18080/swagger`
- Redpanda Schema Registry: `http://localhost:18081/subjects`
- MinIO Console: `http://localhost:19001` (`swapmid_minio` / `SwapMid_Demo_Minio!2026`)

## 2. Apply the Declarative GitOps Bundle (`swapmidctl.mjs`)

```bash
export SWAPMID_API_URL=http://localhost:18080
export SWAPMID_TOKEN="<bearer-token>"
export DEMO_MINIO_SECRET="SwapMid_Demo_Minio!2026"

node scripts/swapmidctl.mjs diff \
  --file examples/streaming-lakehouse-iceberg/swapmid-bundle.yaml \
  --resolve-env-secrets

node scripts/swapmidctl.mjs apply \
  --file examples/streaming-lakehouse-iceberg/swapmid-bundle.yaml \
  --resolve-env-secrets \
  --execute
```

## 3. Audit `staging` and Fast-Forward Promote to `main`

1. Activate `orders-redpanda-iceberg-wap` in `/data-integration/cdc` (created in `Draft` by GitOps safety policy).
2. Produce Avro, Protobuf, or JSON Debezium events to Redpanda topic `dbserver1.shop.orders`.
3. Open **Time-Travel** on the CDC stream:
   - Notice `branch:staging` advances while `main` remains isolated.
   - Preview rows directly via Zero-Infra Parquet/MoR preview and run **Promote to main** (with optional audit tag `release-v1`).
4. Run official DuckDB `iceberg_scan()` against the published Iceberg v2 table:

```bash
docker compose \
  -f docker/docker-compose.yml \
  -f docker/docker-compose.streaming-lakehouse.yml \
  --env-file docker/.env \
  --profile query run --rm duckdb
```
