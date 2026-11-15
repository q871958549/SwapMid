namespace SwapMid.Permissions;

public static class SwapMidPermissions
{
    /// <summary>在职人员自助入口；由人员绑定实时计算。</summary>
    public const string EmployeeSelfService = "SwapMid.EmployeeSelfService";

    public const string GroupName = "SwapMid";
    public const string ExportSuffix = ".Export";
    public const string ImportSuffix = ".Import";

    public static class AccountManagement
    {
        public static class Users
        {
            public const string Default = GroupName + ".AccountManagement.Users";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
            public const string Impersonate = Default + ".Impersonate";
        }

        public static class Roles
        {
            public const string Default = GroupName + ".AccountManagement.Roles";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }
    }

    public static class ApiManagement
    {
        public static class ApiRoutes
        {
            public const string Default = GroupName + ".ApiManagement.ApiRoutes";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class ApiGroups
        {
            public const string Default = GroupName + ".ApiManagement.ApiGroups";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class TestConsole
        {
            public const string Default = GroupName + ".ApiManagement.TestConsole";
        }

        public static class ThirdPartyClients
        {
            public const string Default = GroupName + ".ApiManagement.ThirdPartyClients";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }
    }

    public static class DataIntegration
    {
        public static class DataSources
        {
            public const string Default = GroupName + ".DataIntegration.DataSources";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class DataPipelines
        {
            public const string Default = GroupName + ".DataIntegration.DataPipelines";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
            public const string Execute = Default + ".Execute";
        }

        public static class PipelineExecutions
        {
            public const string Default = GroupName + ".DataIntegration.PipelineExecutions";
            public const string ResolveOutcome = Default + ".ResolveOutcome";
            public const string RecoverRunning = Default + ".RecoverRunning";
            public const string VerifyTargetReceipt = Default + ".VerifyTargetReceipt";
        }

        public static class CdcStreams
        {
            public const string Default = GroupName + ".DataIntegration.CdcStreams";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
            public const string Manage = Default + ".Manage";
        }
    }

    public static class DeveloperDocs
    {
        public static class ApiReference
        {
            public const string Default = GroupName + ".DeveloperDocs.ApiReference";
        }

        public static class DbDictionary
        {
            public const string Default = GroupName + ".DeveloperDocs.DbDictionary";
        }
    }

    public static class EnterpriseManagement
    {
        public static class Enterprises
        {
            public const string Default = GroupName + ".EnterpriseManagement.Enterprises";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class Departments
        {
            public const string Default = GroupName + ".EnterpriseManagement.Departments";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class Employees
        {
            public const string Default = GroupName + ".EnterpriseManagement.Employees";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class Positions
        {
            public const string Default = GroupName + ".EnterpriseManagement.Positions";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }
    }

    public static class MessageBus
    {
        public static class Topics
        {
            public const string Default = GroupName + ".MessageBus.Topics";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class Subscriptions
        {
            public const string Default = GroupName + ".MessageBus.Subscriptions";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class Records
        {
            public const string Default = GroupName + ".MessageBus.Records";
            public const string Publish = Default + ".Publish";
            public const string Replay = Default + ".Replay";
        }
    }

    public static class NotificationCenter
    {
        public static class Settings
        {
            public const string Default = GroupName + ".NotificationCenter.Settings";
        }
    }

    public static class SystemManagement
    {
        public static class CodeRules
        {
            public const string Default = GroupName + ".SystemManagement.CodeRules";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class DataDictionaries
        {
            public const string Default = GroupName + ".SystemManagement.DataDictionaries";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class HangfireDashboard
        {
            public const string Default = GroupName + ".SystemManagement.HangfireDashboard";
        }

        public static class ScheduledJobs
        {
            public const string Default = GroupName + ".SystemManagement.ScheduledJobs";
            public const string Manage = Default + ".Manage";
        }

        public static class AuditLogs
        {
            public const string Default = GroupName + ".SystemManagement.AuditLogs";
        }

        public static class SystemHealth
        {
            public const string Default = GroupName + ".SystemManagement.SystemHealth";
        }
    }
}
