INSTALL httpfs; LOAD httpfs;
INSTALL iceberg; LOAD iceberg;

CREATE SECRET minio_demo (
  TYPE S3,
  KEY_ID 'swapmid_minio',
  SECRET 'SwapMid_Demo_Minio!2026',
  ENDPOINT 'minio:9000',
  URL_STYLE 'path',
  USE_SSL false,
  REGION 'us-east-1'
);

-- Query the current published Apache Iceberg v2 main branch via metadata/version-hint.text
SELECT event_id, op, business_key, commit_timestamp_utc, after_json
FROM iceberg_scan('s3://swapmid-lake/streaming/orders/iceberg_wap', allow_moved_paths = true)
ORDER BY business_key;
