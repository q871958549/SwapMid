using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwapMid.Infrastructure;

/// <summary>
/// Prometheus 瞬时查询的只读端口。把「查 Prometheus」从系统体检业务逻辑里解耦：
/// 生产用 <see cref="PrometheusClient"/>（连接 Prometheus HTTP API）；测试可注入假实现喂内存样本，
/// 从而在不连真 Prometheus 的前提下验证指标→中文健康的翻译逻辑。
/// ⚠️ 所有方法在未配置/连不上/超时/返回异常时返回 null 或空集合，绝不抛异常——调用方据此降级为「未知」。
/// </summary>
public interface IPrometheusClient
{
    /// <summary>Prometheus 配置是否齐全（BaseUrl 非空；缺失时调用方应降级为未知）。</summary>
    bool IsConfigured { get; }

    /// <summary>执行瞬时查询，返回第一条结果的标量值；无数据 / 任何异常返回 null。</summary>
    Task<double?> QueryScalarAsync(string promql);

    /// <summary>执行瞬时查询，返回全部结果（含标签集与值）；无数据 / 任何异常返回空列表。</summary>
    Task<IReadOnlyList<PrometheusSample>> QueryVectorAsync(string promql);
}
