using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

/// <summary>
/// 管道 ETL 执行器的端口。把「跑 ETL(连源/目标库读写)」从 <see cref="DataPipelineAppService"/>
/// 解耦出来:生产用 <see cref="EtlPipelineRunner"/>(ADO.NET 流式读取 + Dapper 参数化
/// 临时暂存 + 源关闭后的短事务批写，直连 SqlServer/MySQL);测试可注入假实现
/// (<c>FakeEtlPipelineRunner</c>)直接返回结果,从而在不连真库的前提下验证管道激活→执行→暂停的
/// 状态流转与执行落库逻辑(外部依赖抽接口 + 测试替身)。
/// </summary>
public interface IEtlPipelineRunner
{
    /// <summary>跑一次 ETL:读 <paramref name="source"/> 的 sourceQuery → 写 <paramref name="target"/> 的 targetTable。</summary>
    Task<EtlRunResult> RunAsync(
        EtlExecutionContext executionContext,
        DataSource source,
        DataSource target,
        string? transformConfig,
        CancellationToken ct = default);
}
