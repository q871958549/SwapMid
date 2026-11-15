# CNCF CloudEvents v1.0 Webhook Fanout & Signature Verification

SwapMid automatically publishes `DataIntegration` terminal execution outcomes through the Transactional Outbox to topic `integration.pipeline.events` and delivers HTTP Webhooks with standard CNCF CloudEvents v1.0 headers (`ce-specversion`, `ce-id`, `ce-source`, `ce-type`, `ce-time`, `ce-subject`) plus HMAC-SHA256 `X-SwapMid-Signature`.

## Files

- `subscription.json`: Structured filter matching failed or `OutcomeUnknown` pipeline events (`subject:pipeline.*;$.status=Failed`).
- `verify-signature.mjs`: Zero-dependency Node.js receiver helper verifying `X-SwapMid-Signature` in constant time.
