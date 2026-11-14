/*
  Target Receipt v1 / MySQL/MariaDB 后续协议草案（当前应用不允许激活）

  在目标 DataSource 配置的当前 database 内由 DBA 执行。应用只会 SELECT/INSERT，绝不会
  CREATE/ALTER/UPDATE/DELETE 这些固定对象。脚本故意不使用 IF NOT EXISTS；若任一对象已
  存在，必须先核验/回滚部署，不能用脚本覆盖未知结构。

  当前运行账号无法在“不获得可创建/删除 trigger 的 TRIGGER 权限”的同时证明
  INFORMATION_SCHEMA.TRIGGERS 完整可见，因此本脚本不得用于生产。独立 inspector/definer
  attestation 协议落地后再启用。BINARY(16) 按 RFC 4122 网络字节序保存。
  不要使用 UUID_TO_BIN(..., 1) 的 time-swap 格式。
*/

CREATE TABLE `swapmid_etl_target_resource_v1`
(
    `singleton_id` TINYINT UNSIGNED NOT NULL,
    `protocol_version` SMALLINT UNSIGNED NOT NULL,
    `resource_instance_id` BINARY(16) NOT NULL,
    `created_at_utc` DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    CONSTRAINT `PK_swapmid_etl_target_resource_v1`
        PRIMARY KEY (`singleton_id`),
    CONSTRAINT `UQ_swapmid_etl_target_resource_v1_resource_instance_id`
        UNIQUE (`resource_instance_id`),
    CONSTRAINT `CK_swapmid_etl_target_resource_v1_singleton_id`
        CHECK (`singleton_id` = 1),
    CONSTRAINT `CK_swapmid_etl_target_resource_v1_protocol_version`
        CHECK (`protocol_version` = 1)
) ENGINE=InnoDB;

CREATE TABLE `swapmid_etl_execution_receipt_v1`
(
    `run_id` BINARY(16) NOT NULL,
    `protocol_version` SMALLINT UNSIGNED NOT NULL,
    `resource_instance_id` BINARY(16) NOT NULL,
    `pipeline_id` BINARY(16) NOT NULL,
    `legacy_target_resource_hash` BINARY(32) NOT NULL,
    `verified_target_resource_hash` BINARY(32) NOT NULL,
    `execution_contract_hash` BINARY(32) NOT NULL,
    `source_query_hash` BINARY(32) NOT NULL,
    `target_schema_hash` BINARY(32) NOT NULL,
    `batch_digest` BINARY(32) NOT NULL,
    `write_mode` TINYINT UNSIGNED NOT NULL,
    `processed_rows` BIGINT NOT NULL,
    `recorded_at_utc` DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    CONSTRAINT `PK_swapmid_etl_execution_receipt_v1`
        PRIMARY KEY (`run_id`),
    CONSTRAINT `FK_swapmid_etl_execution_receipt_v1_target_resource_v1`
        FOREIGN KEY (`resource_instance_id`)
        REFERENCES `swapmid_etl_target_resource_v1` (`resource_instance_id`)
        ON UPDATE RESTRICT ON DELETE RESTRICT,
    CONSTRAINT `CK_swapmid_etl_execution_receipt_v1_protocol_version`
        CHECK (`protocol_version` = 1),
    CONSTRAINT `CK_swapmid_etl_execution_receipt_v1_write_mode`
        CHECK (`write_mode` = 1),
    CONSTRAINT `CK_swapmid_etl_execution_receipt_v1_processed_rows`
        CHECK (`processed_rows` >= 0)
) ENGINE=InnoDB;

INSERT INTO `swapmid_etl_target_resource_v1`
    (`singleton_id`, `protocol_version`, `resource_instance_id`)
VALUES
    (1, 1, UNHEX(REPLACE(UUID(), '-', '')));

/* 当前禁止为 receipt-v1 运行账号授权或部署这些对象。 */
