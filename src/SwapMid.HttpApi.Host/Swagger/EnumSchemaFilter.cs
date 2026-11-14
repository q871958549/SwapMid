using System;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SwapMid.Swagger;

/// <summary>
/// 枚举文档化：把每个枚举的「值=成员名（[Description] 中文）」写进该 schema 的 description，
/// 让 Swagger UI 与前端「开发者文档 · API 参考」页都能显示枚举含义（否则只剩裸数字 0/1）。
/// <para>
/// 枚举仍按各业务域内聚定义（TopicType 属消息域、EnterpriseStatus 属企业域），本 filter 只是
/// 全局统一的「文档化机制」——新增枚举无需改这里，自动生效；要中文则给成员加 [Description("…")]。
/// </para>
/// </summary>
public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        var type = context.Type;
        // Swashbuckle 10 / Microsoft.OpenApi v2：形参是 IOpenApiSchema 接口（只读），
        // 但枚举本体生成的是具体可变 OpenApiSchema，cast 后才能写回 Description
        if (type is not { IsEnum: true } || schema is not OpenApiSchema concrete)
        {
            return;
        }

        var sb = new StringBuilder();
        foreach (var name in Enum.GetNames(type))
        {
            var value = Convert.ToInt64(Enum.Parse(type, name));
            var desc = type.GetField(name)?.GetCustomAttribute<DescriptionAttribute>()?.Description;
            sb.Append(value).Append('=').Append(name);
            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.Append('（').Append(desc).Append('）');
            }
            sb.Append('；');
        }

        var enumDoc = sb.ToString().TrimEnd('；');
        concrete.Description = string.IsNullOrWhiteSpace(concrete.Description)
            ? enumDoc
            : $"{concrete.Description}\n{enumDoc}";
    }
}
