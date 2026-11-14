namespace SwapMid.SystemManagement;

/// <summary>
/// 审计详情参数的读取侧披露策略。历史审计行不可因当前 DTO 已禁审计就被视为安全；
/// DataSource 的所有动作参数固定返回空对象，保留方法、用户、时间等取证元数据。
/// </summary>
internal static class AuditLogParameterDisclosurePolicy
{
    internal const string HiddenParameters = "{}";

    internal static string? Protect(string? serviceName, string? parameters)
        => IsDataSourceAction(serviceName) ? HiddenParameters : parameters;

    internal static bool IsDataSourceAction(string? serviceName)
    {
        return serviceName is
            "SwapMid.DataIntegration.DataSourceAppService" or
            "SwapMid.DataIntegration.IDataSourceAppService" or
            "SwapMid.Controllers.DataIntegration.DataSourceController";
    }
}
