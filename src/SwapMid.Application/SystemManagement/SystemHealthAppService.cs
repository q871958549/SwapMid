using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Infrastructure;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using static SwapMid.SystemManagement.HealthCheckCatalog;

namespace SwapMid.SystemManagement;

/// <summary>
/// 系统体检（只读）。遍历 <see cref="HealthCheckCatalog"/> 的检查项清单，查 Prometheus 把每项指标
/// 翻译成中文健康状态（结论 / 影响 / 建议），面向非技术管理者说「影响」不说「零件」。
/// 总状态 = 所有项里最差的一项（「未知」不拉低总状态）。Prometheus 连不上/超时全程降级为「未知」，绝不抛异常。
/// </summary>
[Authorize(SwapMidPermissions.SystemManagement.SystemHealth.Default)]
public class SystemHealthAppService : SwapMidAppService, ISystemHealthAppService
{
    private readonly IPrometheusClient _prometheus;

    public SystemHealthAppService(IPrometheusClient prometheus)
    {
        _prometheus = prometheus;
    }

    public async Task<SystemHealthDto> GetAsync()
    {
        var items = await EvaluateAllAsync();

        var overall = ComputeOverall(items);
        return new SystemHealthDto
        {
            OverallStatus = overall,
            OverallConclusion = OverallConclusionOf(overall),
            CheckedAt = Clock.Now, // 服务器本地时间（平台未配 AbpClockOptions=UTC，Clock.Now 即本地）。
            Items = items
        };
    }

    /// <summary>
    /// 固定检查项彼此独立，并发查询可保持清单顺序，同时避免 Prometheus 断连时把逐项超时串成分钟级等待。
    /// 同窗请求也让默认 MinimumThroughput=10 的熔断器在本页真实可达。
    /// </summary>
    private async Task<List<HealthItemDto>> EvaluateAllAsync()
    {
        var evaluations = Items.Select(EvaluateAsync);
        return (await Task.WhenAll(evaluations)).ToList();
    }

    private async Task<HealthItemDto> EvaluateAsync(HealthCheckDefinition def)
    {
        // 未配置 Prometheus（本地开发常见）：直接全项「未知」，省去逐条查询。
        if (!_prometheus.IsConfigured)
        {
            return Unknown(def);
        }

        return def.Kind switch
        {
            HealthCheckKind.Availability => EvaluateAvailability(def, await _prometheus.QueryScalarAsync(def.PromQl)),
            HealthCheckKind.AvailabilityMulti => EvaluateAvailabilityMulti(def, await _prometheus.QueryVectorAsync(def.PromQl)),
            HealthCheckKind.UsagePercent => EvaluateUsage(def, await _prometheus.QueryScalarAsync(def.PromQl)),
            HealthCheckKind.FailureRatio => EvaluateFailureRatio(def, await _prometheus.QueryScalarAsync(def.PromQl)),
            _ => Unknown(def)
        };
    }

    // ===== 各类判定 =====

    // 可用性单序列：≈1 正常，≈0 异常，无数据未知。
    private static HealthItemDto EvaluateAvailability(HealthCheckDefinition def, double? value)
    {
        if (value == null)
        {
            return Unknown(def);
        }

        return value.Value >= 0.5 ? Healthy(def) : Critical(def, def.CriticalConclusion);
    }

    // 可用性多目标：任一序列为 0 即异常并点名；全部≥1 正常；无序列未知。
    private static HealthItemDto EvaluateAvailabilityMulti(HealthCheckDefinition def, IReadOnlyList<PrometheusSample> samples)
    {
        if (samples.Count == 0)
        {
            return Unknown(def);
        }

        var down = samples.Where(s => s.Value < 0.5).ToList();
        if (down.Count == 0)
        {
            return Healthy(def);
        }

        var names = down
            .Select(DescribeTarget)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();
        var detail = names.Count > 0 ? string.Join("、", names) : $"{down.Count} 个目标";
        return Critical(def, def.CriticalConclusion.Replace("{value}", detail), detail);
    }

