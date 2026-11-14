/*
  Target Receipt v1 / SQL Server 2016+

  由目标库 DBA 在停用对应 ETL 管道后执行。应用只会 SELECT/INSERT，绝不会
  CREATE/ALTER/UPDATE/DELETE 这些固定对象。脚本故意不使用 IF NOT EXISTS：已存在对象
  必须先由 DBA 对照契约核验，不能把结构漂移静默当成部署成功。
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF SCHEMA_ID(N'swapmid_etl') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA [swapmid_etl] AUTHORIZATION [dbo];');
END;

IF OBJECT_ID(N'[swapmid_etl].[TargetResourceV1]', N'U') IS NOT NULL
   OR OBJECT_ID(N'[swapmid_etl].[ExecutionReceiptV1]', N'U') IS NOT NULL
BEGIN
    THROW 51000, 'Target Receipt v1 objects already exist; verify instead of overwriting.', 1;
END;

CREATE TABLE [swapmid_etl].[TargetResourceV1]
(
    [SingletonId] tinyint NOT NULL,
    [ProtocolVersion] smallint NOT NULL,
    [ResourceInstanceId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetime2(6) NOT NULL
        CONSTRAINT [DF_swapmid_etl_TargetResourceV1_CreatedAtUtc]
        DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_swapmid_etl_TargetResourceV1]
        PRIMARY KEY CLUSTERED ([SingletonId]),
    CONSTRAINT [UQ_swapmid_etl_TargetResourceV1_ResourceInstanceId]
        UNIQUE ([ResourceInstanceId]),
    CONSTRAINT [CK_swapmid_etl_TargetResourceV1_SingletonId]
        CHECK ([SingletonId] = 1),
    CONSTRAINT [CK_swapmid_etl_TargetResourceV1_ProtocolVersion]
        CHECK ([ProtocolVersion] = 1)
);

CREATE TABLE [swapmid_etl].[ExecutionReceiptV1]
(
    [RunId] uniqueidentifier NOT NULL,
    [ProtocolVersion] smallint NOT NULL,
    [ResourceInstanceId] uniqueidentifier NOT NULL,
    [PipelineId] uniqueidentifier NOT NULL,
    [LegacyTargetResourceHash] binary(32) NOT NULL,
    [VerifiedTargetResourceHash] binary(32) NOT NULL,
    [ExecutionContractHash] binary(32) NOT NULL,
    [SourceQueryHash] binary(32) NOT NULL,
    [TargetSchemaHash] binary(32) NOT NULL,
    [BatchDigest] binary(32) NOT NULL,
    [WriteMode] tinyint NOT NULL,
    [ProcessedRows] bigint NOT NULL,
    [RecordedAtUtc] datetime2(6) NOT NULL
        CONSTRAINT [DF_swapmid_etl_ExecutionReceiptV1_RecordedAtUtc]
        DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_swapmid_etl_ExecutionReceiptV1]
        PRIMARY KEY CLUSTERED ([RunId]),
    CONSTRAINT [FK_swapmid_etl_ExecutionReceiptV1_TargetResourceV1]
        FOREIGN KEY ([ResourceInstanceId])
        REFERENCES [swapmid_etl].[TargetResourceV1] ([ResourceInstanceId]),
    CONSTRAINT [CK_swapmid_etl_ExecutionReceiptV1_ProtocolVersion]
        CHECK ([ProtocolVersion] = 1),
    CONSTRAINT [CK_swapmid_etl_ExecutionReceiptV1_WriteMode]
        CHECK ([WriteMode] IN (1, 2)),
    CONSTRAINT [CK_swapmid_etl_ExecutionReceiptV1_ProcessedRows]
        CHECK ([ProcessedRows] >= 0)
);

INSERT INTO [swapmid_etl].[TargetResourceV1]
    ([SingletonId], [ProtocolVersion], [ResourceInstanceId])
VALUES
    (1, 1, NEWID());

COMMIT TRANSACTION;

/*
  最小权限示例（由 DBA 替换主体名后单独执行）：

  GRANT SELECT ON OBJECT::[swapmid_etl].[TargetResourceV1] TO [swapmid_etl_runtime];
  GRANT SELECT, INSERT ON OBJECT::[swapmid_etl].[ExecutionReceiptV1] TO [swapmid_etl_runtime];
  GRANT VIEW DEFINITION ON OBJECT::[swapmid_etl].[TargetResourceV1] TO [swapmid_etl_runtime];
  GRANT VIEW DEFINITION ON OBJECT::[swapmid_etl].[ExecutionReceiptV1] TO [swapmid_etl_runtime];
  DENY INSERT, UPDATE, DELETE, ALTER, TAKE OWNERSHIP
      ON OBJECT::[swapmid_etl].[TargetResourceV1] TO [swapmid_etl_runtime];
  DENY UPDATE, DELETE ON OBJECT::[swapmid_etl].[ExecutionReceiptV1] TO [swapmid_etl_runtime];
  DENY ALTER, TAKE OWNERSHIP
      ON OBJECT::[swapmid_etl].[ExecutionReceiptV1] TO [swapmid_etl_runtime];

  不得另授 CONTROL，也不得让该主体成为对象/schema/database owner；激活会按有效权限复验。
*/
