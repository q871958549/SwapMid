using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// 关系库→关系库 ETL 执行器(MVP,2026-06-14):连源跑 sourceQuery 读结果集 → 按列名写目标 targetTable。
/// 同步执行 + 行数上限保护(<see cref="MaxRows"/>);仅 SqlServer/MySQL(复用 <see cref="RelationalDataSourceConnection"/>)。
/// 安全:sourceQuery 经方言感知的单语句只读守卫；源结果流式有界读取；目标 DELETE + 全部 INSERT
/// 位于源 reader 关闭后的同一短事务；targetTable/列名均引用、值全参数化。
/// </summary>
public partial class EtlPipelineRunner : IEtlPipelineRunner, ITransientDependency
{
    /// <summary>同步执行行数上限——读取到第 50,001 行立即拒绝并回滚目标事务。</summary>
    public const int MaxRows = 50_000;
    /// <summary>结果集列数上限，避免极宽查询放大 SQL、参数和单行内存。</summary>
    public const int MaxColumns = 256;
    /// <summary>单个文本/二进制值上限；LOB 必须走后续专用异步搬运能力。</summary>
    public const int MaxCellPayloadBytes = 4 * 1024 * 1024;
    /// <summary>单行近似载荷上限；MySQL 还会在首行前按 schema 声明宽度预检。</summary>
    public const int MaxRowPayloadBytes = 4 * 1024 * 1024;
    /// <summary>已保留批次近似载荷上限；读取下一行时另预留最多一个单行上限。</summary>
    public const int MaxBatchPayloadBytes = 4 * 1024 * 1024;
    /// <summary>整次同步任务近似载荷上限，防止临时表空间被 50K 个大行耗尽。</summary>
    public const long MaxTotalPayloadBytes = 256L * 1024 * 1024;
    /// <summary>整次单元格上限，防止极宽小值结果制造海量参数与数据库往返。</summary>
    public const long MaxTotalCells = 1_000_000;
    /// <summary>正式替换模式允许同步删除的旧目标行上限。</summary>
    public const int MaxExistingTargetRows = 50_000;
    /// <summary>正式替换模式允许同步删除的旧目标已分配空间上限。</summary>
    public const long MaxExistingTargetAllocatedBytes = 512L * 1024 * 1024;
    /// <summary>暂存批次命令上限；更大任务必须走后续 bulk/异步能力。</summary>
    public const int MaxStagingBatches = 1_000;

    private const int BatchSize = 500;
    private const int TargetTransferBatchSize = 5_000;
    // SQL Server 单命令最多 2100 个参数，保留 100 个余量给未来固定参数。
    private const int SqlServerBatchParameterLimit = 2_000;
    // 避免极宽 MySQL 结果构造数万占位符；同时仍受 500 行与 4 MiB 双重上限。
    private const int MySqlBatchParameterLimit = 20_000;
    private const int CommandTimeoutSeconds = 60;
    private const int TotalExecutionTimeoutSeconds = 5 * 60;
    private const int CleanupTimeoutSeconds = 10;

    private readonly ILogger<EtlPipelineRunner> _logger;
    private readonly IEtlRelationalConnectionFactory _connectionFactory;
    private readonly IEtlTargetReceiptStore _receiptStore;

    public EtlPipelineRunner(
        ILogger<EtlPipelineRunner> logger,
        IEtlRelationalConnectionFactory connectionFactory,
        IEtlTargetReceiptStore? receiptStore = null)
    {
        _logger = logger;
        _connectionFactory = connectionFactory;
        _receiptStore = receiptStore ??
            new EtlTargetReceiptStore(connectionFactory);
    }

