## Summary

<!-- Briefly describe what this PR changes and why. Link related issues (e.g., Closes #123). -->

## Scope

- [ ] `DataIntegration` (Connectors / Streaming ETL / Target Receipt v1)
- [ ] `MessageBus` (Topics / Subscriptions / Webhook / RabbitMQ / BotWebhook)
- [ ] `ApiManagement` (Gateway / Routes / OpenIddict Client Credentials)
- [ ] Platform Core (`Scheduling` / `AccountManagement` / `EnterpriseManagement` / `SystemManagement` / `Notification` / `DeveloperDocs`)
- [ ] `web/` (Next.js 16 BFF / UI Components / OpenAPI SDK / i18n)
- [ ] Infrastructure / Docker / CI / Documentation

## Verification Checklist

- [ ] Backend builds and passes tests with zero warnings (`dotnet build SwapMid.slnx -c Release --nologo && dotnet test SwapMid.slnx -c Release --no-build --nologo`)
- [ ] If backend endpoints or DTOs changed, OpenAPI contracts & docs were regenerated (`cd web && pnpm sync:api --skip-portal && pnpm api-summary:baseline`)
- [ ] If user-facing copy changed, `zh-Hans` / `zh-Hant` / `en` dictionaries are synchronized (`cd web && pnpm lint`)
- [ ] Frontend gates pass (`npx tsc --noEmit && pnpm check:deps && pnpm check:ui && pnpm test:run && pnpm build`)
