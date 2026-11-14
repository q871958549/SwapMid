# SwapMid Web Frontend Guide

Next.js 16 (App Router) + React 19 + TypeScript + Tailwind CSS 4 + shadcn/ui + `next-intl` (`zh-Hans` / `zh-Hant` / `en`).

## Architecture & Request Boundaries

- **Server Components**: Use `apiFetch('/...')` (`src/lib/api-client.ts`) with server-side session Bearer token injection.
- **Client Components**: Call `/api/proxy/...` (`src/app/api/proxy/[...path]/route.ts`), which enforces session identity preconditions, CSRF/origin checks, body limits, and streaming upstream timeouts.
- **OpenAPI Contract Types**: Generated deterministically from `../docs/api/openapi.generated.json` via `pnpm gen:api` and `pnpm gen:api-doc`.

## UI Hard Rule Card

<!-- BEGIN:ui-hard-rule-card -->
- **一屏一个主角**：列表在 1366×768、1440×900、1920×1080 首屏必须看见表头与至少一行或空状态。列表页用 `ListPageShell` 插槽（`header` / `filters` / `notice` / `summary`）+ `FilterBar` 扁平筛选条；表上方最多一行 `shrink-0` 筛选 + 一块 `max-h-[28vh]` 内滚汇总（`swapmid/above-table-budget`、`ui-health: aboveTableBlocks`），根容器保持 flex 高度链（`swapmid/datatable-list-root`）。
- **按钮与层级**：每区最多 1 个主按钮（`swapmid/page-header-actions`、`swapmid/single-primary-per-region`、`ui-health: headerPrimary / footerPrimary`）。页头文字按钮 ≤3，第 4 个起进 `PageHeader.moreActions` 或 `DropdownButton`（`swapmid/page-header-actions`）。选中态用 `SegmentedControl` / `FilterChip`（`ui-health: variantToggles`）；实心红只在确认页脚，批量用 `destructive-outline`（`ui-health: solidDestructive`）。
- **对话框、尺寸档与控件**：对话框页脚钉在面板底部（`ui-health: unpinnedDialogFooters`，长表单用 `ScrollableDialog` / `CrudFormDialog`）；自定义 `max-w-*` 必带 `sm:` 前缀（`swapmid/dialog-max-width-prefix`）。禁用 `window.confirm`、原生 `<select>`、`type="date"`（`swapmid/no-native-dialog-controls`）：库表下拉用 `Combobox`（`Select` 仅限静态枚举），空状态用 `EmptyState`。
- **文案、配色与焦点**：页头描述 ≤40 字（`ui-health: descriptionLength`），按钮 2–4 字（>6 字拦：`ui-health: longButtonLabels`）。纯图标按钮用 `IconButton`（`swapmid/icon-button-label`）；数字列用 `numberColumn` / `moneyColumn` / `percentColumn`（`ui-health: inferredNumericColumns`）。状态色走 Badge 语义变体（`swapmid/no-palette-classes`）；焦点环只读 `--focus-ring-*` 令牌（`swapmid/focus-ring-token`）。
- **脚手架、CRUD 与验证**：写表单优先 `useCrudForm` + `CrudFormDialog`（`swapmid/prefer-crud-form`）；服务端分页走 `useCrudResource`（顶层表 `urlSync: true`），写后失效 Router Cache（`reloadAndRefresh` / `useMutateAndRefresh`）。提交前通过 eslint `swapmid/*` 与 `pnpm check:ui`。
<!-- END:ui-hard-rule-card -->