    /// <summary>仅供 runner 低层测试的 manual-v0 兼容入口；生产端口强制传执行上下文。</summary>
    public Task<EtlRunResult> RunAsync(
        DataSource source,
        DataSource target,
        string? transformConfig,
        CancellationToken ct = default)
    {
        var cfg = EtlTransformConfig.Parse(transformConfig);
        if (cfg.ReconciliationStrategy != EtlReconciliationStrategy.ManualV0)
        {
            throw InvalidInput();
        }

        return RunAsync(
            new EtlExecutionContext(
                Guid.Empty,
                Guid.Empty,
                new string('0', PipelineExecutionConsts.Sha256HexLength),
                VerifiedTargetResourceHash: null,
                ExecutionContractHash: null,
                TargetCoordinationIdentityHash: null,
                Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(cfg.SourceQuery))),
                cfg.TargetTable,
                cfg.Mode,
                TargetReceipt: null),
            source,
            target,
            transformConfig,
            ct);
    }

    public async Task<EtlRunResult> RunAsync(
        EtlExecutionContext executionContext,
        DataSource source,
        DataSource target,
        string? transformConfig,
        CancellationToken ct = default)
    {
        if (source.Type != DataSourceType.RelationalDatabase || target.Type != DataSourceType.RelationalDatabase)
        {
            throw InvalidInput();
        }

        var cfg = EtlTransformConfig.Parse(transformConfig);
        var receiptEnabled = cfg.ReconciliationStrategy ==
            EtlReconciliationStrategy.TargetReceiptV1;
        if (receiptEnabled != (executionContext.TargetReceipt != null) ||
            (receiptEnabled && string.IsNullOrWhiteSpace(
                executionContext.ExecutionContractHash)) ||
            (receiptEnabled && string.IsNullOrWhiteSpace(
                executionContext.TargetCoordinationIdentityHash)) ||
            (!receiptEnabled && executionContext.ExecutionContractHash != null) ||
            (!receiptEnabled &&
             executionContext.TargetCoordinationIdentityHash != null) ||
            executionContext.WriteMode != cfg.Mode ||
            !string.Equals(
                executionContext.SourceQueryHash,
                Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(cfg.SourceQuery))),
                StringComparison.Ordinal))
        {
            throw InvalidInput();
        }
        RelationalDataSourceConnection.Probe sourceProbe;
        RelationalDataSourceConnection.Probe targetProbe;
        try
        {
            sourceProbe = RelationalDataSourceConnection.Parse(source.Config);
            targetProbe = RelationalDataSourceConnection.Parse(target.Config);
        }
        catch (JsonException)
        {
            throw InvalidInput();
        }
        catch (InvalidOperationException)
        {
            throw InvalidInput();
        }

        if (!IsSupportedProvider(sourceProbe) || !IsSupportedProvider(targetProbe))
        {
            throw InvalidInput();
        }

        // 历史脏配置也必须在最终执行边界重验，且须在创建任何外部连接之前完成。
        EtlSourceQueryGuard.Validate(cfg.SourceQuery, sourceProbe.NormalizedProvider);
        // 请求取消/执行权丢失可以终止提交前工作；目标 COMMIT 一旦开始则只能由独立的
        // 总期限取消。这样既不把客户端断线误报为未提交，也不会让黑洞连接无限占住容量槽。
        using var totalExecutionDeadline = new CancellationTokenSource(
            TimeSpan.FromSeconds(TotalExecutionTimeoutSeconds));
        using var executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                totalExecutionDeadline.Token);
        var executionCt = executionCancellation.Token;

        EtlRunResult? committedResult = null;
        try
        {
            // ① 以 SequentialAccess 打开源 reader；任何时刻只保留当前有限批次。
            await using var srcConn = _connectionFactory.Create(source.Config, connectTimeoutSeconds: 15);
            await srcConn.OpenAsync(executionCt);
            await EtlProviderSession.ConfigureAsync(
                srcConn,
                sourceProbe,
                CommandTimeoutSeconds,
                executionCt);
            await using var sourceTransaction =
                await BeginSourceReadOnlyTransactionAsync(
                    srcConn,
                    sourceProbe,
                    executionCt);
            await using var sourceCommand = srcConn.CreateCommand();
            sourceCommand.CommandText = cfg.SourceQuery;
            sourceCommand.CommandTimeout = CommandTimeoutSeconds;
            sourceCommand.Transaction = sourceTransaction;
            await using var reader = await sourceCommand.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
                executionCt);

            var projection = ResolveColumnProjection(reader, cfg, targetProbe);
            var columns = projection.TargetColumns;
            EtlReaderBoundary.ValidatePreReadSchema(reader, sourceProbe);
            var maxRowsPerBatch = GetMaxRowsPerBatch(columns.Length, targetProbe);
            var maxRowsForShape = GetMaxRowsForShape(
                Math.Max(columns.Length, projection.SourceFieldCount),
                maxRowsPerBatch);

            // ② 目标连接仅在源查询已成功返回合法 schema 后打开；先做引擎/trigger/
            // packet 预检，再创建会话临时暂存表，尚不修改正式目标表。
            var targetCleanupState = new EtlTargetConnectionCleanupState();
            await using var targetConnectionLease = new EtlTargetConnectionLease(
                _connectionFactory.CreateTarget(
                    target.Config,
                    connectTimeoutSeconds: 15,
                    commandTimeoutSeconds: CommandTimeoutSeconds),
                targetCleanupState,
                CleanupTimeoutSeconds,
                _logger);
            var tgtConn = targetConnectionLease.Connection;
            await tgtConn.OpenAsync(executionCt);
            await EtlProviderSession.ConfigureAsync(
                tgtConn,
                targetProbe,
                CommandTimeoutSeconds,
                executionCt);
            var upsertSpec = cfg.UpsertSpec;
            var upsertPlan = upsertSpec == null
                ? null
                : EtlTargetUpsertPreflight.ResolveProjection(columns, upsertSpec);
            var targetPreparation = await EtlTargetPreflight.PrepareAsync(
                tgtConn,
                cfg.TargetTable,
                columns,
                targetProbe,
                cfg.Mode == EtlWriteMode.Truncate,
                MaxBatchPayloadBytes,
                CommandTimeoutSeconds,
                executionCt,
                upsertSpec);
            EtlTargetReceiptStore.EnsureBusinessTargetIsNotReserved(
                targetPreparation.CanonicalTargetTable,
                targetProbe,
                tgtConn.Database);
            if (receiptEnabled && !string.Equals(
                    executionContext.CanonicalTargetTable,
                    targetPreparation.CanonicalTargetTable,
                    StringComparison.Ordinal))
            {
                throw InvalidInput();
            }
            var stage = await EtlTargetStagingWriter.CreateAsync(
                tgtConn,
                targetPreparation.CanonicalTargetTable,
                columns,
                targetProbe,
                CommandTimeoutSeconds,
                executionCt);
            long readRows = 0;
            try
            {
                var rows = new List<object?[]>(maxRowsPerBatch);
                using var batchDigest = receiptEnabled
                    ? new EtlReceiptHashes.BatchDigest(columns)
                    : null;
                long batchPayloadBytes = 0;
                var totalPayloadBudget = new EtlPayloadBudget();
                long nextStageRowId = 1;

                while (await reader.ReadAsync(executionCt))
                {
                    readRows++;
                    if (readRows > maxRowsForShape)
                    {
                        throw InvalidInput();
                    }

                    var row = await EtlReaderBoundary.ReadRowAsync(
                        reader,
                        projection.SourceFieldCount,
                        executionCt);
                    if (row.PayloadBytes >
                        targetPreparation.BatchPayloadLimit)
                    {
                        throw InvalidInput();
                    }

                    totalPayloadBudget.Add(row.PayloadBytes);

                    if (rows.Count > 0 &&
                        (rows.Count >= maxRowsPerBatch ||
                         batchPayloadBytes + row.PayloadBytes >
                            targetPreparation.BatchPayloadLimit))
                    {
                        await EtlTargetStagingWriter.InsertBatchAsync(
                            tgtConn,
                            stage,
                            nextStageRowId,
                            columns,
                            rows,
                            targetProbe,
                            CommandTimeoutSeconds,
                            executionCt);
                        if (batchDigest != null)
                        {
                            foreach (var stagedRow in rows)
                            {
                                batchDigest.AddRow(stagedRow);
                            }
                        }
                        nextStageRowId += rows.Count;
                        rows.Clear();
                        batchPayloadBytes = 0;
                    }

                    rows.Add(projection.ProjectRow(row.Values));
                    batchPayloadBytes += row.PayloadBytes;
                }

                if (rows.Count > 0)
                {
                    await EtlTargetStagingWriter.InsertBatchAsync(
                        tgtConn,
                        stage,
                        nextStageRowId,
                        columns,
                        rows,
                        targetProbe,
                        CommandTimeoutSeconds,
                        executionCt);
                    if (batchDigest != null)
                    {
                        foreach (var stagedRow in rows)
                        {
                            batchDigest.AddRow(stagedRow);
                        }
                    }
                    nextStageRowId += rows.Count;
                }

                if (nextStageRowId != readRows + 1)
                {
                    throw InvalidInput();
                }

                // 先彻底结束 reader / 源只读事务，再开启正式表事务。即使源查询经 view/
                // synonym/linked server 间接读取目标，也不会再与 DELETE 形成自等待环。
                await reader.DisposeAsync();
                if (sourceTransaction != null)
                {
                    await sourceTransaction.CommitAsync(executionCt);
                }

                if (upsertPlan != null && readRows > 0)
                {
                    await EtlTargetUpsertWriter.VerifyStageKeysAsync(
                        tgtConn,
                        stage,
                        upsertPlan,
                        targetProbe,
                        CommandTimeoutSeconds,
                        executionCt);
                }

                executionCt.ThrowIfCancellationRequested();
                var receiptEvidence = executionContext.TargetReceipt == null
                    ? null
                    : new EtlTargetReceiptEvidence(
                        executionContext.RunId,
                        executionContext.PipelineId,
                        executionContext.TargetReceipt.ResourceInstanceId,
                        executionContext.LegacyTargetResourceHash,
                        executionContext.VerifiedTargetResourceHash ??
                            throw InvalidInput(),
                        executionContext.ExecutionContractHash ??
                            throw InvalidInput(),
                        executionContext.SourceQueryHash,
                        EtlReceiptHashes.ComputeSchemaHash(
                            targetPreparation.SchemaFingerprint),
                        batchDigest!.Complete(readRows),
                        cfg.Mode,
                        readRows);
                if (cfg.Mode == EtlWriteMode.Truncate ||
                    readRows > 0 ||
                    receiptEvidence != null)
                {
                    await EtlTargetAtomicWriter.ApplyAsync(
                        tgtConn,
                        stage,
                        targetPreparation.CanonicalTargetTable,
                        columns,
                        readRows,
                        cfg.Mode == EtlWriteMode.Truncate,
                        TargetTransferBatchSize,
                        MaxExistingTargetRows,
                        MaxExistingTargetAllocatedBytes,
                        targetPreparation.SchemaFingerprint,
                        targetProbe,
                        CommandTimeoutSeconds,
                        CleanupTimeoutSeconds,
                        _logger,
                        targetCleanupState,
                        _receiptStore,
                        receiptEvidence,
                        executionContext.TargetCoordinationIdentityHash,
                        totalExecutionDeadline.Token,
                        executionCt,
                        upsertSpec,
                        upsertPlan);
                    // ApplyAsync 只有在 COMMIT ACK 已收到后才返回。从此刻起，后续临时表/
                    // transaction/connection 释放或日志异常都不得把已提交事实翻成 Failed。
                    committedResult = new EtlRunResult(
                        readRows,
                        cfg.Mode == EtlWriteMode.Truncate,
                        receiptEvidence);
                }
            }
            finally
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(
                        TimeSpan.FromSeconds(CleanupTimeoutSeconds));
                    await EtlTargetStagingWriter.DropAsync(
                        tgtConn,
                        stage,
                        targetProbe,
                        CommandTimeoutSeconds,
                        cleanup.Token);
                }
                catch (Exception cleanupException)
                {
                    // 正式事务可能已经提交；临时表清理失败绝不能把成功翻成假失败。
                    // 连接销毁/连接池 reset 仍会回收会话临时表。
                    _logger.LogWarning(
                        "ETL 临时暂存表清理失败 exceptionType={ExceptionType}",
                        cleanupException.GetType().FullName);
                }
            }

            _logger.LogInformation(
                "ETL 完成:{Rows} 行 → {Table}(truncate={Truncated})",
                readRows,
                targetPreparation.CanonicalTargetTable,
                cfg.Mode == EtlWriteMode.Truncate);
            return new EtlRunResult(
                readRows,
                cfg.Mode == EtlWriteMode.Truncate,
                committedResult?.ReceiptEvidence);
        }
        catch (Exception postCommitException) when (committedResult != null)
        {
            LogPostCommitCleanupFailureNoThrow(
                postCommitException.GetType().FullName ??
                postCommitException.GetType().Name);
            return committedResult;
        }
    }

}
