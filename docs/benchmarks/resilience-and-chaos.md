# Resilience, Crash-Recovery & Chaos Benchmark Matrix

This document summarizes the automated fault-injection guarantees and reproducible streaming SLA throughput benchmarks continuously verified across SwapMid's ETL `Target Receipt v1` coordinator, Apache Delta Lake (`_delta_log`), native Apache Iceberg v2 (`metadata/v*.metadata.json`), authenticated Push Webhook CDC ingress, and External Open Catalog integration.

All scenarios and throughput assertions run automatically on every commit in [CI](../../.github/workflows/ci.yml) (`dotnet test SwapMid.slnx`) without requiring manual intervention.

## 1. Fault-Injection & Chaos Verification Matrix

| Scenario | Fault Injected | Invariant / Protocol Exercised | Verified Outcome | Automated Proof |
| :--- | :--- | :--- | :--- | :--- |
| **1. Mid-flight Crash (`OutcomeUnknown`)** | Commit returns ambiguous timeout / process crash mid-flight | Per-target `TargetResourceGroup` quarantine fence + `RunId` idempotent replay + `__swapmid_etl_receipt_v1` verification | Concurrent runs on same target blocked; same `RunId` replays safely without re-executing; exact machine receipt converges state to `Succeeded` with zero duplicate writes (`RPO = 0`) | [ChaosResilienceContractTests](../../test/SwapMid.EntityFrameworkCore.Tests/DataIntegration/ChaosResilienceContractTests.cs) & [TargetReceiptVerificationTests](../../test/SwapMid.EntityFrameworkCore.Tests/DataIntegration/TargetReceiptVerificationTests.cs) |
| **2. Delta Lake `_delta_log` Concurrent CAS Race** | Competing writer claims `_delta_log/{v+1}.json` right after pre-check GET | Conditional PUT `If-None-Match: *` -> `412 PreconditionFailed` detection -> retry on next monotonic version + `swapmidRunId` crash replay | Advances atomically onto `v+2`; replaying the crashed run with identical `swapmidRunId` recognizes its own log entry and skips duplicate writes | [DeltaLakeCommitWriterTests](../../test/SwapMid.Application.Tests/DataIntegration/DeltaLakeCommitWriterTests.cs) & [ChaosResilienceContractTests](../../test/SwapMid.EntityFrameworkCore.Tests/DataIntegration/ChaosResilienceContractTests.cs) |
| **3. External Catalog `503` Outage During CDC** | Unity Catalog / Iceberg REST returns `HTTP 503` (including simulated secret echo) | Non-blocking best-effort `TryBestEffortSyncAsync` isolated from CDC WAL/Binlog checkpoint commit | CDC stream stays `Active`, `CheckpointLsn` & captured counts never roll back, secret strings never leak into `CatalogLastSyncStatus`, recovery succeeds later | [ChaosResilienceContractTests](../../test/SwapMid.EntityFrameworkCore.Tests/DataIntegration/ChaosResilienceContractTests.cs) & [ExternalLakehouseCatalogSyncTests](../../test/SwapMid.Application.Tests/DataIntegration/ExternalLakehouseCatalogSyncTests.cs) |
| **4. Apache Iceberg v2 Concurrent CAS Race (Binary Avro OCF)** | Competing writer claims `metadata/v2.metadata.json` right before conditional PUT | S3 `If-None-Match: *` -> `412 PreconditionFailed` retry -> `metadata/v3.metadata.json` + binary `.avro` manifest-list/manifest-file (`Obj\x01`) + `version-hint.text` + `swapmid-run-id` crash replay | Advances atomically to sequence `v3`; stale caller replay detects its own `swapmid-run-id` inside `snapshots[]` and converges without creating `v4` (`RPO = 0`) | [IcebergAvroManifestTests](../../test/SwapMid.Application.Tests/DataIntegration/IcebergAvroManifestTests.cs), [IcebergV2CommitAndReaderTests](../../test/SwapMid.Application.Tests/DataIntegration/IcebergV2CommitAndReaderTests.cs) & [ChaosResilienceContractTests](../../test/SwapMid.EntityFrameworkCore.Tests/DataIntegration/ChaosResilienceContractTests.cs) |
| **5. Push Webhook Forged HMAC, Dead-Letter Replay & Auto-Schema SLA** | Attacker sends forged `X-SwapMid-Signature` (`401`), valid caller pushes while stream is `Draft` (`409` dead-letter), then operator activates & clicks 1-click `Replay` | Fail-closed constant-time `FixedTimeEquals` before stream check (zero stored payload on `401`); bounded `<= 64 KiB` dead-letter ledger (`AppCdcWebhookDeliveries`); auto-schema inference -> `RequiredColumns` SLA + binary Avro Iceberg v2 commit | `401` rejected with `0` writes & `StoredPayloadJson = null`; `409` captured as replayable dead-letter; 1-click `Replay` commits Parquet (`FieldId 1..11`, `TIMESTAMP_MICROS`) + binary Avro Iceberg v2 `v1`, populates `SchemaBaselineJson`, achieves `Healthy` SLA, and frees stored payload (`ReplayCount = 1`) | [CdcWebhookIngressServiceTests](../../test/SwapMid.Application.Tests/DataIntegration/CdcWebhookIngressServiceTests.cs) & [ChaosResilienceContractTests](../../test/SwapMid.EntityFrameworkCore.Tests/DataIntegration/ChaosResilienceContractTests.cs) |
| **6. Time-Travel, Daily Hidden Partition Pruning & DuckDB `iceberg_scan()` Interop** | `OPTIMIZE` (Delta) / same-day partition-aware `REPLACE` (Iceberg v2 `day(commit_timestamp_utc)`) merges small Parquet files inside a single UTC day while leaving prior days untouched | Binary Apache Avro 1.11 OCF manifest chains (`snap-*.avro` `r508` little-endian `int32` partition bounds + `*-m0.avro` `r102` `commit_timestamp_utc_day`) + `schema.name-mapping.default` + Parquet `FieldId 1..11` + fixed SigV4 Hive-partition `=` URI signing + bounded Zero-Infra Parquet preview | Latest `v3` serves untouched `2026-11-18` + compacted `2026-11-19/compact_*.parquet` (`5` rows); official `duckdb/duckdb:latest` `iceberg_scan()` prunes `>= 2026-11-19` (`3` rows) vs `< 2026-11-19` (`2` rows) and Time-Travels `v1` (`4` rows) natively against MinIO | [IcebergAvroManifestTests](../../test/SwapMid.Application.Tests/DataIntegration/IcebergAvroManifestTests.cs), [KafkaDebeziumCdcTests](../../test/SwapMid.Application.Tests/DataIntegration/KafkaDebeziumCdcTests.cs) & [CdcLakehouseDataPreviewServiceTests](../../test/SwapMid.Application.Tests/DataIntegration/CdcLakehouseDataPreviewServiceTests.cs) |
| **7. SSRF Metadata Endpoint in Sink / Catalog Config** | Sink or Catalog `RestApi` points to `169.254.169.254` or un-allowlisted private IP | Fail-closed `DataIntegrationSinkEndpointPolicy` validation prior to `HttpClient.SendAsync` | Blocked with `BusinessException` before opening any outbound socket | [ExternalLakehouseCatalogSyncTests](../../test/SwapMid.Application.Tests/DataIntegration/ExternalLakehouseCatalogSyncTests.cs) |

