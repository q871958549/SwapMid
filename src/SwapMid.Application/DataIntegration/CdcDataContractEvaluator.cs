using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SwapMid.Monitoring;
using SwapMid.Notification;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>
/// CDC 数据契约与新鲜度 SLA 评估器：校验新鲜度延迟、必填业务列、上游 Schema 漂移及 WAL/Binlog 滞留字节预算，
/// 上报低基数 Prometheus 指标并在状态跃迁时触发去重告警。
/// </summary>
public sealed class CdcDataContractEvaluator : ITransientDependency
{
    private static readonly JsonSerializerOptions BaselineJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ICdcStreamRepository _streamRepository;
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly PgOutputCdcCaptureService _pgCaptureService;
    private readonly SqlServerCdcCaptureService? _sqlServerCaptureService;
    private readonly MySqlBinlogCdcCaptureService? _mySqlCaptureService;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly INotificationService? _notificationService;
    private readonly ILogger<CdcDataContractEvaluator> _logger;

    public CdcDataContractEvaluator(
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        PgOutputCdcCaptureService pgCaptureService,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<CdcDataContractEvaluator> logger,
        SqlServerCdcCaptureService? sqlServerCaptureService = null,
        MySqlBinlogCdcCaptureService? mySqlCaptureService = null,
        INotificationService? notificationService = null)
    {
        _streamRepository = streamRepository;
        _sourceRepository = sourceRepository;
        _pgCaptureService = pgCaptureService;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
        _sqlServerCaptureService = sqlServerCaptureService;
        _mySqlCaptureService = mySqlCaptureService;
        _notificationService = notificationService;
    }

