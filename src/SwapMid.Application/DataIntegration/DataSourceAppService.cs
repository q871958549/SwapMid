using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.DataIntegration;

[Authorize(SwapMidPermissions.DataIntegration.DataSources.Default)]
public class DataSourceAppService :
    CrudAppService<DataSource, DataSourceDto, Guid, GetDataSourceListInput, DataSourceCreateUpdateDto>,
    IDataSourceAppService
{
    private readonly IDataSourceRepository _dataSourceRepository;

    public DataSourceAppService(
        IRepository<DataSource, Guid> repository,
        IDataSourceRepository dataSourceRepository)
        : base(repository)
    {
        _dataSourceRepository = dataSourceRepository;
        GetPolicyName = SwapMidPermissions.DataIntegration.DataSources.Default;
        GetListPolicyName = SwapMidPermissions.DataIntegration.DataSources.Default;
        CreatePolicyName = SwapMidPermissions.DataIntegration.DataSources.Create;
        UpdatePolicyName = SwapMidPermissions.DataIntegration.DataSources.Update;
        DeletePolicyName = SwapMidPermissions.DataIntegration.DataSources.Delete;
    }

    public override async Task<PagedResultDto<DataSourceDto>> GetListAsync(GetDataSourceListInput input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataSources.Default);
        await CheckGetListPolicyAsync();

        var query = await Repository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
            query = query.Where(x => x.Name.Contains(input.Filter));

        if (input.Type.HasValue)
            query = query.Where(x => x.Type == input.Type.Value);

        query = query.ApplyTimeRange(input.StartTime, input.EndTime, input.TimeType);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            .ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "name", logger: Logger)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<DataSourceDto>(
            totalCount,
            items.Select(DataSourceConfigProtector.ToResponseDto).ToList());
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）。此前硬编码 OrderBy(Name) 完全忽略 input.Sorting。
    // 默认序保持数据源名升序不变。
    // 🔴 Config **故意不进白名单**：它是连接配置（含密码等秘密），响应侧已由 DataSourceConfigProtector
    // 脱敏，允许按它排序等于把脱敏字段的原始值顺序泄露出去（同 EmployeeProfile 敏感字段的处理）。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<DataSource>, bool, IOrderedQueryable<DataSource>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<DataSource>, bool, IOrderedQueryable<DataSource>>>
        {
            ["name"] = (q, d) => d ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
            ["type"] = (q, d) => d ? q.OrderByDescending(x => x.Type) : q.OrderBy(x => x.Type),
            ["description"] = (q, d) => d ? q.OrderByDescending(x => x.Description) : q.OrderBy(x => x.Description),
            ["isenabled"] = (q, d) => d ? q.OrderByDescending(x => x.IsEnabled) : q.OrderBy(x => x.IsEnabled),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
            ["lastmodificationtime"] = (q, d) => d ? q.OrderByDescending(x => x.LastModificationTime) : q.OrderBy(x => x.LastModificationTime),
        };

    public override async Task<DataSourceDto> GetAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataSources.Default);
        await CheckGetPolicyAsync();

        var ds = await Repository.GetAsync(id);
        return DataSourceConfigProtector.ToResponseDto(ds);
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Create)]
    public override async Task<DataSourceDto> CreateAsync(DataSourceCreateUpdateDto input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataSources.Create);
        await CheckCreatePolicyAsync();

        if (await _dataSourceRepository.NameExistsAsync(input.Name))
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.DataSourceNameAlreadyExists)
                .WithData("name", input.Name);

        var config = NormalizeConfigForWrite(input.Type, input.Config);
        var ds = new DataSource(GuidGenerator.Create(), input.Name, input.Type, config, input.Description);
        if (!input.IsEnabled) ds.Disable();

        await Repository.InsertAsync(ds, autoSave: true);
        return DataSourceConfigProtector.ToResponseDto(ds);
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Update)]
    public override async Task<DataSourceDto> UpdateAsync(Guid id, DataSourceCreateUpdateDto input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataSources.Update);
        await CheckUpdatePolicyAsync();

        var ds = await Repository.GetAsync(id);

        if (await _dataSourceRepository.NameExistsAsync(input.Name, excludeId: id))
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.DataSourceNameAlreadyExists)
                .WithData("name", input.Name);

        var config = MergeConfigForWrite(ds.Type, ds.Config, input.Type, input.Config);
        EnsureEstablishedTargetResourceGroupIsStable(
            ds,
            input.Type,
            config);
        ds.Update(input.Name, input.Type, config, input.Description);
        if (input.IsEnabled) ds.Enable(); else ds.Disable();

        await Repository.UpdateAsync(ds, autoSave: true);
        return DataSourceConfigProtector.ToResponseDto(ds);
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Delete)]
    public override async Task DeleteAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataSources.Delete);
        await CheckDeletePolicyAsync();
        var dataSource = await Repository.GetAsync(id);

        // targetResourceGroup 是历史 Running/OutcomeUnknown 的永久隔离身份；软删同样会让
        // 默认数据过滤器隐藏核验所需的目标凭据，使 durable quarantine 无法再收敛。
        // 已建立稳定组的数据源只能禁用，物理归属迁移应新建数据源并保留旧记录。
        if (dataSource.Type == DataSourceType.RelationalDatabase &&
            !string.IsNullOrWhiteSpace(
                TryReadEstablishedTargetResourceGroup(dataSource.Config)))
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .DataSourceTargetResourceGroupImmutable);
        }

        await Repository.DeleteAsync(dataSource, autoSave: true);
    }

    private static string NormalizeConfigForWrite(DataSourceType type, string config)
    {
        try
        {
            return DataSourceConfigProtector.NormalizeForWrite(type, config);
        }
        catch (DataSourceConfigFormatException)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration.DataSourceConfigInvalid);
        }
    }

    private static string MergeConfigForWrite(
        DataSourceType storedType,
        string storedConfig,
        DataSourceType proposedType,
        string proposedConfig)
    {
        try
        {
            return DataSourceConfigProtector.MergeForWrite(
                storedType,
                storedConfig,
                proposedType,
                proposedConfig);
        }
        catch (DataSourceCredentialScopeChangedException)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration.DataSourceCredentialScopeChanged);
        }
        catch (DataSourceConfigReplacementRequiredException)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration.DataSourceConfigReplacementRequired);
        }
        catch (DataSourceConfigFormatException)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration.DataSourceConfigInvalid);
        }
    }

    private static void EnsureEstablishedTargetResourceGroupIsStable(
        DataSource stored,
        DataSourceType proposedType,
        string proposedConfig)
    {
        if (stored.Type != DataSourceType.RelationalDatabase)
        {
            return;
        }

        var storedGroup = TryReadEstablishedTargetResourceGroup(stored.Config);
        if (string.IsNullOrWhiteSpace(storedGroup))
        {
            // 旧配置允许一次性补齐稳定组；从此进入不可变边界。
            return;
        }

        var proposedGroup = proposedType == DataSourceType.RelationalDatabase
            ? RelationalDataSourceConnection.Parse(proposedConfig)
                .TargetResourceGroup?.Trim()
            : null;
        if (string.Equals(
                storedGroup,
                proposedGroup,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 资源组也是历史 Running/OutcomeUnknown 快照的隔离身份。即使当前已无管道引用，
        // 改组也会让后续引用绕过旧快照；物理归属变化只能新建 DataSource 再迁移引用。
        throw new BusinessException(
            SwapMidDomainErrorCodes.DataIntegration
                .DataSourceTargetResourceGroupImmutable);
    }

    private static string? TryReadEstablishedTargetResourceGroup(
        string? storedConfig)
    {
        if (!DataSourceConfigJson.TryParseObject(storedConfig, out var root))
        {
            // 历史损坏配置没有可验证的稳定组；允许管理员通过已有的完整替换边界修复，
            // 但绝不从损坏 JSON 猜测或恢复任何值。
            return null;
        }

        try
        {
            var candidate = DataSourceConfigJson.ReadOptionalStringStrict(
                    root,
                    "targetResourceGroup")
                ?.Trim();
            return candidate != null &&
                   RelationalDataSourceConfigContract
                       .IsValidTargetResourceGroup(candidate)
                    ? candidate
                    : null;
        }
        catch (DataSourceConfigFormatException)
        {
            // 非字符串旧值同样不构成已建立的稳定组；proposedConfig 已在调用前严格规范化。
            return null;
        }
    }

    // 保存前先验证连通性：解析配置 JSON → 按 provider 建连接 → OpenAsync + SELECT 1。
    // 失败不抛 500，只返回稳定安全文案；驱动异常原文可能含凭据，禁止进响应或日志。
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Create)]
    public async Task<TestConnectionResultDto> TestConnectionAsync(DataSourceTestConnectionInput input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataSources.Create);

        if (input.Type != DataSourceType.RelationalDatabase)
            return new TestConnectionResultDto { Success = false, Message = "目前仅支持关系数据库类型的连接测试。" };

        RelationalDataSourceConnection.Probe probe;
        try
        {
            // 与持久化/ETL 共用同一强类型规范化和连接工厂，避免连接测试形成
            // 一条更宽松、可被畸形 host/provider 绕过的旁路。
            var normalized = DataSourceConfigProtector.NormalizeForWrite(input.Type, input.Config);
            probe = RelationalDataSourceConnection.Parse(normalized);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                "数据源连接配置解析失败（type={DataSourceType}, exceptionType={ExceptionType}）",
                input.Type,
                ex.GetType().Name);
            return new TestConnectionResultDto { Success = false, Message = "连接配置格式无效。" };
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await using var connection = RelationalDataSourceConnection.Create(
                probe,
                connectTimeoutSeconds: 8);
            await connection.OpenAsync();
            await connection.ExecuteScalarAsync<int>("SELECT 1");

            sw.Stop();
            return new TestConnectionResultDto { Success = true, Message = "连接成功", ElapsedMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            sw.Stop();
            // 不记录异常 Message/ConnectionString，也不向客户端回传驱动原文；
            // 两者都可能携带用户名、连接串或签名参数。
            Logger.LogWarning(
                "数据源连接测试失败（type={DataSourceType}, exceptionType={ExceptionType}, elapsedMs={ElapsedMs}）",
                input.Type,
                ex.GetType().Name,
                sw.ElapsedMilliseconds);
            return new TestConnectionResultDto
            {
                Success = false,
                Message = "连接失败，请检查主机、端口、数据库、账号及凭据配置。",
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
    }

    // 测试已保存的数据源：只有 provider/host/port/database/username/TLS 姿态均未变化时，
    // 密码留空才沿用库中原密码；目标或安全姿态变化必须显式提交新密码。
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Update)]
    public async Task<TestConnectionResultDto> TestConnectionByIdAsync(Guid id, DataSourceTestConnectionInput input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataSources.Update);

        var ds = await Repository.GetAsync(id);
        var config = MergeConfigForWrite(ds.Type, ds.Config, input.Type, input.Config);
        return await TestConnectionAsync(new DataSourceTestConnectionInput { Type = input.Type, Config = config });
    }
}
