using System.Threading;
using System.Threading.Tasks;
using SwapMid.Scheduling;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace SwapMid.HttpApiHost.Scheduling;

/// <summary>
/// 管控页「后台执行」的直接 Hangfire 入口。必须直接返回 Runner 的 Task，禁止 async/await 包装：
/// typed OCE marker 只有以 Faulted Task 原样到达 Hangfire，才能同时保留停机重排与历史去重语义。
/// </summary>
/// <remarks>
/// 🔴 本类**不得加 <c>sealed</c>**：<see cref="UnitOfWorkAttribute"/> 使 ABP 判定该类需要 UoW 拦截器，
/// Autofac 遂经 Castle DynamicProxy 生成**类代理**（派生自本类）——sealed 无法被继承，代理生成失败，
/// 异常发生在**容器构建阶段**，Host 直接起不来。
/// 2026-07-29 生产实锤：加 sealed 后 api 容器启动即崩、Restarting 循环、全站接口不可用，已回滚。
/// 报错文本：<c>Could not load type 'Castle.Proxies.RunScheduledJobHangfireJobProxy' … parent type is sealed</c>。
/// ⚠️ 编译、单元测试、CI 全绿也拦不住——单测不构建完整 ABP 容器，只有真实启动才暴露。
/// </remarks>
public class RunScheduledJobHangfireJob : IRunnerDrivenJob, ITransientDependency
{
    private readonly ScheduledJobRunner _runner;

    public RunScheduledJobHangfireJob(ScheduledJobRunner runner)
    {
        _runner = runner;
    }

    [UnitOfWork(IsDisabled = true)]
    public Task ExecuteAsync(
        RunScheduledJobArgs args,
        CancellationToken cancellationToken)
        => _runner.RunForHangfireAsync(
            args.JobKey,
            new JobRunContext
            {
                TriggerKind = JobTriggerKind.Manual,
                IsDryRun = false,
                ArgsJson = args.ArgsJson is
                    { Length: > JobRunContext.MaxArgsJsonCharacters }
                    ? args.ArgsJson
                    : string.IsNullOrWhiteSpace(args.ArgsJson)
                        ? null
                        : args.ArgsJson,
            },
            cancellationToken);
}
