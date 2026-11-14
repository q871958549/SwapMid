using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.AuditLogging;
using Volo.Abp.Auditing;
using Volo.Abp.Data;
using Xunit;

namespace SwapMid.SystemManagement;

[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class AuditLogParameterDisclosureTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IAuditLogAppService _auditLogAppService;

    public AuditLogParameterDisclosureTests()
    {
        _auditLogRepository = GetRequiredService<IAuditLogRepository>();
        _auditLogAppService = GetRequiredService<IAuditLogAppService>();
    }

    [Fact]
    public async Task GetAsync_Should_Hide_Historical_DataSource_Parameters_But_Keep_Forensic_Metadata()
    {
        const string historicalSecret =
            """{"input":{"config":{"password":"historical-secret"}}}""";
        const string ordinaryParameters = """{"id":"visible-id"}""";
        var auditLogId = Guid.NewGuid();
        var executionTime = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var actions = new List<AuditLogAction>
        {
            CreateAction(
                auditLogId,
                "SwapMid.DataIntegration.DataSourceAppService",
                "UpdateAsync",
                historicalSecret,
                executionTime,
                executionDuration: 17),
            CreateAction(
                auditLogId,
                "SwapMid.Controllers.DataIntegration.DataSourceController",
                "TestConnectionAsync",
                historicalSecret,
                executionTime.AddMilliseconds(1),
                executionDuration: 5),
            CreateAction(
                auditLogId,
                "SwapMid.DataIntegration.DataPipelineAppService",
                "GetAsync",
                ordinaryParameters,
                executionTime.AddMilliseconds(2),
                executionDuration: 3),
        };
        var auditLog = new AuditLog(
            id: auditLogId,
            applicationName: "test",
            tenantId: null,
            tenantName: null,
            userId: null,
            userName: "auditor",
            executionTime: executionTime,
            executionDuration: 20,
            clientIpAddress: null,
            clientName: null,
            clientId: null,
            correlationId: "correlation-id",
            browserInfo: null,
            httpMethod: "PUT",
            url: "/api/v1/data-sources/id",
            httpStatusCode: 200,
            impersonatorUserId: null,
            impersonatorUserName: null,
            impersonatorTenantId: null,
            impersonatorTenantName: null,
            extraPropertyDictionary: new ExtraPropertyDictionary(),
            entityChanges: new List<EntityChange>(),
            actions: actions,
            exceptions: null,
            comments: null);

        await WithUnitOfWorkAsync(() =>
            _auditLogRepository.InsertAsync(auditLog, autoSave: true));

        var result = await _auditLogAppService.GetAsync(auditLogId);

        result.UserName.ShouldBe("auditor");
        result.CorrelationId.ShouldBe("correlation-id");
        result.Actions.ShouldNotBeNull();
        var sensitiveAction = result.Actions!.Single(x => x.MethodName == "UpdateAsync");
        sensitiveAction.Parameters.ShouldBe("{}");
        sensitiveAction.Parameters!.ShouldNotContain("historical-secret");
        sensitiveAction.ServiceName.ShouldBe(
            "SwapMid.DataIntegration.DataSourceAppService");
        sensitiveAction.ExecutionTime.ShouldBe(executionTime);
        sensitiveAction.ExecutionDuration.ShouldBe(17);

        var controllerAction = result.Actions.Single(x => x.MethodName == "TestConnectionAsync");
        controllerAction.Parameters.ShouldBe("{}");
        controllerAction.ServiceName.ShouldBe(
            "SwapMid.Controllers.DataIntegration.DataSourceController");

        result.Actions.Single(x => x.MethodName == "GetAsync").Parameters
            .ShouldBe(ordinaryParameters);
    }

    private static AuditLogAction CreateAction(
        Guid auditLogId,
        string serviceName,
        string methodName,
        string parameters,
        DateTime executionTime,
        int executionDuration)
    {
        return new AuditLogAction(
            Guid.NewGuid(),
            auditLogId,
            new AuditLogActionInfo
            {
                ServiceName = serviceName,
                MethodName = methodName,
                Parameters = parameters,
                ExecutionTime = executionTime,
                ExecutionDuration = executionDuration,
            });
    }
}
