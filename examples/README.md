# SwapMid Reference Examples

Runnable end-to-end integration blueprints demonstrating SwapMid's three core engines:

| Directory | Engine(s) | Pattern |
| :--- | :--- | :--- |
| [`postgres-incremental-upsert/`](./postgres-incremental-upsert/) | `DataIntegration` | Atomic staging + PostgreSQL `ON CONFLICT` incremental upsert with watermark protection and Target Receipt v1 |
| [`cloudevents-webhook-fanout/`](./cloudevents-webhook-fanout/) | `DataIntegration` → `MessageBus` | Transactional Outbox terminal events delivered as CNCF CloudEvents v1.0 HTTP Webhooks with `X-SwapMid-Signature` verification |
| [`partner-api-gateway/`](./partner-api-gateway/) | `ApiManagement` + `MessageBus` | OpenIddict `client_credentials` token issuance, per-client route-group ACL + rate limit policy, and idempotent `/api/gateway/events/{topic}` ingress |
