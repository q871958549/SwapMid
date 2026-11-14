# Contributing to SwapMid

Thank you for your interest in contributing to **SwapMid**! This guide summarizes the local development workflow, architecture boundaries, and quality gates required for every pull request.

## Prerequisites

- **.NET SDK**: `10.0.x`
- **Node.js**: `24.x` + **pnpm** `11.x` (managed via `corepack enable`)
- **Docker & Docker Compose**: Optional for running PostgreSQL 16, Redis 8, RabbitMQ 3.13, and Seq locally

## Local Setup

> **Quick path**: Run `pwsh ./scripts/dev.ps1` (Windows) or `./scripts/dev.sh` (Linux / macOS / WSL2) to start backing containers, apply migrations & demo seed, and launch both API Host (`https://localhost:44362`) and Next.js BFF (`http://localhost:5175`).

1. **Start Backing Services (Optional via Docker)**:
   ```bash
   cp docker/.env.example docker/.env
   docker compose -f docker/docker-compose.yml --env-file docker/.env up -d postgres redis rabbitmq
   ```

2. **Build & Test Backend**:
   ```bash
   dotnet restore SwapMid.slnx
   dotnet build SwapMid.slnx --nologo
   dotnet test SwapMid.slnx --nologo
   ```
   > All C# projects enforce `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `common.props`.

3. **Run Database Migrations & Seed Initial Admin**:
   ```bash
   dotnet run --project src/SwapMid.DbMigrator
   ```

4. **Install & Verify Frontend**:
   ```bash
   cd web
   pnpm install --frozen-lockfile
   pnpm dev
   ```

## Contract & Localization Synchronization

- **OpenAPI Contract & TypeScript SDK**:
  When modifying backend DTOs or controllers, export the OpenAPI snapshot and regenerate frontend types and API docs:
  ```bash
  cd web
  pnpm sync:api
  pnpm api-summary:baseline
  ```

- **Internationalization (`zh-Hans` / `zh-Hant` / `en`)**:
  `web/messages/zh-Hans.json` and `src/SwapMid.Domain.Shared/Localization/SwapMid/zh-Hans.json` are the primary sources of truth:
  ```bash
  cd web
  pnpm i18n:prune
  pnpm i18n:sync
  pnpm check:i18n
  ```
  Never hardcode user-facing UI strings directly in `.tsx` components; use `next-intl` (`useTranslations` / `getTranslations`).

## Pre-PR Verification Checklist

Before opening a pull request, ensure all checks pass with **zero warnings**:

```bash
# Backend
dotnet build SwapMid.slnx --nologo
dotnet test SwapMid.slnx --nologo

# Frontend
cd web
npx tsc --noEmit
pnpm check:deps
pnpm lint
pnpm check:ui
pnpm test:security-deps
pnpm check:api-summary
pnpm test:run
pnpm build
```

## License

By contributing to SwapMid, you agree that your contributions will be licensed under the [Apache License 2.0](./LICENSE).
