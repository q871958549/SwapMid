using System;
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using SwapMid.SharedKernel;
using SwapMid.SharedKernel.Employees;

namespace SwapMid.Swagger;

/// <summary>
/// 生产 Host 与无外部依赖的契约测试宿主共用的 Swagger 配置。
/// </summary>
public static class SwapMidSwaggerConfiguration
{
    private const string LegacyBatchIdsSchemaId =
        "SwapMid.EnterpriseManagement.BatchIdsInput";
    private const string LegacyBatchOperationResultSchemaId =
        "SwapMid.EnterpriseManagement.BatchOperationResultDto";
    private const string LegacyEmployeeStatusSchemaId =
        "SwapMid.EnterpriseManagement.EmployeeStatus";

    public static void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc("all", new OpenApiInfo
        {
            Title = "SwapMid API（全部受支持版本）",
            Version = "supported",
            Description = "契约生成聚合文档；包含当前仍受支持的全部路径主版本。",
        });
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "SwapMid API v1",
            Version = "v1",
            Description = "SwapMid 企业级集成与数据交换中台 RESTful API 文档。覆盖数据集成（ETL）、消息总线、API 管理与网关、定时任务调度、通知中心及基础管理模块。",
        });
        options.SwaggerDoc("v2", new OpenApiInfo
        {
            Title = "SwapMid API v2",
            Version = "v2",
            Description = "SwapMid 路径主版本 v2。",
        });
        options.DocInclusionPredicate((documentName, apiDescription) =>
        {
            if (documentName.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var pathVersion = GetPathMajorVersion(apiDescription);
            return documentName.Equals("v1", StringComparison.OrdinalIgnoreCase)
                ? pathVersion is null or "v1"
                : documentName.Equals(pathVersion, StringComparison.OrdinalIgnoreCase);
        });
        options.CustomSchemaIds(GetStableSchemaId);
        options.SchemaFilter<EnumSchemaFilter>();
        options.OperationFilter<ApiDeprecationOperationFilter>();

        foreach (var xmlName in new[]
        {
            $"{Assembly.GetAssembly(typeof(SwapMidHttpApiModule))!.GetName().Name}.xml",
            "SwapMid.Application.Contracts.xml",
        })
        {
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlName);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
        }
    }

    private static string? GetPathMajorVersion(ApiDescription apiDescription)
    {
        var path = apiDescription.RelativePath?.TrimStart('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var segments = path.Split('/', 3, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 ||
            !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) ||
            segments[1].Length < 2 ||
            char.ToLowerInvariant(segments[1][0]) != 'v' ||
            !int.TryParse(segments[1].AsSpan(1), out var majorVersion) ||
            majorVersion < 1)
        {
            return null;
        }

        return $"v{majorVersion}";
    }

    internal static string GetStableSchemaId(Type type)
    {
        if (type == typeof(BatchIdsInput))
        {
            return LegacyBatchIdsSchemaId;
        }

        if (type == typeof(BatchOperationResultDto))
        {
            return LegacyBatchOperationResultSchemaId;
        }

        if (type == typeof(EmployeeStatus))
        {
            return LegacyEmployeeStatusSchemaId;
        }

        return type.FullName ?? type.Name;
    }
}
