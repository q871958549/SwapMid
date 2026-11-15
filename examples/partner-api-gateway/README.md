# Partner API Gateway & Async Event Ingress

Demonstrates machine-to-machine partner integration using OpenIddict `client_credentials` (`scope=external-integration`), route-group ACLs, per-client sliding-window rate limits, and idempotent event ingestion (`POST /api/gateway/events/{topicName}`).
