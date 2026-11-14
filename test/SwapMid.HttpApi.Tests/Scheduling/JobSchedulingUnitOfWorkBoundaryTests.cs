using System.Net;
using System.Net.Http.Json;
using System.Threading;
using SwapMid.DataIntegration;
using SwapMid.Permissions;
using SwapMid.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.HttpApi.Tests.Scheduling;

public class JobSchedulingUnitOfWorkBoundaryTests
{
    [Fact]
    public async Task 手工执行MVC入口必须在ActionFilter初始化前禁用工作单元()
    {
        var schedulingService =
            Substitute.For<IJobSchedulingAppService>();
        IUnitOfWorkManager? unitOfWorkManager = null;
        bool? observedAmbientUnitOfWork = null;
        schedulingService
            .RunAsync(Arg.Any<RunScheduledJobInput>())
            .Returns(_ =>
            {
                observedAmbientUnitOfWork =
                    unitOfWorkManager!.Current != null;
                return Task.FromResult(new RunScheduledJobResultDto
                {
                    Success = true,
                    Summary = "contract-test",
                });
            });

        using var host = new ContractTestHost(
            jobSchedulingService: schedulingService,
            enableUnitOfWorkPipeline: true);
        unitOfWorkManager =
            host.Services.GetRequiredService<IUnitOfWorkManager>();
        using var client = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.SystemManagement
                .ScheduledJobs.Manage);

        var response = await client.PostAsJsonAsync(
            "/api/v1/scheduled-jobs/run",
            new RunScheduledJobInput
            {
                Key = ScheduledJobKeys.AuditLogCleanup,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        observedAmbientUnitOfWork.ShouldBe(
            false,
            "Controller Action 必须在 ABP UoW Filter 初始化保留工作单元前显式禁用");
    }

    [Fact]
    public async Task 禁用工作单元的MVC入口在内部短事务触发TryBeginReserved后仍不得残留外层工作单元()
    {
        IUnitOfWorkManager? unitOfWorkManager = null;
        bool? ambientAfterInnerShortUow = null;
        var pipelineService = Substitute.For<IDataPipelineAppService>();
        var expectedId = Guid.NewGuid();
        var expectedRunId = Guid.NewGuid();

        pipelineService
            .ExecuteAsync(expectedId, expectedRunId, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var uowManager = unitOfWorkManager!;
                using (var shortUow = uowManager.Begin(requiresNew: true, isTransactional: false))
                {
                    uowManager.TryBeginReserved(
                        UnitOfWork.UnitOfWorkReservationName,
                        new AbpUnitOfWorkOptions());
                    await shortUow.CompleteAsync();
                }

                ambientAfterInnerShortUow = uowManager.Current != null;
                return new PipelineExecutionDto
                {
                    Id = expectedRunId,
                    PipelineId = expectedId,
                };
            });

        using var host = new ContractTestHost(
            enableUnitOfWorkPipeline: true,
            configureServices: services =>
            {
                services.AddSingleton(pipelineService);
            });
        unitOfWorkManager = host.Services.GetRequiredService<IUnitOfWorkManager>();
        using var client = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.DataIntegration.DataPipelines.Default,
            SwapMidPermissions.DataIntegration.DataPipelines.Execute);

        var response = await client.PostAsync(
            $"/api/v1/data-pipelines/{expectedId}/execute?runId={expectedRunId}",
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ambientAfterInnerShortUow.ShouldBe(
            false,
            "AbpUnitOfWorkMiddleware 不得为 [UnitOfWork(IsDisabled = true)] 端点预留 _AbpActionUnitOfWork，否则内部仓储拦截器 TryBeginReserved 会激活外层保留 UoW");
    }
}
