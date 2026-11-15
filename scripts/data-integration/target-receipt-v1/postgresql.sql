/*
  Target Receipt v1 / PostgreSQL 13+

  由目标库 DBA 在停用对应 ETL 管道后执行。应用只会 SELECT/INSERT，绝不会
  CREATE/ALTER/UPDATE/DELETE 这些固定对象。脚本故意不使用 IF NOT EXISTS：已存在对象
  必须先由 DBA 对照契约核验，不能把结构漂移静默当成部署成功。
*/
BEGIN;

CREATE SCHEMA IF NOT EXISTS swapmid_etl;

DO $$
BEGIN
    IF to_regclass('swapmid_etl.target_resource_v1') IS NOT NULL
       OR to_regclass('swapmid_etl.execution_receipt_v1') IS NOT NULL THEN
        RAISE EXCEPTION 'Target Receipt v1 objects already exist; verify instead of overwriting.';
    END IF;
END
$$;

CREATE TABLE swapmid_etl.target_resource_v1
(
    singleton_id smallint NOT NULL,
    protocol_version smallint NOT NULL,
    resource_instance_id uuid NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT pk_swapmid_etl_target_resource_v1
        PRIMARY KEY (singleton_id),
    CONSTRAINT uq_swapmid_etl_target_resource_v1_resource_instance_id
        UNIQUE (resource_instance_id),
    CONSTRAINT ck_swapmid_etl_target_resource_v1_singleton_id
        CHECK (singleton_id = 1),
    CONSTRAINT ck_swapmid_etl_target_resource_v1_protocol_version
        CHECK (protocol_version = 1)
);

CREATE TABLE swapmid_etl.execution_receipt_v1
(
    run_id uuid NOT NULL,
    protocol_version smallint NOT NULL,
    resource_instance_id uuid NOT NULL,
    pipeline_id uuid NOT NULL,
    legacy_target_resource_hash bytea NOT NULL,
    verified_target_resource_hash bytea NOT NULL,
    execution_contract_hash bytea NOT NULL,
    source_query_hash bytea NOT NULL,
    target_schema_hash bytea NOT NULL,
    batch_digest bytea NOT NULL,
    write_mode smallint NOT NULL,
    processed_rows bigint NOT NULL,
    recorded_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT pk_swapmid_etl_execution_receipt_v1
        PRIMARY KEY (run_id),
    CONSTRAINT fk_swapmid_etl_execution_receipt_v1_target_resource_v1
        FOREIGN KEY (resource_instance_id)
        REFERENCES swapmid_etl.target_resource_v1 (resource_instance_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT,
    CONSTRAINT ck_swapmid_etl_execution_receipt_v1_protocol_version
        CHECK (protocol_version = 1),
    CONSTRAINT ck_swapmid_etl_execution_receipt_v1_write_mode
        CHECK (write_mode IN (1, 2, 3) AND octet_length(batch_digest) = 32),
    CONSTRAINT ck_swapmid_etl_execution_receipt_v1_processed_rows
        CHECK (processed_rows >= 0)
);

INSERT INTO swapmid_etl.target_resource_v1
    (singleton_id, protocol_version, resource_instance_id)
VALUES
    (1, 1, gen_random_uuid());

COMMIT;

/*
  最小权限示例（由 DBA 替换主体名后单独执行）：

  GRANT USAGE ON SCHEMA swapmid_etl TO swapmid_etl_runtime;
  REVOKE ALL ON TABLE swapmid_etl.target_resource_v1 FROM swapmid_etl_runtime;
  REVOKE ALL ON TABLE swapmid_etl.execution_receipt_v1 FROM swapmid_etl_runtime;
  GRANT SELECT ON TABLE swapmid_etl.target_resource_v1 TO swapmid_etl_runtime;
  GRANT SELECT, INSERT ON TABLE swapmid_etl.execution_receipt_v1 TO swapmid_etl_runtime;

  不得让运行账号成为表/schema/database owner 或 superuser；激活会按 has_table_privilege 复验。
*/
