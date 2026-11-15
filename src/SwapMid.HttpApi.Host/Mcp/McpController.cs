using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.ApiGateway;
using SwapMid.DataIntegration;
using SwapMid.MessageBus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Uow;

namespace SwapMid.Mcp;

/// <summary>
/// 受控只读 / 草稿型 Model Context Protocol（MCP Streamable HTTP / JSON-RPC 2.0）端点（<c>POST /api/mcp</c>）：
/// 1. 默认由 <c>Mcp:Enabled=false</c> 关闭（Fail-Closed）；
/// 2. 要求已登录身份并逐工具强制校验平台 RBAC 权限；
/// 3. 绝不暴露连接凭据，绝不直接激活或触发生产写入，写操作仅限生成 <c>Draft</c> 态管道。
/// </summary>
[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/mcp")]
public class McpController : AbpController
{
    public const string ProtocolVersion = "2025-03-26";
    public const long MaxMcpPayloadBytes = 128L * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly IConfiguration _configuration;
    private readonly IAuthorizationService _authorizationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpController> _logger;

    public McpController(
        IConfiguration configuration,
        IAuthorizationService authorizationService,
        IServiceProvider serviceProvider,
        ILogger<McpController> logger)
    {
        _configuration = configuration;
        _authorizationService = authorizationService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [UnitOfWork(IsDisabled = true)]
    [HttpPost]
    public async Task<IActionResult> HandleAsync(CancellationToken cancellationToken)
    {
        if (!bool.TryParse(_configuration["Mcp:Enabled"], out var enabled) || !enabled)
        {
            return await WriteRawJsonAsync(
                StatusCodes.Status404NotFound,
                new { error = new { code = "SwapMid:MCP:Disabled", message = "Not Found" } },
                cancellationToken);
        }

        if (Request.ContentLength is { } contentLength && contentLength > MaxMcpPayloadBytes)
        {
            return await WriteRpcErrorAsync(null, -32600, "MCP request payload exceeds 128 KiB budget.", StatusCodes.Status413PayloadTooLarge, cancellationToken);
        }

        var bodyBytes = await GatewayController.ReadBoundedStreamAsync(Request.Body, MaxMcpPayloadBytes, cancellationToken);
        if (bodyBytes is null)
        {
            return await WriteRpcErrorAsync(null, -32600, "MCP request payload exceeds 128 KiB budget.", StatusCodes.Status413PayloadTooLarge, cancellationToken);
        }

        if (bodyBytes.Length == 0)
        {
            return await WriteRpcErrorAsync(null, -32700, "Empty JSON-RPC payload.", StatusCodes.Status400BadRequest, cancellationToken);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bodyBytes);
        }
        catch (JsonException)
        {
            return await WriteRpcErrorAsync(null, -32700, "Parse error: invalid JSON.", StatusCodes.Status400BadRequest, cancellationToken);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return await WriteRpcErrorAsync(null, -32600, "Invalid Request: expected JSON-RPC 2.0 object.", StatusCodes.Status400BadRequest, cancellationToken);
            }

            var hasId = root.TryGetProperty("id", out var idElem);
            object? rpcId = hasId ? ExtractRpcId(idElem) : null;
            var jsonrpc = root.TryGetProperty("jsonrpc", out var vElem) && vElem.ValueKind == JsonValueKind.String
                ? vElem.GetString()
                : null;
            var method = root.TryGetProperty("method", out var mElem) && mElem.ValueKind == JsonValueKind.String
                ? mElem.GetString()
                : null;

            if (jsonrpc != "2.0" || string.IsNullOrWhiteSpace(method))
            {
                return await WriteRpcErrorAsync(rpcId, -32600, "Invalid Request: missing jsonrpc=2.0 or method.", StatusCodes.Status400BadRequest, cancellationToken);
            }

            // Notifications (no id) such as notifications/initialized return 202 Accepted
            if (!hasId)
            {
                Response.StatusCode = StatusCodes.Status202Accepted;
                await Response.Body.FlushAsync(cancellationToken);
                return new EmptyResult();
            }

            var @params = root.TryGetProperty("params", out var pElem) && pElem.ValueKind == JsonValueKind.Object
                ? pElem
                : default;

            return method switch
            {
                "initialize" => await WriteRpcResultAsync(
                    rpcId,
                    new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new
                        {
                            name = "swapmid-mcp",
                            version = typeof(McpController).Assembly.GetName().Version?.ToString(3) ?? "0.5.0",
                        },
                        instructions = "SwapMid controlled MCP server: read-only diagnostics and draft-only pipeline authoring enforced by platform RBAC.",
                    },
                    cancellationToken),
                "ping" => await WriteRpcResultAsync(rpcId, new { }, cancellationToken),
                "tools/list" => await HandleListToolsAsync(rpcId, cancellationToken),
                "tools/call" => await HandleCallToolAsync(rpcId, @params, cancellationToken),
                _ => await WriteRpcErrorAsync(rpcId, -32601, $"Method not found: {method}", StatusCodes.Status200OK, cancellationToken),
            };
        }
    }

    private async Task<IActionResult> HandleListToolsAsync(object? rpcId, CancellationToken cancellationToken)
    {
        var authorizedTools = new List<object>();
        foreach (var tool in McpToolCatalog.All)
        {
            var auth = await _authorizationService.AuthorizeAsync(User, null, tool.RequiredPermission);
            if (auth.Succeeded)
            {
                authorizedTools.Add(new
                {
                    name = tool.Name,
                    description = tool.Description,
                    inputSchema = tool.InputSchema,
                });
            }
        }

        return await WriteRpcResultAsync(rpcId, new { tools = authorizedTools }, cancellationToken);
    }

    private async Task<IActionResult> HandleCallToolAsync(
        object? rpcId,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        if (@params.ValueKind != JsonValueKind.Object ||
            !@params.TryGetProperty("name", out var nameElem) ||
            nameElem.ValueKind != JsonValueKind.String)
        {
            return await WriteRpcErrorAsync(rpcId, -32602, "Invalid params: missing tool name.", StatusCodes.Status200OK, cancellationToken);
        }

        var toolName = nameElem.GetString()!;
        var descriptor = McpToolCatalog.All.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));
        if (descriptor is null)
        {
            return await WriteRpcErrorAsync(rpcId, -32602, $"Unknown MCP tool: {toolName}", StatusCodes.Status200OK, cancellationToken);
        }

        var auth = await _authorizationService.AuthorizeAsync(User, null, descriptor.RequiredPermission);
        if (!auth.Succeeded)
        {
            return await WriteRpcErrorAsync(
                rpcId,
                -32003,
                $"Forbidden: caller lacks required permission '{descriptor.RequiredPermission}'.",
                StatusCodes.Status403Forbidden,
                cancellationToken);
        }

        var args = @params.TryGetProperty("arguments", out var argsElem) && argsElem.ValueKind == JsonValueKind.Object
            ? argsElem
            : default;

        try
        {
            object payload = toolName switch
            {
                McpToolCatalog.ListDatasources => await ExecuteListDatasourcesAsync(args),
                McpToolCatalog.PreflightPipelineSchema => await ExecutePreflightSchemaAsync(args, cancellationToken),
                McpToolCatalog.DiagnoseFailedExecutions => await ExecuteDiagnoseExecutionsAsync(args),
                McpToolCatalog.DiagnoseDeadLetters => await ExecuteDiagnoseDeadLettersAsync(args),
                McpToolCatalog.DraftDataPipeline => await ExecuteDraftPipelineAsync(args),
                _ => throw new InvalidOperationException("Unhandled tool"),
            };

            return await WriteRpcResultAsync(
                rpcId,
                new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = JsonSerializer.Serialize(payload, JsonOptions),
                        },
                    },
                    isError = false,
                },
                cancellationToken);
        }
        catch (BusinessException bex)
        {
            return await WriteRpcResultAsync(
                rpcId,
                new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = JsonSerializer.Serialize(
                                new { errorCode = bex.Code ?? "SwapMid:MCP:BusinessError" },
                                JsonOptions),
                        },
                    },
                    isError = true,
                },
                cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("MCP tool execution failed tool={ToolName} exceptionType={ExceptionType}", toolName, ex.GetType().Name);
            return await WriteRpcResultAsync(
                rpcId,
                new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = JsonSerializer.Serialize(
                                new { errorCode = "SwapMid:MCP:ToolExecutionFailed" },
                                JsonOptions),
                        },
                    },
                    isError = true,
                },
                cancellationToken);
        }
    }

    private async Task<object> ExecuteListDatasourcesAsync(JsonElement args)
    {
        var service = _serviceProvider.GetRequiredService<IDataSourceAppService>();
        var limit = ReadClampedInt(args, "limit", defaultValue: 20, min: 1, max: 100);
        var filter = ReadOptionalString(args, "filter");
        var page = await service.GetListAsync(new GetDataSourceListInput
        {
            Filter = filter,
            MaxResultCount = limit,
            SkipCount = 0,
        });
        return new
        {
            totalCount = page.TotalCount,
            dataSources = page.Items.Select(ds => new
            {
                ds.Id,
                ds.Name,
                ds.Type,
                ds.IsEnabled,
                ds.Description,
            }),
            connectorCapabilities = ConnectorCapabilityCatalog.GetAll(),
        };
    }

    private async Task<object> ExecutePreflightSchemaAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var service = _serviceProvider.GetRequiredService<IDataPipelineSchemaPreflightAppService>();
        var sourceId = ReadRequiredGuid(args, "sourceId");
        var targetId = ReadOptionalGuid(args, "targetId");
        var transformConfig = ReadOptionalString(args, "transformConfig") ?? string.Empty;
        return await service.PreflightSchemaAsync(
            new PreflightPipelineSchemaInput
            {
                SourceId = sourceId,
                TargetId = targetId,
                TransformConfig = transformConfig,
            },
            cancellationToken);
    }

    private async Task<object> ExecuteDiagnoseExecutionsAsync(JsonElement args)
    {
        var service = _serviceProvider.GetRequiredService<IPipelineExecutionAppService>();
        var pipelineId = ReadOptionalGuid(args, "pipelineId");
        var limit = ReadClampedInt(args, "limit", defaultValue: 10, min: 1, max: 50);

        var outcomeUnknown = await service.GetListAsync(new GetPipelineExecutionListInput
        {
            PipelineId = pipelineId,
            Status = ExecutionStatus.OutcomeUnknown,
            MaxResultCount = limit,
        });
        var failed = await service.GetListAsync(new GetPipelineExecutionListInput
        {
            PipelineId = pipelineId,
            Status = ExecutionStatus.Failed,
            MaxResultCount = limit,
        });

        return new
        {
            outcomeUnknownCount = outcomeUnknown.TotalCount,
            failedCount = failed.TotalCount,
            remediationPolicy = "OutcomeUnknown executions MUST be reconciled via target receipt verification or manual DBA evidence before any new run; Failed executions are safe to inspect and replay with a new or retained RunId.",
            outcomeUnknownItems = outcomeUnknown.Items,
            failedItems = failed.Items,
        };
    }

    private async Task<object> ExecuteDiagnoseDeadLettersAsync(JsonElement args)
    {
        var service = _serviceProvider.GetRequiredService<IMessageRecordAppService>();
        var topicId = ReadOptionalGuid(args, "topicId");
        var limit = ReadClampedInt(args, "limit", defaultValue: 10, min: 1, max: 30);

        var deadLetters = await service.GetListAsync(new GetMessageRecordListInput
        {
            TopicId = topicId,
            Status = MessageStatus.DeadLettered,
            MaxResultCount = limit,
        });
        var failed = await service.GetListAsync(new GetMessageRecordListInput
        {
            TopicId = topicId,
            Status = MessageStatus.Failed,
            MaxResultCount = limit,
        });

        var records = deadLetters.Items.Concat(failed.Items).Take(limit).ToList();
        var detailed = new List<object>(records.Count);
        foreach (var record in records)
        {
            var attempts = await service.GetAttemptsAsync(record.Id);
            detailed.Add(new
            {
                record.Id,
                record.TopicId,
                record.TopicName,
                record.Subject,
                record.Status,
                record.RetryCount,
                record.ErrorMessage,
                record.CreationTime,
                attempts,
            });
        }

        return new
        {
            deadLetterCount = deadLetters.TotalCount,
            failedCount = failed.TotalCount,
            records = detailed,
        };
    }

    private async Task<object> ExecuteDraftPipelineAsync(JsonElement args)
    {
        var name = ReadOptionalString(args, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        }

        var sourceId = ReadRequiredGuid(args, "sourceId");
        var targetId = ReadOptionalGuid(args, "targetId");
        var transformConfig = ReadOptionalString(args, "transformConfig") ?? string.Empty;
        _ = EtlTransformConfig.Parse(transformConfig);

        var service = _serviceProvider.GetRequiredService<IDataPipelineAppService>();
        var created = await service.CreateAsync(new DataPipelineCreateUpdateDto
        {
            Name = name,
            Description = ReadOptionalString(args, "description"),
            SourceId = sourceId,
            TargetId = targetId,
            TransformConfig = transformConfig,
            ScheduleCron = null,
            ScheduleEnabled = false,
        });

        return new
        {
            created.Id,
            created.Name,
            created.Status,
            scheduleEnabled = created.ScheduleEnabled,
            note = "Created strictly in Draft state with scheduling disabled. Operator review and explicit activation are required before execution.",
        };
    }

    private static object? ExtractRpcId(JsonElement elem) => elem.ValueKind switch
    {
        JsonValueKind.String => elem.GetString(),
        JsonValueKind.Number when elem.TryGetInt64(out var l) => l,
        JsonValueKind.Number => elem.GetDouble(),
        _ => null,
    };

    private static string? ReadOptionalString(JsonElement obj, string prop)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()
            : null;

    private static Guid ReadRequiredGuid(JsonElement obj, string prop)
        => Guid.TryParse(ReadOptionalString(obj, prop), out var g)
            ? g
            : throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);

    private static Guid? ReadOptionalGuid(JsonElement obj, string prop)
    {
        var raw = ReadOptionalString(obj, prop);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Guid.TryParse(raw, out var g)
            ? g
            : throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
    }

    private static int ReadClampedInt(JsonElement obj, string prop, int defaultValue, int min, int max)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var e) && e.TryGetInt32(out var v))
        {
            return Math.Clamp(v, min, max);
        }
        return defaultValue;
    }

    private Task<IActionResult> WriteRpcResultAsync(object? id, object result, CancellationToken cancellationToken)
        => WriteRawJsonAsync(
            StatusCodes.Status200OK,
            new { jsonrpc = "2.0", id, result },
            cancellationToken);

    private Task<IActionResult> WriteRpcErrorAsync(
        object? id,
        int code,
        string message,
        int httpStatus,
        CancellationToken cancellationToken)
        => WriteRawJsonAsync(
            httpStatus,
            new { jsonrpc = "2.0", id, error = new { code, message } },
            cancellationToken);

    private async Task<IActionResult> WriteRawJsonAsync(
        int statusCode,
        object payload,
        CancellationToken cancellationToken)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "application/json; charset=utf-8";
        await Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
        return new EmptyResult();
    }
}
