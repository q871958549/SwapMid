using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SwapMid.ApiManagement;
using SwapMid.Controllers.Notification;
using SwapMid.HttpApi.Versioning;
using SwapMid.Notification;
using SwapMid.Permissions;
using SwapMid.Scheduling;
using SwapMid.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Http;
using Volo.Abp.Swashbuckle;
using Volo.Abp.Validation;

namespace SwapMid.HttpApi.Tests;

internal sealed class ContractTestHost : IDisposable
{
    public const string AuthHeaderName = "X-Contract-Auth";
    public const string ClientIdHeaderName = "X-Contract-Client-Id";
    public const string PermissionsHeaderName = "X-Contract-Permissions";
    public const string ScopesHeaderName = "X-Contract-Scopes";
    public const string OpenIddictValidationScheme = "OpenIddict.Validation.AspNetCore";

    private static readonly string[] AllPlatformPermissions = CollectAllPermissionConstants();
    private static readonly AbpExceptionHttpStatusCodeOptions StatusCodeMappings = BuildStatusCodeMappings();

    private readonly WebApplication _app;

    public ContractTestHost(
        bool enableRuntimeSwagger = false,
        bool? enableApiGateway = null,
        IJobSchedulingAppService? jobSchedulingService = null,
        bool enableUnitOfWorkPipeline = false,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        ContentRoot = Path.Combine(Path.GetTempPath(), "swapmid-http-contract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ContentRoot);

        var settings = new Dictionary<string, string?>
        {
            ["AuthServer:Authority"] = "https://contract.invalid",
        };
        if (enableApiGateway.HasValue)
        {
            settings["ApiGateway:Enabled"] = enableApiGateway.Value.ToString();
        }
        if (extraSettings != null)
        {
            foreach (var pair in extraSettings)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "ContractTest",
            ContentRootPath = ContentRoot,
            Args = [],
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        if (enableRuntimeSwagger || enableUnitOfWorkPipeline)
        {
            builder.AddApplicationAsync<SwapMidHttpApiModule>().GetAwaiter().GetResult();
        }

        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

        RegisterDefaultAppServiceSubstitutes(builder.Services);
        if (jobSchedulingService != null)
        {
            builder.Services.AddSingleton(jobSchedulingService);
        }

        ApiRouteRepository = Substitute.For<IApiRouteRepository>();
        HttpClientFactory = Substitute.For<IHttpClientFactory>();
        builder.Services.AddSingleton(ApiRouteRepository);
        builder.Services.AddSingleton(HttpClientFactory);
        builder.Services.AddTransient<Swagger.ApiDeprecationContractController>();

        configureServices?.Invoke(builder.Services);

        builder.Services
            .AddAuthentication(ContractTestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ContractTestAuthHandler>(
                ContractTestAuthHandler.SchemeName,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, ContractTestAuthHandler>(
                OpenIddictValidationScheme,
                _ => { });

        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter(AlertWebhookController.RateLimitPolicy, limiter =>
            {
                limiter.PermitLimit = 100;
                limiter.Window = TimeSpan.FromMinutes(1);
            });
            options.AddFixedWindowLimiter("ThirdPartyApi", limiter =>
            {
                limiter.PermitLimit = 100;
                limiter.Window = TimeSpan.FromMinutes(1);
            });
        });

        builder.Services.AddAuthorization(options =>
        {
            foreach (var permission in AllPlatformPermissions)
            {
                options.AddPolicy(permission, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("permission", permission);
                });
            }
        });
        global::SwapMid.ApiGateway.GatewayAuthorizationPolicies.AddGatewayServices(builder.Services);

        var mvc = builder.Services
            .AddControllers(options =>
            {
                options.ModelBinderProviders.Insert(0, new ChinaDateTimeModelBinderProvider());
                if (!(enableRuntimeSwagger || enableUnitOfWorkPipeline))
                {
                    options.Filters.Add<ApiDeprecationHeaderFilter>();
                }
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.Converters.Add(new ChinaDateTimeJsonConverter());
            });

        mvc.PartManager.ApplicationParts.Clear();
        mvc.PartManager.ApplicationParts.Add(
            new AssemblyPart(typeof(SwapMidHttpApiModule).Assembly));
        mvc.PartManager.ApplicationParts.Add(
            new AssemblyPart(typeof(SwapMidHttpApiHostModule).Assembly));
        mvc.PartManager.ApplicationParts.Add(
            new AssemblyPart(typeof(ContractTestHost).Assembly));

        if (enableRuntimeSwagger)
        {
            builder.Services.AddAbpSwaggerGenWithOidc(
                "https://contract.invalid",
                ["SwapMid"],
                [AbpSwaggerOidcFlows.AuthorizationCode],
                null,
                SwapMidSwaggerConfiguration.Configure);
        }

        var app = builder.Build();
        if (enableRuntimeSwagger || enableUnitOfWorkPipeline)
        {
            app.InitializeApplicationAsync().GetAwaiter().GetResult();
        }

        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (AbpValidationException ex)
            {
                await WriteRemoteServiceErrorAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    new RemoteServiceErrorInfo(ex.Message)
                    {
                        ValidationErrors = ex.ValidationErrors
                            .Select(e => new RemoteServiceValidationErrorInfo(
                                e.ErrorMessage ?? "Validation error",
                                e.MemberNames.ToArray()))
                            .ToArray(),
                    });
            }
            catch (AbpAuthorizationException ex)
            {
                var status = context.User.Identity?.IsAuthenticated == true
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status401Unauthorized;
                await WriteRemoteServiceErrorAsync(
                    context,
                    status,
                    new RemoteServiceErrorInfo(ex.Message));
            }
            catch (EntityNotFoundException ex)
            {
                await WriteRemoteServiceErrorAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    new RemoteServiceErrorInfo(ex.Message));
            }
            catch (BusinessException ex)
            {
                var status = ResolveBusinessStatus(ex);
                await WriteRemoteServiceErrorAsync(
                    context,
                    status,
                    new RemoteServiceErrorInfo(ex.Message, ex.Details, ex.Code));
            }
        });

        app.UseRouting();
        app.UseStatusCodePages(async context =>
        {
            if (context.HttpContext.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = new
                    {
                        code = (string?)null,
                        message = context.HttpContext.Response.StatusCode == StatusCodes.Status401Unauthorized
                            ? "Unauthorized"
                            : "Forbidden",
                    },
                });
            }
        });
        app.UseAuthentication();
        if (enableUnitOfWorkPipeline)
        {
            SwapMidHttpApiHostModule.UseEndpointAwareUnitOfWork(app);
        }
        app.UseRateLimiter();
        app.UseAuthorization();

        if (enableRuntimeSwagger)
        {
            app.UseSwagger();
        }

        app.MapControllers();
        app.StartAsync().GetAwaiter().GetResult();
        _app = app;
    }

    public string ContentRoot { get; }
    public IApiRouteRepository ApiRouteRepository { get; }
    public IHttpClientFactory HttpClientFactory { get; }
    public IServiceProvider Services => _app.Services;

    public HttpClient CreateClient(bool authenticated = true, params string[] permissions)
        => CreateClientWithScopes(authenticated, ["SwapMid"], permissions);

    public HttpClient CreateClientWithScopes(
        bool authenticated,
        string[] scopes,
        params string[] permissions)
    {
        var client = _app.GetTestClient();
        if (authenticated)
        {
            client.DefaultRequestHeaders.Add(AuthHeaderName, "contract-user");
            if (scopes.Length > 0)
            {
                client.DefaultRequestHeaders.Add(ScopesHeaderName, string.Join(" ", scopes));
            }
            if (permissions.Length > 0)
            {
                client.DefaultRequestHeaders.Add(PermissionsHeaderName, string.Join(",", permissions));
            }
        }

        return client;
    }

    public HttpClient CreateExternalClient(string clientId)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(AuthHeaderName, clientId);
        client.DefaultRequestHeaders.Add(ClientIdHeaderName, clientId);
        client.DefaultRequestHeaders.Add(ScopesHeaderName, "external-integration");
        return client;
    }

    public void Dispose()
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Directory.Exists(ContentRoot))
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
    }

    private static void RegisterDefaultAppServiceSubstitutes(IServiceCollection services)
    {
        var contractsAssembly = typeof(SwapMidApplicationContractsModule).Assembly;
        foreach (var iface in contractsAssembly.GetTypes().Where(t => t.IsInterface && t.IsPublic && !t.IsGenericTypeDefinition))
        {
            if (iface.Name.EndsWith("AppService", StringComparison.Ordinal))
            {
                services.AddSingleton(iface, _ => Substitute.For([iface], []));
            }
        }
    }

    private static string[] CollectAllPermissionConstants()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        void Walk(Type t)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string) && f.GetRawConstantValue() is string val)
                {
                    result.Add(val);
                }
            }
            foreach (var n in t.GetNestedTypes(BindingFlags.Public))
            {
                Walk(n);
            }
        }
        Walk(typeof(SwapMidPermissions));
        return result.ToArray();
    }

    private static AbpExceptionHttpStatusCodeOptions BuildStatusCodeMappings()
    {
        var options = new AbpExceptionHttpStatusCodeOptions();
        SwapMidHttpApiHostModule.ConfigureSchedulingStatusCodes(options);
        SwapMidHttpApiHostModule.ConfigureNotificationStatusCodes(options);
        SwapMidHttpApiHostModule.ConfigureDataIntegrationStatusCodes(options);
        return options;
    }

    private static int ResolveBusinessStatus(BusinessException exception)
    {
        if (!string.IsNullOrWhiteSpace(exception.Code) &&
            StatusCodeMappings.ErrorCodeToHttpStatusCodeMappings.TryGetValue(exception.Code, out var mapped))
        {
            return (int)mapped;
        }

        return exception is UserFriendlyException
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status400BadRequest;
    }

    private static async Task WriteRemoteServiceErrorAsync(
        HttpContext context,
        int statusCode,
        RemoteServiceErrorInfo error)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new RemoteServiceErrorResponse(error);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        await context.Response.WriteAsync(json);
    }
}

internal sealed class ContractTestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ContractTestHost.AuthHeaderName, out var user) ||
            string.IsNullOrWhiteSpace(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ToString()),
            new(ClaimTypes.Name, user.ToString()),
        };

        if (Request.Headers.TryGetValue(ContractTestHost.PermissionsHeaderName, out var header))
        {
            foreach (var permission in header.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim("permission", permission));
            }
        }

        if (Request.Headers.TryGetValue(ContractTestHost.ClientIdHeaderName, out var clientIdHeader) &&
            !string.IsNullOrWhiteSpace(clientIdHeader.ToString()))
        {
            claims.Add(new Claim("client_id", clientIdHeader.ToString()));
        }

        if (Request.Headers.TryGetValue(ContractTestHost.ScopesHeaderName, out var scopesHeader) &&
            !string.IsNullOrWhiteSpace(scopesHeader.ToString()))
        {
            claims.Add(new Claim("scope", scopesHeader.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
