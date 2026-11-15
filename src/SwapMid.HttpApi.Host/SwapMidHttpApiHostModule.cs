using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Hangfire.Storage;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Prometheus.DotNetRuntime;
using StackExchange.Redis;
using SwapMid.Controllers.Notification;
using SwapMid.DataIntegration;
using SwapMid.EntityFrameworkCore;
using SwapMid.Hangfire;
using SwapMid.HealthChecks;
using SwapMid.HttpApiHost.DataIntegration;
using SwapMid.HttpApiHost.Scheduling;
using SwapMid.Monitoring;
using SwapMid.MultiTenancy;
using SwapMid.OpenIddict;
using SwapMid.Scheduling;
using SwapMid.Swagger;
using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Libs;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundJobs.Hangfire;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Uow;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Hangfire;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.Security.Claims;
using Volo.Abp.Studio;
using Volo.Abp.Studio.Client.AspNetCore;
using Volo.Abp.Swashbuckle;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.VirtualFileSystem;

namespace SwapMid;

[DependsOn(
    typeof(SwapMidHttpApiModule),
    typeof(AbpStudioClientAspNetCoreModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(SwapMidApplicationModule),
    typeof(SwapMidEntityFrameworkCoreModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpCachingStackExchangeRedisModule),
    typeof(AbpDistributedLockingModule),
    typeof(AbpBackgroundJobsHangfireModule)
)]
public class SwapMidHttpApiHostModule : AbpModule
{
    private static IDisposable? _dotNetRuntimeStats;

    private const string CommittedWeakCertificatePassPhrase = "4f2c98fb-ba9c-4305-8efb-9cbb853fe921";
    private const string CommittedWeakStringEncryptionPassPhrase = "YSEICfiUagaCNbZW";

    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("SwapMid");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        PreConfigure<OpenIddictServerBuilder>(serverBuilder =>
        {
            serverBuilder.AllowCustomFlow(ImpersonationTokenExtensionGrant.ExtensionGrantName);
            serverBuilder.AllowCustomFlow(ExternalOidcTokenExtensionGrant.ExtensionGrantName);
        });

        if (!hostingEnvironment.IsDevelopment())
        {
            GuardAgainstWeakProductionSecrets(
                configuration["AuthServer:CertificatePassPhrase"],
                configuration["StringEncryption:DefaultPassPhrase"]);

            PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
            {
                options.AddDevelopmentEncryptionAndSigningCertificate = false;
            });

            PreConfigure<OpenIddictServerBuilder>(serverBuilder =>
            {
                serverBuilder.AddProductionEncryptionAndSigningCertificate(
                    "openiddict.pfx",
                    configuration["AuthServer:CertificatePassPhrase"]!);
                serverBuilder.SetIssuer(new Uri(configuration["AuthServer:Authority"]!));
            });
        }
    }

    private static void GuardAgainstWeakProductionSecrets(
        string? certPassPhrase,
        string? stringEncryptionPassPhrase)
    {
        var offending = new List<string>();
        if (certPassPhrase == CommittedWeakCertificatePassPhrase)
        {
            offending.Add("AuthServer:CertificatePassPhrase");
        }

        if (stringEncryptionPassPhrase == CommittedWeakStringEncryptionPassPhrase)
        {
            offending.Add("StringEncryption:DefaultPassPhrase");
        }

        if (offending.Count > 0)
        {
            throw new Exception(
                "[安全] 非开发环境检测到仍在使用 appsettings.json 中的开发占位密钥：" +
                string.Join("、", offending) +
                "。请通过环境变量或密钥管理系统覆盖后再启动。");
        }
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        context.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(
                configuration["Redis:Configuration"]
                ?? throw new InvalidOperationException("Redis:Configuration 未配置，无法启用分布式锁")));
        context.Services.AddSingleton<IDistributedLockProvider>(provider =>
            new RedisDistributedSynchronizationProvider(
                provider.GetRequiredService<IConnectionMultiplexer>().GetDatabase()));

        if (!configuration.GetValue<bool>("App:DisablePII"))
        {
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.LogCompleteSecurityArtifact = true;
        }

        if (!configuration.GetValue<bool>("AuthServer:RequireHttpsMetadata") ||
            configuration.GetValue<bool>("AuthServer:DisableTransportSecurityRequirement"))
        {
            Configure<OpenIddictServerAspNetCoreOptions>(options =>
            {
                options.DisableTransportSecurityRequirement = true;
            });
        }

        Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            if (!configuration.GetValue<bool>("AuthServer:RequireHttpsMetadata"))
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
            else
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
            }
        });

        ConfigureStudio(hostingEnvironment);
        ConfigureExceptionStatusCodes();
        ConfigureAuthentication(context);
        ConfigureUrls(configuration);
        ConfigureBundles(hostingEnvironment);
        ConfigureConventionalControllers();
        ConfigureHealthChecks(context);
        ConfigureSwagger(context, configuration);
        ConfigureVirtualFileSystem(context);
        ConfigureCors(context, configuration);
        ConfigureChineseDateTime();
        ConfigureLocalization();
        ConfigureHangfire(context, configuration);
        ConfigureApiGateway(context);
        ConfigureOpenTelemetryTracing(context, configuration);
        ConfigureEfCommandMetrics(context);
        ConfigureRateLimiting(context);
        ConfigureImpersonationGrant();
    }

    private void ConfigureExceptionStatusCodes()
    {
        Configure<AbpExceptionHttpStatusCodeOptions>(options =>
        {
            ConfigureSchedulingStatusCodes(options);
            ConfigureNotificationStatusCodes(options);
            ConfigureDataIntegrationStatusCodes(options);
        });
    }

    internal static void ConfigureSchedulingStatusCodes(
        AbpExceptionHttpStatusCodeOptions options)
    {
        options.Map(
            SwapMidDomainErrorCodes.Scheduling.JobArgumentsTooLong,
            HttpStatusCode.BadRequest);
    }

    internal static void ConfigureNotificationStatusCodes(
        AbpExceptionHttpStatusCodeOptions options)
    {
        options.Map(
            SwapMidDomainErrorCodes.NotificationCenter.InvalidWebhookToken,
            HttpStatusCode.Unauthorized);
    }

    internal static void ConfigureDataIntegrationStatusCodes(
        AbpExceptionHttpStatusCodeOptions options)
    {
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid,
            HttpStatusCode.BadRequest);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionBusy,
            HttpStatusCode.Conflict);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionCoordinationUnavailable,
            HttpStatusCode.ServiceUnavailable);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionOutcomeUnknown,
            HttpStatusCode.Conflict);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionOutcomeUnresolved,
            HttpStatusCode.Conflict);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionResolutionInvalid,
            HttpStatusCode.Conflict);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionTargetBusy,
            HttpStatusCode.Conflict);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionCapacityExhausted,
            HttpStatusCode.TooManyRequests);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineTargetResourceGroupRequired,
            HttpStatusCode.BadRequest);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.DataSourceTargetResourceGroupImmutable,
            HttpStatusCode.Conflict);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionRecoveryInvalid,
            HttpStatusCode.BadRequest);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionRecoveryConflict,
            HttpStatusCode.Conflict);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineTargetReceiptInvalid,
            HttpStatusCode.BadRequest);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineTargetReceiptUnavailable,
            HttpStatusCode.ServiceUnavailable);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionReceiptInconclusive,
            HttpStatusCode.Conflict);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionReceiptVerificationRequired,
            HttpStatusCode.Conflict);
        options.Map(
            SwapMidDomainErrorCodes.DataIntegration.PipelineScheduleCronInvalid,
            HttpStatusCode.BadRequest);
    }

    private void ConfigureImpersonationGrant()
    {
        Configure<AbpOpenIddictExtensionGrantsOptions>(options =>
        {
            options.Grants.Add(
                ImpersonationTokenExtensionGrant.ExtensionGrantName,
                new ImpersonationTokenExtensionGrant());
            options.Grants.Add(
                ExternalOidcTokenExtensionGrant.ExtensionGrantName,
                new ExternalOidcTokenExtensionGrant());
        });
    }

    internal void ConfigureRateLimiting(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var permitPerMinute = configuration.GetValue<int?>("ThirdPartyApi:RateLimitPerMinute") ?? 60;
        var alertWebhookPerMinute = configuration.GetValue<int?>("AlertWebhook:RateLimitPerMinute") ?? 600;

        context.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var endpoint = rejected.HttpContext.GetEndpoint();
                var configuredPolicy = endpoint?
                    .Metadata
                    .GetMetadata<EnableRateLimitingAttribute>()?
                    .PolicyName;

                if (string.Equals(configuredPolicy, AlertWebhookController.RateLimitPolicy, StringComparison.Ordinal))
                {
                    await RejectAlertWebhookAsync(rejected, cancellationToken);
                    return;
                }

                var policy = configuredPolicy switch
                {
                    "ThirdPartyApi" => "third_party",
                    _ => "global",
                };
                PlatformMetrics.OutRateLimitRejections.WithLabels(policy, "request").Inc();
            };

            options.AddPolicy("ThirdPartyApi", (HttpContext httpContext) =>
            {
                var clientId = httpContext.User?.FindFirst("client_id")?.Value;
                var partitionKey = !string.IsNullOrWhiteSpace(clientId)
                    ? $"client:{clientId}"
                    : $"ip:{httpContext.Connection.RemoteIpAddress}";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, permitPerMinute),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });

            options.AddPolicy(AlertWebhookController.RateLimitPolicy, (HttpContext httpContext) =>
            {
                var address = httpContext.Connection.RemoteIpAddress;
                if (address?.IsIPv4MappedToIPv6 == true)
                {
                    address = address.MapToIPv4();
                }

                var ip = address?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"alert-webhook:ip:{ip}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, alertWebhookPerMinute),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });
    }

    private static async Task RejectAlertWebhookAsync(
        OnRejectedContext rejected,
        CancellationToken cancellationToken)
    {
        PlatformMetrics.OutRateLimitRejections.WithLabels("alert_webhook", "request").Inc();
        var response = rejected.HttpContext.Response;
        response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        response.Headers.RetryAfter = AlertWebhookController.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        await response.WriteAsJsonAsync(
            new
            {
                error = new
                {
                    code = "SwapMid:NC:RateLimited",
                    message = "告警接收繁忙，请稍后重试",
                },
            },
            cancellationToken);
    }

    private void ConfigureOpenTelemetryTracing(ServiceConfigurationContext context, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("OTel:Tracing:Enabled"))
        {
            return;
        }

        context.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(
                    "SwapMid.Host",
                    serviceVersion: typeof(SwapMidHttpApiHostModule).Assembly.GetName().Version?.ToString())
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = configuration["OTel:Environment"] ?? "unknown",
                    ["service.instance.id"] = Environment.MachineName,
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new SwapMidCompositeSampler(
                        configuration.GetValue<double?>("OTel:Tracing:SampleRatio") ?? 0.10))
                    .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/metrics")
                        && !ctx.Request.Path.StartsWithSegments("/health")
                        && !ctx.Request.Path.StartsWithSegments("/health-status")
                        && !ctx.Request.Path.StartsWithSegments("/api/v1/notification/webhooks/alertmanager"))
                    .AddHttpClientInstrumentation(o =>
                    {
                        o.FilterHttpRequestMessage = SensitiveOutboundTelemetryFilter.ShouldTrace;
                    })
                    .AddSqlClientInstrumentation(o =>
                    {
                        o.RecordException = true;
                    })
                    .AddSource("MySqlConnector")
                    .AddSource("RabbitMQ.Client.Publisher")
                    .AddSource(SwapMidTracing.ActivitySourceName);

                if (configuration.GetValue<bool>("OTel:Tracing:EnableEfCore"))
                {
                    tracing.AddEntityFrameworkCoreInstrumentation();
                }

                if (string.Equals(configuration["OTel:Tracing:Exporter"], "otlp", StringComparison.OrdinalIgnoreCase))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(
                        configuration["OTel:Otlp:Endpoint"] ?? "http://localhost:4317"));
                }
                else
                {
                    tracing.AddConsoleExporter();
                }
            });
    }

    private static void ConfigureEfCommandMetrics(ServiceConfigurationContext context)
    {
        context.Services.Configure<AbpDbContextOptions>(options =>
        {
            options.PreConfigure(dbContext => dbContext.DbContextOptions.AddInterceptors(
                dbContext.ServiceProvider.GetRequiredService<PlatformEfCommandMetricsInterceptor>()));
        });
    }

    private static void ConfigureApiGateway(ServiceConfigurationContext context)
    {
        ApiGateway.GatewayAuthorizationPolicies.AddGatewayServices(context.Services);
        context.Services.AddHttpClient(OpenIddict.DefaultExternalOidcCodeExchanger.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        context.Services.AddTransient<OpenIddict.IExternalOidcCodeExchanger, OpenIddict.DefaultExternalOidcCodeExchanger>();
    }

    private void ConfigureHangfire(ServiceConfigurationContext context, IConfiguration configuration)
    {
        lock (JobFilterProviders.Providers)
        {
            if (!JobFilterProviders.Providers.Any(x => x is RunnerDrivenJobRetryFilterProvider))
            {
                JobFilterProviders.Providers.Add(new RunnerDrivenJobRetryFilterProvider());
            }
        }

        context.Services.AddHangfire(config =>
        {
            config.UsePostgreSqlStorage(
                c => c.UseNpgsqlConnection(configuration.GetConnectionString("Default")),
                new PostgreSqlStorageOptions
                {
                    QueuePollInterval = TimeSpan.FromSeconds(15),
                    PrepareSchemaIfNecessary = true,
                });
        });

        var role = configuration["Scheduling:Role"];
        var executesJobs = !string.Equals(role, "Api", StringComparison.OrdinalIgnoreCase);
        if (!executesJobs)
        {
            Configure<AbpBackgroundJobOptions>(options => options.IsJobExecutionEnabled = false);
        }
        else
        {
            context.Services.AddHostedService<TransactionalOutboxRelayHostedService>();
        }

        Configure<AbpHangfireOptions>(options =>
        {
            options.ServerOptions = new BackgroundJobServerOptions
            {
                WorkerCount = configuration.GetValue<int?>("Hangfire:WorkerCount") ?? 5,
                ShutdownTimeout = TimeSpan.FromSeconds(15),
            };
            if (!executesJobs)
            {
                options.BackgroundJobServerFactory = _ => null;
            }
        });
    }

    private void ConfigureStudio(IHostEnvironment hostingEnvironment)
    {
        if (hostingEnvironment.IsProduction())
        {
            Configure<AbpStudioClientOptions>(options =>
            {
                options.IsLinkEnabled = false;
            });
        }
    }

    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.ForwardIdentityAuthenticationForBearer(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        context.Services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin =
                MachineEndpointPathPolicy.HandleLoginRedirectAsync;
            options.Events.OnRedirectToAccessDenied =
                MachineEndpointPathPolicy.HandleAccessDeniedRedirectAsync;
        });
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });
    }

    private void ConfigureUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            options.RedirectAllowedUrls.AddRange(
                configuration["App:RedirectAllowedUrls"]?.Split(',') ?? Array.Empty<string>());
        });
    }

    private void ConfigureBundles(IHostEnvironment hostingEnvironment)
    {
        Configure<AbpMvcLibsOptions>(options =>
        {
            options.CheckLibs = false;
        });

        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                LeptonXLiteThemeBundles.Styles.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-styles.css");
                });

            options.ScriptBundles.Configure(
                LeptonXLiteThemeBundles.Scripts.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-scripts.js");
                    if (hostingEnvironment.IsDevelopment())
                    {
                        bundle.AddFiles("/dev-login-helper.js");
                    }
                });
        });
    }

    private void ConfigureVirtualFileSystem(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        if (hostingEnvironment.IsDevelopment())
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.ReplaceEmbeddedByPhysical<SwapMidDomainSharedModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}SwapMid.Domain.Shared"));
                options.FileSets.ReplaceEmbeddedByPhysical<SwapMidDomainModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}SwapMid.Domain"));
                options.FileSets.ReplaceEmbeddedByPhysical<SwapMidApplicationContractsModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}SwapMid.Application.Contracts"));
                options.FileSets.ReplaceEmbeddedByPhysical<SwapMidApplicationModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}SwapMid.Application"));
            });
        }
    }

    private void ConfigureConventionalControllers()
    {
    }

    private static void ConfigureSwagger(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddAbpSwaggerGenWithOidc(
            configuration["AuthServer:Authority"]!,
            ["SwapMid"],
            [AbpSwaggerOidcFlows.AuthorizationCode],
            null,
            SwapMidSwaggerConfiguration.Configure);
    }

    private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder
                    .WithOrigins(
                        configuration["App:CorsOrigins"]?
                            .Split(",", StringSplitOptions.RemoveEmptyEntries)
                            .Select(o => o.Trim().RemovePostFix("/"))
                            .ToArray() ?? Array.Empty<string>())
                    .WithAbpExposedHeaders()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    private void ConfigureHealthChecks(ServiceConfigurationContext context)
    {
        context.Services.AddSwapMidHealthChecks();
    }

    private void ConfigureLocalization()
    {
        Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture("zh-Hans");
        });
    }

    private void ConfigureChineseDateTime()
    {
        Configure<JsonOptions>(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new ChinaDateTimeJsonConverter());
        });

        Configure<MvcOptions>(options =>
        {
            options.ModelBinderProviders.Insert(0, new ChinaDateTimeModelBinderProvider());
        });
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        Volo.Abp.Application.Dtos.LimitedResultRequestDto.MaxMaxResultCount = 5000;

        context.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();

        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        app.UseForwardedHeaders();
        app.UseMetricServer();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAbpRequestLocalization();

        if (!env.IsDevelopment())
        {
            app.UseWhen(
                ctx => MachineEndpointPathPolicy.ShouldUseHtmlErrorPage(ctx.Request.Path),
                b => b.UseErrorPage());
        }

        app.UseRouting();
        app.UseHttpMetrics();
        app.MapAbpStaticAssets();
        app.UseAbpStudioLink();
        app.UseAbpSecurityHeaders();
        app.UseCors();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();

        if (MultiTenancyConsts.IsEnabled)
        {
            app.UseMultiTenancy();
        }

        UseEndpointAwareUnitOfWork(app);
        app.UseDynamicClaims();
        app.UseAuthorization();
        app.UseRateLimiter();

        ConfigureHangfireDashboardAndJobs(context, app, env);

        app.UseSwagger();
        app.UseAbpSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "SwapMid API v1");
            options.SwaggerEndpoint("/swagger/v2/swagger.json", "SwapMid API v2");

            var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
            options.OAuthClientId(configuration["AuthServer:SwaggerClientId"]);
        });
        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();

        _dotNetRuntimeStats ??= DotNetRuntimeStatsBuilder.Default().StartCollecting();
    }

    internal static void UseEndpointAwareUnitOfWork(IApplicationBuilder app)
    {
        app.UseWhen(
            httpContext => !IsEndpointUnitOfWorkDisabled(httpContext),
            branch => branch.UseUnitOfWork());
    }

    internal static bool IsEndpointUnitOfWorkDisabled(HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint();
        if (endpoint == null)
        {
            return false;
        }

        var metadataAttr = endpoint.Metadata.GetMetadata<UnitOfWorkAttribute>();
        if (metadataAttr != null)
        {
            return metadataAttr.IsDisabled;
        }

        var actionDescriptor = endpoint.Metadata
            .GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>();
        if (actionDescriptor != null)
        {
            return UnitOfWorkHelper
                .GetUnitOfWorkAttributeOrNull(actionDescriptor.MethodInfo)?
                .IsDisabled == true;
        }

        return false;
    }

    private void ConfigureHangfireDashboardAndJobs(
        ApplicationInitializationContext context,
        IApplicationBuilder app,
        IWebHostEnvironment env)
    {
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            DashboardTitle = "SwapMid · 任务调度",
            Authorization = new[] { new HangfireDashboardAuthorizationFilter(env.IsDevelopment()) },
        });

        RefreshRegisteredRecurringJobs(context);

        GlobalJobFilters.Filters.Add(new RunnerDrivenCancellationStateFilter());
        GlobalJobFilters.Filters.Add(new JobFailureNotificationFilter(
            context.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            context.ServiceProvider.GetRequiredService<ILogger<JobFailureNotificationFilter>>()));
        GlobalJobFilters.Filters.Add(new HangfireTracingFilter(
            context.ServiceProvider.GetRequiredService<ILogger<HangfireTracingFilter>>()));
        GlobalJobFilters.Filters.Add(new ScheduledJobExecutionFilter(
            context.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            context.ServiceProvider.GetRequiredService<ILogger<ScheduledJobExecutionFilter>>()));

        CleanupInterruptedExecutions(context);
    }

    private static void CleanupInterruptedExecutions(ApplicationInitializationContext context)
    {
        var logger = context.ServiceProvider
            .GetRequiredService<ILogger<SwapMidHttpApiHostModule>>();
        try
        {
            var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
            var thresholdHours = configuration.GetValue<double?>("ScheduledJobExecution:ZombieThresholdHours") ?? 1;

            using var scope = context.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var uowManager = scope.ServiceProvider.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>();
            var repository = scope.ServiceProvider.GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<ScheduledJobExecution, Guid>>();
            var recorder = scope.ServiceProvider.GetRequiredService<IScheduledJobExecutionRecorder>();
            var clock = scope.ServiceProvider.GetRequiredService<Volo.Abp.Timing.IClock>();

            using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
            var cutoff = clock.Now.AddHours(-thresholdHours);
            var queryable = repository.GetQueryableAsync().GetAwaiter().GetResult();
            var zombieIds = queryable
                .Where(x => x.Status == JobExecutionStatus.Running && x.StartedAt < cutoff)
                .Select(x => x.Id)
                .ToList();
            uow.CompleteAsync().GetAwaiter().GetResult();

            var interrupted = 0;
            foreach (var id in zombieIds)
            {
                if (recorder.InterruptAsync(id, "进程重启或超时未正常结束，已标记为中断").GetAwaiter().GetResult())
                {
                    interrupted++;
                }
            }

            if (zombieIds.Count > 0)
            {
                logger.LogWarning(
                    "启动清理僵尸执行历史：{Count} 条「执行中」超 {Hours}h，已标记为中断",
                    interrupted,
                    thresholdHours);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "启动清理僵尸执行历史失败，跳过");
        }
    }

    private static void RefreshRegisteredRecurringJobs(ApplicationInitializationContext context)
    {
        var logger = context.ServiceProvider
            .GetRequiredService<ILogger<SwapMidHttpApiHostModule>>();
        try
        {
            var manager = context.ServiceProvider.GetRequiredService<IRecurringJobManager>();
            using var connection = context.ServiceProvider.GetRequiredService<JobStorage>().GetConnection();
            var recurringJobs = connection.GetRecurringJobs();

            foreach (var rj in recurringJobs)
            {
                var def = ScheduledJobCatalog.Find(rj.Id);
                if (def == null)
                {
                    continue;
                }

                try
                {
                    var cron = string.IsNullOrWhiteSpace(rj.Cron) ? def.DefaultCron : rj.Cron;
                    def.Register(manager, cron);
                    logger.LogInformation("定时任务注册已刷新：{JobKey}（cron={Cron}）", rj.Id, cron);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "定时任务 {JobKey} 注册刷新失败，保留原注册", rj.Id);
                }
            }

            RefreshDataPipelineRecurringJobs(context, manager, recurringJobs, logger);
            RefreshCdcRecurringJobs(context, manager, recurringJobs, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "启动时刷新定时任务注册失败，跳过自愈");
        }
    }

    private static void RefreshDataPipelineRecurringJobs(
        ApplicationInitializationContext context,
        IRecurringJobManager manager,
        List<RecurringJobDto> recurringJobs,
        ILogger logger)
    {
        try
        {
            using var scope = context.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var pipelineRepo = scope.ServiceProvider.GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<DataPipeline, Guid>>();
            var synchronizer = scope.ServiceProvider.GetRequiredService<IDataPipelineScheduleSynchronizer>();

            List<DataPipeline> activeScheduled;
            using (var uow = uowManager.Begin(requiresNew: true, isTransactional: false))
            {
                var queryable = pipelineRepo.GetQueryableAsync().GetAwaiter().GetResult();
                activeScheduled = queryable
                    .Where(p => p.Status == PipelineStatus.Active && p.ScheduleEnabled && p.ScheduleCron != null && p.ScheduleCron != "")
                    .ToList();
                uow.CompleteAsync().GetAwaiter().GetResult();
            }

            var expectedJobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pipeline in activeScheduled)
            {
                try
                {
                    synchronizer.SyncAsync(
                        pipeline.Id,
                        pipeline.Status,
                        pipeline.ScheduleEnabled,
                        pipeline.ScheduleCron).GetAwaiter().GetResult();
                    expectedJobIds.Add(HangfireDataPipelineScheduleSynchronizer.BuildRecurringJobId(pipeline.Id));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "数据管道定时调度刷新失败 pipelineId={PipelineId}", pipeline.Id);
                }
            }

            foreach (var rj in recurringJobs)
            {
                if (rj.Id.StartsWith(HangfireDataPipelineScheduleSynchronizer.JobIdPrefix, StringComparison.OrdinalIgnoreCase) &&
                    !expectedJobIds.Contains(rj.Id))
                {
                    manager.RemoveIfExists(rj.Id);
                    logger.LogInformation("已清理孤立数据管道定时调度：{JobId}", rj.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "启动时同步数据管道定时调度失败，跳过");
        }
    }

    private static void RefreshCdcRecurringJobs(
        ApplicationInitializationContext context,
        IRecurringJobManager manager,
        List<RecurringJobDto> recurringJobs,
        ILogger logger)
    {
        try
        {
            using var scope = context.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var cdcRepo = scope.ServiceProvider.GetRequiredService<ICdcStreamRepository>();
            var synchronizer = scope.ServiceProvider.GetRequiredService<ICdcScheduleSynchronizer>();

            List<CdcStreamDefinition> activeStreams;
            using (var uow = uowManager.Begin(requiresNew: true, isTransactional: false))
            {
                activeStreams = cdcRepo.GetActiveAsync().GetAwaiter().GetResult();
                uow.CompleteAsync().GetAwaiter().GetResult();
            }

            var expectedJobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var stream in activeStreams)
            {
                try
                {
                    synchronizer.SyncAsync(stream.Id, stream.Status, stream.PollIntervalSeconds)
                        .GetAwaiter().GetResult();
                    expectedJobIds.Add(HangfireCdcScheduleSynchronizer.BuildRecurringJobId(stream.Id));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "CDC 流后台轮询刷新失败 streamId={StreamId}", stream.Id);
                }
            }

            foreach (var rj in recurringJobs)
            {
                if (rj.Id.StartsWith(HangfireCdcScheduleSynchronizer.JobIdPrefix, StringComparison.OrdinalIgnoreCase) &&
                    !expectedJobIds.Contains(rj.Id))
                {
                    manager.RemoveIfExists(rj.Id);
                    logger.LogInformation("已清理孤立 CDC 流定时轮询：{JobId}", rj.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "启动时同步 CDC 流定时轮询失败，跳过");
        }
    }
}