    public static CdcContractEvaluationDto EvaluateSnapshot(
        CdcStreamDefinition stream,
        DateTime nowUtc,
        long? walLagBytes = null,
        IReadOnlyCollection<string>? observedColumns = null)
    {
        var requiredCols = ParseRequiredColumns(stream.RequiredColumns);
        var freshnessLagSeconds = ComputeFreshnessLagSeconds(stream, nowUtc);
        var violations = new List<CdcContractViolationItemDto>();
        var missingCols = new List<string>();

        if (!stream.HasDataContractConfigured)
        {
            return new CdcContractEvaluationDto
            {
                StreamId = stream.Id,
                StreamName = stream.Name,
                Status = CdcContractStatus.Unconfigured,
                Violated = false,
                FreshnessLagSeconds = freshnessLagSeconds,
                MaxFreshnessLagSeconds = null,
                WalLagBytes = walLagBytes,
                MaxAllowedWalLagBytes = null,
                RequiredColumns = requiredCols,
                MissingRequiredColumns = missingCols,
                SchemaDriftDetected = stream.SchemaDriftDetected,
                Violations = violations,
                EvaluatedAtUtc = nowUtc,
                Summary = "尚未配置数据契约 SLA（可在流编辑弹窗中设置新鲜度阈值、必填列或 WAL 滞留预算）。",
            };
        }

        if (stream.MaxFreshnessLagSeconds is int maxFreshness && stream.Status == CdcStreamStatus.Active)
        {
            if (freshnessLagSeconds.HasValue && freshnessLagSeconds.Value > maxFreshness)
            {
                violations.Add(new CdcContractViolationItemDto
                {
                    Type = "freshness",
                    Severity = "error",
                    Message = $"数据新鲜度滞后 {freshnessLagSeconds.Value}s，超过 SLA 上限 {maxFreshness}s。",
                });
            }
            else if (freshnessLagSeconds.HasValue && freshnessLagSeconds.Value >= (long)(maxFreshness * 0.8))
            {
                violations.Add(new CdcContractViolationItemDto
                {
                    Type = "freshness",
                    Severity = "warning",
                    Message = $"数据新鲜度滞后 {freshnessLagSeconds.Value}s，已达 SLA 上限 {maxFreshness}s 的 80% 警戒线。",
                });
            }
        }

        if (requiredCols.Count > 0)
        {
            var available = BuildObservedColumnSet(stream.SchemaBaselineJson, observedColumns);
            if (available.Count > 0)
            {
                foreach (var col in requiredCols)
                {
                    if (!available.Contains(col))
                    {
                        missingCols.Add(col);
                    }
                }

                if (missingCols.Count > 0)
                {
                    violations.Add(new CdcContractViolationItemDto
                    {
                        Type = "required_columns",
                        Severity = "error",
                        Message = $"上游监控表缺失契约必填列：{string.Join(", ", missingCols)}。",
                    });
                }
            }
            else
            {
                violations.Add(new CdcContractViolationItemDto
                {
                    Type = "required_columns",
                    Severity = "warning",
                    Message = "尚未采集到上游表 Schema 基线，执行预检或激活后将自动核验必填列。",
                });
            }
        }

        if (stream.SchemaDriftDetected)
        {
            violations.Add(new CdcContractViolationItemDto
            {
                Type = "schema_drift",
                Severity = "error",
                Message = string.IsNullOrWhiteSpace(stream.SchemaDriftSummary)
                    ? "检测到未确认的上游表结构 Schema 漂移。"
                    : $"上游 Schema 漂移未确认：{stream.SchemaDriftSummary}",
            });
        }

        if (stream.MaxAllowedWalLagBytes is long maxWal && walLagBytes.HasValue)
        {
            if (walLagBytes.Value > maxWal)
            {
                violations.Add(new CdcContractViolationItemDto
                {
                    Type = "wal_lag",
                    Severity = "error",
                    Message = $"WAL/Binlog 滞留 {FormatBytes(walLagBytes.Value)} 超过预算上限 {FormatBytes(maxWal)}。",
                });
            }
            else if (walLagBytes.Value >= (long)(maxWal * 0.8))
            {
                violations.Add(new CdcContractViolationItemDto
                {
                    Type = "wal_lag",
                    Severity = "warning",
                    Message = $"WAL/Binlog 滞留 {FormatBytes(walLagBytes.Value)} 已达预算上限 {FormatBytes(maxWal)} 的 80%。",
                });
            }
        }

        var hasError = violations.Any(v => string.Equals(v.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var hasWarning = violations.Any(v => string.Equals(v.Severity, "warning", StringComparison.OrdinalIgnoreCase));
        var status = hasError
            ? CdcContractStatus.Breached
            : hasWarning
                ? CdcContractStatus.Warning
                : CdcContractStatus.Healthy;

        var summary = status switch
        {
            CdcContractStatus.Breached => string.Join(" ", violations.Where(v => v.Severity == "error").Select(v => v.Message)),
            CdcContractStatus.Warning => string.Join(" ", violations.Select(v => v.Message)),
            _ => "数据契约 SLA 全部达标（新鲜度、必填列与 Schema 基线一致）。",
        };

        return new CdcContractEvaluationDto
        {
            StreamId = stream.Id,
            StreamName = stream.Name,
            Status = status,
            Violated = hasError,
            FreshnessLagSeconds = freshnessLagSeconds,
            MaxFreshnessLagSeconds = stream.MaxFreshnessLagSeconds,
            WalLagBytes = walLagBytes,
            MaxAllowedWalLagBytes = stream.MaxAllowedWalLagBytes,
            RequiredColumns = requiredCols,
            MissingRequiredColumns = missingCols,
            SchemaDriftDetected = stream.SchemaDriftDetected,
            Violations = violations,
            EvaluatedAtUtc = nowUtc,
            Summary = summary,
        };
    }

    public async Task<CdcContractEvaluationDto> EvaluateAndPersistAsync(
        Guid streamId,
        bool probeLiveUpstream,
        CancellationToken cancellationToken = default)
    {
        var stream = await _streamRepository.GetAsync(streamId, cancellationToken: cancellationToken);
        long? walLagBytes = null;
        IReadOnlyCollection<string>? observedColumns = null;

        if (probeLiveUpstream && stream.HasDataContractConfigured)
        {
            var source = await _sourceRepository.FindAsync(stream.SourceId, cancellationToken: cancellationToken);
            if (source != null)
            {
                try
                {
                    var preflight = await CdcStreamMappingHelpers.DispatchPreflightAsync(
                        _pgCaptureService, _sqlServerCaptureService, _mySqlCaptureService, source, stream, cancellationToken);
                    walLagBytes = preflight.WalLagBytes;

                    var schemaEval = await CdcStreamMappingHelpers.EvaluateSchemaForStreamAsync(
                        _pgCaptureService, _sqlServerCaptureService, _mySqlCaptureService, source, stream, cancellationToken);
                    observedColumns = ExtractColumnsFromBaselineJson(schemaEval.CurrentJson);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "CDC 契约评估实时探测上游失败（回退至已存储基线）streamId={StreamId} exceptionType={Type}",
                        stream.Id, ex.GetType().FullName ?? ex.GetType().Name);
                }
            }
        }

        return await PersistEvaluationCoreAsync(stream, walLagBytes, observedColumns, cancellationToken);
    }

    public async Task TryEvaluateAfterCaptureAsync(
        CdcStreamDefinition stream,
        long? walLagBytes,
        CancellationToken cancellationToken = default)
    {
        if (!stream.HasDataContractConfigured)
        {
            return;
        }

        try
        {
            await PersistEvaluationCoreAsync(stream, walLagBytes, observedColumns: null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "CDC 捕获后契约评估失败 streamId={StreamId} exceptionType={Type}",
                stream.Id, ex.GetType().FullName ?? ex.GetType().Name);
        }
    }

    private async Task<CdcContractEvaluationDto> PersistEvaluationCoreAsync(
        CdcStreamDefinition stream,
        long? walLagBytes,
        IReadOnlyCollection<string>? observedColumns,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var evaluation = EvaluateSnapshot(stream, nowUtc, walLagBytes, observedColumns);

        RecordMetrics(evaluation);

        var previouslyViolated = stream.ContractViolated;
        var summaryChanged = !string.Equals(stream.ContractViolationSummary, evaluation.Summary, StringComparison.Ordinal);

        if (stream.HasDataContractConfigured)
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var latest = await _streamRepository.FindAsync(stream.Id, cancellationToken: cancellationToken);
            if (latest != null)
            {
                latest.RecordContractEvaluation(
                    evaluation.Violated,
                    evaluation.Violated ? evaluation.Summary : null,
                    nowUtc);
                await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
            }

            await uow.CompleteAsync(cancellationToken);

            if (evaluation.Violated && (!previouslyViolated || summaryChanged))
            {
                await TryNotifyAsync(stream, evaluation, recovered: false, cancellationToken);
            }
            else if (!evaluation.Violated && previouslyViolated)
            {
                await TryNotifyAsync(stream, evaluation, recovered: true, cancellationToken);
            }
        }

        return evaluation;
    }

