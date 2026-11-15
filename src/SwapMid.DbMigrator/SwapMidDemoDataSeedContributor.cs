using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using SwapMid.ApiManagement;
using SwapMid.DataIntegration;
using SwapMid.MessageBus;
using SwapMid.SystemManagement;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace SwapMid.DbMigrator;

/// <summary>
/// 开箱即用演示数据种子（仅在 DbMigrator 中注册，保持自动化测试基线纯净）：
/// 1. 10 个保留业务对象默认编码规则（CodeRule）；
/// 2. 示例 API 分组与网关路由（ApiGroup / ApiRoute）；
/// 3. 零外部依赖的本地 RabbitMQ 消息总线演示订阅（demo-pipeline-events-rmq）；
/// 4. 零外部依赖的本地 PostgreSQL ETL 演示源表、目标表、数据源与已激活数据管道（demo-orders-pg-to-pg）。
/// </summary>
public sealed class SwapMidDemoDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICodeRuleRepository _codeRuleRepository;
    private readonly IApiGroupRepository _apiGroupRepository;
    private readonly IApiRouteRepository _apiRouteRepository;
    private readonly IMessageTopicRepository _messageTopicRepository;
    private readonly IRepository<MessageSubscription, Guid> _messageSubscriptionRepository;
    private readonly IDataSourceRepository _dataSourceRepository;
    private readonly IDataPipelineRepository _dataPipelineRepository;
    private readonly ILogger<SwapMidDemoDataSeedContributor> _logger;

    public SwapMidDemoDataSeedContributor(
        IConfiguration configuration,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        ICodeRuleRepository codeRuleRepository,
        IApiGroupRepository apiGroupRepository,
        IApiRouteRepository apiRouteRepository,
        IMessageTopicRepository messageTopicRepository,
        IRepository<MessageSubscription, Guid> messageSubscriptionRepository,
        IDataSourceRepository dataSourceRepository,
        IDataPipelineRepository dataPipelineRepository,
        ILogger<SwapMidDemoDataSeedContributor> logger)
    {
        _configuration = configuration;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
        _codeRuleRepository = codeRuleRepository;
        _apiGroupRepository = apiGroupRepository;
        _apiRouteRepository = apiRouteRepository;
        _messageTopicRepository = messageTopicRepository;
        _messageSubscriptionRepository = messageSubscriptionRepository;
        _dataSourceRepository = dataSourceRepository;
        _dataPipelineRepository = dataPipelineRepository;
        _logger = logger;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId.HasValue)
        {
            return;
        }

        if (!_configuration.GetValue("DemoSeed:Enabled", defaultValue: true))
        {
            return;
        }

        using (_currentTenant.Change(null))
        {
            await SeedCodeRulesAsync();
            await SeedApiGatewayDemoAsync();
            await SeedMessageBusDemoAsync();
            await SeedLocalPostgreSqlEtlDemoAsync();
        }
    }

    private async Task SeedCodeRulesAsync()
    {
        var rules = new (string EntityType, string DisplayName, string Prefix, bool IncludeDate, SequenceResetCycle Cycle, string Description)[]
        {
            ("Enterprise", "企业", "ENT-", false, SequenceResetCycle.Never, "企业主数据编码规则"),
            ("Department", "部门", "DEPT-", false, SequenceResetCycle.Never, "组织部门编码规则"),
            ("Position", "岗位", "POS-", false, SequenceResetCycle.Never, "组织岗位编码规则"),
            ("Employee", "员工", "EMP-", false, SequenceResetCycle.Never, "员工主数据工号规则"),
            ("ApiGroup", "API分组", "AGRP-", false, SequenceResetCycle.Never, "API 网关分组编码规则"),
            ("ApiRoute", "API路由", "AROT-", true, SequenceResetCycle.Monthly, "API 网关路由编码规则"),
            ("DataSource", "数据源", "DS-", false, SequenceResetCycle.Never, "集成数据源编码规则"),
            ("DataPipeline", "数据管道", "PIP-", true, SequenceResetCycle.Monthly, "ETL 数据管道编码规则"),
            ("MessageTopic", "消息主题", "TOP-", false, SequenceResetCycle.Never, "消息总线主题编码规则"),
            ("MessageSubscription", "消息订阅", "SUB-", true, SequenceResetCycle.Monthly, "消息总线订阅编码规则"),
        };

        foreach (var item in rules)
        {
            if (await _codeRuleRepository.AnyAsync(x => x.EntityType == item.EntityType))
            {
                continue;
            }

            var rule = new CodeRule(
                _guidGenerator.Create(),
                item.EntityType,
                item.DisplayName,
                prefix: item.Prefix,
                suffix: null,
                includeDate: item.IncludeDate,
                dateFormat: item.IncludeDate ? "yyyyMM" : null,
                sequencePad: 4,
                resetCycle: item.Cycle,
                description: item.Description);
            await _codeRuleRepository.InsertAsync(rule, autoSave: true);
        }
    }

    private async Task SeedApiGatewayDemoAsync()
    {
        const string demoGroupName = "Platform Diagnostics";
        const string demoRoutePath = "/gw/demo/health";

        Guid groupId;
        var existingGroup = await _apiGroupRepository.FirstOrDefaultAsync(x => x.Name == demoGroupName);
        if (existingGroup != null)
        {
            groupId = existingGroup.Id;
        }
        else
        {
            var createdGroup = new ApiGroup(
                _guidGenerator.Create(),
                demoGroupName,
                "SwapMid 平台自检与网关转发演示分组",
                order: 10);
            await _apiGroupRepository.InsertAsync(createdGroup, autoSave: true);
            groupId = createdGroup.Id;
        }

        if (!await _apiRouteRepository.PathAndMethodExistsAsync(demoRoutePath, HttpMethodType.GET))
        {
            var selfUrl = (
                _configuration["ApiGateway:DemoUpstreamBaseUrl"]
                ?? _configuration["App:SelfUrl"]
                ?? "https://localhost:44362").TrimEnd('/');
            var route = new ApiRoute(
                _guidGenerator.Create(),
                "Platform Liveness Probe",
                demoRoutePath,
                HttpMethodType.GET,
                $"{selfUrl}/health/live",
                groupId,
                "网关转发示例：将 /gw/demo/health 转发至平台自身存活探针端点",
                order: 10);
            await _apiRouteRepository.InsertAsync(route, autoSave: true);
        }
    }

    private async Task SeedMessageBusDemoAsync()
    {
        var topic = await _messageTopicRepository.FirstOrDefaultAsync(
            x => x.Name == MessageBusTopics.IntegrationPipelineEvents);
        if (topic == null)
        {
            topic = new MessageTopic(
                _guidGenerator.Create(),
                MessageBusTopics.IntegrationPipelineEvents,
                TopicType.Broadcast,
                description: "数据管道生命周期事件：ETL 管道触发、完成、失败与对账恢复事件流，可订阅转发至下游系统或机器人 Webhook。");
            await _messageTopicRepository.InsertAsync(topic, autoSave: true);
        }

        const string demoSubName = "demo-pipeline-events-rmq";
        if (!await _messageSubscriptionRepository.AnyAsync(
                x => x.TopicId == topic.Id && x.Name == demoSubName))
        {
            var subscription = new MessageSubscription(
                _guidGenerator.Create(),
                topic.Id,
                demoSubName,
                DeliveryType.RabbitMq,
                endpointUrl: null,
                exchangeName: "swapmid.demo.events",
                routingKey: "pipeline.events");
            await _messageSubscriptionRepository.InsertAsync(subscription, autoSave: true);
        }

        var cdcTopic = await _messageTopicRepository.FirstOrDefaultAsync(
            x => x.Name == MessageBusTopics.IntegrationCdcEvents);
        if (cdcTopic == null)
        {
            cdcTopic = new MessageTopic(
                _guidGenerator.Create(),
                MessageBusTopics.IntegrationCdcEvents,
                TopicType.Broadcast,
                description: "PostgreSQL 逻辑复制（pgoutput）CDC 变更事件流：实时捕获表级 INSERT/UPDATE/DELETE 变更与 LSN 检查点，推送至 CloudEvents 下游订阅。");
            await _messageTopicRepository.InsertAsync(cdcTopic, autoSave: true);
        }

        const string demoCdcSubName = "demo-cdc-events-rmq";
        if (!await _messageSubscriptionRepository.AnyAsync(
                x => x.TopicId == cdcTopic.Id && x.Name == demoCdcSubName))
        {
            var cdcSub = new MessageSubscription(
                _guidGenerator.Create(),
                cdcTopic.Id,
                demoCdcSubName,
                DeliveryType.RabbitMq,
                endpointUrl: null,
                exchangeName: "swapmid.demo.cdc",
                routingKey: "cdc.events");
            await _messageSubscriptionRepository.InsertAsync(cdcSub, autoSave: true);
        }
    }

    private async Task SeedLocalPostgreSqlEtlDemoAsync()
    {
        var defaultConnStr = _configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(defaultConnStr))
        {
            return;
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(defaultConnStr);
            if (string.IsNullOrWhiteSpace(builder.Host) ||
                string.IsNullOrWhiteSpace(builder.Database))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Default 连接串非标准 PostgreSQL 格式，跳过本地 ETL 演示表初始化（{ExceptionType}）", ex.GetType().Name);
            return;
        }

        const string ddlAndSeedSql = """
            CREATE TABLE IF NOT EXISTS public.etl_demo_source_orders (
                order_id bigint PRIMARY KEY,
                order_no varchar(32) NOT NULL,
                customer_code varchar(32) NOT NULL,
                currency varchar(8) NOT NULL,
                total_amount numeric(18, 2) NOT NULL,
                order_status varchar(24) NOT NULL,
                ordered_at_utc timestamptz NOT NULL
            );

            CREATE TABLE IF NOT EXISTS public.etl_demo_target_orders (
                order_id bigint PRIMARY KEY,
                order_no varchar(32) NOT NULL,
                customer_code varchar(32) NOT NULL,
                currency varchar(8) NOT NULL,
                total_amount numeric(18, 2) NOT NULL,
                order_status varchar(24) NOT NULL,
                ordered_at_utc timestamptz NOT NULL
            );

            INSERT INTO public.etl_demo_source_orders
                (order_id, order_no, customer_code, currency, total_amount, order_status, ordered_at_utc)
            VALUES
                (1001, 'SO-202601-0001', 'CUST-ACME',   'USD',  1280.50, 'CONFIRMED', '2026-01-15T08:30:00Z'),
                (1002, 'SO-202601-0002', 'CUST-GLOBEX', 'EUR',  4590.00, 'SHIPPED',   '2026-01-15T09:15:00Z'),
                (1003, 'SO-202601-0003', 'CUST-INITECH','CNY', 18600.00, 'COMPLETED', '2026-01-16T02:10:00Z'),
                (1004, 'SO-202601-0004', 'CUST-UMBRELLA','USD',  920.75, 'CONFIRMED', '2026-01-16T11:45:00Z'),
                (1005, 'SO-202601-0005', 'CUST-STARK',  'USD', 32500.00, 'SHIPPED',   '2026-01-17T14:20:00Z'),
                (1006, 'SO-202601-0006', 'CUST-WAYNE',  'GBP',  7410.20, 'COMPLETED', '2026-01-18T16:05:00Z'),
                (1007, 'SO-202601-0007', 'CUST-CYBER',  'JPY', 98000.00, 'CONFIRMED', '2026-01-19T04:50:00Z'),
                (1008, 'SO-202601-0008', 'CUST-SOYLENT','CNY',  6350.80, 'SHIPPED',   '2026-01-20T07:00:00Z'),
                (1009, 'SO-202601-0009', 'CUST-MASSIVE','EUR', 11200.00, 'COMPLETED', '2026-01-21T10:35:00Z'),
                (1010, 'SO-202601-0010', 'CUST-HOOLI',  'USD',  2999.99, 'CONFIRMED', '2026-01-22T13:12:00Z'),
                (1011, 'SO-202601-0011', 'CUST-PIED',   'USD',   450.00, 'COMPLETED', '2026-01-23T15:40:00Z'),
                (1012, 'SO-202601-0012', 'CUST-APEX',   'CNY', 24800.00, 'SHIPPED',   '2026-01-24T18:25:00Z')
            ON CONFLICT (order_id) DO NOTHING;
            """;

        await using (var conn = new NpgsqlConnection(defaultConnStr))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ddlAndSeedSql;
            await cmd.ExecuteNonQueryAsync();
        }

        const string demoSourceName = "Local Demo PostgreSQL";
        DataSource demoSource;
        var existingSource = await _dataSourceRepository.FirstOrDefaultAsync(x => x.Name == demoSourceName);
        if (existingSource != null)
        {
            demoSource = existingSource;
        }
        else
        {
            var configJson = new JsonObject
            {
                ["provider"] = "postgresql",
                ["host"] = builder.Host.Trim().ToLowerInvariant(),
                ["port"] = builder.Port > 0 ? builder.Port : 5432,
                ["database"] = builder.Database.Trim(),
                ["username"] = (builder.Username ?? string.Empty).Trim(),
                ["encrypt"] = false,
                ["trustServerCertificate"] = true,
                ["targetResourceGroup"] = "local-pg-demo",
            };
            if (!string.IsNullOrEmpty(builder.Password))
            {
                configJson["password"] = builder.Password;
            }

            demoSource = new DataSource(
                _guidGenerator.Create(),
                demoSourceName,
                DataSourceType.RelationalDatabase,
                configJson.ToJsonString(),
                "开箱即用本地 PostgreSQL 演示数据源（指向平台同一实例演示表）");
            await _dataSourceRepository.InsertAsync(demoSource, autoSave: true);
        }

        const string demoPipelineName = "demo-orders-pg-to-pg";
        if (!await _dataPipelineRepository.NameExistsAsync(demoPipelineName))
        {
            var transformJson = new JsonObject
            {
                ["sourceQuery"] = "SELECT order_id, order_no, customer_code, currency, total_amount, order_status, ordered_at_utc FROM public.etl_demo_source_orders ORDER BY order_id",
                ["targetTable"] = "public.etl_demo_target_orders",
                ["mode"] = "truncate",
                ["reconciliationStrategy"] = "manual-v0",
            };
            var pipeline = new DataPipeline(
                _guidGenerator.Create(),
                demoPipelineName,
                demoSource.Id,
                demoSource.Id,
                "零外部依赖本地 PostgreSQL 订单同步示例管道（etl_demo_source_orders → etl_demo_target_orders，全量替换模式）",
                transformJson.ToJsonString(),
                scheduleCron: "0 */6 * * *");
            pipeline.Activate();
            await _dataPipelineRepository.InsertAsync(pipeline, autoSave: true);
        }
    }
}
