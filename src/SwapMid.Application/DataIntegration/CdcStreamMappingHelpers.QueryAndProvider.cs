using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.DataIntegration;

internal static partial class CdcStreamMappingHelpers
{
    internal static void EnsureSourceMatchesType(CdcSourceType sourceType, DataSource source)
    {
        switch (sourceType)
        {
            case CdcSourceType.SqlServerChangeTracking:
                SqlServerCdcCaptureService.EnsureSqlServerSource(source);
                break;
            case CdcSourceType.MySqlBinlog:
                MySqlBinlogCdcCaptureService.EnsureMySqlSource(source);
                break;
            default:
                PgOutputCdcCaptureService.EnsurePostgreSqlSource(source);
                break;
        }
    }

    internal static Task<CdcSchemaDriftEvaluation> EvaluateSchemaForStreamAsync(
        PgOutputCdcCaptureService pgCaptureService,
        SqlServerCdcCaptureService? sqlServerCaptureService,
        MySqlBinlogCdcCaptureService? mySqlCaptureService,
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        return stream.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => (sqlServerCaptureService
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", "SQL Server Change Tracking 捕获引擎未启用。"))
                .EvaluateSchemaAsync(source, stream, cancellationToken),
            CdcSourceType.MySqlBinlog => (mySqlCaptureService
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", "MySQL Binlog 捕获引擎未启用。"))
                .EvaluateSchemaAsync(source, stream, cancellationToken),
            _ => pgCaptureService.EvaluateSchemaAsync(source, stream, cancellationToken),
        };
    }

    internal static async Task<CdcPreflightResultDto> DispatchPreflightAsync(
        PgOutputCdcCaptureService pgCaptureService,
        SqlServerCdcCaptureService? sqlServerCaptureService,
        MySqlBinlogCdcCaptureService? mySqlCaptureService,
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        return stream.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => await (sqlServerCaptureService
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", "SQL Server Change Tracking 捕获引擎未启用。"))
                .PreflightAsync(source, stream, cancellationToken),
            CdcSourceType.MySqlBinlog => await (mySqlCaptureService
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", "MySQL Binlog 捕获引擎未启用。"))
                .PreflightAsync(source, stream, cancellationToken),
            _ => await pgCaptureService.PreflightAsync(source, stream, cancellationToken),
        };
    }

    internal static string? NormalizeCheckpointResetToken(CdcSourceType sourceType, string? rawCheckpoint)
    {
        if (string.IsNullOrWhiteSpace(rawCheckpoint))
        {
            return null;
        }

        if (sourceType == CdcSourceType.SqlServerChangeTracking)
        {
            if (!long.TryParse(rawCheckpoint.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version < 0)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "SQL Server Change Tracking 位点必须是非负整数版本号。");
            }

            return version.ToString(CultureInfo.InvariantCulture);
        }

        if (sourceType == CdcSourceType.MySqlBinlog)
        {
            return MySqlBinlogCdcCaptureService.ValidateCheckpoint(rawCheckpoint).Token;
        }

        return CdcProtocolHelpers.ValidateLsn(rawCheckpoint).ToString();
    }

    internal static async Task<List<CdcStreamDto>> MapToDtoListAsync(
        IReadOnlyList<CdcStreamDefinition> items,
        IRepository<DataSource, Guid> sourceRepository,
        IRepository<DataPipeline, Guid> pipelineRepository)
    {
        var sourceIds = items.SelectMany(x =>
            x.TargetSinkId.HasValue ? new[] { x.SourceId, x.TargetSinkId.Value } : new[] { x.SourceId })
            .Distinct()
            .ToList();
        var sources = sourceIds.Count > 0
            ? (await sourceRepository.GetListAsync(x => sourceIds.Contains(x.Id))).ToDictionary(x => x.Id)
            : new Dictionary<Guid, DataSource>();

        var pipelineIds = items.Where(x => x.TargetPipelineId.HasValue).Select(x => x.TargetPipelineId!.Value).Distinct().ToList();
        var pipelines = pipelineIds.Count > 0
            ? (await pipelineRepository.GetListAsync(x => pipelineIds.Contains(x.Id))).ToDictionary(x => x.Id, x => x.Name)
            : new Dictionary<Guid, string>();

        return items.Select(x =>
        {
            var src = sources.GetValueOrDefault(x.SourceId);
            var sink = x.TargetSinkId.HasValue ? sources.GetValueOrDefault(x.TargetSinkId.Value) : null;
            var pipe = x.TargetPipelineId.HasValue ? pipelines.GetValueOrDefault(x.TargetPipelineId.Value) : null;
            return MapToDto(x, src?.Name, pipe, sink);
        }).ToList();
    }
}
