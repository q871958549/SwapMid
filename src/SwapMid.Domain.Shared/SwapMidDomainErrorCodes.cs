namespace SwapMid;

/// <summary>
/// 业务错误码定义（格式：SwapMid:{域}:{序号}）。
/// </summary>
public static class SwapMidDomainErrorCodes
{
    public static class AccountRoles
    {
        public const string InvalidMode = "SwapMid:ACC:001";
        public const string AppendRequiresRole = "SwapMid:ACC:002";
        public const string RemoveRequiresRole = "SwapMid:ACC:003";
    }

    public static class ApiManagement
    {
        public const string RouteNotFound = "SwapMid:API:001";
        public const string RoutePathAlreadyExists = "SwapMid:API:002";
        public const string GroupNotFound = "SwapMid:API:003";
        public const string GroupNameAlreadyExists = "SwapMid:API:004";
        public const string ThirdPartyClientIdDuplicate = "SwapMid:API:005";
    }

    public static class DataIntegration
    {
        public const string DataSourceNotFound = "SwapMid:ETL:001";
        public const string DataSourceNameAlreadyExists = "SwapMid:ETL:002";
        public const string PipelineNotFound = "SwapMid:ETL:003";
        public const string PipelineNameAlreadyExists = "SwapMid:ETL:004";
        public const string ExecutionNotFound = "SwapMid:ETL:005";
        public const string DataSourceConfigInvalid = "SwapMid:ETL:006";
        public const string DataSourceCredentialScopeChanged = "SwapMid:ETL:007";
        public const string DataSourceConfigReplacementRequired = "SwapMid:ETL:008";
        public const string PipelineNotActive = "SwapMid:ETL:009";
        public const string PipelineTargetNotConfigured = "SwapMid:ETL:010";
        public const string PipelineExecutionInputInvalid = "SwapMid:ETL:011";
        public const string PipelineExecutionFailed = "SwapMid:ETL:012";
        public const string PipelineExecutionBusy = "SwapMid:ETL:013";
        public const string PipelineExecutionCoordinationUnavailable = "SwapMid:ETL:014";
        public const string PipelineExecutionOutcomeUnknown = "SwapMid:ETL:015";
        public const string PipelineExecutionOutcomeUnresolved = "SwapMid:ETL:016";
        public const string PipelineExecutionReconciledNotApplied = "SwapMid:ETL:017";
        public const string PipelineExecutionResolutionInvalid = "SwapMid:ETL:018";
        public const string PipelineExecutionTargetBusy = "SwapMid:ETL:019";
        public const string PipelineExecutionCapacityExhausted = "SwapMid:ETL:020";
        public const string PipelineTargetResourceGroupRequired = "SwapMid:ETL:021";
        public const string DataSourceTargetResourceGroupImmutable = "SwapMid:ETL:022";
        public const string PipelineExecutionRecoveryInvalid = "SwapMid:ETL:023";
        public const string PipelineExecutionRecoveryConflict = "SwapMid:ETL:024";
        public const string PipelineTargetReceiptInvalid = "SwapMid:ETL:025";
        public const string PipelineTargetReceiptUnavailable = "SwapMid:ETL:026";
        public const string PipelineExecutionReceiptInconclusive = "SwapMid:ETL:027";
        public const string PipelineExecutionReceiptVerificationRequired = "SwapMid:ETL:028";
        public const string PipelineScheduleCronInvalid = "SwapMid:ETL:029";
    }

    public static class EnterpriseManagement
    {
        public const string EnterpriseNotFound = "SwapMid:ENT:001";
        public const string EnterpriseCodeAlreadyExists = "SwapMid:ENT:002";
        public const string DepartmentNotFound = "SwapMid:ENT:003";
        public const string DepartmentCodeAlreadyExists = "SwapMid:ENT:004";
        public const string EmployeeNotFound = "SwapMid:ENT:005";
        public const string EmployeeNoAlreadyExists = "SwapMid:ENT:006";
        public const string EmployeeUserAlreadyBound = "SwapMid:ENT:007";
        public const string PositionNotFound = "SwapMid:ENT:008";
    }

    public static class Layout
    {
        public const string NavOrderTooLong = "SwapMid:LAY:001";
        public const string TablePreferenceTooLong = "SwapMid:LAY:002";
    }

    public static class MessageBus
    {
        public const string TopicNotFound = "SwapMid:MQ:001";
        public const string TopicNameAlreadyExists = "SwapMid:MQ:002";
        public const string SubscriptionNotFound = "SwapMid:MQ:003";
        public const string MessageRecordNotFound = "SwapMid:MQ:004";
    }

    public static class NotificationCenter
    {
        public const string ChannelNotConfigured = "SwapMid:NC:001";
        public const string SendFailed = "SwapMid:NC:002";
        public const string InvalidWebhookToken = "SwapMid:NC:003";
    }

    public static class Scheduling
    {
        public const string JobArgumentsTooLong = "SwapMid:SCH:001";
    }

    public static class SystemManagement
    {
        public const string DictionaryOptionProtected = "SwapMid:SYS:006";
        public const string DictionaryOptionValueTooLong = "SwapMid:SYS:007";
        public const string DataDictionaryCodeExists = "SwapMid:System:DataDictionaryCodeExists";
        public const string DataDictionaryItemValueExists = "SwapMid:System:DataDictionaryItemValueExists";
        public const string DataDictionaryItemReorderMismatch = "SwapMid:System:DataDictionaryItemReorderMismatch";
        public const string CodeRuleEntityTypeExists = "SwapMid:System:CodeRuleEntityTypeExists";
        public const string CodeRuleNotFound = "SwapMid:System:CodeRuleNotFound";
    }
}
