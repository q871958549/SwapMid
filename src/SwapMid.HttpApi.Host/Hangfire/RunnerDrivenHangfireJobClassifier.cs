using System;
using SwapMid.Scheduling;
using Hangfire.Common;

namespace SwapMid.Hangfire;

/// <summary>
/// 精确识别由 ScheduledJobRunner 驱动的 Hangfire 作业。
/// 覆盖 35 个 recurring 直接入口、Host 的手工带参直接入口，以及升级期间仍可能残留的
/// ABP RunScheduledJobArgs 泛型 adapter；绝不把其它 ABP adapter 或普通 Hangfire 作业纳入。
/// 独立的 non-retry marker 只参与零重试策略，不能把非 Runner adapter 混入统一执行历史。
/// </summary>
internal static class RunnerDrivenHangfireJobClassifier
{
    private const string AbpAdapterTypeName =
        "Volo.Abp.BackgroundJobs.Hangfire.HangfireJobExecutionAdapter`1";

    public static bool IsRunnerDriven(Job job)
    {
        if (!IsPublicInstanceExecuteAsync(job))
        {
            return false;
        }

        var parameters = job.Method.GetParameters();
        if (typeof(IRunnerDrivenJob).IsAssignableFrom(job.Type))
        {
            var directRecurring = parameters.Length == 1
                && parameters[0].ParameterType == typeof(System.Threading.CancellationToken);
            var directWithArgs = parameters.Length == 2
                && typeof(IRunnerDrivenJobArgs).IsAssignableFrom(parameters[0].ParameterType)
                && parameters[1].ParameterType == typeof(System.Threading.CancellationToken);
            return directRecurring || directWithArgs;
        }

        if (!IsAbpAdapter(job.Type, out var argsType)
            || !typeof(IRunnerDrivenJobArgs).IsAssignableFrom(argsType))
        {
            return false;
        }

        return HasExactAbpAdapterSignature(parameters, argsType);
    }

    internal static bool IsRunnerDrivenType(Type jobType)
        => typeof(IRunnerDrivenJob).IsAssignableFrom(jobType)
            || (IsAbpAdapter(jobType, out var argsType)
                && typeof(IRunnerDrivenJobArgs).IsAssignableFrom(argsType));

    /// <summary>
    /// Runner 作业和带精确 non-retry 参数 marker 的 ABP adapter 都必须覆盖全局重试策略；
    /// 后者仍保持非 Runner 分类，让 <see cref="ScheduledJobExecutionFilter"/> 正常记录其执行。
    /// </summary>
    internal static bool RequiresZeroAutomaticRetry(Job job)
    {
        if (IsRunnerDriven(job))
        {
            return true;
        }

        if (!IsPublicInstanceExecuteAsync(job)
            || !IsAbpAdapter(job.Type, out var argsType)
            || !typeof(INonRetryableAbpBackgroundJobArgs).IsAssignableFrom(argsType))
        {
            return false;
        }

        return HasExactAbpAdapterSignature(job.Method.GetParameters(), argsType);
    }

    private static bool IsPublicInstanceExecuteAsync(Job job)
        => string.Equals(job.Method.Name, "ExecuteAsync", StringComparison.Ordinal)
            && !job.Method.IsStatic
            && job.Method.IsPublic;

    private static bool HasExactAbpAdapterSignature(
        System.Reflection.ParameterInfo[] parameters,
        Type argsType)
        => parameters.Length == 3
            && parameters[0].ParameterType == typeof(string)
            && parameters[1].ParameterType == argsType
            && parameters[2].ParameterType == typeof(System.Threading.CancellationToken);

    private static bool IsAbpAdapter(Type jobType, out Type argsType)
    {
        argsType = null!;
        if (!jobType.IsGenericType
            || !string.Equals(
                jobType.GetGenericTypeDefinition().FullName,
                AbpAdapterTypeName,
                StringComparison.Ordinal))
        {
            return false;
        }

        var arguments = jobType.GetGenericArguments();
        if (arguments.Length != 1)
        {
            return false;
        }

        argsType = arguments[0];
        return true;
    }
}
