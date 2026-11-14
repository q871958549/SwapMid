# SwapMid — Engineering & Agent Guidelines

SwapMid is an open-source enterprise integration and data swap middleware built on **.NET 10 + ABP v10.1** (`SwapMid.slnx`) and **Next.js 16 + React 19** (`web/`).

## 1. Architecture Overview

- **Three Core Integration Engines**:
  1. `DataIntegration`: Multi-source connectors (`PostgreSQL`, `SqlServer`, `MySql`, REST API, File), streaming ETL pipelines, Cron scheduling, idempotent `RunId` execution coordination, and Target Receipt v1 (`__swapmid_etl_receipt_v1`).
  2. `MessageBus`: Topics, subscriptions, transactional outbox / delivery attempts, multi-channel delivery (`HTTP Webhook` with HMAC-SHA256 `X-SwapMid-Signature` + SSRF guard, `RabbitMQ`, and `BotWebhook`), and dead-letter replay.
  3. `ApiManagement`: API groups, routes, dynamic gateway forwarding (`/gateway/{groupPrefix}/{routePath}`), interactive test console, and OpenIddict `client_credentials` third-party client management (`external-integration` scope).
- **Platform Foundation**:
  - `Scheduling`: Quartz/background job definitions, mutex groups, manual trigger, and execution logs.
  - `EnterpriseManagement` & `AccountManagement`: Clean multi-enterprise organization tree (`Enterprise`, `CompanyDepartment`, `Position`, `Employee`), ABP Identity users/roles/permissions, and 5-invariant `ImpersonationGuardService`.
  - `SystemManagement`, `Notification` & `DeveloperDocs`: `CodeRule`, two-tier `DataDictionary`, `AuditLog`, native `SystemHealth`, `Webhook` + `SMTP Email` + incoming Prometheus `Alertmanager` webhook, OpenAPI reference, and live EF Core database schema dictionary.

## 2. Hard Invariants

1. **Zero Warnings Policy**:
   - Backend `common.props` enforces `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `NuGetAudit` (`all` / `low`). Never suppress warnings without documented architectural justification.
   - Frontend ESLint runs with `--max-warnings=0`, alongside `knip`, `dependency-cruiser`, and `ui-health.mjs`.
2. **BFF Session & Proxy Boundary (`web/`)**:
   - Browser code must never hold OAuth tokens or call the `.NET` backend origin directly. All client requests route through `/api/proxy/*` (`web/src/app/api/proxy/[...path]/route.ts`), while Server Components use `apiFetch` (`web/src/lib/api-client.ts`).
   - Direct `window.localStorage` / `window.sessionStorage` access is restricted to allowlisted infrastructure modules (`browser-storage-policy-contract.test.ts`).
3. **Contract-Driven Types**:
   - Frontend DTO types come from `docs/api/openapi.generated.json` via `pnpm gen:api` (`web/src/lib/api/types.ts`) and `pnpm gen:api-doc`. Do not hand-write duplicate backend DTO interfaces.
4. **Localization & UI Governance**:
   - All user-visible strings must use `next-intl` (`web/messages/{zh-Hans,zh-Hant,en}.json`) and ABP localization (`src/SwapMid.Domain.Shared/Localization/SwapMid/{zh-Hans,zh-Hant,en}.json`).
   - For frontend UI layout, component primitives, and custom `swapmid/*` ESLint rules, also read `web/AGENTS.md`.

## 3. Verification Commands

```bash
# Backend build & tests
dotnet build SwapMid.slnx --nologo
dotnet test SwapMid.slnx --nologo

# Frontend full gate suite (run inside ./web)
npx tsc --noEmit
pnpm check:deps
pnpm lint
pnpm check:ui
pnpm test:security-deps
pnpm check:api-summary
pnpm test:run
pnpm build
```
