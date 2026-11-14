using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

[Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Default)]
public class DataPipelineAppService :
    CrudAppService<DataPipeline, DataPipelineDto, Guid, GetDataPipelineListInput, DataPipelineCreateUpdateDto>,
    IDataPipelineAppService
{
    private readonly IDataPipelineRepository _pipelineRepository;
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly DataPipelineExecutionCoordinator _executionCoordinator;
    private readonly PipelineExecutionFailurePolicy _failurePolicy;
    private readonly DataPipelineActivationCoordinator _activationCoordinator;
    private readonly IDataPipelineScheduleSynchronizer _scheduleSynchronizer;

    public DataPipelineAppService(
        IRepository<DataPipeline, Guid> repository,
        IDataPipelineRepository pipelineRepository,
        IRepository<DataSource, Guid> sourceRepository,
        DataPipelineExecutionCoordinator executionCoordinator,
        PipelineExecutionFailurePolicy failurePolicy,
        DataPipelineActivationCoordinator activationCoordinator,
        IDataPipelineScheduleSynchronizer? scheduleSynchronizer = null)
        : base(repository)
    {
        _pipelineRepository = pipelineRepository;
        _sourceRepository = sourceRepository;
        _executionCoordinator = executionCoordinator;
        _failurePolicy = failurePolicy;
        _activationCoordinator = activationCoordinator;
        _scheduleSynchronizer = scheduleSynchronizer ?? NullDataPipelineScheduleSynchronizer.Instance;
        GetPolicyName = SwapMidPermissions.DataIntegration.DataPipelines.Default;
        GetListPolicyName = SwapMidPermissions.DataIntegration.DataPipelines.Default;
        CreatePolicyName = SwapMidPermissions.DataIntegration.DataPipelines.Create;
        UpdatePolicyName = SwapMidPermissions.DataIntegration.DataPipelines.Update;
        DeletePolicyName = SwapMidPermissions.DataIntegration.DataPipelines.Delete;
    }

    public override async Task<PagedResultDto<DataPipelineDto>> GetListAsync(GetDataPipelineListInput input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataPipelines.Default);
        await CheckGetListPolicyAsync();

        var totalCount = await _pipelineRepository.GetCountAsync(
            input.Filter, input.SourceId, input.Status,
            input.StartTime, input.EndTime, input.TimeType);
        var items = await _pipelineRepository.GetListAsync(
            input.Filter, input.SourceId, input.Status,
            input.StartTime, input.EndTime, input.TimeType,
            input.Sorting, input.MaxResultCount, input.SkipCount);

        var sourceIds = items.SelectMany(x => new[] { x.SourceId }.Concat(x.TargetId.HasValue ? new[] { x.TargetId.Value } : Array.Empty<Guid>())).Distinct().ToList();
        var sources = sourceIds.Any()
            ? (await _sourceRepository.GetListAsync(x => sourceIds.Contains(x.Id))).ToDictionary(x => x.Id, x => x.Name)
            : new Dictionary<Guid, string>();

        var dtos = items.Select(p =>
        {
            var dto = p.ToDto();
            sources.TryGetValue(p.SourceId, out var srcName); dto.SourceName = srcName;
            if (p.TargetId.HasValue) { sources.TryGetValue(p.TargetId.Value, out var tgtName); dto.TargetName = tgtName; }
            return dto;
        }).ToList();

        return new PagedResultDto<DataPipelineDto>(totalCount, dtos);
    }

    public override async Task<DataPipelineDto> GetAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataPipelines.Default);
        await CheckGetPolicyAsync();

        var pipeline = await Repository.GetAsync(id);
        var dto = pipeline.ToDto();

        var source = await _sourceRepository.FindAsync(pipeline.SourceId);
        dto.SourceName = source?.Name;

        if (pipeline.TargetId.HasValue)
        {
            var target = await _sourceRepository.FindAsync(pipeline.TargetId.Value);
            dto.TargetName = target?.Name;
        }

        return dto;
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Create)]
    public override async Task<DataPipelineDto> CreateAsync(DataPipelineCreateUpdateDto input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataPipelines.Create);
        await CheckCreatePolicyAsync();

        var normalizedCron = DataPipelineCronValidator.ValidateAndNormalize(
            input.ScheduleEnabled,
            input.ScheduleCron);

        if (await _pipelineRepository.NameExistsAsync(input.Name))
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineNameAlreadyExists)
                .WithData("name", input.Name);

        var pipeline = new DataPipeline(GuidGenerator.Create(), input.Name, input.SourceId, input.TargetId,
            input.Description, input.TransformConfig, normalizedCron);
        if (input.ScheduleEnabled) pipeline.Update(input.Name, input.SourceId, input.TargetId,
            input.Description, input.TransformConfig, normalizedCron, scheduleEnabled: true);

        await Repository.InsertAsync(pipeline, autoSave: true);
        await _scheduleSynchronizer.SyncAsync(
            pipeline.Id,
            pipeline.Status,
            pipeline.ScheduleEnabled,
            pipeline.ScheduleCron);
        return pipeline.ToDto();
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Update)]
    public override async Task<DataPipelineDto> UpdateAsync(Guid id, DataPipelineCreateUpdateDto input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataPipelines.Update);
        await CheckUpdatePolicyAsync();

        var normalizedCron = DataPipelineCronValidator.ValidateAndNormalize(
            input.ScheduleEnabled,
            input.ScheduleCron);

        var pipeline = await Repository.GetAsync(id);

        if (await _pipelineRepository.NameExistsAsync(input.Name, excludeId: id))
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineNameAlreadyExists)
                .WithData("name", input.Name);

        var executionContractChanged =
            pipeline.SourceId != input.SourceId ||
            pipeline.TargetId != input.TargetId ||
            !string.Equals(
                pipeline.TransformConfig,
                input.TransformConfig,
                StringComparison.Ordinal);
        pipeline.Update(input.Name, input.SourceId, input.TargetId,
            input.Description, input.TransformConfig, normalizedCron, input.ScheduleEnabled);
        if (executionContractChanged)
        {
            DataPipelineReceiptActivation.Clear(pipeline);
            if (pipeline.Status == PipelineStatus.Active)
            {
                pipeline.Pause();
            }
        }

        await Repository.UpdateAsync(pipeline, autoSave: true);
        await _scheduleSynchronizer.SyncAsync(
            pipeline.Id,
            pipeline.Status,
            pipeline.ScheduleEnabled,
            pipeline.ScheduleCron);
        return pipeline.ToDto();
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Execute)]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> ExecuteAsync(
        Guid id,
        Guid runId)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .DataPipelines.Execute);
        return ExecuteAsync(id, runId, CancellationToken.None);
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Execute)]
    [UnitOfWork(IsDisabled = true)]
    public async Task<PipelineExecutionDto> ExecuteAsync(
        Guid id,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .DataPipelines.Execute);
        var execution = await _executionCoordinator.ExecuteAsync(
            id,
            runId,
            TriggerType.Manual,
            cancellationToken);
        return MapExecutionToDto(execution);
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Update)]
    [UnitOfWork(IsDisabled = true)]
    public Task ActivateAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .DataPipelines.Update);
        return ActivateAsync(id, CancellationToken.None);
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Update)]
    [UnitOfWork(IsDisabled = true)]
    public async Task ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration
                .DataPipelines.Update);
        await _activationCoordinator.ActivateAsync(id, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Update)]
    public async Task PauseAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataPipelines.Update);
        await CheckUpdatePolicyAsync();

        var pipeline = await Repository.GetAsync(id);
        pipeline.Pause();
        await Repository.UpdateAsync(pipeline, autoSave: true);
        await _scheduleSynchronizer.RemoveAsync(pipeline.Id);
    }

    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Delete)]
    public override async Task DeleteAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.DataPipelines.Delete);
        await CheckDeletePolicyAsync();
        await Repository.DeleteAsync(id, autoSave: true);
        await _scheduleSynchronizer.RemoveAsync(id);
    }

    private PipelineExecutionDto MapExecutionToDto(PipelineExecution e)
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

}
