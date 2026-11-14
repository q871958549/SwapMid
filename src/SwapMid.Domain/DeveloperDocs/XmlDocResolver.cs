using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DeveloperDocs;

/// <summary>
/// 解析平台程序集编译产出的 XML 文档（<c>GenerateDocumentationFile</c>），按成员名提供类型 /
/// 属性的 <c>&lt;summary&gt;</c> 中文文本。供数据库表字典把实体 <c>///</c> 注释作为表 / 字段的中文含义。
/// 扫描运行目录下 <c>SwapMid.*.xml</c>，结果进程内缓存（XML 随 dll 固定）。
/// </summary>
public class XmlDocResolver : ISingletonDependency
{
    private Dictionary<string, string>? _summaries;
    private readonly object _lock = new();
    private readonly ILogger<XmlDocResolver> _logger;

    public XmlDocResolver(ILogger<XmlDocResolver> logger)
    {
        _logger = logger;
    }

    /// <summary>类型的 summary（XML member <c>T:FullName</c>），无则返回 null。</summary>
    public string? GetTypeSummary(Type type)
        => type.FullName != null ? Lookup("T:" + type.FullName) : null;

    /// <summary>属性的 summary（XML member <c>P:FullName.Prop</c>），无则返回 null。</summary>
    public string? GetPropertySummary(Type declaringType, string propertyName)
        => declaringType.FullName != null ? Lookup("P:" + declaringType.FullName + "." + propertyName) : null;

    private string? Lookup(string memberName)
    {
        EnsureLoaded();
        return _summaries!.TryGetValue(memberName, out var s) ? s : null;
    }

    private void EnsureLoaded()
    {
        if (_summaries != null) return;
        lock (_lock)
        {
            if (_summaries != null) return;
            var dict = new Dictionary<string, string>();
            string[] files;
            try
            {
                files = Directory.GetFiles(AppContext.BaseDirectory, "SwapMid.*.xml");
            }
            catch (Exception ex)
            {
                // 扫目录失败与「成员无注释」不同：前者是部署/产物问题，必须可观测。
                _logger.LogWarning(ex,
                    "无法扫描 XML 文档目录 {BaseDirectory}（模式 SwapMid.*.xml）。表字典中文含义将全部缺失。",
                    AppContext.BaseDirectory);
                files = Array.Empty<string>();
            }

            if (files.Length == 0)
            {
                _logger.LogWarning(
                    "运行目录 {BaseDirectory} 下未找到任何 SwapMid.*.xml。请确认相关项目已开启 GenerateDocumentationFile 且发布物包含 XML。表字典中文含义将全部缺失。",
                    AppContext.BaseDirectory);
            }

            foreach (var file in files)
            {
                try
                {
                    var doc = XDocument.Load(file);
                    foreach (var member in doc.Descendants("member"))
                    {
                        var name = member.Attribute("name")?.Value;
                        var summary = member.Element("summary")?.Value;
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(summary))
                        {
                            dict[name] = Clean(summary);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 单个 XML 损坏不影响其余；记 WARN 以便与「该成员本来就没写 summary」区分。
                    _logger.LogWarning(ex, "解析 XML 文档失败，已跳过：{XmlPath}", file);
                }
            }
            _summaries = dict;
        }
    }

    /// <summary>把多行 summary 压成单行（去缩进/换行）。</summary>
    private static string Clean(string raw)
        => string.Join(" ", raw.Split('\n', '\r').Select(x => x.Trim()).Where(x => x.Length > 0));
}
