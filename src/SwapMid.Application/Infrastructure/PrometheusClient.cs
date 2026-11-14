using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace SwapMid.Infrastructure;

/// <summary>
/// Prometheus HTTP API 只读客户端（仅用于系统体检翻译指标）：
/// 从 IConfiguration 读配置 + IHttpClientFactory 命名客户端 + 短超时。
/// ⚠️ 设计要点：任何失败（未配置 / 连不上 / 超时 / 返回体异常）一律返回 null 或空集合、绝不抛异常——
///    本地开发连不到 Prometheus 属正常，调用方（SystemHealthAppService）据此优雅降级为「未知」。
/// </summary>
public class PrometheusClient : IPrometheusClient, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PrometheusClient> _logger;

    public PrometheusClient(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<PrometheusClient> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private string? BaseUrl => _configuration["Prometheus:BaseUrl"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>
    /// 执行瞬时查询，返回第一条结果的标量值；无数据 / 任何异常返回 null。
    /// </summary>
    public async Task<double?> QueryScalarAsync(string promql)
    {
        var results = await QueryVectorAsync(promql);
        if (results.Count == 0)
        {
            return null;
        }

        return results[0].Value;
    }

    /// <summary>
    /// 执行瞬时查询，返回全部结果（含每条的标签集与值）；无数据 / 任何异常返回空列表。
    /// 供「按 target/job 区分」的检查项使用（如 blackbox 多探测目标、up 多实例）。
    /// </summary>
    public async Task<IReadOnlyList<PrometheusSample>> QueryVectorAsync(string promql)
    {
        if (!IsConfigured)
        {
            return Array.Empty<PrometheusSample>();
        }

        try
        {
            using var http = _httpClientFactory.CreateClient(ExternalHttpClientNames.Prometheus);

            var url = BaseUrl!.TrimEnd('/') + "/api/v1/query?query=" + HttpUtility.UrlEncode(promql);
            using var response = await http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Prometheus 查询返回 HTTP {Status}：{PromQL}", (int)response.StatusCode, promql);
                return Array.Empty<PrometheusSample>();
            }

            var text = await response.Content.ReadAsStringAsync();
            return Parse(text);
        }
        catch (Exception ex)
        {
            // 连不上 / 超时 / DNS 解析失败等，本地开发常见——降级为空、不打扰。
            _logger.LogDebug(ex, "Prometheus 查询失败（将降级为未知）：{PromQL}", promql);
            return Array.Empty<PrometheusSample>();
        }
    }

    // 解析 Prometheus 瞬时查询响应：{ "status":"success", "data":{ "resultType":"vector",
    // "result":[ { "metric":{...labels...}, "value":[<ts>, "<num>"] }, ... ] } }
    private static IReadOnlyList<PrometheusSample> Parse(string json)
    {
        var samples = new List<PrometheusSample>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("status", out var status) || status.GetString() != "success")
            {
                return samples;
            }

            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Array)
            {
                return samples;
            }

            foreach (var item in result.EnumerateArray())
            {
                if (!item.TryGetProperty("value", out var value) ||
                    value.ValueKind != JsonValueKind.Array ||
                    value.GetArrayLength() < 2)
                {
                    continue;
                }

                // value = [ <timestamp double>, "<numeric string>" ]
                var raw = value[1].GetString();
                if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                {
                    continue;
                }

                var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (item.TryGetProperty("metric", out var metric) && metric.ValueKind == JsonValueKind.Object)
                {
                    foreach (var label in metric.EnumerateObject())
                    {
                        labels[label.Name] = label.Value.GetString() ?? string.Empty;
                    }
                }

                samples.Add(new PrometheusSample(num, labels));
            }
        }
        catch (JsonException)
        {
            // 返回体不是预期 JSON，视作无数据。
        }

        return samples;
    }
}

/// <summary>Prometheus 瞬时查询的一条结果：数值 + 标签集。</summary>
public record PrometheusSample(double Value, IReadOnlyDictionary<string, string> Labels)
{
    /// <summary>取标签值（不存在返回 null）。</summary>
    public string? Label(string name) => Labels.TryGetValue(name, out var v) ? v : null;
}
