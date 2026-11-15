using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

[Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Default)]
public class DataPipelineSchemaPreflightAppService
    : SwapMidAppService, IDataPipelineSchemaPreflightAppService
{
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly IEtlRelationalConnectionFactory _connectionFactory;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<DataPipelineSchemaPreflightAppService> _logger;

    public DataPipelineSchemaPreflightAppService(
        IRepository<DataSource, Guid> sourceRepository,
        IEtlRelationalConnectionFactory connectionFactory,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<DataPipelineSchemaPreflightAppService> logger)
    {
        _sourceRepository = sourceRepository;
        _connectionFactory = connectionFactory;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public Task<List<ConnectorCapabilityDto>> GetCapabilitiesAsync()
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataPipelines.Default);
        return Task.FromResult(ConnectorCapabilityCatalog.GetAll().ToList());
    }

    [UnitOfWork(IsDisabled = true)]
    public async Task<PipelineSchemaPreflightResultDto> PreflightSchemaAsync(
        PreflightPipelineSchemaInput input,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataPipelines.Default);
        ArgumentNullException.ThrowIfNull(input);

        var (source, target) = await LoadDataSourcesShortAsync(
            input.SourceId,
            input.TargetId,
            cancellationToken);

        var result = new PipelineSchemaPreflightResultDto();
        EtlTransformConfig cfg;
        try
        {
            cfg = EtlTransformConfig.Parse(input.TransformConfig);
        }
        catch (BusinessException)
        {
            result.Issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:InvalidTransformConfig",
                Message = "ETL 转换规则不合法：请检查源查询、目标表名、写入模式及主键/水位线配置。",
            });
            result.Compatible = false;
            return result;
        }

        result.WriteMode = FormatWriteMode(cfg);

        if (source.Type != DataSourceType.RelationalDatabase)
        {
            result.SourceProvider = ConnectorCapabilityCatalog.CanonicalProvider(source.Type, null);
            result.Issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "warning",
                Code = "SwapMid:ETL:NonRelationalSource",
                Message = "当前源为非关系型连接器（REST API / 文件），运行时按动态载荷列映射，不支持 SQL Schema 静态探测。",
            });
            result.Compatible = true;
            return result;
        }

        RelationalDataSourceConnection.Probe sourceProbe;
        try
        {
            sourceProbe = RelationalDataSourceConnection.Parse(source.Config);
            result.SourceProvider = ConnectorCapabilityCatalog.CanonicalProvider(
                source.Type,
                sourceProbe.NormalizedProvider);
            EtlSourceQueryGuard.Validate(cfg.SourceQuery, sourceProbe.NormalizedProvider);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Schema 预检源查询语法或配置拒绝 sourceId={SourceId} exceptionType={ExceptionType}",
                source.Id,
                ex.GetType().Name);
            result.Issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:SourceQueryUnsafe",
                Message = "源查询未通过只读单条 SELECT 安全词法守卫，或源连接配置无效。",
            });
            result.Compatible = false;
            return result;
        }

        try
        {
            await using var sourceConn = _connectionFactory.Create(source.Config, connectTimeoutSeconds: 8);
            await sourceConn.OpenAsync(cancellationToken);
            result.SourceColumns = await EtlTargetSchemaInspector.ProbeSourceColumnsAsync(
                sourceConn,
                sourceProbe.NormalizedProvider,
                cfg.SourceQuery,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Schema 预检探测源查询列失败 sourceId={SourceId} exceptionType={ExceptionType}",
                source.Id,
                ex.GetType().Name);
            result.Issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:SourceProbeFailed",
                Message = "无法连接源数据库或源查询语法/对象在源库不可执行，请检查源表及列名。",
            });
            result.Compatible = false;
            return result;
        }

        if (result.SourceColumns.Count == 0)
        {
            result.Issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:EmptySourceProjection",
                Message = "源查询未返回任何具名列，请为表达式列显式指定 AS 列别名。",
            });
        }

        if (target is null)
        {
            result.Issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "warning",
                Code = "SwapMid:ETL:TargetNotSelected",
                Message = "尚未选择目标数据源，仅完成源查询只读列结构探测。",
            });
            result.Compatible = result.Issues.All(i => i.Severity != "error");
            return result;
        }

        if (target.Type != DataSourceType.RelationalDatabase)
        {
            result.TargetProvider = ConnectorCapabilityCatalog.CanonicalProvider(target.Type, null);
            result.Issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:UnsupportedTargetType",
                Message = "目标数据源必须是关系型数据库（PostgreSQL / SQL Server / MySQL）。",
            });
            result.Compatible = false;
            return result;
        }

        RelationalDataSourceConnection.Probe targetProbe;
        try
        {
            targetProbe = RelationalDataSourceConnection.Parse(target.Config);
            result.TargetProvider = ConnectorCapabilityCatalog.CanonicalProvider(
                target.Type,
                targetProbe.NormalizedProvider);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Schema 预检目标连接配置解析失败 targetId={TargetId} exceptionType={ExceptionType}",
                target.Id,
                ex.GetType().Name);
            result.Issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:TargetConfigInvalid",
                Message = "目标数据源连接配置无效。",
            });
            result.Compatible = false;
            return result;
        }

        if (cfg.ReconciliationStrategy == EtlReconciliationStrategy.TargetReceiptV1)
        {
            var receiptCapable = result.TargetProvider is "postgresql" or "sqlserver"
                && targetProbe.Encrypt
                && !targetProbe.TrustServerCertificate;
            if (!receiptCapable)
            {
                result.Issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:ReceiptTargetUnsupported",
                    Message = "机器回执 v1 仅支持启用严格 TLS（encrypt=true, trustServerCertificate=false）的 PostgreSQL 或 SQL Server 目标。",
                });
            }
        }

        try
        {
            await using var targetConn = _connectionFactory.Create(target.Config, connectTimeoutSeconds: 8);
            await targetConn.OpenAsync(cancellationToken);
            result.TargetColumns = await EtlTargetSchemaInspector.InspectTargetTableAsync(
                targetConn,
                targetProbe.NormalizedProvider,
                cfg.TargetTable,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Schema 预检读取目标表结构失败 targetId={TargetId} exceptionType={ExceptionType}",
                target.Id,
                ex.GetType().Name);
            result.Issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:TargetProbeFailed",
                Message = "无法连接目标数据库或读取目标表元数据失败。",
            });
            result.Compatible = false;
            return result;
        }

        EtlSchemaCompatibilityEvaluator.Evaluate(cfg, result);
        result.Compatible = result.Issues.All(i => i.Severity != "error");
        return result;
    }

    private static string FormatWriteMode(EtlTransformConfig cfg)
    {
        var baseMode = cfg.Mode switch
        {
            EtlWriteMode.Truncate => "truncate",
            EtlWriteMode.Upsert => "upsert",
            _ => "append",
        };
        return cfg.ReconciliationStrategy == EtlReconciliationStrategy.TargetReceiptV1
            ? $"{baseMode}-receipt-v1"
            : baseMode;
    }

    private async Task<(DataSource Source, DataSource? Target)> LoadDataSourcesShortAsync(
        Guid sourceId,
        Guid? targetId,
        CancellationToken cancellationToken)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var source = await _sourceRepository.GetAsync(sourceId, cancellationToken: cancellationToken);
        DataSource? target = null;
        if (targetId.HasValue)
        {
            target = await _sourceRepository.GetAsync(targetId.Value, cancellationToken: cancellationToken);
        }
        await uow.CompleteAsync(cancellationToken);
        return (source, target);
    }
}
