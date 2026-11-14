import type { IntegrationGuide } from './types'

export const INTEGRATION_GUIDES: IntegrationGuide[] = [
  {
    id: 'oauth-gateway',
    title: '第三方应用 OAuth2 鉴权与 API 网关调用',
    intro:
      '外部业务系统通过管理员分配的 Client ID 与 Client Secret，走标准 OAuth 2.0 Client Credentials 模式换取访问令牌，并经 SwapMid 网关安全调用受管上游接口。',
    audience: '外部业务系统后端服务 / 合作伙伴集成端',
    auth: 'OAuth 2.0 client_credentials（Bearer Token，scope=external-integration）',
    baseUrl: '/gateway',
    steps: [
      {
        title: '1. 使用客户端凭据换取 Access Token',
        method: 'POST',
        path: '/connect/token',
        auth: '客户端凭据（application/x-www-form-urlencoded）',
        description:
          '使用在「API 管理 → 第三方应用」中签发的 client_id 与 client_secret 请求 OpenIddict 令牌端点，声明 scope=external-integration。',
        request: `curl -X POST 'https://swapmid.example.com/connect/token' \\
  -H 'Content-Type: application/x-www-form-urlencoded' \\
  -d 'grant_type=client_credentials&client_id=partner_erp_sync&client_secret=<your-client-secret>&scope=external-integration'`,
        response: `{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6...",
  "token_type": "Bearer",
  "expires_in": 3600
}`,
        note: '请在客户端本地缓存 access_token 并在过期前（expires_in 秒）主动刷新，避免每次业务请求都重复换取令牌触发限流。',
      },
      {
        title: '2. 携带 Bearer 令牌调用网关路由',
        method: 'POST',
        path: '/gateway/{groupPrefix}/{routePath}',
        description:
          '将上一步取得的 access_token 放入 Authorization 请求头，按「API 分组前缀 + 路由路径」访问网关转发入口。网关校验应用授权与速率限制后转发至目标上游。',
        request: `curl -X POST 'https://swapmid.example.com/gateway/orders/v1/sync' \\
  -H 'Authorization: Bearer <access_token>' \\
  -H 'Content-Type: application/json' \\
  -d '{
    "externalOrderNo": "SO-20260701-0001",
    "items": [{ "sku": "ITEM-01", "qty": 10 }]
  }'`,
        response: `{
  "accepted": true,
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}`,
        note: '若返回 401 请检查令牌是否过期；返回 403 表示该第三方应用尚未被授予目标网关路由权限；返回 429 表示触发路由级或应用级限流。',
      },
    ],
    notes: [
      '第三方应用仅授予 external-integration 作用域，与管理后台用户会话严格隔离，无法凭此令牌访问 /api/v1 平台管理接口。',
      '生产环境请务必通过 HTTPS 调用 /connect/token 与 /gateway/*，并在密钥定期轮换时利用双应用或维护窗口平滑切换。',
    ],
  },
  {
    id: 'message-bus-webhook',
    title: '消息总线事件发布与 Webhook 签名验签',
    intro:
      '上游系统向 SwapMid 消息主题发布领域事件，下游系统通过 HTTP Webhook 接收实时推送并使用 HMAC-SHA256 校验请求来源与完整性。',
    audience: '事件发布方与 HTTP Webhook 订阅接收方',
    auth: '发布端：Bearer Token；接收端：X-SwapMid-Signature（HMAC-SHA256）',
    baseUrl: '/api/v1/message-records',
    steps: [
      {
        title: '1. 向指定主题发布事件消息',
        method: 'POST',
        path: '/api/v1/message-records/publish',
        description: '指定主题 ID（或主题编码）与 JSON 负载发布消息，消息落库后由后台投递工作器按订阅通道异步扇出。',
        request: `curl -X POST 'https://swapmid.example.com/api/v1/message-records/publish' \\
  -H 'Authorization: Bearer <access_token>' \\
  -H 'Content-Type: application/json' \\
  -d '{
    "topicId": "3f2a9c10-7b4e-4d21-9f88-0c1d2e3f4a5b",
    "eventKey": "inventory.updated",
    "payload": "{\\"warehouse\\":\\"WH-01\\",\\"sku\\":\\"ITEM-01\\",\\"availableQty\\":120}"
  }'`,
        response: `{
  "id": "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d",
  "status": 0
}`,
        note: '发布接口在消息持久化写入后即返回成功，不阻塞等待下游 Webhook 响应。',
      },
      {
        title: '2. 下游接收 HTTP Webhook 并校验签名',
        method: 'POST',
        path: 'https://consumer.example.com/webhooks/swapmid',
        auth: 'X-SwapMid-Signature: sha256=<hex>',
        description:
          '当订阅配置了密钥（Secret）时，SwapMid 使用该密钥对 UTF-8 原始请求体计算 HMAC-SHA256，并以十六进制编码写入 X-SwapMid-Signature 请求头。',
        request: `POST /webhooks/swapmid HTTP/1.1
Host: consumer.example.com
Content-Type: application/json
X-SwapMid-Topic: inventory.events
X-SwapMid-Delivery-Id: 7c6d5e4f-3a2b-4c1d-9e0f-8a7b6c5d4e3f
X-SwapMid-Signature: sha256=5d41402abc4b2a76b9719d911017c592...

{"warehouse":"WH-01","sku":"ITEM-01","availableQty":120}`,
        response: `{ "ok": true }`,
        note: '接收端返回 2xx 视为投递成功；非 2xx 或超时将触发指数退避重试，请以 X-SwapMid-Delivery-Id 做好消费端幂等去重。',
      },
    ],
    notes: [
      '验签时必须读取未经过 JSON 反序列化重排的原始请求体字节（raw body），并使用恒定时间比较（constant-time comparison）比对摘要。',
      '目标 Webhook URL 受服务端 SSRF 策略保护，不允许指向 127.0.0.0/8、169.254.0.0/16 等受限地址段。',
    ],
  },
  {
    id: 'etl-target-receipt',
    title: '流式 ETL 目标库回执协议（Target Receipt v1）',
    intro:
      '针对跨库同步中「目标库事务已提交但网络断连导致中间件事态未知」的经典难题，Target Receipt v1 将业务数据写入与提交回执绑定在同一目标库本地事务中，实现零猜测的机器自动核验。',
    audience: '数据工程师 / DBA / 运维团队',
    auth: 'Bearer Token（含 SwapMid.DataIntegration.PipelineExecutions.VerifyTargetReceipt 权限）',
    baseUrl: '/api/v1/pipeline-executions',
    steps: [
      {
        title: '1. 在目标库初始化回执控制表',
        method: 'POST',
        path: '/api/v1/data-sources/{id}/test-connection',
        description:
          'DBA 在目标 SQL Server 或 PostgreSQL 数据库执行仓库 scripts/data-integration/target-receipt-v1/ 下对应方言脚本（创建 __swapmid_etl_receipt_v1 等对象），并在数据源中启用 encrypt=true、trustServerCertificate=false。',
        request: `# PostgreSQL 目标库初始化示例
psql "host=pg-target.example.internal port=5432 dbname=warehouse sslmode=verify-full" \\
  -f scripts/data-integration/target-receipt-v1/postgresql.sql`,
        response: `CREATE TABLE`,
        note: '目标连接必须开启严格证书校验（encrypt=true 且 trustServerCertificate=false），防止中间人伪造目标库回执证据。',
      },
      {
        title: '2. 对「结果未知」执行发起目标回执核验',
        method: 'POST',
        path: '/api/v1/pipeline-executions/{id}/verify-target-receipt',
        description:
          '当某次管道执行因提交阶段连接中断进入 CommitOutcomeUnknown 状态时，调用本接口直连目标库核对 RunId、目标实例指纹、Schema 摘要与批次摘要，自动收敛为 Succeeded 或 Failed。',
        request: `curl -X POST 'https://swapmid.example.com/api/v1/pipeline-executions/11111111-1111-4111-8111-111111111111/verify-target-receipt' \\
  -H 'Authorization: Bearer <access_token>'`,
        response: `{
  "id": "11111111-1111-4111-8111-111111111111",
  "status": 2,
  "processedRows": 50000
}`,
        note: '同一目标资源组存在未决的未知执行时，协调器会自动阻断后续调度运行，直至通过回执核验或人工裁决解除锁定，杜绝重复追加或脏覆盖。',
      },
    ],
    notes: [
      'Target Receipt v1 当前开放 SQL Server 与 PostgreSQL 目标库（支持 append-receipt-v1 与 truncate-receipt-v1 两种模式）。',
      '若目标库发生灾难性重建无法自动核验，拥有 ResolveOutcome 权限的管理员可调用 resolve-outcome 接口登记人工核实结论并释放互斥锁。',
    ],
  },
  {
    id: 'alertmanager-webhook',
    title: 'Prometheus Alertmanager 告警接入通知中心',
    intro:
      '将 Prometheus Alertmanager 或兼容格式的监控系统告警推送至 SwapMid 通知中心，复用平台的分级订阅策略统一分发到 SMTP 邮件与团队群机器人。',
    audience: 'SRE / 基础设施监控管理员',
    auth: '请求头令牌 X-SwapMid-Alert-Token',
    baseUrl: '/api/v1/notification/webhooks',
    steps: [
      {
        title: '1. 生成入站告警令牌',
        method: 'POST',
        path: '/api/v1/notification/settings/alert-token',
        auth: 'Bearer Token（管理员会话）',
        description: '在「系统管理 → 通知设置」点击生成或轮换告警令牌，保存一次性返回的随机令牌字符串。',
        request: `curl -X POST 'https://swapmid.example.com/api/v1/notification/settings/alert-token' \\
  -H 'Authorization: Bearer <access_token>'`,
        response: `"swm_alert_8f9e0d1c2b3a495867768594a3b2c1d0"`,
        note: '服务端仅保存令牌的 SHA-256 摘要，明文只在生成当次响应返回一次；轮换后旧令牌立即失效。',
      },
      {
        title: '2. 推送 Alertmanager Webhook 负载',
        method: 'POST',
        path: '/api/v1/notification/webhooks/alertmanager',
        auth: 'X-SwapMid-Alert-Token: <alert-token>',
        description: '在 Alertmanager 的 webhook_configs 中配置该地址并通过 http_config 或反代注入 X-SwapMid-Alert-Token 请求头。',
        request: `curl -X POST 'https://swapmid.example.com/api/v1/notification/webhooks/alertmanager' \\
  -H 'X-SwapMid-Alert-Token: swm_alert_8f9e0d1c2b3a495867768594a3b2c1d0' \\
  -H 'Content-Type: application/json' \\
  -d '{
    "status": "firing",
    "alerts": [
      {
        "status": "firing",
        "labels": {
          "alertname": "HighApiLatency",
          "severity": "critical",
          "service": "swapmid-api"
        },
        "annotations": {
          "summary": "API P99 延迟超过 2s",
          "description": "过去 5 分钟网关上游响应延迟持续升高"
        }
      }
    ]
  }'`,
        response: `{ "accepted": 1 }`,
        note: 'labels.severity 会映射为通知级别（critical → 严重，warning → 警告，其余 → 提示），按通知接收人的 minLevel 与 sources 规则精准投递。',
      },
    ],
    notes: [
      '请勿将告警令牌拼在 URL 查询串中，以免泄露到反向代理或负载均衡访问日志；服务端仅从 X-SwapMid-Alert-Token 头读取并做恒定时间比对。',
    ],
  },
]
