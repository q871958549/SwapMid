using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

public sealed partial class DataPipelineExecutionCoordinator
{
    private readonly IDataPipelineRepository _pipelineRepository;
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly IPipelineExecutionRepository _executionRepository;
    private readonly IEtlPipelineRunner _etlRunner;
    private readonly IEtlTargetReceiptStore _targetReceiptStore;
    private readonly PipelineExecutionFailurePolicy _failurePolicy;
    private readonly PipelineFailureNotificationScheduler
        _notificationScheduler;
    private readonly IPipelineTerminalStoreTopologyGuard
        _terminalStoreTopologyGuard;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly IDataPipelineExecutionFence _databaseFence;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly EtlExecutionOptions _options;
    private readonly ILogger<DataPipelineExecutionCoordinator> _logger;

    public DataPipelineExecutionCoordinator(
        IDataPipelineRepository pipelineRepository,
        IRepository<DataSource, Guid> sourceRepository,
        IPipelineExecutionRepository executionRepository,
        IEtlPipelineRunner etlRunner,
        IEtlTargetReceiptStore targetReceiptStore,
        PipelineExecutionFailurePolicy failurePolicy,
        PipelineFailureNotificationScheduler notificationScheduler,
        IPipelineTerminalStoreTopologyGuard terminalStoreTopologyGuard,
        IAbpDistributedLock distributedLock,
        IDataPipelineExecutionFence databaseFence,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        IOptions<EtlExecutionOptions> options,
        ILogger<DataPipelineExecutionCoordinator> logger)
    {
        _pipelineRepository = pipelineRepository;
        _sourceRepository = sourceRepository;
        _executionRepository = executionRepository;
        _etlRunner = etlRunner;
        _targetReceiptStore = targetReceiptStore;
        _failurePolicy = failurePolicy;
        _notificationScheduler = notificationScheduler;
        _terminalStoreTopologyGuard = terminalStoreTopologyGuard;
        _distributedLock = distributedLock;
        _databaseFence = databaseFence;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _options = options.Value;
        _logger = logger;
    }
}
