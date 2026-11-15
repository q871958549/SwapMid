using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using SwapMid.DataIntegration;
using SwapMid.EntityFrameworkCore;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace SwapMid.MessageBus;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class PipelineExecutionMessageBusBridgeTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IDataSourceAppService _dataSourceAppService;
    private readonly IDataPipelineAppService _dataPipelineAppService;
    private readonly FakeEtlPipelineRunner _etlRunner;
    private readonly TestDataPipelineExecutionFence _executionFence;
    private readonly TestPipelineTerminalStoreTopologyGuard _topologyGuard;
    private readonly FakeEtlTargetReceiptStore _receiptStore;
    private readonly IBackgroundJobStore _backgroundJobStore;
    private readonly IBackgroundJobSerializer _backgroundJobSerializer;
    private readonly AbpBackgroundJobWorkerOptions _workerOptions;
    private readonly PipelineTerminalEventDispatchJob _dispatchJob;
    private readonly IMessageTopicRepository _topicRepository;
    private readonly IMessageRecordRepository _recordRepository;
    private readonly MessageDeliveryRecorder _deliveryRecorder;

    public PipelineExecutionMessageBusBridgeTests()
    {
        _dataSourceAppService = GetRequiredService<IDataSourceAppService>();
        _dataPipelineAppService = GetRequiredService<IDataPipelineAppService>();
        _etlRunner = (FakeEtlPipelineRunner)GetRequiredService<IEtlPipelineRunner>();
        _executionFence = GetRequiredService<TestDataPipelineExecutionFence>();
        _topologyGuard = GetRequiredService<TestPipelineTerminalStoreTopologyGuard>();
        _receiptStore = GetRequiredService<FakeEtlTargetReceiptStore>();
        _backgroundJobStore = GetRequiredService<IBackgroundJobStore>();
        _backgroundJobSerializer = GetRequiredService<IBackgroundJobSerializer>();
        _workerOptions = GetRequiredService<IOptions<AbpBackgroundJobWorkerOptions>>().Value;
        _dispatchJob = GetRequiredService<PipelineTerminalEventDispatchJob>();
        _topicRepository = GetRequiredService<IMessageTopicRepository>();
        _recordRepository = GetRequiredService<IMessageRecordRepository>();
        _deliveryRecorder = GetRequiredService<MessageDeliveryRecorder>();

        _etlRunner.Reset();
        _executionFence.Reset();
        _topologyGuard.Reset();
        _receiptStore.Reset();
        _deliveryRecorder.ResetSchedules();
    }

    [Fact]
    public async Task Succeeded_Pipeline_Execution_Should_Enqueue_Outbox_And_Idempotently_Publish_To_MessageBus()
    {
        await EnsurePipelineEventsTopicEnabledAsync();
        var pipeline = await CreateActivePipelineAsync("Bridge Succeeded Pipeline");
        _etlRunner.Result = new EtlRunResult(42, TargetTruncated: true);

        var runId = Guid.NewGuid();
        var execution = await _dataPipelineAppService.ExecuteAsync(pipeline.Id, runId);
        execution.Status.ShouldBe(ExecutionStatus.Succeeded);
        execution.ProcessedRows.ShouldBe(42);

        var jobName = BackgroundJobNameAttribute.GetName<PipelineTerminalEventJobArgs>();
        var waitingJobs = await _backgroundJobStore.GetWaitingJobsAsync(
            _workerOptions.ApplicationName,
            maxResultCount: 50);
        var terminalJob = waitingJobs
            .Select(job => (Job: job, Args: TryDeserializeArgs(job, jobName)))
            .Single(item => item.Args?.ExecutionId == runId);

        terminalJob.Args!.PipelineId.ShouldBe(pipeline.Id);
        terminalJob.Args.Status.ShouldBe(ExecutionStatus.Succeeded);
        terminalJob.Args.ProcessedRows.ShouldBe(42);

        // First dispatch inserts MessageRecord and schedules delivery; retry is idempotent by DeterministicEventId.
        await WithUnitOfWorkAsync(() => _dispatchJob.ExecuteAsync(terminalJob.Args));
        await WithUnitOfWorkAsync(() => _dispatchJob.ExecuteAsync(terminalJob.Args));

        var records = await WithUnitOfWorkAsync(() => _recordRepository.GetListAsync(
            x => x.TopicName == IntegrationTopicNames.PipelineEvents));
        var matching = records
            .Where(r => r.Payload.Contains(runId.ToString(), StringComparison.OrdinalIgnoreCase))
            .ShouldHaveSingleItem();

        matching.Subject.ShouldBe("pipeline.execution.succeeded");
        using var payloadDoc = JsonDocument.Parse(matching.Payload);
        payloadDoc.RootElement.GetProperty("executionId").GetGuid().ShouldBe(runId);
        payloadDoc.RootElement.GetProperty("pipelineId").GetGuid().ShouldBe(pipeline.Id);
        payloadDoc.RootElement.GetProperty("status").GetString().ShouldBe("Succeeded");
        payloadDoc.RootElement.GetProperty("processedRows").GetInt64().ShouldBe(42);

        _deliveryRecorder.ScheduledDeliveries.ShouldContain(x => x.MessageRecordId == matching.Id);
    }

    [Fact]
    public async Task Failed_Pipeline_Execution_Should_Publish_Desensitized_Failure_Event_Without_Raw_Exception()
    {
        await EnsurePipelineEventsTopicEnabledAsync();
        var pipeline = await CreateActivePipelineAsync("Bridge Failed Pipeline");
        _etlRunner.ExceptionToThrow = new InvalidOperationException("Host=10.0.0.9;Password=SuperSecret123!");

        var runId = Guid.NewGuid();
        var execution = await _dataPipelineAppService.ExecuteAsync(pipeline.Id, runId);
        execution.Status.ShouldBe(ExecutionStatus.Failed);

        var jobName = BackgroundJobNameAttribute.GetName<PipelineTerminalEventJobArgs>();
        var waitingJobs = await _backgroundJobStore.GetWaitingJobsAsync(
            _workerOptions.ApplicationName,
            maxResultCount: 50);
        var terminalJob = waitingJobs
            .Select(job => TryDeserializeArgs(job, jobName))
            .Single(args => args?.ExecutionId == runId)!;

        await WithUnitOfWorkAsync(() => _dispatchJob.ExecuteAsync(terminalJob));

        var records = await WithUnitOfWorkAsync(() => _recordRepository.GetListAsync(
            x => x.TopicName == IntegrationTopicNames.PipelineEvents));
        var matching = records
            .Where(r => r.Payload.Contains(runId.ToString(), StringComparison.OrdinalIgnoreCase))
            .ShouldHaveSingleItem();

        matching.Subject.ShouldBe("pipeline.execution.failed");
        matching.Payload.ShouldNotContain("SuperSecret123!");
        matching.Payload.ShouldNotContain("10.0.0.9");
        using var payloadDoc = JsonDocument.Parse(matching.Payload);
        payloadDoc.RootElement.GetProperty("status").GetString().ShouldBe("Failed");
        payloadDoc.RootElement.GetProperty("errorCode").GetString().ShouldNotBeNullOrWhiteSpace();
        payloadDoc.RootElement.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private PipelineTerminalEventJobArgs? TryDeserializeArgs(BackgroundJobInfo job, string expectedJobName)
    {
        if (!string.Equals(job.JobName, expectedJobName, StringComparison.Ordinal))
        {
            return null;
        }

        return (PipelineTerminalEventJobArgs)_backgroundJobSerializer.Deserialize(
            job.JobArgs,
            typeof(PipelineTerminalEventJobArgs));
    }

    private async Task EnsurePipelineEventsTopicEnabledAsync()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var existing = await _topicRepository.FirstOrDefaultAsync(
                x => x.Name == IntegrationTopicNames.PipelineEvents);
            if (existing is null)
            {
                await _topicRepository.InsertAsync(
                    new MessageTopic(
                        Guid.NewGuid(),
                        IntegrationTopicNames.PipelineEvents,
                        TopicType.Broadcast,
                        "ETL pipeline terminal events",
                        retryCount: 3,
                        retryIntervalSeconds: 30),
                    autoSave: true);
            }
            else if (!existing.IsEnabled)
            {
                existing.Enable();
                await _topicRepository.UpdateAsync(existing, autoSave: true);
            }
        });
    }

    private async Task<DataPipelineDto> CreateActivePipelineAsync(string namePrefix)
    {
        var group = $"bridge-{Guid.NewGuid():N}"[..24];
        var source = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"{namePrefix} Source {Guid.NewGuid():N}",
            Type = DataSourceType.RelationalDatabase,
            Config = JsonSerializer.Serialize(new
            {
                provider = "postgresql",
                host = "localhost",
                port = 5432,
                database = "SwapMid",
                username = "reader",
                targetResourceGroup = $"{group}-src",
            }),
        });
        var target = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"{namePrefix} Target {Guid.NewGuid():N}",
            Type = DataSourceType.RelationalDatabase,
            Config = JsonSerializer.Serialize(new
            {
                provider = "postgresql",
                host = "localhost",
                port = 5432,
                database = "SwapMid",
                username = "writer",
                targetResourceGroup = $"{group}-dst",
            }),
        });

        var created = await _dataPipelineAppService.CreateAsync(new DataPipelineCreateUpdateDto
        {
            Name = $"{namePrefix} {Guid.NewGuid():N}",
            SourceId = source.Id,
            TargetId = target.Id,
            TransformConfig = JsonSerializer.Serialize(new
            {
                sourceQuery = "SELECT 1 AS Id",
                targetTable = "public.demo_sink",
                mode = "append",
            }),
        });

        await _dataPipelineAppService.ActivateAsync(created.Id);
        return created;
    }
}
