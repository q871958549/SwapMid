# Security Policy

## Supported Versions

| Version | Supported |
| ------- | --------- |
| `1.0.x` (`main`, LTS) | :white_check_mark: |
| `< 1.0.0` (pre-release) | :x: |

## Built-in Security Controls

SwapMid is designed with fail-closed security boundaries across the middleware and BFF layers:

- **Zero Browser Token Exposure**: OAuth2 / OpenIddict tokens are held server-side in Redis-backed encrypted/isolated sessions; browser clients interact exclusively via HttpOnly session cookies and `/api/proxy/*` BFF routes.
- **SSRF & Outbound Delivery Guards**: HTTP Webhook delivery in Message Bus and REST connectors enforce URL validation and private/loopback network protections unless explicitly allowlisted by operators.
- **Webhook Authenticity**: Outbound HTTP subscriptions support HMAC-SHA256 signing (`X-SwapMid-Signature: sha256=...`), and inbound Prometheus Alertmanager webhooks validate `X-SwapMid-Alert-Token` using constant-time comparison.
- **ETL Execution & Target Receipt Safety**: Mutating pipeline triggers require explicit policy checks, idempotent `RunId` coordination, and TLS certificate validation (`Encrypt=true`, `TrustServerCertificate=false`) for Target Receipt v1 verification.
- **Impersonation Guard**: Account impersonation enforces 5 invariants (explicit permission, active target, non-self, non-admin target, mandatory audit trail) and cannot be chained.
- **Supply-Chain Hardening**: `common.props` enforces `NuGetAudit` (`all` / `low`) with warnings treated as errors, and `web/pnpm-workspace.yaml` enforces a 24-hour `minimumReleaseAge` observation window.

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions, or pull requests.**

Instead, please report suspected vulnerabilities privately via **GitHub Private Vulnerability Reporting** (`Security` -> `Report a vulnerability` in the repository navigation).

When reporting, please include:

1. Affected component (`SwapMid.HttpApi.Host`, `SwapMid.Application`, `web` BFF proxy, Docker deployment, etc.).
2. Prerequisites and step-by-step reproduction instructions or minimal proof-of-concept.
3. Expected vs. actual security boundary behavior and potential impact.

We aim to acknowledge receipt within **72 hours** and coordinate remediation and disclosure once a fix is verified.