## 2. Automated 1,000-Event Streaming SLA & Throughput Benchmarks

Executed by [ChaosSlaThroughputBenchmarkTests](../../test/SwapMid.Application.Tests/DataIntegration/ChaosSlaThroughputBenchmarkTests.cs) (`10` batches × `100` CDC rows = `1,000` rows, real `Parquet.Net` encoding with `FieldId 1..11` & `TIMESTAMP_MICROS` + S3 SigV4 signing + atomic CAS metadata commits + pure-.NET binary Apache Avro OCF manifest writing + out-of-order crash replay of batch #5 + mid-history Time-Travel replay + Zero-Infra Parquet preview + Data Contract SLA evaluation):

| Lakehouse Protocol | Workload | Total Elapsed | Throughput | Batch P50 | Batch P95 / P99 | Time-Travel Replay (`latest` + mid-version) | Out-of-Order Batch #5 Replay | Data Contract SLA | RPO |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | :--- | :--- | :---: |
| **Apache Delta Lake (`_delta_log`)** | 1,000 rows (10 × 100 Parquet) | `246.8 ms` | **`4,052 rows/s`** | `6.20 ms` | `171.25 ms` *(includes cold Parquet JIT)* | `13.25 ms` | Converged idempotently (`10` commits, `0` duplicates) | `Healthy` | **`0`** |
| **Apache Iceberg v2 (Binary Avro OCF)** | 1,000 rows (10 × 100 Parquet + `.avro` manifests) | `116.6 ms` | **`8,576 rows/s`** | `8.66 ms` | `31.27 ms` | `7.91 ms` | Converged idempotently (`10` snapshots, `0` duplicates) | `Healthy` | **`0`** |

> Measured on .NET 10.0.15 (x64, in-process S3 SigV4 handler verifying real Parquet serialization, SHA-256 hashing, SigV4 signing, conditional `If-None-Match: *` CAS semantics, and Parquet reader decoding). Live network latency adds S3/MinIO round-trip RTT per commit.

## 3. Reproducing Locally

```bash
# Run full chaos, Iceberg v2, Push Webhook, and 1,000-row SLA benchmark suites
dotnet test test/SwapMid.Application.Tests/SwapMid.Application.Tests.csproj \
  --filter "FullyQualifiedName~DeltaLake|FullyQualifiedName~IcebergV2|FullyQualifiedName~CdcWebhookIngress|FullyQualifiedName~ExternalLakehouseCatalogSync|FullyQualifiedName~ChaosSlaThroughputBenchmarkTests" \
  --logger "console;verbosity=detailed" --nologo

dotnet test test/SwapMid.EntityFrameworkCore.Tests/SwapMid.EntityFrameworkCore.Tests.csproj \
  --filter "FullyQualifiedName~ChaosResilienceContractTests" --nologo
```

## 4. Architecture & Static Boundary Enforcement

Beyond functional fault tests, SwapMid runs Roslyn AST architecture guards in CI:

- **Failure Boundary Guard** (`EtlFailureBoundaryGuardTests`): forbids leaking driver/HTTP exception messages or credential strings in any `catch` block across `DataIntegration` services; only `PipelineExecutionFailurePolicy.DescribeFailure` and redacted `SinkConfigContractHelpers` diagnostics are permitted.
- **File Length Guard** (`FileLengthGuardTests`): enforces `<= 500` lines per production `.cs` file to maintain modular separation of concerns.
- **Host Authorization Contract** (`DataIntegrationCrudAuthorizationContractTests`): verifies every public `DataIntegration` AppService entry enforces host-only isolation and exact ABP permission policies.
- **UI & BFF Storage Contract** (`browser-storage-policy-contract.test.ts` + `ui-health.mjs`): forbids OAuth token exposure or unallowlisted `localStorage` usage in frontend modules.
