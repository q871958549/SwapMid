using SwapMid.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace SwapMid.Permissions;

public class SwapMidPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        const string prefix = SwapMidPermissions.GroupName;

        context.AddGroup(prefix + ".EmployeeSelfService", L("Permission:EmployeeSelfService"))
            .AddPermission(SwapMidPermissions.EmployeeSelfService, L("Permission:EmployeeSelfService"))
            .WithProviders("ActiveEmployee");

        DefineEnterprise(context, prefix);
        DefineAccount(context, prefix);
        DefineMessageBus(context, prefix);
        DefineDataIntegration(context, prefix);
        DefineApiManagement(context, prefix);
        DefineSystemManagement(context, prefix);
        DefineDeveloperDocs(context, prefix);
        DefineNotificationCenter(context, prefix);
    }

    private static void DefineEnterprise(IPermissionDefinitionContext context, string prefix)
    {
        var enterpriseGroup = context.AddGroup(prefix + ".EnterpriseManagement", L("Permission:Group:EnterpriseManagement"));

        var enterprisesPermission = enterpriseGroup.AddPermission(
            SwapMidPermissions.EnterpriseManagement.Enterprises.Default,
            L("Permission:EnterpriseManagement:Enterprises"));
        enterprisesPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Enterprises.Create, L("Permission:Create"));
        enterprisesPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Enterprises.Update, L("Permission:Update"));
        enterprisesPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Enterprises.Delete, L("Permission:Delete"));

        var departmentsPermission = enterpriseGroup.AddPermission(
            SwapMidPermissions.EnterpriseManagement.Departments.Default,
            L("Permission:EnterpriseManagement:Departments"));
        departmentsPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Departments.Create, L("Permission:Create"));
        departmentsPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Departments.Update, L("Permission:Update"));
        departmentsPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Departments.Delete, L("Permission:Delete"));

        var employeesPermission = enterpriseGroup.AddPermission(
            SwapMidPermissions.EnterpriseManagement.Employees.Default,
            L("Permission:EnterpriseManagement:Employees"));
        employeesPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Employees.Create, L("Permission:Create"));
        employeesPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Employees.Update, L("Permission:Update"));
        employeesPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Employees.Delete, L("Permission:Delete"));

        var positionsPermission = enterpriseGroup.AddPermission(
            SwapMidPermissions.EnterpriseManagement.Positions.Default,
            L("Permission:EnterpriseManagement:Positions"));
        positionsPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Positions.Create, L("Permission:Create"));
        positionsPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Positions.Update, L("Permission:Update"));
        positionsPermission.AddChild(SwapMidPermissions.EnterpriseManagement.Positions.Delete, L("Permission:Delete"));

        AddDataIo(enterprisesPermission);
        AddDataIo(departmentsPermission);
        AddDataIo(employeesPermission);
        AddDataIo(positionsPermission);
    }

    private static void DefineAccount(IPermissionDefinitionContext context, string prefix)
    {
        var accountGroup = context.AddGroup(prefix + ".AccountManagement", L("Permission:Group:AccountManagement"));

        var usersPermission = accountGroup.AddPermission(
            SwapMidPermissions.AccountManagement.Users.Default,
            L("Permission:AccountManagement:Users"));
        usersPermission.AddChild(SwapMidPermissions.AccountManagement.Users.Create, L("Permission:Create"));
        usersPermission.AddChild(SwapMidPermissions.AccountManagement.Users.Update, L("Permission:Update"));
        usersPermission.AddChild(SwapMidPermissions.AccountManagement.Users.Delete, L("Permission:Delete"));
        usersPermission.AddChild(SwapMidPermissions.AccountManagement.Users.Impersonate, L("Permission:AccountManagement:Users:Impersonate"));

        var rolesPermission = accountGroup.AddPermission(
            SwapMidPermissions.AccountManagement.Roles.Default,
            L("Permission:AccountManagement:Roles"));
        rolesPermission.AddChild(SwapMidPermissions.AccountManagement.Roles.Create, L("Permission:Create"));
        rolesPermission.AddChild(SwapMidPermissions.AccountManagement.Roles.Update, L("Permission:Update"));
        rolesPermission.AddChild(SwapMidPermissions.AccountManagement.Roles.Delete, L("Permission:Delete"));

        AddDataIo(usersPermission);
        AddDataIo(rolesPermission);
    }

    private static void DefineMessageBus(IPermissionDefinitionContext context, string prefix)
    {
        var messageBusGroup = context.AddGroup(prefix + ".MessageBus", L("Permission:Group:MessageBus"));

        var topicsPermission = messageBusGroup.AddPermission(
            SwapMidPermissions.MessageBus.Topics.Default,
            L("Permission:MessageBus:Topics"));
        topicsPermission.AddChild(SwapMidPermissions.MessageBus.Topics.Create, L("Permission:Create"));
        topicsPermission.AddChild(SwapMidPermissions.MessageBus.Topics.Update, L("Permission:Update"));
        topicsPermission.AddChild(SwapMidPermissions.MessageBus.Topics.Delete, L("Permission:Delete"));

        var subsPermission = messageBusGroup.AddPermission(
            SwapMidPermissions.MessageBus.Subscriptions.Default,
            L("Permission:MessageBus:Subscriptions"));
        subsPermission.AddChild(SwapMidPermissions.MessageBus.Subscriptions.Create, L("Permission:Create"));
        subsPermission.AddChild(SwapMidPermissions.MessageBus.Subscriptions.Update, L("Permission:Update"));
        subsPermission.AddChild(SwapMidPermissions.MessageBus.Subscriptions.Delete, L("Permission:Delete"));

        var recordsPermission = messageBusGroup.AddPermission(
            SwapMidPermissions.MessageBus.Records.Default,
            L("Permission:MessageBus:Records"));
        recordsPermission.AddChild(SwapMidPermissions.MessageBus.Records.Publish, L("Permission:Publish"));
        recordsPermission.AddChild(SwapMidPermissions.MessageBus.Records.Replay, L("Permission:Replay"));

        AddDataIo(topicsPermission);
        AddDataIo(subsPermission);
        AddDataIo(recordsPermission);
    }

    private static void DefineDataIntegration(IPermissionDefinitionContext context, string prefix)
    {
        var dataIntegrationGroup = context.AddGroup(prefix + ".DataIntegration", L("Permission:Group:DataIntegration"));

        var dataSourcesPermission = dataIntegrationGroup.AddPermission(
            SwapMidPermissions.DataIntegration.DataSources.Default,
            L("Permission:DataIntegration:DataSources"),
            multiTenancySide: MultiTenancySides.Host);
        dataSourcesPermission.AddChild(SwapMidPermissions.DataIntegration.DataSources.Create, L("Permission:Create"), multiTenancySide: MultiTenancySides.Host);
        dataSourcesPermission.AddChild(SwapMidPermissions.DataIntegration.DataSources.Update, L("Permission:Update"), multiTenancySide: MultiTenancySides.Host);
        dataSourcesPermission.AddChild(SwapMidPermissions.DataIntegration.DataSources.Delete, L("Permission:Delete"), multiTenancySide: MultiTenancySides.Host);

        var dataPipelinesPermission = dataIntegrationGroup.AddPermission(
            SwapMidPermissions.DataIntegration.DataPipelines.Default,
            L("Permission:DataIntegration:DataPipelines"),
            multiTenancySide: MultiTenancySides.Host);
        dataPipelinesPermission.AddChild(SwapMidPermissions.DataIntegration.DataPipelines.Create, L("Permission:Create"), multiTenancySide: MultiTenancySides.Host);
        dataPipelinesPermission.AddChild(SwapMidPermissions.DataIntegration.DataPipelines.Update, L("Permission:Update"), multiTenancySide: MultiTenancySides.Host);
        dataPipelinesPermission.AddChild(SwapMidPermissions.DataIntegration.DataPipelines.Delete, L("Permission:Delete"), multiTenancySide: MultiTenancySides.Host);
        dataPipelinesPermission.AddChild(SwapMidPermissions.DataIntegration.DataPipelines.Execute, L("Permission:Execute"), multiTenancySide: MultiTenancySides.Host);

        var pipelineExecutionsPermission = dataIntegrationGroup.AddPermission(
            SwapMidPermissions.DataIntegration.PipelineExecutions.Default,
            L("Permission:DataIntegration:PipelineExecutions"),
            multiTenancySide: MultiTenancySides.Host);
        pipelineExecutionsPermission.AddChild(SwapMidPermissions.DataIntegration.PipelineExecutions.ResolveOutcome, L("Permission:ResolveOutcome"), multiTenancySide: MultiTenancySides.Host);
        pipelineExecutionsPermission.AddChild(SwapMidPermissions.DataIntegration.PipelineExecutions.RecoverRunning, L("Permission:RecoverRunning"), multiTenancySide: MultiTenancySides.Host);
        pipelineExecutionsPermission.AddChild(SwapMidPermissions.DataIntegration.PipelineExecutions.VerifyTargetReceipt, L("Permission:VerifyTargetReceipt"), multiTenancySide: MultiTenancySides.Host);

        AddDataIo(dataSourcesPermission, MultiTenancySides.Host);
        AddDataIo(dataPipelinesPermission, MultiTenancySides.Host);
        AddDataIo(pipelineExecutionsPermission, MultiTenancySides.Host);
    }

    private static void DefineApiManagement(IPermissionDefinitionContext context, string prefix)
    {
        var apiManagementGroup = context.AddGroup(prefix + ".ApiManagement", L("Permission:Group:ApiManagement"));

        var apiRoutesPermission = apiManagementGroup.AddPermission(
            SwapMidPermissions.ApiManagement.ApiRoutes.Default,
            L("Permission:ApiManagement:ApiRoutes"));
        apiRoutesPermission.AddChild(SwapMidPermissions.ApiManagement.ApiRoutes.Create, L("Permission:Create"));
        apiRoutesPermission.AddChild(SwapMidPermissions.ApiManagement.ApiRoutes.Update, L("Permission:Update"));
        apiRoutesPermission.AddChild(SwapMidPermissions.ApiManagement.ApiRoutes.Delete, L("Permission:Delete"));

        var apiGroupsPermission = apiManagementGroup.AddPermission(
            SwapMidPermissions.ApiManagement.ApiGroups.Default,
            L("Permission:ApiManagement:ApiGroups"));
        apiGroupsPermission.AddChild(SwapMidPermissions.ApiManagement.ApiGroups.Create, L("Permission:Create"));
        apiGroupsPermission.AddChild(SwapMidPermissions.ApiManagement.ApiGroups.Update, L("Permission:Update"));
        apiGroupsPermission.AddChild(SwapMidPermissions.ApiManagement.ApiGroups.Delete, L("Permission:Delete"));

        apiManagementGroup.AddPermission(
            SwapMidPermissions.ApiManagement.TestConsole.Default,
            L("Permission:ApiManagement:TestConsole"));

        var thirdPartyClientsPermission = apiManagementGroup.AddPermission(
            SwapMidPermissions.ApiManagement.ThirdPartyClients.Default,
            L("Permission:ApiManagement:ThirdPartyClients"));
        thirdPartyClientsPermission.AddChild(SwapMidPermissions.ApiManagement.ThirdPartyClients.Create, L("Permission:Create"));
        thirdPartyClientsPermission.AddChild(SwapMidPermissions.ApiManagement.ThirdPartyClients.Update, L("Permission:Update"));
        thirdPartyClientsPermission.AddChild(SwapMidPermissions.ApiManagement.ThirdPartyClients.Delete, L("Permission:Delete"));

        AddDataIo(apiRoutesPermission);
        AddDataIo(apiGroupsPermission);
    }

    private static void DefineSystemManagement(IPermissionDefinitionContext context, string prefix)
    {
        var systemManagementGroup = context.AddGroup(prefix + ".SystemManagement", L("Permission:Group:SystemManagement"));

        var codeRulesPermission = systemManagementGroup.AddPermission(
            SwapMidPermissions.SystemManagement.CodeRules.Default,
            L("Permission:SystemManagement:CodeRules"));
        codeRulesPermission.AddChild(SwapMidPermissions.SystemManagement.CodeRules.Create, L("Permission:Create"));
        codeRulesPermission.AddChild(SwapMidPermissions.SystemManagement.CodeRules.Update, L("Permission:Update"));
        codeRulesPermission.AddChild(SwapMidPermissions.SystemManagement.CodeRules.Delete, L("Permission:Delete"));

        var dataDictionariesPermission = systemManagementGroup.AddPermission(
            SwapMidPermissions.SystemManagement.DataDictionaries.Default,
            L("Permission:SystemManagement:DataDictionaries"));
        dataDictionariesPermission.AddChild(SwapMidPermissions.SystemManagement.DataDictionaries.Create, L("Permission:Create"));
        dataDictionariesPermission.AddChild(SwapMidPermissions.SystemManagement.DataDictionaries.Update, L("Permission:Update"));
        dataDictionariesPermission.AddChild(SwapMidPermissions.SystemManagement.DataDictionaries.Delete, L("Permission:Delete"));

        systemManagementGroup.AddPermission(
            SwapMidPermissions.SystemManagement.HangfireDashboard.Default,
            L("Permission:SystemManagement:HangfireDashboard"));

        var scheduledJobsPermission = systemManagementGroup.AddPermission(
            SwapMidPermissions.SystemManagement.ScheduledJobs.Default,
            L("Permission:SystemManagement:ScheduledJobs"));
        scheduledJobsPermission.AddChild(
            SwapMidPermissions.SystemManagement.ScheduledJobs.Manage,
            L("Permission:Manage"));

        systemManagementGroup.AddPermission(
            SwapMidPermissions.SystemManagement.AuditLogs.Default,
            L("Permission:SystemManagement:AuditLogs"));

        systemManagementGroup.AddPermission(
            SwapMidPermissions.SystemManagement.SystemHealth.Default,
            L("Permission:SystemManagement:SystemHealth"));

        AddDataIo(codeRulesPermission);
    }

    private static void DefineDeveloperDocs(IPermissionDefinitionContext context, string prefix)
    {
        var developerDocsGroup = context.AddGroup(prefix + ".DeveloperDocs", L("Permission:Group:DeveloperDocs"));

        developerDocsGroup.AddPermission(
            SwapMidPermissions.DeveloperDocs.ApiReference.Default,
            L("Permission:DeveloperDocs:ApiReference"));

        developerDocsGroup.AddPermission(
            SwapMidPermissions.DeveloperDocs.DbDictionary.Default,
            L("Permission:DeveloperDocs:DbDictionary"));
    }

    private static void DefineNotificationCenter(IPermissionDefinitionContext context, string prefix)
    {
        var notificationCenterGroup = context.AddGroup(prefix + ".NotificationCenter", L("Permission:Group:NotificationCenter"));
        notificationCenterGroup.AddPermission(
            SwapMidPermissions.NotificationCenter.Settings.Default,
            L("Permission:NotificationCenter:Settings"));
    }

    private static void AddDataIo(
        PermissionDefinition permission,
        MultiTenancySides multiTenancySide = MultiTenancySides.Both)
    {
        permission.AddChild(
            permission.Name + SwapMidPermissions.ExportSuffix,
            L("Permission:Export"),
            multiTenancySide);
        permission.AddChild(
            permission.Name + SwapMidPermissions.ImportSuffix,
            L("Permission:Import"),
            multiTenancySide);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<SwapMidResource>(name);
    }
}
