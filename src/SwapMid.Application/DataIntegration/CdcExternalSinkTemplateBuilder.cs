using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// 生成 CDC 外部接收端（ClickHouse / Kafka / ObjectStorage Parquet）的标准 11 列契约描述、
/// ClickHouse <c>MergeTree</c> 建表 DDL、分区键示例以及样例 JSON 载荷，并在 ClickHouse 模式下探测目标表是否就绪。
/// </summary>
internal static class CdcExternalSinkTemplateBuilder
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    internal static async Task<CdcSinkTemplateDto> BuildAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        DataIntegrationSinkProbeService? probeService,
        CancellationToken cancellationToken)
    {
        if (targetSink.Type is not (DataSourceType.Kafka or DataSourceType.ClickHouse or DataSourceType.ObjectStorage))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"外部接收端仅支持 Kafka / ClickHouse / ObjectStorage，当前为 {targetSink.Type}。");
        }

        var destination = stream.TargetSinkDestination?.Trim() ?? string.Empty;
        var keyColumns = CdcProtocolHelpers.ParseSinkKeyColumns(stream.TargetSinkKeyColumns);
        var partitionKeyExpr = keyColumns.Count > 0
            ? BuildSamplePartitionKeyExpression(stream, keyColumns)
            : null;

        bool? targetExists = null;
        string? clickHouseDdl = null;
        string? manifestDir = null;
        string? manifestLatestKey = null;
        string? latestManifestJson = null;
        string? lakehouseQuerySql = null;
        if (targetSink.Type == DataSourceType.ClickHouse)
        {
            var cfg = ClickHouseSinkConfigContract.ParseForWrite(targetSink.Config);
            var (dbName, tableName) = DataIntegrationSinkProbeService.SplitClickHouseTable(
                cfg.Database,
                destination);
            clickHouseDdl = BuildClickHouseDdl(dbName, tableName, keyColumns);

            if (probeService != null)
            {
                try
                {
                    var cols = await probeService.InspectClickHouseColumnsAsync(
                        cfg,
                        destination,
                        cancellationToken);
                    targetExists = cols.Count > 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    targetExists = null;
                }
            }
        }
        else if (targetSink.Type == DataSourceType.ObjectStorage && !string.IsNullOrWhiteSpace(destination))
        {
            var cfg = ObjectStorageSinkConfigContract.ParseForWrite(targetSink.Config, requireSecretKey: false);
            manifestDir = cfg.BuildManifestDirectory(destination);
            manifestLatestKey = cfg.BuildLatestManifestKey(destination);
            lakehouseQuerySql = BuildLakehouseQuerySql(cfg, destination);
            if (probeService != null && !string.IsNullOrWhiteSpace(cfg.SecretKey))
            {
                try
                {
                    latestManifestJson = await probeService.TryReadObjectStorageJsonAsync(
                        cfg,
                        manifestLatestKey,
                        cancellationToken);
                    targetExists = !string.IsNullOrWhiteSpace(latestManifestJson);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    targetExists = null;
                }
            }
        }

        var columns = BuildColumnDefinitions();
        var sampleJson = BuildSampleJson(stream, partitionKeyExpr);
        var summary = targetSink.Type switch
        {
            DataSourceType.ClickHouse => targetExists == true
                ? "ClickHouse 目标表已存在，已生成对应的 11 列 CDC 变更日志建表契约。"
                : keyColumns.Count > 0
                    ? "请执行下方 ClickHouse ReplacingMergeTree 按主键排序建表语句（或一键建表）后再激活投递。"
                    : "请在 ClickHouse 目标库执行下方 MergeTree 建表语句后再激活实时投递。",
            DataSourceType.Kafka => keyColumns.Count > 0
                ? $"Kafka 将按主键（{string.Join(", ", keyColumns)}）构造分区 Message Key 保证同实体严格有序。"
                : "Kafka 将投递 11 列标准 JSON 事件（建议配置分区主键列以保持实体更新有序）。",
            DataSourceType.ObjectStorage => targetExists == true
                ? "对象存储 Lakehouse 检出点索引已就绪，每次提交自动追加 11 列 Parquet/JSONL 与不可变 Manifest。"
                : "对象存储将按 LSN 幂等写入 11 列 Apache Parquet/JSONL 分片，并同步生成 _swapmid/manifests 批次清单与 latest.json 检出点。",
            _ => "已生成标准 CDC 接收端结构模板。",
        };

        return new CdcSinkTemplateDto
        {
            StreamId = stream.Id,
            TargetType = targetSink.Type,
            Destination = destination,
            TargetExists = targetExists,
            PartitionKeyExpression = partitionKeyExpr,
            Columns = columns,
            ClickHouseDdl = clickHouseDdl,
            ManifestDirectory = manifestDir,
            ManifestLatestKey = manifestLatestKey,
            LatestManifestJson = latestManifestJson,
            LakehouseQuerySql = lakehouseQuerySql,
            SamplePayloadJson = sampleJson,
            Summary = summary,
        };
    }

    internal static string BuildLakehouseQuerySql(ObjectStorageSinkConfig cfg, string destination)
    {
        var baseDir = cfg.BuildDestinationDirectory(destination);
        var ext = cfg.Format == "jsonl" ? "jsonl" : "parquet";
        var chFormat = cfg.Format == "jsonl" ? "JSONEachRow" : "Parquet";
        var chUrl = $"{cfg.Endpoint.TrimEnd('/')}/{cfg.Bucket}/{baseDir}/*.{ext}";

        return $"""
            -- DuckDB (读取最新压缩快照与增量分片)
            SELECT * FROM read_{ext}('s3://{cfg.Bucket}/{baseDir}/*.{ext}');

            -- ClickHouse S3 即席分析查询
            SELECT * FROM s3('{chUrl}', '<accessKey>', '<secretKey>', '{chFormat}');

            -- Apache Spark SQL (Lakehouse 检查点路径: s3a://{cfg.Bucket}/{cfg.BuildLatestManifestKey(destination)})
            SELECT * FROM {ext}.`s3a://{cfg.Bucket}/{baseDir}/`;
            """;
    }

    internal static async Task<CdcSinkTemplateDto> ProvisionClickHouseAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        DataIntegrationSinkProbeService? probeService,
        CancellationToken cancellationToken)
    {
        if (targetSink.Type != DataSourceType.ClickHouse)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "仅 ClickHouse 外部接收端支持自动执行建表 DDL 初始化。");
        }

        if (probeService == null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "未注册外部接收端探测与 DDL 执行服务。");
        }

        var destination = stream.TargetSinkDestination?.Trim() ?? string.Empty;
        var cfg = ClickHouseSinkConfigContract.ParseForWrite(targetSink.Config);
        var (dbName, tableName) = DataIntegrationSinkProbeService.SplitClickHouseTable(cfg.Database, destination);
        var keyColumns = CdcProtocolHelpers.ParseSinkKeyColumns(stream.TargetSinkKeyColumns);
        var ddl = BuildClickHouseDdl(dbName, tableName, keyColumns);

        try
        {
            await probeService.ExecuteClickHouseDdlAsync(cfg, destination, ddl, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BusinessException)
        {
            throw;
        }
        catch
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                .WithData("Reason", "自动初始化 ClickHouse 目标表失败，请检查网络白名单、凭据与目标表名格式。");
        }

        return await BuildAsync(stream, targetSink, probeService, cancellationToken);
    }

    internal static string BuildClickHouseDdl(
        string database,
        string table,
        IReadOnlyList<string>? keyColumns = null)
    {
        var db = database.Replace("`", "``", StringComparison.Ordinal);
        var tbl = table.Replace("`", "``", StringComparison.Ordinal);
        var engineClause = keyColumns is { Count: > 0 }
            ? BuildKeyedReplacingMergeTreeClause(keyColumns)
            : "ENGINE = MergeTree()\nORDER BY (`stream_id`, `source_schema`, `source_table`, `commit_timestamp_utc`, `event_id`);";

        return
            $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`{tbl}`
            (
                `event_id` String,
                `stream_id` String,
                `stream_name` String,
                `source_schema` String,
                `source_table` String,
                `op` LowCardinality(String),
                `lsn` String,
                `commit_timestamp_utc` DateTime64(3, 'UTC'),
                `changed_columns` Nullable(String),
                `after_json` Nullable(String),
                `before_json` Nullable(String)
            )
            {engineClause}
            """;
    }

    private static string BuildKeyedReplacingMergeTreeClause(IReadOnlyList<string> keyColumns)
    {
        var orderParts = new List<string>(keyColumns.Count + 3)
        {
            "`stream_id`",
            "`source_schema`",
            "`source_table`",
        };
        foreach (var col in keyColumns)
        {
            orderParts.Add($"JSONExtractRaw(coalesce(`after_json`, `before_json`, '{{}}'), '{col}')");
        }

        return $"ENGINE = ReplacingMergeTree(`commit_timestamp_utc`)\nORDER BY ({string.Join(", ", orderParts)});";
    }

    private static List<CdcSinkColumnTemplateDto> BuildColumnDefinitions() =>
    [
        new() { Name = "event_id", LogicalType = "string", ClickHouseType = "String", Nullable = false, Description = "基于 streamId + LSN + 序号的幂等事件 UUID" },
        new() { Name = "stream_id", LogicalType = "string", ClickHouseType = "String", Nullable = false, Description = "CDC 流定义 Id" },
        new() { Name = "stream_name", LogicalType = "string", ClickHouseType = "String", Nullable = false, Description = "CDC 流名称" },
        new() { Name = "source_schema", LogicalType = "string", ClickHouseType = "String", Nullable = false, Description = "PostgreSQL 源表 Schema" },
        new() { Name = "source_table", LogicalType = "string", ClickHouseType = "String", Nullable = false, Description = "PostgreSQL 源表表名" },
        new() { Name = "op", LogicalType = "string", ClickHouseType = "LowCardinality(String)", Nullable = false, Description = "WAL 操作类型：insert / update / delete" },
        new() { Name = "lsn", LogicalType = "string", ClickHouseType = "String", Nullable = false, Description = "事务提交 WAL LSN 位点" },
        new() { Name = "commit_timestamp_utc", LogicalType = "datetime", ClickHouseType = "DateTime64(3, 'UTC')", Nullable = false, Description = "WAL 事务提交 UTC 时间" },
        new() { Name = "changed_columns", LogicalType = "string", ClickHouseType = "Nullable(String)", Nullable = true, Description = "变更列名 JSON 数组（需旧行映像时非空）" },
        new() { Name = "after_json", LogicalType = "string", ClickHouseType = "Nullable(String)", Nullable = true, Description = "变更后整行 JSON 快照（delete 为 null）" },
        new() { Name = "before_json", LogicalType = "string", ClickHouseType = "Nullable(String)", Nullable = true, Description = "变更前旧行 JSON 快照（REPLICA IDENTITY FULL/delete 非空）" },
    ];

    private static string BuildSamplePartitionKeyExpression(
        CdcStreamDefinition stream,
        IReadOnlyList<string> keyColumns)
    {
        var tables = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables);
        var schema = tables.Count > 0 ? tables[0].Schema.ToLowerInvariant() : "public";
        var table = tables.Count > 0 ? tables[0].Table.ToLowerInvariant() : "target_table";
        var samplePairs = new string[keyColumns.Count];
        for (var i = 0; i < keyColumns.Count; i++)
        {
            samplePairs[i] = $"{keyColumns[i]}=<value>";
        }

        return $"{schema}.{table}:{string.Join("|", samplePairs)}";
    }

    private static string BuildSampleJson(CdcStreamDefinition stream, string? partitionKeyExpr)
    {
        var tables = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables);
        var schema = tables.Count > 0 ? tables[0].Schema : "public";
        var table = tables.Count > 0 ? tables[0].Table : "orders";
        var sample = new Dictionary<string, object?>
        {
            ["event_id"] = "00000000-0000-4000-8000-000000000001",
            ["stream_id"] = stream.Id.ToString("D"),
            ["stream_name"] = stream.Name,
            ["source_schema"] = schema,
            ["source_table"] = table,
            ["op"] = "update",
            ["lsn"] = stream.CheckpointLsn ?? "0/1DB61B8",
            ["commit_timestamp_utc"] = "2026-11-16T05:00:00.0000000Z",
            ["changed_columns"] = "[\"order_status\",\"total_amount\"]",
            ["after_json"] = "{\"order_id\":91001,\"order_status\":\"SHIPPED\",\"total_amount\":640.50}",
            ["before_json"] = "{\"order_id\":91001,\"order_status\":\"CREATED\",\"total_amount\":500.00}",
        };
        if (!string.IsNullOrWhiteSpace(partitionKeyExpr))
        {
            sample["_kafka_partition_key_pattern"] = partitionKeyExpr;
        }

        return JsonSerializer.Serialize(sample, IndentedJson);
    }
}
