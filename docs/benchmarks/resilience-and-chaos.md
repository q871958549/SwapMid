# Resilience, Crash-Recovery & Chaos Benchmark Matrix

This document summarizes the automated fault-injection guarantees continuously verified across SwapMid's ETL Target Receipt v1 coordinator, Delta Lake optimistic concurrency protocol, and CDC Open Catalog integration.

All scenarios run automatically on every commit in [CI](../../.github/workflows/ci.yml) (`dotnet test SwapMid.slnx`) without requiring manual intervention.

## 1. Fault-Injection & Chaos Verification Matrix

| Scenario | Fault Injected | Invariant / Protocol Exercised | Verified Outcome | Automated Proof |
| :--- | :--- | :--- | :--- | :--- |
| **Mid-flight Crash (`OutcomeUnknown`)** | Commit returns ambiguous timeout / process crash mid-flight | Per-target `TargetResourceGroup` quarantine fence + `RunId` idempotent replay + `__swapmid_etl_receipt_v1` verification | Concurrent runs on same target blocked; same `RunId` replays safely without re-executing; exact machine receipt converges state to `Succeeded` with zero duplicate writes | [ChaosResilienceContractTests](../../test/SwapMid.EntityFrameworkCore.Tests/DataIntegration/ChaosResilienceContractTests.cs) & [TargetReceiptVerificationTests](../../test/SwapMid.EntityFrameworkCore.Tests/DataIntegration/TargetReceiptVerificationTests.cs) |
| **Delta Lake `_delta_log` Concurrent CAS Race** | Competing writer claims `_delta_log/{v+1}.json` right after pre-check GET | Conditional PUT `If-None-Match: *` -> `412 PreconditionFailed` detection -> retry on next monotonic version + `swapmidRunId` crash replay | Advances atomically onto `v+2`; replaying the crashed run with identical `swapmidRunId` recognizes its own log entry and skips duplicate writes | [DeltaLakeCommitWriterTests](../../test/SwapMid.Application.Tests/DataIntegration/DeltaLakeCommitWriterTests.cs) & [ChaosResilienceContractTests](../../test/SwapMid.EntityFrameworkCore.Tests/DataIntegration/ChaosResilienceContractTests.cs) |
| **Delta Lake Time-Travel Across Compactions** | `OPTIMIZE` merges small Parquet files (`remove` + `add(dataChange=false)`) | Full replay of `add` / `remove` actions across `_delta_log/{v:D20}.json` (`VERSION AS OF v`) | Current version shows post-compaction files; querying historical `v1` accurately reconstructs pre-compaction files & exact row count | [DeltaLakeHistoryReaderTests](../../test/SwapMid.Application.Tests/DataIntegration/DeltaLakeHistoryReaderTests.cs) |
| **External Catalog `503` Outage During CDC** | Unity Catalog / Iceberg REST returns `HTTP 503` (including simulated secret echo) | Non-blocking best-effort `TryBestEffortSyncAsync` isolated from CDC WAL/Binlog checkpoint commit | CDC stream stays `Active`, `CheckpointLsn` & captured counts never roll back, secret strings never leak into `CatalogLastSyncStatus`, recovery succeeds later | [ChaosResilienceContractTests](../../test/SwapMid.EntityFrameworkCore.Tests/DataIntegration/ChaosResilienceContractTests.cs) & [ExternalLakehouseCatalogSyncTests](../../test/SwapMid.Application.Tests/DataIntegration/ExternalLakehouseCatalogSyncTests.cs) |
| **SSRF Metadata Endpoint in Catalog REST Config** | Catalog `RestApi` points to `169.254.169.254` or un-allowlisted private IP | Fail-closed `DataIntegrationSinkEndpointPolicy` validation prior to `HttpClient.SendAsync` | Blocked with `BusinessException` before opening any outbound socket | [ExternalLakehouseCatalogSyncTests](../../test/SwapMid.Application.Tests/DataIntegration/ExternalLakehouseCatalogSyncTests.cs) |

## 2. Reproducing Locally

```bash
# Run full chaos, contract, and unit suites
dotnet test test/SwapMid.Application.Tests/SwapMid.Application.Tests.csproj --filter "FullyQualifiedName~DeltaLake|FullyQualifiedName~ExternalLakehouseCatalogSync" --nologo
dotnet test test/SwapMid.EntityFrameworkCore.Tests/SwapMid.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~ChaosResilienceContractTests" --nologo
```

## 3. Architecture & Static Boundary Enforcement

Beyond functional fault tests, SwapMid runs Roslyn AST architecture guards in CI:

- **Failure Boundary Guard** (`EtlFailureBoundaryGuardTests`): forbids leaking driver/HTTP exception messages or credential strings in any `catch` block across `DataIntegration` services; only `PipelineExecutionFailurePolicy.DescribeFailure` and redacted `SinkConfigContractHelpers` diagnostics are permitted.
- **File Length Guard** (`FileLengthGuardTests`): enforces `<= 500` lines per production `.cs` file to maintain modular separation of concerns.
- **UI & BFF Storage Contract** (`browser-storage-policy-contract.test.ts` + `ui-health.mjs`): forbids OAuth token exposure or unallowlisted `localStorage` usage in frontend modules.
