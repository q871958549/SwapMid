# SwapMid Reference Examples

Runnable end-to-end integration blueprints demonstrating SwapMid's three core engines:

| Directory | Engine(s) | Pattern |
| :--- | :--- | :--- |
| [`postgres-incremental-upsert/`](./postgres-incremental-upsert/) | `DataIntegration` | Atomic staging + PostgreSQL `ON CONFLICT` incremental upsert with watermark protection and Target Receipt v1 |
| [`postgres-pgoutput-cdc/`](./postgres-pgoutput-cdc/) | `DataIntegration` + `MessageBus` | PostgreSQL `wal_level=logical` (`pgoutput`) CDC stream with `ConfirmedFlushLsn` checkpoint advancement and CloudEvents fanout |
| [`s3-parquet-lakehouse/`](./s3-parquet-lakehouse/) | `DataIntegration` (`ObjectStorage`) | S3 / MinIO bounded-memory Apache Parquet writer with SigV4 signing, schema-on-write projection, and `RunId`-keyed paths |
| [`clickhouse-analytics-sink/`](./clickhouse-analytics-sink/) | `DataIntegration` (`ClickHouse`) | ClickHouse HTTP `JSONEachRow` batch writer with `system.columns` pre-flight compatibility checks and SSRF guards |
| [`kafka-event-stream-sink/`](./kafka-event-stream-sink/) | `DataIntegration` (`Kafka`) | Streaming batch producer with zstd/lz4 compression, TLS/SASL posture, and destination topic validation |
| [`cloudevents-webhook-fanout/`](./cloudevents-webhook-fanout/) | `DataIntegration` → `MessageBus` | Transactional Outbox terminal events delivered as CNCF CloudEvents v1.0 HTTP Webhooks with `X-SwapMid-Signature` verification |
| [`partner-api-gateway/`](./partner-api-gateway/) | `ApiManagement` + `MessageBus` | OpenIddict `client_credentials` token issuance, per-client route-group ACL + rate limit policy, and idempotent `/api/gateway/events/{topic}` ingress |
