# PostgreSQL Incremental Upsert + Target Receipt v1

This example configures an idempotent incremental synchronization pipeline using `mode: "upsert"` and `reconciliationStrategy: "target-receipt-v1"`.

## Prerequisites

1. Provision `__swapmid_etl_receipt_v1` on the target PostgreSQL database using `scripts/data-integration/target-receipt-v1/postgresql.sql`.
2. Ensure the target table defines a `NOT NULL` primary key or non-partial unique index over `order_id`.
3. Run `POST /api/v1/data-pipelines/preflight-schema` (or click **Schema Pre-flight** in the pipeline dialog) before activating scheduling.
