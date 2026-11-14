using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

[Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.Default)]
public class PipelineExecutionAppService : SwapMidAppService, IPipelineExecutionAppService
{
    private readonly IPipelineExecutionRepository _executionRepository;
    private readonly DataPipelineExecutionCoordinator _executionCoordinator;
    private readonly PipelineExecutionFailurePolicy _failurePolicy;

    public PipelineExecutionAppService(
        IPipelineExecutionRepository executionRepository,
        DataPipelineExecutionCoordinator executionCoordinator,
        PipelineExecutionFailurePolicy failurePolicy)
    {
        _executionRepository = executionRepository;
        _executionCoordinator = executionCoordinator;
        _failurePolicy = failurePolicy;
    }

    public async Task<PagedResultDto<PipelineExecutionDto>> GetListAsync(GetPipelineExecutionListInput input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.Default);

        var useLegacyStartedRange = input.StartedFrom.HasValue || input.StartedTo.HasValue;
        var startTime = input.StartTime ?? input.StartedFrom;
        var endTime = input.EndTime ?? input.StartedTo;
        var timeType = useLegacyStartedRange ? ListQueryTimeType.StartedAt : input.TimeType;

        var totalCount = await _executionRepository.GetCountAsync(
            input.PipelineId, input.Status, startTime, endTime, timeType);

        var items = await _executionRepository.GetListAsync(
            input.PipelineId, input.Status, startTime, endTime, timeType,
            input.Sorting, input.MaxResultCount, input.SkipCount);

        return new PagedResultDto<PipelineExecutionDto>(totalCount, items.Select(MapToDto).ToList());
    }

    public async Task<PipelineExecutionDto> GetAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.Default);

        var e = await _executionRepository.GetAsync(id);
        if (e == null)
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.ExecutionNotFound)
                .WithData("id", id);
        return MapToDto(e);
    }

    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.ResolveOutcome)]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> ResolveOutcomeAsync(
        Guid id,
        ResolvePipelineExecutionOutcomeInput input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.ResolveOutcome);
        return ResolveOutcomeAsync(id, input, CancellationToken.None);
    }

    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.ResolveOutcome)]
    [UnitOfWork(IsDisabled = true)]
    public async Task<PipelineExecutionDto> ResolveOutcomeAsync(
        Guid id,
        ResolvePipelineExecutionOutcomeInput input,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.ResolveOutcome);
        var resolverId = CurrentUser.Id ??
            throw new AbpAuthorizationException(
                "当前请求未携带有效登录身份");
        var execution = await _executionCoordinator.ResolveOutcomeAsync(
            id,
            input,
            resolverId,
            cancellationToken);
        return MapToDto(execution);
    }

    /// <summary>使用已核验的摘流与目标会话退出证据，受控恢复滞留的运行中执行。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.RecoverRunning)]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> RecoverRunningAsync(
        Guid id,
        RecoverPipelineExecutionInput input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.RecoverRunning);
        return RecoverRunningAsync(id, input, CancellationToken.None);
    }

    /// <summary>使用已核验的摘流与目标会话退出证据，受控恢复滞留的运行中执行。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.RecoverRunning)]
    [UnitOfWork(IsDisabled = true)]
    public async Task<PipelineExecutionDto> RecoverRunningAsync(
        Guid id,
        RecoverPipelineExecutionInput input,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.RecoverRunning);
        var recovererId = CurrentUser.Id ??
            throw new AbpAuthorizationException(
                "当前请求未携带有效登录身份");
        var execution = await _executionCoordinator.RecoverRunningAsync(
            id,
            input,
            recovererId,
            cancellationToken);
        return MapToDto(execution);
    }

    /// <summary>只读核验目标库同事务回执；仅完整匹配时收敛为成功。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.VerifyTargetReceipt)]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> VerifyTargetReceiptAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.VerifyTargetReceipt);
        return VerifyTargetReceiptAsync(id, CancellationToken.None);
    }

    /// <summary>只读核验目标库同事务回执；仅完整匹配时收敛为成功。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.VerifyTargetReceipt)]
    [UnitOfWork(IsDisabled = true)]
    public async Task<PipelineExecutionDto> VerifyTargetReceiptAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.VerifyTargetReceipt);
        var verifierId = CurrentUser.Id ??
            throw new AbpAuthorizationException(
                "当前请求未携带有效登录身份");
        var execution = await _executionCoordinator
            .VerifyTargetReceiptAsync(
                id,
                verifierId,
                cancellationToken);
        return MapToDto(execution);
    }

    private PipelineExecutionDto MapToDto(PipelineExecution e)
    {
        var dto = new PipelineExecutionDto
        {
            Id = e.Id,
            ConcurrencyStamp = e.ConcurrencyStamp,
            PipelineId = e.PipelineId,
            PipelineName = e.PipelineName,
            Status = e.Status,
            TriggerType = e.TriggerType,
            StartedAt = e.StartedAt,
            FinishedAt = e.FinishedAt,
            DurationSeconds = e.FinishedAt.HasValue ? (e.FinishedAt.Value - e.StartedAt).TotalSeconds : null,
            ProcessedRows = e.ProcessedRows,
            CreationTime = e.CreationTime,
        };
        _failurePolicy.PopulateDto(e, dto);
        PipelineExecutionSnapshotMapper.Populate(e, dto);
        return dto;
    }

    /// <summary>执行历史为宿主级共享事实，租户上下文不得裁决或恢复。</summary>
    internal static void EnsureHostContext(
        Guid? tenantId,
        string permissionName)
    {
        if (tenantId.HasValue)
        {
            throw new AbpAuthorizationException(code: permissionName);
        }
    }
}
