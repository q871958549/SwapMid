import type { DomainOverview } from './types'

export const DOMAIN_OVERRIDES: Record<string, DomainOverview> = {
  'data-integration': {
    summary:
      '数据集成引擎负责多源连接器注册、流式 ETL 管道编排、Cron 定时调度与执行状态对账。' +
      '支持 PostgreSQL、SQL Server、MySQL 关系型数据库以及 REST / 文件数据源，并在严格 TLS 的 SQL Server / PostgreSQL 目标库上支持 Target Receipt v1 确定性提交回执核验。',
    workflow: [
      {
        title: '1. 注册并测试数据源',
        detail:
          '先登记源库与目标库连接信息（含互斥资源组 targetResourceGroup），调用连接测试接口验证网络与凭据。敏感密码在服务端加密存储且不回显。',
        endpoints: [
          'POST /api/v1/data-sources',
          'POST /api/v1/data-sources/{id}/test-connection',
        ],
      },
      {
        title: '2. 创建数据管道并配置写入模式',
        detail:
          '绑定源数据源与目标数据源，声明只读抽取 SQL、目标表以及写入模式（append / truncate / append-receipt-v1 / truncate-receipt-v1）和 Cron 表达式。',
        endpoints: ['POST /api/v1/data-pipelines'],
      },
      {
        title: '3. 手动触发或对账未知状态执行',
        detail:
          '可即时触发单次执行并跟踪批次耗时与处理行数；当网络抖动导致提交结果未知时，可通过目标库回执机器核验或人工裁决闭环。',
        endpoints: [
          'POST /api/v1/data-pipelines/{id}/execute',
          'POST /api/v1/pipeline-executions/{id}/verify-target-receipt',
          'POST /api/v1/pipeline-executions/{id}/resolve-outcome',
        ],
      },
    ],
    notes: [
      '关系型数据源必须声明非空的 targetResourceGroup，用于同一物理目标库的写入互斥保护。',
      '密码写不可读：更新数据源时留空且连接作用域未变则沿用原密码；若修改 host/port/database/username 或 TLS 属性，必须重新输入密码。',
      '启用 receipt-v1 模式前，目标库需预先执行 scripts/data-integration/target-receipt-v1 下的建表脚本并开启严格 TLS（encrypt=true, trustServerCertificate=false）。',
    ],
  },
  'message-bus': {
    summary:
      '消息总线提供轻量级发布/订阅（Pub/Sub）事件交换能力，支持主题定义、订阅过滤规则、多通道异步投递（HTTP Webhook HMAC-SHA256 签名、RabbitMQ Exchange/RoutingKey、群机器人 Webhook）以及失败重试。',
    workflow: [
      {
        title: '1. 创建消息主题',
        detail: '定义业务事件主题名称与负载说明，作为上游发布与下游订阅的解耦契约。',
        endpoints: ['POST /api/v1/message-topics'],
      },
      {
        title: '2. 配置下游订阅通道',
        detail:
          '为主题挂载一个或多个订阅者，选择 HTTP 回调（可选配置 HMAC 签名密钥）、RabbitMQ 交换机路由或机器人 Webhook。',
        endpoints: ['POST /api/v1/message-subscriptions'],
      },
      {
        title: '3. 发布事件并观测投递记录',
        detail: '业务方或外部应用向主题发布 JSON 消息，平台按订阅规则扇出投递并记录每次尝试的耗时与响应状态，支持失败重放。',
        endpoints: [
          'POST /api/v1/message-records/publish',
          'GET /api/v1/message-records',
        ],
      },
    ],
    notes: [
      'HTTP Webhook 投递内置 SSRF 防护，默认拦截指向回环地址、链路本地与未授权内网元数据的回调 URL。',
      '配置订阅密钥后，HTTP 投递请求头会附带 X-SwapMid-Signature（HMAC-SHA256），接收端应基于原始请求体字节校验签名。',
    ],
  },
  'api-management': {
    summary:
      'API 网关与开放平台负责对外 API 分组、路由转发策略、第三方客户端凭据（OpenIddict client_credentials）签发以及在线调试。外部系统统一经 /gateway/{groupPrefix}/{routePath} 访问受管接口。',
    workflow: [
      {
        title: '1. 建立 API 分组与网关路由',
        detail: '按业务领域建立路由分组（声明 groupPrefix），并在组内注册上游转发路径、HTTP 方法、超时与限流策略。',
        endpoints: [
          'POST /api/v1/api-groups',
          'POST /api/v1/api-routes',
        ],
      },
      {
        title: '2. 注册第三方调用应用并签发密钥',
        detail: '为外部系统创建客户端应用（clientId / clientSecret，仅首次创建或轮换时明文返回一次），并授予可调用的路由权限。',
        endpoints: ['POST /api/v1/third-party-apps'],
      },
      {
        title: '3. 换取令牌并经网关调用',
        detail: '外部应用通过 /connect/token（grant_type=client_credentials, scope=external-integration）换取 Bearer 令牌后请求 /gateway/...。',
        endpoints: ['POST /connect/token'],
      },
    ],
    notes: [
      'clientSecret 采用单向哈希存储，丢失后只能通过轮换密钥接口重新生成，无法反查明文。',
      '在线测试控制台仅在浏览器会话内保留请求方法与路径历史，不持久化任何请求头或请求体载荷。',
    ],
  },
  enterprise: {
    summary:
      '企业组织主数据维护多法人企业主体、树形部门层级、岗位字典与员工名册，为账号开通、数据权限范围与通知路由提供统一组织底座。',
    workflow: [
      {
        title: '1. 建立企业主体与树形部门',
        detail: '先创建企业主体（可标记主企业），再按上下级关系建立部门树，平台自动维护层级路径。',
        endpoints: [
          'POST /api/v1/enterprises',
          'POST /api/v1/departments',
        ],
      },
      {
        title: '2. 维护岗位与员工并一键开通账号',
        detail: '登记岗位与在职员工档案后，可单条或批量为员工生成登录账号并建立双向绑定。',
        endpoints: [
          'POST /api/v1/positions',
          'POST /api/v1/employees',
          'POST /api/v1/account-users/generate-from-employee',
        ],
      },
    ],
  },
  account: {
    summary:
      '账号与权限中心基于 ABP Identity 与 OpenIddict 构建，提供用户生命周期管理、密码复杂度策略、角色与细粒度功能权限树，以及受五重安全不变量保护的管理员模拟登录（Impersonation）。',
    notes: [
      '新建或管理员重置密码后的账号默认标记「下次登录须改密」，首次调用 /connect/token 会返回 changePasswordToken 引导完成改密。',
      '模拟登录严格禁止嵌套模拟、自模拟及向更高权限管理员提权，全链路写入独立审计日志。',
    ],
  },
  system: {
    summary:
      '系统管理汇聚编码规则（前缀+日期+步长自增流水号）、两级数据字典、平台定时任务调度、审计日志以及运行健康度探针。',
  },
  notification: {
    summary:
      '通知中心统一承接平台内部事件（如 ETL 管道异常）与外部监控告警（Prometheus Alertmanager Webhook），按订阅人最小告警级别与来源规则路由至邮件（SMTP）或群机器人 Webhook（Generic / Slack / 飞书 / 钉钉 / 企业微信）。',
    notes: [
      '入站告警入口为 POST /api/v1/notification/webhooks/alertmanager，调用方须在请求头携带 X-SwapMid-Alert-Token。',
    ],
  },
}
