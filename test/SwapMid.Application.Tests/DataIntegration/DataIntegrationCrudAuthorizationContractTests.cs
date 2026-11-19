using System;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace SwapMid.DataIntegration;

public class DataIntegrationCrudAuthorizationContractTests
{
    private const string DataSources =
        "SwapMidPermissions.DataIntegration.DataSources";
    private const string DataPipelines =
        "SwapMidPermissions.DataIntegration.DataPipelines";
    private const string PipelineExecutions =
        "SwapMidPermissions.DataIntegration.PipelineExecutions";
    private const string CdcStreams =
        "SwapMidPermissions.DataIntegration.CdcStreams";

    [Fact]
    public void AppService文件集合和全部public入口必须由契约精确封闭()
    {
        var contracts = CreateContracts();
        var directory = GetDataIntegrationDirectory();
        var serviceViolations = DataIntegrationAppServiceGuardScanner.ValidateServiceSet(
            Directory.GetFiles(
                    directory,
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(path => new DataIntegrationSourceDocument(
                    Path.GetRelativePath(directory, path).Replace('\\', '/'),
                    File.ReadAllText(path))),
            contracts);
        serviceViolations.ShouldBeEmpty(string.Join(Environment.NewLine, serviceViolations));

        var inspectedEntryCount = 0;
        foreach (var contract in contracts)
        {
            var violations = DataIntegrationAppServiceGuardScanner.ValidateHostGuards(
                contract.ClassName,
                ReadSource(contract.FileName),
                contract.ClassAuthorizePermission,
                contract.Entries);

            violations.ShouldBeEmpty(
                $"{contract.FileName} host-only 契约失配：{Environment.NewLine}" +
                string.Join(Environment.NewLine, violations));
            inspectedEntryCount += contract.Entries.Length;
        }

        contracts.Length.ShouldBe(5);
        inspectedEntryCount.ShouldBe(46);
    }

    [Fact]
    public void CRUD写入口必须锁定授权策略检查顺序和全部PolicyName()
    {
        var inspectedCrudCount = 0;
        var inspectedPolicyAssignmentCount = 0;
        foreach (var contract in CreateContracts().Where(x => x.PolicyAssignments.Length != 0))
        {
            var crudEntries = contract.Entries
                .Where(entry => entry.PolicyCheck != null)
                .ToArray();
            var violations = DataIntegrationAppServiceGuardScanner.ValidateCrudContract(
                contract.ClassName,
                ReadSource(contract.FileName),
                crudEntries,
                contract.PolicyAssignments);

            violations.ShouldBeEmpty(
                $"{contract.FileName} CRUD 授权契约失配：{Environment.NewLine}" +
                string.Join(Environment.NewLine, violations));
            inspectedCrudCount += crudEntries.Length;
            inspectedPolicyAssignmentCount += contract.PolicyAssignments.Length;
        }
        inspectedCrudCount.ShouldBe(6);
        inspectedPolicyAssignmentCount.ShouldBe(10);
    }

    [Fact]
    public void 文件和host门禁扫描器必须由负例证明不是恒绿()
    {
        var contracts = CreateContracts();
        var sourceDocuments = contracts.Select(contract =>
                new DataIntegrationSourceDocument(
                    contract.FileName,
                    $"public class {contract.ClassName} : SwapMidAppService {{ }}"))
            .ToArray();
        DataIntegrationAppServiceGuardScanner.ValidateServiceSet(
                sourceDocuments,
                contracts)
            .ShouldBeEmpty();
        DataIntegrationAppServiceGuardScanner.ValidateServiceSet(
                sourceDocuments.Append(new(
                    "NewAppService.cs",
                    "public class NewAppService : SwapMidAppService { }")),
                contracts)
            .ShouldContain(x => x.Contains("未登记", StringComparison.Ordinal));
        DataIntegrationAppServiceGuardScanner.ValidateServiceSet(
                sourceDocuments.Append(new(
                    "Nested/DataIntegrationFacade.cs",
                    "public class DataIntegrationFacade : SwapMidAppService { }")),
                contracts)
            .ShouldContain(x => x.Contains("未登记", StringComparison.Ordinal));
        DataIntegrationAppServiceGuardScanner.ValidateServiceSet(
                sourceDocuments.Select(document => document.FileName == "DataSourceAppService.cs"
                    ? document with
                    {
                        Source = document.Source +
                                 " internal class HiddenAppService : SwapMidAppService { }",
                    }
                    : document),
                contracts)
            .ShouldContain(x => x.Contains("未登记", StringComparison.Ordinal));
        DataIntegrationAppServiceGuardScanner.ValidateServiceSet(
                sourceDocuments.Where(x => x.FileName != "DataPipelineAppService.cs"),
                contracts)
            .ShouldContain(x => x.Contains("缺失", StringComparison.Ordinal));

        var permission = DataSources + ".Default";
        const string hostHelper = "global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext";
        var expected = new[]
        {
            new DataIntegrationEntryContract("GetAsync(Guid)", permission),
        };
        var baseline = BuildHostSnippet(
            $"{hostHelper}(base.CurrentTenant.Id, {permission});");
        DataIntegrationAppServiceGuardScanner.ValidateHostGuards(
                "DataSourceAppService",
                baseline,
                permission,
                expected)
            .ShouldBeEmpty();

        var mutations = new[]
        {
            ("表达式体", BuildHostSnippet(
                $"{hostHelper}(base.CurrentTenant.Id, {permission});",
                expressionBodied: true)),
            ("缺少类级 Authorize", baseline.Replace(
                $"[Authorize({permission})]\n",
                string.Empty,
                StringComparison.Ordinal)),
            ("错误类级 Authorize", baseline.Replace(
                $"[Authorize({permission})]",
                $"[Authorize({DataSources}.Update)]",
                StringComparison.Ordinal)),
            ("多个类级 Authorize", baseline.Replace(
                $"[Authorize({permission})]",
                $"[Authorize({permission})]\n[Authorize({permission})]",
                StringComparison.Ordinal)),
            ("同名假类级 Authorize", baseline.Replace(
                $"[Authorize({permission})]",
                $"[Fake.Authorize({permission})]",
                StringComparison.Ordinal)),
            ("partial 旁路", baseline.Replace(
                "public class DataSourceAppService",
                "public partial class DataSourceAppService",
                StringComparison.Ordinal)),
            ("类级 AllowAnonymous", baseline.Replace(
                "public class DataSourceAppService",
                "[AllowAnonymous]\npublic class DataSourceAppService",
                StringComparison.Ordinal)),
            ("方法级 AllowAnonymous", baseline.Replace(
                "    public async Task GetAsync(Guid id)",
                "    [AllowAnonymous]\n    public async Task GetAsync(Guid id)",
                StringComparison.Ordinal)),
            ("null tenant", BuildHostSnippet(
                $"{hostHelper}(null, {permission});")),
            ("错误 tenant", BuildHostSnippet(
                $"{hostHelper}(base.CurrentUser.Id, {permission});")),
            ("错误权限", BuildHostSnippet(
                $"{hostHelper}(base.CurrentTenant.Id, {DataSources}.Update);")),
            ("if 包裹", BuildHostSnippet(
                $"if (true) {{ {hostHelper}(base.CurrentTenant.Id, {permission}); }}")),
            ("try 包裹", BuildHostSnippet(
                $"try {{ {hostHelper}(base.CurrentTenant.Id, {permission}); }} catch {{ }}")),
            ("同名嵌套类型阴影", BuildHostSnippet(
                $"PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, {permission});")),
            ("隐藏 CurrentTenant 阴影", BuildHostSnippet(
                $"{hostHelper}(CurrentTenant.Id, {permission});")),
            ("未限定同名调用", BuildHostSnippet(
                $"EnsureHostContext(base.CurrentTenant.Id, {permission});")),
            ("门禁不是首句", BuildHostSnippet(
                $"Work();\n        {hostHelper}(base.CurrentTenant.Id, {permission});")),
            ("未登记入口", baseline.Replace(
                "    public async Task GetAsync(Guid id)",
                "    public async Task GetAsync(Guid id)\n" +
                "    {\n        await WorkAsync();\n    }\n\n" +
                "    public async Task AddedAsync()",
                StringComparison.Ordinal)),
            ("显式接口入口", baseline.Replace(
                "\n}",
                "\n    Task IHiddenAppService.DangerAsync() => WorkAsync();\n}",
                StringComparison.Ordinal)),
        };

        foreach (var (name, source) in mutations)
        {
            var violations = DataIntegrationAppServiceGuardScanner.ValidateHostGuards(
                "DataSourceAppService",
                source,
                permission,
                expected);
            violations.ShouldNotBeEmpty($"扫描器必须拒绝{name}变异");
        }
        var childPermission = DataSources + ".Create";
        var childExpected = new[]
        {
            new DataIntegrationEntryContract(
                "GetAsync(Guid)",
                childPermission,
                childPermission),
        };
        var childBaseline = BuildHostSnippet(
            $"{hostHelper}(base.CurrentTenant.Id, {childPermission});",
            methodAuthorizePermission: childPermission);
        DataIntegrationAppServiceGuardScanner.ValidateHostGuards(
                "DataSourceAppService",
                childBaseline,
                permission,
                childExpected)
            .ShouldBeEmpty();
        foreach (var source in new[]
                 {
                     childBaseline.Replace(
                         $"    [Authorize({childPermission})]\n",
                         string.Empty,
                         StringComparison.Ordinal),
                     childBaseline.Replace(
                         $"[Authorize({childPermission})]",
                         $"[Authorize({DataSources}.Update)]",
                         StringComparison.Ordinal),
                     childBaseline.Replace(
                         $"[Authorize({childPermission})]",
                         $"[Authorize({childPermission})]\n    [Authorize({childPermission})]",
                         StringComparison.Ordinal),
                     childBaseline.Replace(
                         $"[Authorize({childPermission})]",
                         $"[Fake.Authorize({childPermission})]",
                         StringComparison.Ordinal),
                 })
        {
            DataIntegrationAppServiceGuardScanner.ValidateHostGuards(
                    "DataSourceAppService",
                    source,
                    permission,
                    childExpected)
                .ShouldNotBeEmpty("方法级子权限变异必须被拒绝");
        }
    }

    [Fact]
    public void CRUD扫描器必须由授权检查顺序和构造器负例证明不是恒绿()
    {
        var deletePermission = DataSources + ".Delete";
        var policyAssignments = new[]
        {
            new DataIntegrationPolicyAssignmentContract(
                "DeletePolicyName",
                deletePermission),
        };
        var expected = new[]
        {
            new DataIntegrationEntryContract(
                "DeleteAsync(Guid)",
                deletePermission,
                deletePermission,
                "CheckDeletePolicyAsync"),
        };
        var baseline = BuildCrudSnippet();
        DataIntegrationAppServiceGuardScanner.ValidateCrudContract(
                "DataSourceAppService",
                baseline,
                expected,
                policyAssignments)
            .ShouldBeEmpty();

        var authorize = $"[Authorize({deletePermission})]";
        var check = "        await CheckDeletePolicyAsync();";
        var mutations = new[]
        {
            ("缺少 Authorize", baseline.Replace(
                authorize,
                string.Empty,
                StringComparison.Ordinal), "Authorize"),
            ("错误 Authorize", baseline.Replace(
                authorize,
                $"[Authorize({DataSources}.Update)]",
                StringComparison.Ordinal), "Authorize"),
            ("多个 Authorize", baseline.Replace(
                authorize,
                authorize + Environment.NewLine + authorize,
                StringComparison.Ordinal), "Authorize"),
            ("同名假 Authorize", baseline.Replace(
                authorize,
                $"[Fake.Authorize({deletePermission})]",
                StringComparison.Ordinal), "Authorize"),
            ("额外同名假 Authorize", baseline.Replace(
                authorize,
                authorize + Environment.NewLine +
                $"[Fake.Authorize({deletePermission})]",
                StringComparison.Ordinal), "Authorize"),
            ("缺少 Check", baseline.Replace(
                "await CheckDeletePolicyAsync();",
                string.Empty,
                StringComparison.Ordinal), "策略检查"),
            ("错误 Check", baseline.Replace(
                check,
                "        await CheckUpdatePolicyAsync();",
                StringComparison.Ordinal), "策略检查"),
            ("未 await Check", baseline.Replace(
                check,
                "        CheckDeletePolicyAsync();",
                StringComparison.Ordinal), "策略检查"),
            ("包裹 Check", baseline.Replace(
                check,
                "        if (true) { await CheckDeletePolicyAsync(); }",
                StringComparison.Ordinal), "策略检查"),
            ("后置 Check", baseline.Replace(
                "await CheckDeletePolicyAsync();",
                "await WorkAsync();" + Environment.NewLine +
                "        await CheckDeletePolicyAsync();",
                StringComparison.Ordinal), "策略检查"),
            ("DeletePolicy=Default", baseline.Replace(
                $"DeletePolicyName = {deletePermission};",
                $"DeletePolicyName = {DataSources}.Default;",
                StringComparison.Ordinal), "PolicyName"),
            ("缺少 DeletePolicy", baseline.Replace(
                $"DeletePolicyName = {deletePermission};", string.Empty,
                StringComparison.Ordinal), "PolicyName"),
            ("包裹 DeletePolicy", baseline.Replace(
                $"DeletePolicyName = {deletePermission};",
                $"if (true) {{ DeletePolicyName = {deletePermission}; }}",
                StringComparison.Ordinal), "PolicyName"),
            ("重复 DeletePolicy", baseline.Replace(
                $"DeletePolicyName = {deletePermission};",
                $"DeletePolicyName = {deletePermission};" + Environment.NewLine +
                $"        DeletePolicyName = {deletePermission};",
                StringComparison.Ordinal), "PolicyName"),
            ("额外构造器", baseline.Replace(
                "    public DataSourceAppService()",
                "    private DataSourceAppService(int marker) { }" + Environment.NewLine +
                "    public DataSourceAppService()",
                StringComparison.Ordinal), "PolicyName"),
        };

        foreach (var (name, source, marker) in mutations)
        {
            var violations = DataIntegrationAppServiceGuardScanner.ValidateCrudContract(
                "DataSourceAppService",
                source,
                expected,
                policyAssignments);
            violations.Any(x => x.Contains(marker, StringComparison.OrdinalIgnoreCase))
                .ShouldBeTrue($"扫描器必须拒绝{name}变异；实际：{string.Join(" | ", violations)}");
        }
    }

    internal static DataIntegrationServiceContract[] CreateContracts()
        =>
        [
            new(
                "DataSourceAppService.cs",
                "DataSourceAppService",
                DataSources + ".Default",
                [
                    new("GetListAsync(GetDataSourceListInput)", DataSources + ".Default"),
                    new("GetAsync(Guid)", DataSources + ".Default"),
                    Crud("CreateAsync(DataSourceCreateUpdateDto)", DataSources + ".Create", "CheckCreatePolicyAsync"),
                    Crud("UpdateAsync(Guid,DataSourceCreateUpdateDto)", DataSources + ".Update", "CheckUpdatePolicyAsync"),
                    Crud("DeleteAsync(Guid)", DataSources + ".Delete", "CheckDeletePolicyAsync"),
                    Secured("TestConnectionAsync(DataSourceTestConnectionInput)", DataSources + ".Create"),
                    Secured("TestConnectionByIdAsync(Guid,DataSourceTestConnectionInput)", DataSources + ".Update"),
                ],
                PolicyAssignments(DataSources)),
            new(
                "DataPipelineAppService.cs",
                "DataPipelineAppService",
                DataPipelines + ".Default",
                [
                    new("GetListAsync(GetDataPipelineListInput)", DataPipelines + ".Default"),
                    new("GetAsync(Guid)", DataPipelines + ".Default"),
                    Crud("CreateAsync(DataPipelineCreateUpdateDto)", DataPipelines + ".Create", "CheckCreatePolicyAsync"),
                    Crud("UpdateAsync(Guid,DataPipelineCreateUpdateDto)", DataPipelines + ".Update", "CheckUpdatePolicyAsync"),
                    Secured("ExecuteAsync(Guid,Guid)", DataPipelines + ".Execute"),
                    Secured("ExecuteAsync(Guid,Guid,CancellationToken)", DataPipelines + ".Execute"),
                    Secured("ActivateAsync(Guid)", DataPipelines + ".Update"),
                    Secured("ActivateAsync(Guid,CancellationToken)", DataPipelines + ".Update"),
                    Secured("PauseAsync(Guid)", DataPipelines + ".Update"),
                    Crud("DeleteAsync(Guid)", DataPipelines + ".Delete", "CheckDeletePolicyAsync"),
                ],
                PolicyAssignments(DataPipelines)),
            new(
                "PipelineExecutionAppService.cs",
                "PipelineExecutionAppService",
                PipelineExecutions + ".Default",
                [
                    new("GetListAsync(GetPipelineExecutionListInput)", PipelineExecutions + ".Default"),
                    new("GetAsync(Guid)", PipelineExecutions + ".Default"),
                    Secured("ResolveOutcomeAsync(Guid,ResolvePipelineExecutionOutcomeInput)", PipelineExecutions + ".ResolveOutcome"),
                    Secured("ResolveOutcomeAsync(Guid,ResolvePipelineExecutionOutcomeInput,CancellationToken)", PipelineExecutions + ".ResolveOutcome"),
                    Secured("RecoverRunningAsync(Guid,RecoverPipelineExecutionInput)", PipelineExecutions + ".RecoverRunning"),
                    Secured("RecoverRunningAsync(Guid,RecoverPipelineExecutionInput,CancellationToken)", PipelineExecutions + ".RecoverRunning"),
                    Secured("VerifyTargetReceiptAsync(Guid)", PipelineExecutions + ".VerifyTargetReceipt"),
                    Secured("VerifyTargetReceiptAsync(Guid,CancellationToken)", PipelineExecutions + ".VerifyTargetReceipt"),
                ],
                []),
            new(
                "DataPipelineSchemaPreflightAppService.cs",
                "DataPipelineSchemaPreflightAppService",
                DataPipelines + ".Default",
                [
                    new("GetCapabilitiesAsync()", DataPipelines + ".Default"),
                    new("PreflightSchemaAsync(PreflightPipelineSchemaInput,CancellationToken)", DataPipelines + ".Default"),
                ],
                []),
            new(
                "CdcStreamAppService.cs",
                "CdcStreamAppService",
                CdcStreams + ".Default",
                [
                    new("GetListAsync(GetCdcStreamListInput)", CdcStreams + ".Default"),
                    new("GetAsync(Guid)", CdcStreams + ".Default"),
                    Secured("CreateAsync(CdcStreamCreateUpdateDto)", CdcStreams + ".Create"),
                    Secured("UpdateAsync(Guid,CdcStreamCreateUpdateDto)", CdcStreams + ".Update"),
                    Secured("DeleteAsync(Guid)", CdcStreams + ".Delete"),
                    Secured("PreflightAsync(Guid,CancellationToken)", CdcStreams + ".Manage"),
                    Secured("ActivateAsync(Guid,CancellationToken)", CdcStreams + ".Manage"),
                    Secured("PauseAsync(Guid)", CdcStreams + ".Manage"),
                    Secured("CaptureNowAsync(Guid,CancellationToken)", CdcStreams + ".Manage"),
                    Secured("ResetCheckpointAsync(Guid,CdcResetCheckpointInput)", CdcStreams + ".Manage"),
                    Secured("DropSlotAsync(Guid,CancellationToken)", CdcStreams + ".Delete"),
                    new("GetSinkTemplateAsync(Guid,CancellationToken)", CdcStreams + ".Default"),
                    Secured("RunSnapshotAsync(Guid,CancellationToken)", CdcStreams + ".Manage"),
                    Secured("ProvisionExternalSinkAsync(Guid,CancellationToken)", CdcStreams + ".Manage"),
                    Secured("AcceptSchemaBaselineAsync(Guid,CancellationToken)", CdcStreams + ".Manage"),
                    Secured("InspectMirrorSchemaAsync(InspectCdcMirrorSchemaInput,CancellationToken)", CdcStreams + ".Manage"),
                    Secured("CompactLakehouseAsync(Guid,CompactCdcLakehouseInput,CancellationToken)", CdcStreams + ".Manage"),
                    new("GetLakehouseHistoryAsync(Guid,GetCdcLakehouseHistoryInput,CancellationToken)", CdcStreams + ".Default"),
                    Secured("SyncCatalogAsync(Guid,CancellationToken)", CdcStreams + ".Manage"),
                ],
                []),
        ];
    private static DataIntegrationPolicyAssignmentContract[] PolicyAssignments(
        string permissions)
        =>
        [
            new("GetPolicyName", permissions + ".Default"),
            new("GetListPolicyName", permissions + ".Default"),
            new("CreatePolicyName", permissions + ".Create"),
            new("UpdatePolicyName", permissions + ".Update"),
            new("DeletePolicyName", permissions + ".Delete"),
        ];

    private static DataIntegrationEntryContract Secured(
        string signature,
        string permission)
        => new(signature, permission, permission);

    private static DataIntegrationEntryContract Crud(
        string signature,
        string permission,
        string policyCheck)
        => new(signature, permission, permission, policyCheck);

    private static string BuildHostSnippet(
        string firstStatement,
        bool expressionBodied = false,
        string? methodAuthorizePermission = null)
    {
        var methodAttribute = methodAuthorizePermission == null
            ? string.Empty
            : $"    [Authorize({methodAuthorizePermission})]\n";
        if (expressionBodied)
        {
            return $"[Authorize({DataSources}.Default)]\n" +
                   "public class DataSourceAppService\n{\n" +
                   methodAttribute + "    public Task GetAsync(Guid id) =>\n" +
                   "        " + firstStatement.TrimEnd(';') + ";\n}";
        }

        return $"[Authorize({DataSources}.Default)]\n" +
               "public class DataSourceAppService\n{\n" +
               methodAttribute + "    public async Task GetAsync(Guid id)\n    {\n" +
               "        " + firstStatement + "\n" +
               "        await WorkAsync();\n    }\n}";
    }

    private static string BuildCrudSnippet()
        => $$"""
            public class DataSourceAppService
            {
                public DataSourceAppService()
                {
                    DeletePolicyName = {{DataSources}}.Delete;
                }

            [Authorize({{DataSources}}.Delete)]
                public async Task DeleteAsync(Guid id)
                {
                    global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, {{DataSources}}.Delete);
                    await CheckDeletePolicyAsync();
                    await Repository.DeleteAsync(id);
                }
            }
            """;

    private static string GetDataIntegrationDirectory()
        => Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "SwapMid.Application",
            "DataIntegration");

    private static string ReadSource(string fileName)
        => File.ReadAllText(Path.Combine(GetDataIntegrationDirectory(), fileName));

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SwapMid.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录。");
    }

}
