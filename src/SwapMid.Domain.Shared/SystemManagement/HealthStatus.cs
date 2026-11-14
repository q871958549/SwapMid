using System.ComponentModel;

namespace SwapMid.SystemManagement;

/// <summary>
/// 系统体检单项 / 总体健康状态。数值越大越糟（Unknown 单列，表示监控数据缺失，不参与「取最差」排序时按最低优先级处理）。
/// 面向非技术管理者的红绿灯：Healthy=绿、Warning=黄、Critical=红、Unknown=灰。
/// </summary>
public enum HealthStatus
{
    /// <summary>正常（绿）。</summary>
    [Description("正常")]
    Healthy = 0,

    /// <summary>需留意（黄）：尚可服务但趋势变差，应安排关注。</summary>
    [Description("需留意")]
    Warning = 1,

    /// <summary>异常（红）：已经或即将影响业务，需立即处置。</summary>
    [Description("异常")]
    Critical = 2,

    /// <summary>未知（灰）：监控数据源未连接 / 查询失败 / 暂无数据，无法判定。</summary>
    [Description("未知")]
    Unknown = 3
}