    private async Task TryNotifyAsync(
        CdcStreamDefinition stream,
        CdcContractEvaluationDto evaluation,
        bool recovered,
        CancellationToken cancellationToken)
    {
        if (_notificationService == null)
        {
            return;
        }

        try
        {
            var breachTitle = $"CDC 流「{stream.Name}」数据契约 SLA 违约";
            await _notificationService.SendAsync(
                new NotificationSendRequest
                {
                    Title = recovered ? $"CDC 流「{stream.Name}」数据契约 SLA 已恢复" : breachTitle,
                    Content = evaluation.Summary,
                    Level = recovered ? NotificationLevel.Info : NotificationLevel.Warning,
                    Source = NotificationSources.DataIntegration,
                    ActionPath = "/data-integration/cdc",
                    ActionLabel = "查看 CDC 流",
                    RecoveryOfTitle = recovered ? breachTitle : null,
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "CDC 契约 SLA 通知发送失败 streamId={StreamId} exceptionType={Type}",
                stream.Id, ex.GetType().FullName ?? ex.GetType().Name);
        }
    }

    private static void RecordMetrics(CdcContractEvaluationDto evaluation)
    {
        var outcomeLabel = evaluation.Status switch
        {
            CdcContractStatus.Healthy => "healthy",
            CdcContractStatus.Warning => "warning",
            CdcContractStatus.Breached => "breached",
            _ => "unconfigured",
        };
        PlatformMetrics.CdcContractEvaluationsTotal.WithLabels(outcomeLabel).Inc();

        foreach (var violation in evaluation.Violations.Where(v => v.Severity == "error"))
        {
            if (violation.Type is "freshness" or "required_columns" or "wal_lag" or "schema_drift")
            {
                PlatformMetrics.CdcContractViolationsTotal.WithLabels(violation.Type).Inc();
            }
        }
    }

    internal static long? ComputeFreshnessLagSeconds(CdcStreamDefinition stream, DateTime nowUtc)
    {
        var reference = stream.LastCapturedAt ?? stream.SnapshotCompletedAt;
        if (!reference.HasValue)
        {
            return null;
        }

        var refUtc = reference.Value.Kind == DateTimeKind.Utc
            ? reference.Value
            : DateTime.SpecifyKind(reference.Value, DateTimeKind.Utc);
        var seconds = (long)Math.Max(0, (nowUtc - refUtc).TotalSeconds);
        return seconds;
    }

    private static List<string> ParseRequiredColumns(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static HashSet<string> BuildObservedColumnSet(
        string? baselineJson,
        IReadOnlyCollection<string>? observedColumns)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (observedColumns != null)
        {
            foreach (var col in observedColumns)
            {
                if (!string.IsNullOrWhiteSpace(col)) set.Add(col.Trim());
            }
        }

        if (set.Count == 0 && !string.IsNullOrWhiteSpace(baselineJson))
        {
            foreach (var col in ExtractColumnsFromBaselineJson(baselineJson))
            {
                set.Add(col);
            }
        }

        return set;
    }

    internal static IReadOnlyCollection<string> ExtractColumnsFromBaselineJson(string? baselineJson)
    {
        if (string.IsNullOrWhiteSpace(baselineJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            var tables = JsonSerializer.Deserialize<CdcCapturedTableSchema[]>(baselineJson, BaselineJsonOptions);
            if (tables == null || tables.Length == 0)
            {
                return Array.Empty<string>();
            }

            return tables
                .SelectMany(t => t.Columns)
                .Select(c => c.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double mb = 1024d * 1024d;
        return bytes >= mb
            ? $"{(bytes / mb).ToString("F1", CultureInfo.InvariantCulture)} MiB"
            : $"{bytes} B";
    }
}
