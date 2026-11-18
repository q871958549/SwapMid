using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// 关系型 CDC 镜像（RelationalTable）双端元数据探测与结构兼容性预检服务：
/// 同时探测上游 PostgreSQL 单表列与下游关系表列（PG / SQL Server / MySQL / SQLite），
/// 验证主键、显式字段映射与软删除墓碑列契约。
/// </summary>
public sealed class CdcRelationalMirrorPreflightService : ITransientDependency
{
    private const int ConnectTimeoutSeconds = 10;
    private const int CommandTimeoutSeconds = 20;

    private readonly IEtlRelationalConnectionFactory _connectionFactory;

    public CdcRelationalMirrorPreflightService(IEtlRelationalConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    internal async Task<CdcMirrorSchemaInspectionDto> InspectAsync(
        DataSource source,
        DataSource target,
        InspectCdcMirrorSchemaInput input,
        CancellationToken cancellationToken)
    {
        CdcStreamMappingHelpers.EnsureSourceMatchesType(input.SourceType, source);

        if (target.Type != DataSourceType.RelationalDatabase)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"关系表行级镜像目标必须为关系型数据库，当前为 {target.Type}。");
        }

        var defaultSchema = input.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => "dbo",
            CdcSourceType.MySqlBinlog => RelationalDataSourceConnection.Parse(source.Config).Database?.Trim() ?? string.Empty,
            _ => "public",
        };
        var monitoredTables = CdcProtocolHelpers.ParseMonitoredTables(
            input.MonitoredTables,
            defaultSchema: defaultSchema);
        if (monitoredTables.Count != 1)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "关系表行级镜像（RelationalTable）模式下必须在监听表清单中指定唯一 1 张源表。");
        }

        var destination = input.TargetSinkDestination?.Trim() ?? string.Empty;
        if (!EtlTransformConfig.IsValidDestinationFor(DataSourceType.RelationalDatabase, destination))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "关系型目标表名缺失或格式不合法（示例：public.orders_mirror）。");
        }

        var keyColumns = CdcProtocolHelpers.ParseSinkKeyColumns(input.TargetSinkKeyColumns);
        var mappingRules = ConvertFieldMappings(input.TargetFieldMappings);

        var sourceTable = monitoredTables[0].FullName;
        var sourceProvider = input.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => "sqlserver",
            CdcSourceType.MySqlBinlog => "mysql",
            _ => "postgresql",
        };
        var sourceColumns = new List<PreflightColumnDto>();
        var targetColumns = new List<PreflightColumnDto>();
        var issues = new List<PreflightSchemaIssueDto>();
        string? targetProvider = null;
        string canonicalTarget = destination;

        try
        {
            await using var srcConn = _connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, CommandTimeoutSeconds);
            await srcConn.OpenAsync(cancellationToken);
            sourceColumns = await EtlTargetSchemaInspector.InspectTargetTableAsync(
                srcConn, sourceProvider, sourceTable, cancellationToken);
            if (sourceColumns.Count == 0)
            {
                issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:SourceTableNotFound",
                    Message = $"上游源库中未找到表「{sourceTable}」或当前账号无元数据读取权限。",
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:SourceInspectFailed",
                Message = $"读取上游源表「{sourceTable}」列定义失败（{ex.GetType().Name}）。",
            });
        }

        try
        {
            var probe = RelationalDataSourceConnection.Parse(target.Config);
            targetProvider = probe.NormalizedProvider;
            await using var tgtConn = _connectionFactory.CreateTarget(target.Config, ConnectTimeoutSeconds, CommandTimeoutSeconds);
            await tgtConn.OpenAsync(cancellationToken);
            await EtlProviderSession.ConfigureAsync(tgtConn, probe, CommandTimeoutSeconds, cancellationToken);
            canonicalTarget = await EtlTargetSession.ResolveCanonicalTargetTableAsync(
                tgtConn, destination, probe, CommandTimeoutSeconds, cancellationToken);
            targetColumns = await EtlTargetSchemaInspector.InspectTargetTableAsync(
                tgtConn, probe.NormalizedProvider, canonicalTarget, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:TargetInspectFailed",
                Message = $"读取目标表「{destination}」列定义失败（{ex.GetType().Name}）。",
            });
        }

        var suggestedMappings = BuildSuggestedMappings(sourceColumns, targetColumns);
        if (issues.Count == 0 || targetColumns.Count > 0)
        {
            EvaluateCompatibility(
                canonicalTarget,
                sourceColumns,
                targetColumns,
                mappingRules,
                keyColumns,
                input.TargetDeletePolicy,
                input.SoftDeleteColumn,
                input.SoftDeleteTimestampColumn,
                issues);
        }

        return new CdcMirrorSchemaInspectionDto
        {
            Compatible = issues.All(i => !string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            SourceProvider = sourceProvider,
            TargetProvider = targetProvider,
            CanonicalSourceTable = sourceTable,
            CanonicalTargetTable = canonicalTarget,
            SourceColumns = sourceColumns,
            TargetColumns = targetColumns,
            SuggestedMappings = suggestedMappings,
            Issues = issues,
        };
    }

    internal async Task<CdcMirrorCompatibilityDto> EvaluateStreamAsync(
        DataSource source,
        DataSource target,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        var mappings = CdcStreamMirrorConfigValidator.ParseFieldMappings(stream.TargetFieldMappingsJson)
            .Select(m => new CdcFieldMappingDto { SourceColumn = m.SourceColumn, TargetColumn = m.TargetColumn })
            .ToList();

        var inspection = await InspectAsync(
            source,
            target,
            new InspectCdcMirrorSchemaInput
            {
                SourceId = stream.SourceId,
                SourceType = stream.SourceType,
                MonitoredTables = stream.MonitoredTables,
                TargetSinkId = target.Id,
                TargetSinkDestination = stream.TargetSinkDestination ?? string.Empty,
                TargetSinkKeyColumns = stream.TargetSinkKeyColumns,
                TargetDeletePolicy = stream.TargetDeletePolicy,
                SoftDeleteColumn = stream.SoftDeleteColumn,
                SoftDeleteTimestampColumn = stream.SoftDeleteTimestampColumn,
                TargetFieldMappings = mappings,
            },
            cancellationToken);

        return new CdcMirrorCompatibilityDto
        {
            Compatible = inspection.Compatible,
            TargetProvider = inspection.TargetProvider,
            CanonicalTargetTable = inspection.CanonicalTargetTable,
            SourceColumnCount = inspection.SourceColumns.Count,
            TargetColumnCount = inspection.TargetColumns.Count,
            MappedColumnCount = mappings.Count > 0 ? mappings.Count : inspection.SuggestedMappings.Count,
            Issues = inspection.Issues,
        };
    }

    internal static void EvaluateCompatibility(
        string targetTable,
        List<PreflightColumnDto> sourceColumns,
        List<PreflightColumnDto> targetColumns,
        IReadOnlyList<EtlColumnMapping> mappings,
        IReadOnlyList<string> keyColumns,
        CdcDeletePolicy deletePolicy,
        string? softDeleteColumn,
        string? softDeleteTimestampColumn,
        List<PreflightSchemaIssueDto> issues)
    {
        var systemManaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (deletePolicy == CdcDeletePolicy.SoftDelete)
        {
            if (!string.IsNullOrWhiteSpace(softDeleteColumn))
            {
                systemManaged.Add(softDeleteColumn.Trim());
            }

            if (!string.IsNullOrWhiteSpace(softDeleteTimestampColumn))
            {
                systemManaged.Add(softDeleteTimestampColumn.Trim());
            }
        }

        EtlSchemaCompatibilityEvaluator.EvaluateMappingContract(
            targetTable,
            sourceColumns,
            targetColumns,
            mappings,
            validateUpsertKeys: keyColumns.Count > 0,
            keyColumns,
            watermarkColumn: null,
            systemManagedTargets: systemManaged.Count > 0 ? systemManaged : null,
            issues);

        if (keyColumns.Count == 0)
        {
            issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:CdcMirrorKeyRequired",
                Message = "关系表行级镜像必须配置业务主键列（TargetSinkKeyColumns）。",
            });
        }

        if (deletePolicy == CdcDeletePolicy.SoftDelete && targetColumns.Count > 0)
        {
            ValidateSoftDeleteColumns(targetColumns, softDeleteColumn, softDeleteTimestampColumn, issues);
        }
    }

    private static void ValidateSoftDeleteColumns(
        List<PreflightColumnDto> targetColumns,
        string? softDeleteColumn,
        string? softDeleteTimestampColumn,
        List<PreflightSchemaIssueDto> issues)
    {
        var targetMap = targetColumns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var flagColName = softDeleteColumn?.Trim();
        if (string.IsNullOrWhiteSpace(flagColName) || !targetMap.TryGetValue(flagColName, out var flagCol))
        {
            issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:SoftDeleteFlagColumnMissing",
                Message = $"目标表中未找到软删除标记列「{softDeleteColumn}」。",
            });
        }
        else if (!IsSupportedSoftDeleteFlagType(flagCol.DataType))
        {
            issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "warning",
                Code = "SwapMid:ETL:SoftDeleteFlagTypeUnexpected",
                Message = $"软删除标记列「{flagCol.Name}」类型（{flagCol.DataType}）非常规布尔/整型，请确认与 true/false 语义兼容。",
            });
        }

        var tsColName = softDeleteTimestampColumn?.Trim();
        if (!string.IsNullOrWhiteSpace(tsColName))
        {
            if (!targetMap.TryGetValue(tsColName, out var tsCol))
            {
                issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:SoftDeleteTimestampColumnMissing",
                    Message = $"目标表中未找到软删除时间戳列「{tsColName}」。",
                });
            }
            else
            {
                if (!tsCol.IsNullable)
                {
                    issues.Add(new PreflightSchemaIssueDto
                    {
                        Severity = "error",
                        Code = "SwapMid:ETL:SoftDeleteTimestampNotNullable",
                        Message = $"软删除时间戳列「{tsCol.Name}」必须允许 NULL，以便有效行与重新激活行清空删除时间。",
                    });
                }

                if (!IsSupportedSoftDeleteTimestampType(tsCol.DataType))
                {
                    issues.Add(new PreflightSchemaIssueDto
                    {
                        Severity = "warning",
                        Code = "SwapMid:ETL:SoftDeleteTimestampTypeUnexpected",
                        Message = $"软删除时间戳列「{tsCol.Name}」类型（{tsCol.DataType}）非标准时间类型，将尝试按 UTC/毫秒写入。",
                    });
                }
            }
        }
    }

    internal static List<CdcFieldMappingDto> BuildSuggestedMappings(
        IReadOnlyList<PreflightColumnDto> sourceColumns,
        IReadOnlyList<PreflightColumnDto> targetColumns)
    {
        var usedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactBySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalizedBySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var src in sourceColumns)
        {
            exactBySource.TryAdd(src.Name, src.Name);
            var norm = NormalizeIdentifierForMatch(src.Name);
            if (!string.IsNullOrEmpty(norm))
            {
                normalizedBySource.TryAdd(norm, src.Name);
            }
        }

        var suggested = new List<CdcFieldMappingDto>();
        foreach (var tgt in targetColumns)
        {
            string? match = null;
            if (exactBySource.TryGetValue(tgt.Name, out var exact) && !usedSources.Contains(exact))
            {
                match = exact;
            }
            else
            {
                var norm = NormalizeIdentifierForMatch(tgt.Name);
                if (!string.IsNullOrEmpty(norm) &&
                    normalizedBySource.TryGetValue(norm, out var normMatch) &&
                    !usedSources.Contains(normMatch))
                {
                    match = normMatch;
                }
            }

            if (match != null)
            {
                usedSources.Add(match);
                suggested.Add(new CdcFieldMappingDto { SourceColumn = match, TargetColumn = tgt.Name });
            }
        }

        return suggested;
    }

    private static List<EtlColumnMapping> ConvertFieldMappings(IReadOnlyList<CdcFieldMappingDto>? input)
    {
        if (input == null || input.Count == 0)
        {
            return new List<EtlColumnMapping>();
        }

        return input
            .Where(x => !string.IsNullOrWhiteSpace(x.SourceColumn) && !string.IsNullOrWhiteSpace(x.TargetColumn))
            .Select(x => new EtlColumnMapping(x.SourceColumn.Trim(), x.TargetColumn.Trim()))
            .ToList();
    }

    private static string NormalizeIdentifierForMatch(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private static bool IsSupportedSoftDeleteFlagType(string rawType)
    {
        var t = rawType.Trim().ToLowerInvariant();
        return t.Contains("bool", StringComparison.Ordinal) ||
               t == "bit" ||
               t.Contains("int", StringComparison.Ordinal) ||
               t.Contains("numeric", StringComparison.Ordinal) ||
               t.Contains("decimal", StringComparison.Ordinal) ||
               t.Contains("char", StringComparison.Ordinal) ||
               t.Contains("text", StringComparison.Ordinal);
    }

    private static bool IsSupportedSoftDeleteTimestampType(string rawType)
    {
        var t = rawType.Trim().ToLowerInvariant();
        return t.Contains("time", StringComparison.Ordinal) ||
               t.Contains("date", StringComparison.Ordinal) ||
               t is "bigint" or "int8" ||
               t.Contains("char", StringComparison.Ordinal) ||
               t.Contains("text", StringComparison.Ordinal);
    }
}
