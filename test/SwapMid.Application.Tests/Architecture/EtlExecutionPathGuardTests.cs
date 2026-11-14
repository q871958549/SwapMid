using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwapMid.DataIntegration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace SwapMid.Architecture;

/// <summary>
/// ETL 长路径必须只有一个协调入口，且从 HTTP 到应用契约全链路显式禁用 ambient UoW。
/// 这些约束用源码守卫固定，避免后续新增入口绕过 Running 提交、双层执行权或请求取消。
/// </summary>
public class EtlExecutionPathGuardTests
{
    [Fact]
    public void IEtlPipelineRunner_Should_Only_Be_Consumed_By_Execution_Coordinator()
    {
        var root = LocateRepositoryRoot();
        var scanRoots = new[]
        {
            Path.Combine(root, "src", "SwapMid.Application"),
            Path.Combine(root, "src", "SwapMid.HttpApi"),
            Path.Combine(root, "src", "SwapMid.HttpApi.Host"),
        };
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IEtlPipelineRunner.cs",
            "EtlPipelineRunner.cs",
        };
        var offenders = new List<string>();

        foreach (var file in scanRoots.SelectMany(directory =>
                     Directory.EnumerateFiles(
                         directory,
                         "*.cs",
                         SearchOption.AllDirectories)))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            foreach (var identifier in tree.GetRoot()
                         .DescendantNodes()
                         .OfType<IdentifierNameSyntax>()
                         .Where(identifier =>
                             identifier.Identifier.ValueText ==
                             "IEtlPipelineRunner"))
            {
                if (allowedFiles.Contains(Path.GetFileName(file)) ||
                    identifier.Ancestors()
                        .OfType<ClassDeclarationSyntax>()
                        .Any(declaration => declaration.Identifier.ValueText ==
                            nameof(DataPipelineExecutionCoordinator)))
                {
                    continue;
                }

                offenders.Add(FormatLocation(file, identifier));
            }
        }

        offenders.ShouldBeEmpty(
            "IEtlPipelineRunner 只能由 DataPipelineExecutionCoordinator 注入；" +
            "其他入口必须调用协调器：\n" + string.Join("\n", offenders));

        var coordinator = ParseFile(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "DataPipelineExecutionCoordinator.cs"));
        coordinator.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_etlRunner",
                    },
                    Name.Identifier.ValueText: "RunAsync",
                })
            .ShouldBe(1, "协调器应保持唯一的外部 ETL 调用点");
    }

    [Fact]
    public void Execute_Path_Should_Disable_Uow_And_Http_Action_Should_Accept_CancellationToken()
    {
        var root = LocateRepositoryRoot();
        var files = new[]
        {
            Path.Combine(
                root,
                "src",
                "SwapMid.Application.Contracts",
                "DataIntegration",
                "IDataPipelineAppService.cs"),
            Path.Combine(
                root,
                "src",
                "SwapMid.Application",
                "DataIntegration",
                "DataPipelineAppService.cs"),
            Path.Combine(
                root,
                "src",
                "SwapMid.HttpApi",
                "DataIntegration",
                "DataPipelineController.cs"),
        };
        var offenders = new List<string>();

        foreach (var file in files)
        {
            var methods = ParseFile(file)
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == "ExecuteAsync")
                .ToArray();
            methods.Length.ShouldBe(2, $"{Path.GetFileName(file)} 执行重载数量发生变化");
            offenders.AddRange(methods
                .Where(method => !HasDisabledUnitOfWork(method))
                .Select(method => FormatLocation(file, method)));
        }

        offenders.ShouldBeEmpty(
            "ETL 执行长路径必须逐层标注 [UnitOfWork(IsDisabled = true)]：\n" +
            string.Join("\n", offenders));

        var publicRunIdParameters = files
            .SelectMany(file => ParseFile(file)
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == "ExecuteAsync"))
            .Select(method => method.ParameterList.Parameters.Single(parameter =>
                parameter.Identifier.ValueText == "runId"))
            .ToArray();
        publicRunIdParameters.Length.ShouldBe(6);
        publicRunIdParameters.ShouldAllBe(parameter =>
            parameter.Type != null &&
            parameter.Type.ToString() == "Guid" &&
            parameter.Default == null,
            "所有公开执行重载必须要求非空、无默认值的 Guid RunId；" +
            "服务端生成新 ID 会让响应丢失后的重试重复写目标库");

        var coordinatorRunId = ParseCoordinator()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "ExecuteAsync")
            .ParameterList.Parameters.Single(parameter =>
                parameter.Identifier.ValueText == "requestedRunId");
        coordinatorRunId.Type?.ToString().ShouldBe("Guid");
        coordinatorRunId.Default.ShouldBeNull();

        var controllerFile = files[2];
        var action = ParseFile(controllerFile)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method =>
                method.Identifier.ValueText == "ExecuteAsync" &&
                HasAttribute(method, "HttpPost"));
        action.ParameterList.Parameters.ShouldContain(parameter =>
            parameter.Type != null &&
            parameter.Type.ToString() == "CancellationToken");
        var actionRunId = action.ParameterList.Parameters.Single(parameter =>
            parameter.Identifier.ValueText == "runId");
        actionRunId.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString() is
                "BindRequired" or "BindRequiredAttribute")
            .ShouldBeTrue(
                "HTTP query 必须把 runId 标成必填，避免 Swagger 客户端省略后重试生成新运行");
        action.DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Any(argument =>
                argument.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "cancellationToken")
            .ShouldBeTrue(
                "HTTP action 必须把请求取消信号继续传给应用服务");
    }

    [Fact]
    public void Claim_Should_Read_All_Durable_States_Once_With_Overflow_Sentinel()
    {
        var coordinator = ParseCoordinator();
        var claim = coordinator.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "ClaimAsync");
        var durableStateReads = claim.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => IsExecutionRepositoryCall(
                invocation,
                "GetDurableStatesAsync"))
            .ToArray();

        durableStateReads.Length.ShouldBe(
            1,
            "ClaimAsync 必须用一次查询同时读取 Running 与 OutcomeUnknown，" +
            "禁止分状态查询留下 TOCTOU 窗口");

        var arguments = durableStateReads[0].ArgumentList.Arguments;
        arguments.Count.ShouldBe(2, "耐久状态读取必须显式传入 Max+1 与取消信号");
        arguments[0].Expression.ShouldBeOfType<BinaryExpressionSyntax>()
            .ShouldSatisfyAllConditions(
                expression => expression.IsKind(SyntaxKind.AddExpression)
                    .ShouldBeTrue("扫描上界必须使用 MaxDurableStateScan + 1 作为溢出哨兵"),
                expression => expression.Left
                    .ShouldBeOfType<IdentifierNameSyntax>()
                    .Identifier.ValueText.ShouldBe("MaxDurableStateScan"),
                expression => expression.Right
                    .ShouldBeOfType<LiteralExpressionSyntax>()
                    .Token.ValueText.ShouldBe("1"));
        arguments[1].Expression.ShouldBeOfType<IdentifierNameSyntax>()
            .Identifier.ValueText.ShouldBe("cancellationToken");

        var splitStatusReads = claim.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation =>
                IsExecutionRepositoryCall(invocation, "GetListAsync") ||
                IsExecutionRepositoryCall(invocation, "GetCountAsync"))
            .Select(invocation => invocation.ToString())
            .ToArray();
        splitStatusReads.ShouldBeEmpty(
            "ClaimAsync 禁止分别按状态 Count/List；两次查询之间会产生 TOCTOU 窗口");
    }

    [Fact]
    public void Claim_Should_Replay_Existing_Run_Before_Any_Insert_After_Durable_Read()
    {
        var coordinator = ParseCoordinator();
        var claim = coordinator.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "ClaimAsync");
        var durableRead = claim.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => IsExecutionRepositoryCall(
                invocation,
                "GetDurableStatesAsync"));
        var insert = claim.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => IsExecutionRepositoryCall(
                invocation,
                "InsertAsync"));
        var existingRunGuard = claim.Body!.Statements
            .OfType<IfStatementSyntax>()
            .Single(statement => IsExistingRunGuard(statement.Condition));

        existingRunGuard.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Any(statement => statement.Expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "Claim",
                    },
                    Name.Identifier.ValueText: "Replay",
                },
            } replay && replay.ArgumentList.Arguments.Any(argument =>
                argument.Expression is IdentifierNameSyntax
                {
                    Identifier.ValueText: "existingById",
                }))
            .ShouldBeTrue(
                "durable 快照遗漏刚转终态的同 RunId 时必须保守回放，不能再次 Insert");
        existingRunGuard.SpanStart.ShouldBeGreaterThan(
            durableRead.Span.End,
            "existingById 二次保守判断必须位于 durable 单查询之后");
        insert.SpanStart.ShouldBeGreaterThan(
            existingRunGuard.Span.End,
            "任何 Running Insert 前必须先短路已存在的同 RunId");
    }

    [Fact]
    public void Success_Finalization_Should_Stay_Outside_Runner_Failure_Catch_Boundary()
    {
        var coordinator = ParseCoordinator();
        var runnerCall = coordinator.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_etlRunner",
                    },
                    Name.Identifier.ValueText: "RunAsync",
                });
        var execute = runnerCall.Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .First();
        var runnerFailureBoundary = runnerCall.Ancestors()
            .OfType<TryStatementSyntax>()
            .First();

        runnerFailureBoundary.Catches.Any(catchClause =>
                HasOrdinaryFailureMapping(catchClause))
            .ShouldBeTrue("runner 的普通执行异常仍须映射为安全失败终态");

        var successfulFinalizations = execute.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(IsSuccessfulFinalization)
            .ToArray();
        successfulFinalizations.Length.ShouldBe(
            1,
            "ExecuteAsync 应只有一个成功终态提交点");

        var successfulFinalization = successfulFinalizations[0];
        runnerFailureBoundary.Block.Span.Contains(successfulFinalization.Span)
            .ShouldBeFalse(
                "runner 返回成功后的 FinalizeAsync 不得位于普通失败 catch 的 try 内");
        successfulFinalization.SpanStart.ShouldBeGreaterThan(
            runnerFailureBoundary.Span.End,
            "成功终态提交必须发生在 runner try/catch 完整结束之后；" +
            "否则提交异常会把已写入目标库的执行误记为 Failed");
        successfulFinalization.Ancestors()
            .OfType<TryStatementSyntax>()
            .Any(tryStatement => tryStatement.Catches.Any(
                HasOrdinaryFailureMapping))
            .ShouldBeFalse(
                "成功 FinalizeAsync 不能被任何普通失败映射 catch 包围");
    }

    [Fact]
    public void PostgreSql_Fence_Should_Use_Dedicated_NonEnlisted_Physical_Session()
    {
        var root = LocateRepositoryRoot();
        var fenceSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.EntityFrameworkCore",
            "DataIntegration",
            "NpgsqlDataPipelineExecutionFence.cs"));
        var factorySource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.EntityFrameworkCore",
            "DataIntegration",
            "NpgsqlDataPipelineExecutionFenceConnectionFactory.cs"));

        fenceSource.ShouldContain("IDataPipelineExecutionFenceConnectionFactory");
        fenceSource.ShouldNotContain("new NpgsqlConnection");
        fenceSource.ShouldContain("Transaction.Current != null");
        fenceSource.ShouldContain("pg_try_advisory_lock");
        fenceSource.ShouldContain(
            "DataPipelineExecutionFenceSessionSettings.HeartbeatSeconds");

        factorySource.ShouldContain("IConnectionStringResolver");
        factorySource.ShouldContain("ConnectionStrings.DefaultConnectionStringName");
        factorySource.ShouldContain("Pooling = false");
        factorySource.ShouldContain("Enlist = false");
        factorySource.ShouldContain(
            "KeepAlive = DataPipelineExecutionFenceSessionSettings.HeartbeatSeconds");
        factorySource.ShouldContain(
            "ApplicationName = DataPipelineExecutionFenceSessionSettings.ApplicationName");
        factorySource.ShouldContain("new NpgsqlConnection(builder.ConnectionString)");
    }

    [Fact]
    public void Platform_State_Commits_Should_Have_Independent_Confirmation_Deadline()
    {
        var root = LocateRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "DataPipelineExecutionCoordinator.Coordination.cs"));

        source.ShouldContain("StateCommitConfirmationTimeoutSeconds = 30");
        source.ShouldContain(
            "unitOfWork.CompleteAsync(commitConfirmation.Token)");
        source.ShouldNotContain(
            "unitOfWork.CompleteAsync(CancellationToken.None)");
    }

    [Fact]
    public void Terminal_Store_Topology_Guard_Should_Precede_External_And_Durable_Writes()
    {
        typeof(IMultiTenant).IsAssignableFrom(typeof(DataSource))
            .ShouldBeFalse("DataSource 当前按 host store 路由");
        typeof(IMultiTenant).IsAssignableFrom(typeof(DataPipeline))
            .ShouldBeFalse("DataPipeline 当前按 host store 路由");
        typeof(IMultiTenant).IsAssignableFrom(typeof(PipelineExecution))
            .ShouldBeFalse("PipelineExecution 当前按 host store 路由");

        var root = LocateRepositoryRoot();
        var guardSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "PipelineTerminalStoreTopologyGuard.cs"));
        guardSource.ShouldContain("_currentTenant.Change(null)");
        guardSource.ShouldContain(
            "AbpBackgroundJobsDbProperties.ConnectionStringName");
        guardSource.ShouldContain("StringComparison.Ordinal");
        guardSource.ShouldNotContain("NpgsqlConnectionStringBuilder");
        var scopeSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "PipelineTerminalStoreConnectionScope.cs"));
        scopeSource.ShouldContain("AsyncLocal<ScopeHolder?>");
        scopeSource.ShouldContain("PipelinePinnedConnectionStringResolver");
        scopeSource.ShouldContain("AbpBackgroundJobsDbProperties.ConnectionStringName");
        scopeSource.ShouldContain("PipelineTerminalStoreTopologyCapability");

        var execute = ParseFile(Path.Combine(
                root,
                "src",
                "SwapMid.Application",
                "DataIntegration",
                "DataPipelineExecutionCoordinator.cs"))
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "ExecuteAsync");
        var admissionProbe = FindTopologyProbe(execute);
        var admissionScope = FindTopologyScopeEntry(execute);
        var executeHostScope = FindHostScopeEntry(execute);
        var preparations = execute.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is
                IdentifierNameSyntax
                {
                    Identifier.ValueText: "PrepareAsync",
                })
            .OrderBy(invocation => invocation.SpanStart)
            .ToList();
        preparations.Count.ShouldBe(2,
            "终态回放可先读，但新执行必须在 pin 内重新 Prepare");
        executeHostScope.SpanStart.ShouldBeLessThan(
            preparations[0].SpanStart,
            "所有数据集成读取与 fence 必须从入口固定到 host scope");
        var databaseFence = execute.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_databaseFence",
                    },
                    Name.Identifier.ValueText: "TryAcquireAsync",
                });
        var claim = execute.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                IdentifierNameSyntax
                {
                    Identifier.ValueText: "ClaimAsync",
                });
        var runner = execute.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_etlRunner",
                    },
                    Name.Identifier.ValueText: "RunAsync",
                });
        admissionProbe.SpanStart.ShouldBeLessThan(databaseFence.SpanStart);
        admissionProbe.SpanStart.ShouldBeLessThan(claim.SpanStart);
        admissionProbe.SpanStart.ShouldBeLessThan(runner.SpanStart);
        preparations[0].SpanStart.ShouldBeLessThan(admissionProbe.SpanStart);
        admissionProbe.SpanStart.ShouldBeLessThan(admissionScope.SpanStart);
        admissionScope.SpanStart.ShouldBeLessThan(preparations[1].SpanStart);
        preparations[1].SpanStart.ShouldBeLessThan(databaseFence.SpanStart);

        var finalize = ParseFile(Path.Combine(
                root,
                "src",
                "SwapMid.Application",
                "DataIntegration",
                "DataPipelineExecutionCoordinator.Coordination.cs"))
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText ==
                "FinalizeCoreAsync");
        var terminalProbe = FindTopologyProbe(finalize);
        var executionRead = finalize.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_executionRepository",
                    },
                    Name.Identifier.ValueText: "GetAsync",
                });
        var executionUpdate = finalize.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_executionRepository",
                    },
                    Name.Identifier.ValueText: "UpdateAsync",
                });
        var durableEnqueue = finalize.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_notificationScheduler",
                    },
                    Name.Identifier.ValueText: "ScheduleAsync",
                });
        terminalProbe.SpanStart.ShouldBeLessThan(executionRead.SpanStart);
        terminalProbe.SpanStart.ShouldBeLessThan(executionUpdate.SpanStart);
        terminalProbe.SpanStart.ShouldBeLessThan(durableEnqueue.SpanStart);

        var reconciliationRoot = ParseFile(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "DataPipelineExecutionCoordinator.Reconciliation.cs"));
        var resolve = reconciliationRoot
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText ==
                "ResolveOutcomeAsync");
        var resolutionProbe = FindTopologyProbe(resolve);
        var resolutionScope = FindTopologyScopeEntry(resolve);
        var resolutionHostScope = FindHostScopeEntry(resolve);
        var resolutionPreparations = resolve.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is
                IdentifierNameSyntax
                {
                    Identifier.ValueText: "PrepareResolutionAsync",
                })
            .OrderBy(invocation => invocation.SpanStart)
            .ToList();
        resolutionPreparations.Count.ShouldBe(2,
            "已收敛结论可先读，但两类会写终态的对账都必须在 pin 内重新 Prepare");
        resolutionPreparations[0].ToString().Contains(
                "requiredTopologyPhase: null",
                StringComparison.Ordinal)
            .ShouldBeTrue("首次读取只能承担无条件回放识别");
        resolutionPreparations[1].ToString().Contains(
                "topologyPhase",
                StringComparison.Ordinal)
            .ShouldBeTrue(
                "非回放路径的第二次读取必须校验对应 topology 能力");
        resolutionHostScope.SpanStart.ShouldBeLessThan(
            resolutionPreparations[0].SpanStart,
            "ConfirmedApplied 也必须在 host 库获取 fence");
        var resolutionFence = resolve.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_databaseFence",
                    },
                    Name.Identifier.ValueText: "TryAcquireAsync",
                });
        resolutionProbe.SpanStart.ShouldBeLessThan(resolutionFence.SpanStart);
        resolutionPreparations[0].SpanStart.ShouldBeLessThan(
            resolutionProbe.SpanStart);
        resolutionProbe.SpanStart.ShouldBeLessThan(
            resolutionScope.SpanStart);
        resolutionScope.SpanStart.ShouldBeLessThan(
            resolutionPreparations[1].SpanStart);
        resolutionPreparations[1].SpanStart.ShouldBeLessThan(
            resolutionFence.SpanStart);
        resolutionProbe.Ancestors()
            .OfType<IfStatementSyntax>()
            .ShouldBeEmpty(
                "ConfirmedApplied 与 ConfirmedNotApplied 都必须捕获 topology pin");
        resolutionScope.Ancestors()
            .OfType<IfStatementSyntax>()
            .ShouldBeEmpty(
                "两类非回放裁决都必须进入 topology scope");

        var resolveCore = reconciliationRoot.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText ==
                "ResolveCoreAsync");
        var resolutionWriteProbe = FindTopologyProbe(resolveCore);
        var resolutionRead = FindInvocation(
            resolveCore,
            "GetAsync",
            "_executionRepository");
        var resolutionUpdate = FindInvocation(
            resolveCore,
            "UpdateAsync",
            "_executionRepository");
        var resolutionSchedule = FindInvocation(
            resolveCore,
            "ScheduleAsync",
            "_notificationScheduler");
        resolutionWriteProbe.ToString().Contains(
                "GetResolutionTopologyPhase(resolution)",
                StringComparison.Ordinal)
            .ShouldBeTrue(
                "写事务必须按 Applied/NotApplied 选择对应能力");
        resolutionWriteProbe.SpanStart.ShouldBeLessThan(
            resolutionRead.SpanStart);
        resolutionWriteProbe.SpanStart.ShouldBeLessThan(
            resolutionUpdate.SpanStart);
        resolutionWriteProbe.SpanStart.ShouldBeLessThan(
            resolutionSchedule.SpanStart);

        var topologySelector = ParseFile(Path.Combine(
                root,
                "src",
                "SwapMid.Application",
                "DataIntegration",
                "DataPipelineExecutionCoordinator.Reconciliation.Topology.cs"))
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText ==
                "GetResolutionTopologyPhase")
            .ToString();
        topologySelector.ShouldContain("OutcomeResolutionApplied");
        topologySelector.ShouldContain("OutcomeResolution");

        var scheduler = ParseFile(Path.Combine(
                root,
                "src",
                "SwapMid.Application",
                "DataIntegration",
                "PipelineFailureNotificationScheduler.cs"))
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "ScheduleAsync");
        var schedulerProbe = FindTopologyProbe(scheduler);
        var transactionalGuard = scheduler.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_terminalUnitOfWorkGuard",
                    },
                    Name.Identifier.ValueText: "EnsureTransactional",
                });
        var insert = scheduler.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_backgroundJobStore",
                    },
                    Name.Identifier.ValueText: "InsertAsync",
                });
        transactionalGuard.SpanStart.ShouldBeLessThan(
            schedulerProbe.SpanStart);
        schedulerProbe.SpanStart.ShouldBeLessThan(insert.SpanStart);
    }

    [Fact]
    public void Running_Recovery_Should_Pin_Host_Then_Take_Both_Locks_And_Commit_One_Atomic_Terminal_Write()
    {
        var root = LocateRepositoryRoot();
        var recoveryFile = Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "DataPipelineExecutionCoordinator.Recovery.cs");
        var recovery = ParseFile(recoveryFile)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method =>
                method.Identifier.ValueText == "RecoverRunningAsync" &&
                method.Modifiers.Any(SyntaxKind.PublicKeyword));

        var hostScope = FindHostScopeEntry(recovery);
        var topologyProbe = FindTopologyProbe(recovery);
        var topologyScope = FindTopologyScopeEntry(recovery);
        var distributedLock = FindInvocation(
            recovery,
            "TryAcquireDistributedLockAsync");
        var databaseFence = FindInvocation(
            recovery,
            "TryAcquireAsync",
            "_databaseFence");
        var transactionalWrite = ParseFile(recoveryFile)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(invocation => IsNamedInvocation(
                    invocation,
                    "GetForUpdateAsync",
                    "_executionRepository")));
        var transactionalWriteCall = recovery.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText ==
                transactionalWrite.Identifier.ValueText);
        var forUpdate = FindInvocation(
            transactionalWrite,
            "GetForUpdateAsync",
            "_executionRepository");
        var update = FindInvocation(
            transactionalWrite,
            "UpdateAsync",
            "_executionRepository");
        var schedule = FindInvocation(
            transactionalWrite,
            "ScheduleAsync",
            "_notificationScheduler");
        var commit = FindInvocation(
            transactionalWrite,
            "CompleteUnitOfWorkAsync");

        hostScope.SpanStart.ShouldBeLessThan(topologyProbe.SpanStart,
            "恢复入口必须先固定 host scope，再解析/固定终态库拓扑");
        topologyProbe.SpanStart.ShouldBeLessThan(topologyScope.SpanStart);
        topologyScope.SpanStart.ShouldBeLessThan(distributedLock.SpanStart);
        distributedLock.SpanStart.ShouldBeLessThan(databaseFence.SpanStart,
            "恢复必须先取 Redis pipeline 锁，再取 PostgreSQL pipeline+target fence");
        databaseFence.SpanStart.ShouldBeLessThan(
            transactionalWriteCall.SpanStart,
            "只有双锁都持有后才允许 SELECT ... FOR UPDATE");
        forUpdate.SpanStart.ShouldBeLessThan(update.SpanStart);
        update.SpanStart.ShouldBeLessThan(schedule.SpanStart,
            "OutcomeUnknown 与恢复通知必须在同一事务内按状态后任务的顺序写入");
        schedule.SpanStart.ShouldBeLessThan(commit.SpanStart,
            "恢复状态、审计 marker 与通知任务必须原子提交");

        var repositorySource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.EntityFrameworkCore",
            "DataIntegration",
            "EfCorePipelineExecutionRepository.cs"));
        var transactionGuard = repositorySource.IndexOf(
            "Database.CurrentTransaction",
            StringComparison.Ordinal);
        var forUpdateSql = repositorySource.IndexOf(
            "FOR UPDATE",
            transactionGuard < 0 ? 0 : transactionGuard,
            StringComparison.Ordinal);
        transactionGuard.ShouldBeGreaterThanOrEqualTo(0,
            "生产 FOR UPDATE 仓储必须拒绝无事务调用");
        forUpdateSql.ShouldBeGreaterThan(transactionGuard,
            "事务守卫必须先于 FOR UPDATE SQL 执行");
    }

    [Fact]
    public void Running_Recovery_Should_Block_Every_Late_Terminal_And_Not_Retry_Ack_Loss_Without_Ownership()
    {
        var root = LocateRepositoryRoot();
        var coordination = ParseFile(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "DataPipelineExecutionCoordinator.Coordination.cs"));
        var finalize = coordination.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText ==
                "FinalizeCoreAsync");
        var markerGuard = finalize.DescendantNodes()
            .OfType<IfStatementSyntax>()
            .Single(statement => statement.Condition.DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(invocation => invocation.Expression is
                    IdentifierNameSyntax
                    {
                        Identifier.ValueText: "HasRunningRecoveryMarker",
                    }));
        var terminalSwitch = finalize.DescendantNodes()
            .OfType<SwitchStatementSyntax>()
            .Single(statement => statement.Expression is IdentifierNameSyntax
            {
                Identifier.ValueText: "kind",
            });

        markerGuard.Condition.ToString()
            .Contains("kind", StringComparison.Ordinal)
            .ShouldBeFalse(
                "恢复 marker 必须统一阻断所有 TerminalKind，不能只保护成功终态");
        markerGuard.SpanStart.ShouldBeLessThan(
            terminalSwitch.SpanStart,
            "恢复 marker 必须在任何终态分支或实体变更前失败关闭");
        markerGuard.Statement.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_logger",
                    },
                    Name.Identifier.ValueText: "LogCritical",
                })
            .ShouldBeTrue("晚到终态必须留下 critical 级审计信号");
        markerGuard.Statement.ToString().ShouldContain(
            "running_recovery_late_finalize_blocked");
        markerGuard.Statement.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is IdentifierNameSyntax
            {
                Identifier.ValueText: "CoordinationUnavailable",
            })
            .ShouldBeTrue("晚到终态必须 fail-close，不能静默返回旧状态");

        var recovery = ParseFile(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "DataPipelineExecutionCoordinator.Recovery.cs"));
        var recoverWithRetry = recovery.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText ==
                "RecoverWithRetryAsync");
        var acknowledgementLossCatch = recoverWithRetry.DescendantNodes()
            .OfType<CatchClauseSyntax>()
            .Single(catchClause =>
                catchClause.Declaration?.Type.ToString() ==
                "PipelineExecutionStateCommitUnknownException" &&
                catchClause.Block.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(invocation => invocation.Expression is
                        MemberAccessExpressionSyntax
                        {
                            Expression: IdentifierNameSyntax
                            {
                                Identifier.ValueText: "ownershipToken",
                            },
                            Name.Identifier.ValueText:
                                "ThrowIfCancellationRequested",
                        }));
        var ownershipCheck = acknowledgementLossCatch.Block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "ownershipToken",
                    },
                    Name.Identifier.ValueText:
                        "ThrowIfCancellationRequested",
                });
        var retry = acknowledgementLossCatch.Block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation =>
                invocation.Expression is IdentifierNameSyntax
                {
                    Identifier.ValueText: "CommitAsync",
                } &&
                invocation.ArgumentList.Arguments.Any(argument =>
                    argument.Expression is IdentifierNameSyntax
                    {
                        Identifier.ValueText: "ownershipToken",
                    }));
        ownershipCheck.SpanStart.ShouldBeLessThan(
            retry.SpanStart,
            "ACK 丢失确认重试前必须先确认 Redis/PG ownership 仍有效");
    }

    [Fact]
    public void Running_Recovery_Http_Path_Should_Disable_Uow_Forward_Cancellation_And_Have_No_Background_Caller()
    {
        var root = LocateRepositoryRoot();
        var files = new[]
        {
            Path.Combine(
                root,
                "src",
                "SwapMid.Application.Contracts",
                "DataIntegration",
                "IPipelineExecutionAppService.cs"),
            Path.Combine(
                root,
                "src",
                "SwapMid.Application",
                "DataIntegration",
                "PipelineExecutionAppService.cs"),
            Path.Combine(
                root,
                "src",
                "SwapMid.HttpApi",
                "DataIntegration",
                "PipelineExecutionController.cs"),
        };

        foreach (var file in files)
        {
            var methods = ParseFile(file)
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method =>
                    method.Identifier.ValueText == "RecoverRunningAsync")
                .ToArray();
            methods.Length.ShouldBe(2,
                $"{Path.GetFileName(file)} 恢复重载数量发生变化");
            methods.ShouldAllBe(method => HasDisabledUnitOfWork(method),
                "Running 恢复长路径必须逐层禁用 ambient UoW");
        }

        var controllerAction = ParseFile(files[2])
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method =>
                method.Identifier.ValueText == "RecoverRunningAsync" &&
                HasAttribute(method, "HttpPost"));
        controllerAction.ParameterList.Parameters.ShouldContain(parameter =>
            parameter.Type != null &&
            parameter.Type.ToString() == "CancellationToken");
        controllerAction.DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Any(argument =>
                argument.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "cancellationToken")
            .ShouldBeTrue("HTTP action 必须把请求取消信号透传给应用服务");

        var allowedCallers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PipelineExecutionAppService.cs",
            "PipelineExecutionController.cs",
        };
        var offenders = Directory.EnumerateFiles(
                Path.Combine(root, "src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(file => !allowedCallers.Contains(Path.GetFileName(file)))
            .SelectMany(file => ParseFile(file)
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation => invocation.Expression is
                    MemberAccessExpressionSyntax
                    {
                        Name.Identifier.ValueText: "RecoverRunningAsync",
                    })
                .Select(invocation => FormatLocation(file, invocation)))
            .ToArray();
        offenders.ShouldBeEmpty(
            "Running 恢复只能由受权 HTTP/AppService 入口触发，后台 Job/Handler 不得自动调用：\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void Failure_Notification_Should_Use_Transactional_Native_Queue_Outside_Fence()
    {
        var root = LocateRepositoryRoot();
        var schedulerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "PipelineFailureNotificationScheduler.cs"));
        var jobSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "PipelineFailureNotificationJob.cs"));
        var moduleSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "SwapMidApplicationModule.cs"));

        schedulerSource.ShouldContain("IBackgroundJobStore");
        schedulerSource.ShouldContain("new BackgroundJobInfo");
        schedulerSource.ShouldContain("IBackgroundJobSerializer");
        schedulerSource.ShouldContain("_backgroundJobStore.InsertAsync");
        schedulerSource.ShouldNotContain("IBackgroundJobManager");
        var schedulerSyntax = CSharpSyntaxTree.ParseText(schedulerSource).GetRoot();
        schedulerSyntax.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "OnCompleted",
                })
            .ShouldBeFalse("终态事务不得注册提交后网络回调");
        schedulerSyntax.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => identifier.Identifier.ValueText ==
                "INotificationService")
            .ShouldBeFalse("scheduler 不得依赖通知网络服务");
        jobSource.ShouldContain("INotificationService");
        jobSource.ShouldContain("_notificationService.SendAsync");
        jobSource.ShouldContain("UnitOfWork(IsDisabled = true)");
        jobSource.Contains(".WaitAsync(", StringComparison.Ordinal)
            .ShouldBeFalse(
                "WaitAsync 只会放弃等待、不会终止底层通知任务，重试时会积累孤儿任务；" +
                "通知通道必须协作式遵守 linked token 或使用自身更短的传输超时");
        moduleSource.ShouldContain(
            "DataIntegration.PipelineFailureNotificationJob");

        var coordinator = ParseFile(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "DataPipelineExecutionCoordinator.Coordination.cs"));
        var finalize = coordinator.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "FinalizeCoreAsync");
        var durableEnqueue = finalize.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_notificationScheduler",
                    },
                    Name.Identifier.ValueText: "ScheduleAsync",
                });
        var terminalCommit = finalize.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                IdentifierNameSyntax
                {
                    Identifier.ValueText: "CompleteUnitOfWorkAsync",
                });
        durableEnqueue.SpanStart.ShouldBeLessThan(
            terminalCommit.SpanStart,
            "失败终态与通知队列记录必须由同一事务原子提交");
    }

    private static bool HasDisabledUnitOfWork(MethodDeclarationSyntax method)
        => method.AttributeLists
            .SelectMany(list => list.Attributes)
            .Where(attribute =>
                attribute.Name.ToString() is "UnitOfWork" or
                    "UnitOfWorkAttribute")
            .SelectMany(attribute =>
                attribute.ArgumentList?.Arguments ?? default)
            .Any(argument =>
                argument.NameEquals?.Name.Identifier.ValueText ==
                    "IsDisabled" &&
                argument.Expression.IsKind(
                    SyntaxKind.TrueLiteralExpression));

    private static InvocationExpressionSyntax FindTopologyProbe(
        MethodDeclarationSyntax method)
        => method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_terminalStoreTopologyGuard",
                    },
                    Name.Identifier.ValueText: "CaptureSafeAsync" or
                        "EnsureSafeScope",
                });

    private static InvocationExpressionSyntax FindTopologyScopeEntry(
        MethodDeclarationSyntax method)
        => method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_terminalStoreTopologyGuard",
                    },
                    Name.Identifier.ValueText: "EnterSafeScope",
                });

    private static InvocationExpressionSyntax FindHostScopeEntry(
        MethodDeclarationSyntax method)
        => method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_currentTenant",
                    },
                    Name.Identifier.ValueText: "Change",
                } &&
                invocation.ArgumentList.Arguments.Count > 0 &&
                invocation.ArgumentList.Arguments[0].Expression.IsKind(
                    SyntaxKind.NullLiteralExpression));

    private static InvocationExpressionSyntax FindInvocation(
        MethodDeclarationSyntax method,
        string methodName,
        string? receiverName = null)
        => method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression switch
            {
                IdentifierNameSyntax identifier =>
                    receiverName == null &&
                    identifier.Identifier.ValueText == methodName,
                MemberAccessExpressionSyntax member =>
                    member.Name.Identifier.ValueText == methodName &&
                    (receiverName == null ||
                     member.Expression is IdentifierNameSyntax receiver &&
                     receiver.Identifier.ValueText == receiverName),
                _ => false,
            });

    private static bool IsNamedInvocation(
        InvocationExpressionSyntax invocation,
        string methodName,
        string receiverName)
        => invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax receiver,
            Name.Identifier.ValueText: var actualMethodName,
        } &&
           receiver.Identifier.ValueText == receiverName &&
           actualMethodName == methodName;

    private static bool HasAttribute(
        MethodDeclarationSyntax method,
        string attributeName)
        => method.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute =>
                attribute.Name.ToString() == attributeName ||
                attribute.Name.ToString() == attributeName + "Attribute");

    private static bool IsExecutionRepositoryCall(
        InvocationExpressionSyntax invocation,
        string methodName)
        => invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax
            {
                Identifier.ValueText: "_executionRepository",
            },
            Name.Identifier.ValueText: var actualMethodName,
        } && actualMethodName == methodName;

    private static bool IsExistingRunGuard(ExpressionSyntax condition)
        => condition is BinaryExpressionSyntax
        {
            RawKind: (int)SyntaxKind.NotEqualsExpression,
            Left: IdentifierNameSyntax
            {
                Identifier.ValueText: "existingById",
            },
            Right: LiteralExpressionSyntax right,
        } && right.IsKind(SyntaxKind.NullLiteralExpression);

    private static bool HasOrdinaryFailureMapping(
        CatchClauseSyntax catchClause)
        => catchClause.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is
                MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "_failurePolicy",
                    },
                    Name.Identifier.ValueText: "DescribeFailure",
                });

    private static bool IsSuccessfulFinalization(
        InvocationExpressionSyntax invocation)
        => invocation.Expression is IdentifierNameSyntax
            {
                Identifier.ValueText: "FinalizeAsync",
            } &&
           invocation.ArgumentList.Arguments.Any(argument =>
               argument.Expression is MemberAccessExpressionSyntax
               {
                   Expression: IdentifierNameSyntax
                   {
                       Identifier.ValueText: "TerminalKind",
                   },
                   Name.Identifier.ValueText: "Succeeded",
               });

    private static SyntaxNode ParseCoordinator()
    {
        var root = LocateRepositoryRoot();
        return ParseFile(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "DataPipelineExecutionCoordinator.cs"));
    }

    private static SyntaxNode ParseFile(string file)
        => CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

    private static string FormatLocation(string file, SyntaxNode node)
        => $"{Path.GetFileName(file)}:" +
           $"{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "SwapMid.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未能定位仓库根目录");
    }
}