    // 使用率百分比：≥红阈异常，≥黄阈需留意，否则正常；无数据未知。
    private static HealthItemDto EvaluateUsage(HealthCheckDefinition def, double? value)
    {
        if (value == null)
        {
            return Unknown(def);
        }

        var text = FormatPercent(value.Value);
        if (value.Value >= def.CriticalThreshold)
        {
            return Critical(def, def.CriticalConclusion.Replace("{value}", text), text);
        }

        if (value.Value >= def.WarnThreshold)
        {
            return Warning(def, def.WarningConclusion?.Replace("{value}", text), text);
        }

        return Healthy(def, text);
    }

    // 失败占比（0~1）：≥红阈异常，≥黄阈需留意，否则正常；无数据未知（可能暂无业务发生）。
    private static HealthItemDto EvaluateFailureRatio(HealthCheckDefinition def, double? value)
    {
        if (value == null || double.IsNaN(value.Value))
        {
            return Unknown(def);
        }

        var text = FormatRatioAsPercent(value.Value);
        if (value.Value >= def.CriticalThreshold)
        {
            return Critical(def, def.CriticalConclusion.Replace("{value}", text), text);
        }

        if (value.Value >= def.WarnThreshold)
        {
            return Warning(def, def.WarningConclusion?.Replace("{value}", text), text);
        }

        return Healthy(def, text);
    }

    // ===== 结果工厂 =====

    private static HealthItemDto Healthy(HealthCheckDefinition def, string? detail = null) => new()
    {
        Name = def.Name,
        Status = HealthStatus.Healthy,
        Conclusion = def.HealthyConclusion,
        Impact = null,
        Suggestion = null,
        Detail = detail
    };

    private static HealthItemDto Warning(HealthCheckDefinition def, string? conclusion, string? detail = null) => new()
    {
        Name = def.Name,
        Status = HealthStatus.Warning,
        Conclusion = string.IsNullOrWhiteSpace(conclusion) ? def.CriticalConclusion : conclusion!,
        Impact = def.Impact,
        Suggestion = def.WarningSuggestion ?? def.CriticalSuggestion,
        Detail = detail
    };

    private static HealthItemDto Critical(HealthCheckDefinition def, string conclusion, string? detail = null) => new()
    {
        Name = def.Name,
        Status = HealthStatus.Critical,
        Conclusion = conclusion,
        Impact = def.Impact,
        Suggestion = def.CriticalSuggestion,
        Detail = detail
    };

    private static HealthItemDto Unknown(HealthCheckDefinition def) => new()
    {
        Name = def.Name,
        Status = HealthStatus.Unknown,
        Conclusion = "监控数据源未连接，暂时无法判定该项状态。",
        Impact = null,
        Suggestion = "如需查看该项，请确认监控（Prometheus）已部署并正常采集。",
        Detail = null
    };

    // ===== 辅助 =====

    // 总状态 = 最差项；「未知」不拉低（只有全部未知时总状态才是未知）。
    private static HealthStatus ComputeOverall(IReadOnlyList<HealthItemDto> items)
    {
        if (items.Count == 0)
        {
            return HealthStatus.Unknown;
        }

        if (items.Any(i => i.Status == HealthStatus.Critical))
        {
            return HealthStatus.Critical;
        }

        if (items.Any(i => i.Status == HealthStatus.Warning))
        {
            return HealthStatus.Warning;
        }

        if (items.Any(i => i.Status == HealthStatus.Healthy))
        {
            return HealthStatus.Healthy;
        }

        return HealthStatus.Unknown;
    }

    private static string OverallConclusionOf(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "系统整体运行正常，各项指标健康。",
        HealthStatus.Warning => "系统基本正常，有个别项目需要留意。",
        HealthStatus.Critical => "系统存在异常，可能已影响部分业务，请关注下方标红项目。",
        _ => "监控数据源未连接，暂时无法评估系统整体状态。"
    };

    // blackbox 用 instance/dep/system 描述目标；up 类用 instance/job。取最有意义的一个。
    private static string DescribeTarget(PrometheusSample s)
        => s.Label("dep") ?? s.Label("system") ?? s.Label("instance") ?? s.Label("job") ?? string.Empty;

    private static string FormatPercent(double value)
        => value.ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string FormatRatioAsPercent(double ratio)
    {
        var pct = ratio * 100;
        var digits = pct < 1 ? "0.0" : "0";
        return pct.ToString(digits, CultureInfo.InvariantCulture) + "%";
    }
}
