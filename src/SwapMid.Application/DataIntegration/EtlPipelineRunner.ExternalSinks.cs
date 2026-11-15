using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

public partial class EtlPipelineRunner
{
    private async Task<EtlRunResult> RunExternalSinkAsync(
        EtlExecutionContext executionContext,
        DataSource source,
        DataSource target,
        string? transformConfig,
        CancellationToken ct)
    {
        if (source.Type != DataSourceType.RelationalDatabase ||
            executionContext.TargetReceipt is not null)
        {
            throw InvalidInput();
        }

        var writer = _sinkWriters.FirstOrDefault(w => w.TargetType == target.Type)
            ?? throw InvalidInput();

        var cfg = EtlTransformConfig.Parse(transformConfig, target.Type);
        var sourceProbe = RelationalDataSourceConnection.Parse(source.Config);
        if (!IsSupportedProvider(sourceProbe))
        {
            throw InvalidInput();
        }

        EtlSourceQueryGuard.Validate(cfg.SourceQuery, sourceProbe.NormalizedProvider);

        using var totalTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        totalTimeoutCts.CancelAfter(TimeSpan.FromSeconds(TotalExecutionTimeoutSeconds));
        var executionCt = totalTimeoutCts.Token;

        var totalRows = await writer.WriteAsync(
            executionContext.PipelineId,
            executionContext.RunId,
            target,
            cfg,
            (onBatchAsync, batchCt) => StreamSourceBatchesAsync(
                source.Config,
                sourceProbe,
                cfg,
                onBatchAsync,
                batchCt),
            executionCt);

        return new EtlRunResult(
            totalRows,
            TargetTruncated: cfg.Mode == EtlWriteMode.Truncate,
            ReceiptEvidence: null);
    }

    private async Task StreamSourceBatchesAsync(
        string sourceConfigJson,
        RelationalDataSourceConnection.Probe sourceProbe,
        EtlTransformConfig cfg,
        Func<EtlSourceBatch, CancellationToken, Task> onBatchAsync,
        CancellationToken ct)
    {
        await using var sourceConn = _connectionFactory.Create(sourceConfigJson, CommandTimeoutSeconds);
        await sourceConn.OpenAsync(ct);

        await using var sourceTx = await BeginSourceReadOnlyTransactionAsync(
            sourceConn,
            sourceProbe,
            ct);

        await using var cmd = sourceConn.CreateCommand();
        cmd.CommandText = cfg.SourceQuery;
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.Transaction = sourceTx;

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        EtlReaderBoundary.ValidatePreReadSchema(reader, sourceProbe);

        var projection = ResolveColumnProjection(
            reader,
            cfg,
            new RelationalDataSourceConnection.Probe { Provider = "sqlserver" });
        var targetClrTypes = ResolveProjectedClrTypes(reader, projection);

        long totalRows = 0;
        long cumulativePayloadBytes = 0;
        long batchIndex = 0;
        var currentBatch = new List<object?[]>(BatchSize);

        while (await reader.ReadAsync(ct))
        {
            totalRows++;
            if (totalRows > MaxRows)
            {
                throw InvalidInput();
            }

            var row = await EtlReaderBoundary.ReadRowAsync(
                reader,
                projection.SourceFieldCount,
                ct);
            cumulativePayloadBytes = checked(cumulativePayloadBytes + row.PayloadBytes);
            if (cumulativePayloadBytes > MaxTotalPayloadBytes)
            {
                throw InvalidInput();
            }

            currentBatch.Add(projection.ProjectRow(row.Values));
            if (currentBatch.Count >= BatchSize)
            {
                await onBatchAsync(
                    new EtlSourceBatch(
                        projection.TargetColumns,
                        targetClrTypes,
                        currentBatch,
                        batchIndex++),
                    ct);
                currentBatch = new List<object?[]>(BatchSize);
            }
        }

        if (currentBatch.Count > 0)
        {
            await onBatchAsync(
                new EtlSourceBatch(
                    projection.TargetColumns,
                    targetClrTypes,
                    currentBatch,
                    batchIndex),
                ct);
        }

        if (sourceTx is not null)
        {
            try
            {
                await sourceTx.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Ignore read-only rollback cleanup errors after stream completion.
            }
        }
    }

    private static Type[] ResolveProjectedClrTypes(
        System.Data.Common.DbDataReader reader,
        EtlColumnProjection projection)
    {
        if (projection.SourceOrdinals is null)
        {
            var types = new Type[projection.TargetColumns.Length];
            for (var i = 0; i < types.Length; i++)
            {
                types[i] = reader.GetFieldType(i) ?? typeof(string);
            }

            return types;
        }

        var projected = new Type[projection.SourceOrdinals.Length];
        for (var i = 0; i < projected.Length; i++)
        {
            projected[i] = reader.GetFieldType(projection.SourceOrdinals[i]) ?? typeof(string);
        }

        return projected;
    }
}
