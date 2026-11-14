using System.Reflection;
using SwapMid.HttpApi.Versioning;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SwapMid.Swagger;

/// <summary>让废弃标记进入 OpenAPI，供生成客户端与开发者门户识别。</summary>
public sealed class ApiDeprecationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var deprecation = context.MethodInfo.GetCustomAttribute<ApiDeprecationAttribute>(inherit: true)
            ?? context.MethodInfo.DeclaringType?.GetCustomAttribute<ApiDeprecationAttribute>(inherit: true);

        if (deprecation != null)
        {
            operation.Deprecated = true;
        }
    }
}
