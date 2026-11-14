using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Auditing;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>证明 CRUD/连接测试参数和实体 Config 均不会进入 ABP 审计 JSON。</summary>
public class DataSourceAuditBoundaryTests
{
    [Fact]
    public void 三个Config属性_均声明DisableAuditing()
    {
        AssertConfigIsNotAudited(typeof(DataSourceCreateUpdateDto));
        AssertConfigIsNotAudited(typeof(DataSourceTestConnectionInput));
        AssertConfigIsNotAudited(typeof(DataSource));
    }

    [Fact]
    public void JsonAuditSerializer_真实序列化时排除DTO和实体Config()
    {
        const string secret = "audit-secret-must-not-appear";
        var serializer = new JsonAuditSerializer(Options.Create(new AbpAuditingOptions()));
        var createInput = new DataSourceCreateUpdateDto
        {
            Name = "source",
            Type = DataSourceType.RestApi,
            Config = $$"""{"token":"{{secret}}"}"""
        };
        var testInput = new DataSourceTestConnectionInput
        {
            Type = DataSourceType.RelationalDatabase,
            Config = $$"""{"password":"{{secret}}"}"""
        };
        var entity = new DataSource(
            Guid.NewGuid(),
            "source",
            DataSourceType.RelationalDatabase,
            $$"""{"connectionString":"{{secret}}"}""");

        var actionParameterBags = new object[]
        {
            new Dictionary<string, object> { ["input"] = createInput },
            new Dictionary<string, object> { ["input"] = testInput },
            entity,
        };
        foreach (var value in actionParameterBags)
        {
            var json = serializer.Serialize(value);
            json.ShouldNotContain(secret);
            json.ShouldNotContain("\"config\"", Case.Insensitive);
        }
    }

    private static void AssertConfigIsNotAudited(Type type)
    {
        type.GetProperty(nameof(DataSource.Config), BindingFlags.Instance | BindingFlags.Public)!
            .GetCustomAttribute<DisableAuditingAttribute>()
            .ShouldNotBeNull($"{type.Name}.Config 必须阻止 action/entity 审计记录原始秘密");
    }
}
