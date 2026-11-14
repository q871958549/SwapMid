#!/usr/bin/env node
// 新页面合规脚手架（docs/ui-governance-blueprint.md §3.1 P1 ⑥）。
//
// 用法：
//   pnpm new:page <domain>/<page> [--kind list|crud|report|form|detail|dashboard] [--dry-run]
//
// 生成物默认采用 ListPageShell / PageHeader 结构化插槽 / FilterBar / EmptyState /
// 数字列工厂（numberColumn / moneyColumn）与 useCrudForm + CrudFormDialog，从第一行代码起零违规。

import { existsSync, mkdirSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const DASHBOARD_DIR = join(ROOT, 'src', 'app', '(dashboard)')

export const SUPPORTED_KINDS = Object.freeze(['list', 'crud', 'report', 'form', 'detail', 'dashboard'])
const SEGMENT_RE = /^[a-z][a-z0-9-]*$/

export function toPascalCase(routePath) {
  return routePath
    .split('/')
    .flatMap((seg) => seg.split('-'))
    .filter(Boolean)
    .map((part) => part[0].toUpperCase() + part.slice(1))
    .join('')
}

export function validateRouteSlug(rawSlug) {
  const normalized = String(rawSlug ?? '').trim().replace(/^\/+|\/+$/g, '')
  if (!normalized) throw new Error('缺少路由路径，示例：pnpm new:page sales-management/rebates --kind crud')
  const segments = normalized.split('/')
  if (segments.length < 2 || segments.length > 4) {
    throw new Error(`路由路径须为 2–4 段 kebab-case（如 domain/feature），收到：${rawSlug}`)
  }
  for (const seg of segments) {
    if (!SEGMENT_RE.test(seg)) {
      throw new Error(`非法路由段 "${seg}"：只允许小写字母开头的小写字母、数字与连字符`)
    }
  }
  return normalized
}

export function parseCliArgs(argv) {
  let slug = ''
  let kind = 'list'
  let dryRun = false
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i]
    if (arg === '--dry-run') {
      dryRun = true
    } else if (arg === '--kind' || arg === '-k') {
      kind = argv[++i] ?? ''
    } else if (arg.startsWith('--kind=')) {
      kind = arg.slice('--kind='.length)
    } else if (!arg.startsWith('-') && !slug) {
      slug = arg
    } else {
      throw new Error(`未知参数：${arg}`)
    }
  }
  const normalizedSlug = validateRouteSlug(slug)
  if (!SUPPORTED_KINDS.includes(kind)) {
    throw new Error(`不支持的页面类型 "${kind}"，可选：${SUPPORTED_KINDS.join(', ')}`)
  }
  return { slug: normalizedSlug, kind, dryRun }
}

function buildListPageServer(pascalName, routeUrl) {
  return `import { Suspense } from 'react'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ${pascalName}Client, type ${pascalName}Row } from './client'

export default async function ${pascalName}Page() {
  const title = await resolvePageShellTitle('${routeUrl}')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={5} />}>
      <${pascalName}Data title={title} />
    </Suspense>
  )
}

async function ${pascalName}Data({ title }: { title: string }) {
  const items: ${pascalName}Row[] = []
  return <${pascalName}Client title={title} initialItems={items} />
}
`
}

function buildPlainPageServer(pascalName, routeUrl) {
  return `import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ${pascalName}Client } from './client'

export default async function ${pascalName}Page() {
  const title = await resolvePageShellTitle('${routeUrl}')
  return <${pascalName}Client title={title} />
}
`
}

function buildListClient(pascalName, storageKey) {
  return `'use client'

import { useMemo, useState } from 'react'
import { type ColumnDef } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import { Inbox } from 'lucide-react'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { numberColumn } from '@/components/data-table/column-factories'
import { FilterBar } from '@/components/data-table/filter-bar'
import { ListPageShell } from '@/components/data-table/list-page-shell'
import { PageHeader } from '@/components/data-table/page-header'
import { Button } from '@/components/ui/button'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'

export interface ${pascalName}Row {
  id: string
  code: string
  name: string
  quantity: number
}

export function ${pascalName}Client({
  title,
  initialItems,
}: {
  title: string
  initialItems: ${pascalName}Row[]
}) {
  const tc = useTranslations('Common')
  const [keyword, setKeyword] = useState('')

  const filtered = useMemo(() => {
    const q = keyword.trim().toLowerCase()
    if (!q) return initialItems
    return initialItems.filter((item) => item.code.toLowerCase().includes(q) || item.name.toLowerCase().includes(q))
  }, [initialItems, keyword])

  const columns = useMemo<ColumnDef<${pascalName}Row, unknown>[]>(() => [
    {
      accessorKey: 'code',
      header: ({ column }) => <DataTableColumnHeader column={column} title="Code" />,
    },
    {
      accessorKey: 'name',
      header: ({ column }) => <DataTableColumnHeader column={column} title="Name" />,
    },
    numberColumn<${pascalName}Row>('quantity', 'Qty'),
  ], [])

  return (
    <ListPageShell
      header={<PageHeader title={title} />}
      filters={
        <FilterBar>
          <Input
            value={keyword}
            onChange={(e) => setKeyword(e.target.value)}
            placeholder={tc('searchPlaceholder')}
            aria-label={tc('search')}
            className="h-8 w-field-md"
          />
        </FilterBar>
      }
      table={
        <DataTable
          columns={columns}
          data={filtered}
          tableKey="${storageKey}"
          emptyState={
            <EmptyState
              compact
              icon={<Inbox />}
              title={tc('noData')}
              action={keyword ? (
                <Button size="sm" variant="outline" onClick={() => setKeyword('')}>
                  {tc('reset')}
                </Button>
              ) : undefined}
            />
          }
        />
      }
    />
  )
}
`
}

function buildCrudClient(pascalName, storageKey) {
  return `'use client'

import { useMemo, useState } from 'react'
import { type ColumnDef } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import { Inbox, Plus } from 'lucide-react'
import { DataTable } from '@/components/data-table/data-table'
import { actionsColumn, numberColumn, primaryColumn } from '@/components/data-table/column-factories'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { FilterBar } from '@/components/data-table/filter-bar'
import { ListPageShell } from '@/components/data-table/list-page-shell'
import { PageHeader } from '@/components/data-table/page-header'
import { Button } from '@/components/ui/button'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useCrudForm } from '@/lib/crud/use-crud-form'

export interface ${pascalName}Row {
  id: string
  code: string
  name: string
  quantity: number
}

const EMPTY_FORM = { code: '', name: '', quantity: 0 }

export function ${pascalName}Client({
  title,
  initialItems,
}: {
  title: string
  initialItems: ${pascalName}Row[]
}) {
  const tc = useTranslations('Common')
  const [items, setItems] = useState<${pascalName}Row[]>(initialItems)
  const [keyword, setKeyword] = useState('')
  const [deleting, setDeleting] = useState<${pascalName}Row | null>(null)

  const crud = useCrudForm<typeof EMPTY_FORM, ${pascalName}Row>({
    emptyForm: EMPTY_FORM,
    toForm: (row) => ({ code: row.code, name: row.name, quantity: row.quantity }),
    onSubmit: async (form, editing) => {
      if (editing) {
        setItems((prev) => prev.map((row) => (row.id === editing.id ? { ...row, ...form } : row)))
      } else {
        setItems((prev) => [{ id: crypto.randomUUID(), ...form }, ...prev])
      }
    },
  })

  const missingFields = useMemo(() => {
    const missing: string[] = []
    if (!crud.form.code.trim()) missing.push('Code')
    if (!crud.form.name.trim()) missing.push('Name')
    return missing
  }, [crud.form.code, crud.form.name])

  const filtered = useMemo(() => {
    const q = keyword.trim().toLowerCase()
    if (!q) return items
    return items.filter((item) => item.code.toLowerCase().includes(q) || item.name.toLowerCase().includes(q))
  }, [items, keyword])

  const columns = useMemo<ColumnDef<${pascalName}Row, unknown>[]>(() => [
    primaryColumn<${pascalName}Row>('code', 'Code', crud.openEdit),
    {
      accessorKey: 'name',
      header: 'Name',
    },
    numberColumn<${pascalName}Row>('quantity', 'Qty'),
    actionsColumn<${pascalName}Row>({
      onEdit: crud.openEdit,
      onDelete: (row) => setDeleting(row),
    }),
  ], [crud.openEdit])

  return (
    <>
      <ListPageShell
        header={
          <PageHeader
            title={title}
            primaryAction={
              <Button size="sm" onClick={crud.openCreate}>
                <Plus className="size-4" />
                {tc('add')}
              </Button>
            }
          />
        }
        filters={
          <FilterBar>
            <Input
              value={keyword}
              onChange={(e) => setKeyword(e.target.value)}
              placeholder={tc('searchPlaceholder')}
              aria-label={tc('search')}
              className="h-8 w-field-md"
            />
          </FilterBar>
        }
        table={
          <DataTable
            columns={columns}
            data={filtered}
            tableKey="${storageKey}"
            emptyState={
              <EmptyState
                compact
                icon={<Inbox />}
                title={tc('noData')}
                action={
                  <Button size="sm" variant="outline" onClick={crud.openCreate}>
                    {tc('add')}
                  </Button>
                }
              />
            }
          />
        }
      />

      <CrudFormDialog
        crud={crud}
        size="md"
        title={{ create: tc('add'), edit: title }}
        canSubmit={missingFields.length === 0}
        missingFields={missingFields}
      >
        <div className="space-y-1.5">
          <Label htmlFor="${storageKey}-code">Code</Label>
          <Input
            id="${storageKey}-code"
            value={crud.form.code}
            onChange={(e) => crud.set({ code: e.target.value })}
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="${storageKey}-name">Name</Label>
          <Input
            id="${storageKey}-name"
            value={crud.form.name}
            onChange={(e) => crud.set({ name: e.target.value })}
          />
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        open={Boolean(deleting)}
        onOpenChange={(open) => { if (!open) setDeleting(null) }}
        itemName={deleting?.name}
        onConfirm={async () => {
          if (!deleting) return
          setItems((prev) => prev.filter((item) => item.id !== deleting.id))
          setDeleting(null)
        }}
      />
    </>
  )
}
`
}

function buildReportClient(pascalName, storageKey) {
  return `'use client'

import { useMemo, useState } from 'react'
import { type ColumnDef } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { moneyColumn, percentColumn } from '@/components/data-table/column-factories'
import { FilterBar } from '@/components/data-table/filter-bar'
import { ListPageShell } from '@/components/data-table/list-page-shell'
import { PageHeader } from '@/components/data-table/page-header'
import { Input } from '@/components/ui/input'

export interface ${pascalName}Row {
  id: string
  groupName: string
  amount: number
  completionRate: number
}

export function ${pascalName}Client({
  title,
  initialItems,
}: {
  title: string
  initialItems: ${pascalName}Row[]
}) {
  const tc = useTranslations('Common')
  const [keyword, setKeyword] = useState('')

  const filtered = useMemo(() => {
    const q = keyword.trim().toLowerCase()
    if (!q) return initialItems
    return initialItems.filter((row) => row.groupName.toLowerCase().includes(q))
  }, [initialItems, keyword])

  const totalAmount = useMemo(() => filtered.reduce((sum, r) => sum + r.amount, 0), [filtered])

  const columns = useMemo<ColumnDef<${pascalName}Row, unknown>[]>(() => [
    {
      accessorKey: 'groupName',
      header: ({ column }) => <DataTableColumnHeader column={column} title="Group" />,
    },
    moneyColumn<${pascalName}Row>('amount', 'Amount'),
    percentColumn<${pascalName}Row>('completionRate', 'Rate'),
  ], [])

  return (
    <ListPageShell
      header={
        <PageHeader
          title={title}
          help={{
            label: tc('pageHelp'),
            content: <p className="text-muted-foreground text-xs">{title}</p>,
          }}
        />
      }
      filters={
        <FilterBar>
          <Input
            value={keyword}
            onChange={(e) => setKeyword(e.target.value)}
            placeholder={tc('searchPlaceholder')}
            aria-label={tc('search')}
            className="h-8 w-field-md"
          />
        </FilterBar>
      }
      summary={
        <div className="bg-muted/40 flex items-center justify-between rounded-lg border px-3 py-2 text-sm">
          <span className="text-muted-foreground">Total</span>
          <span className="font-medium tabular-nums">{totalAmount.toFixed(2)}</span>
        </div>
      }
      table={
        <DataTable
          columns={columns}
          data={filtered}
          tableKey="${storageKey}"
          disableCompactHeight
        />
      }
    />
  )
}
`
}

function buildFormClient(pascalName, storageKey) {
  return `'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { PageHeader } from '@/components/data-table/page-header'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export function ${pascalName}Client({ title }: { title: string }) {
  const tc = useTranslations('Common')
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [saving, setSaving] = useState(false)
  const canSave = Boolean(code.trim() && name.trim())

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!canSave) return
    setSaving(true)
    try {
      await Promise.resolve()
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4">
      <PageHeader title={title} />
      <Card className="max-w-xl">
        <CardContent className="pt-6">
          <form onSubmit={(e) => void handleSubmit(e)} className="grid gap-4">
            <div className="space-y-1.5">
              <Label htmlFor="${storageKey}-code">Code</Label>
              <Input id="${storageKey}-code" value={code} onChange={(e) => setCode(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="${storageKey}-name">Name</Label>
              <Input id="${storageKey}-name" value={name} onChange={(e) => setName(e.target.value)} />
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <Button type="submit" size="sm" loading={saving} disabled={!canSave}>
                {tc('save')}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
`
}

function buildDetailClient(pascalName) {
  return `'use client'

import { useTranslations } from 'next-intl'
import { PageHeader } from '@/components/data-table/page-header'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'

export function ${pascalName}Client({ title }: { title: string }) {
  const tc = useTranslations('Common')
  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4">
      <PageHeader
        title={title}
        primaryAction={<Button size="sm">{tc('confirm')}</Button>}
      />
      <Card>
        <CardContent className="grid gap-4 pt-6 sm:grid-cols-3">
          <div>
            <p className="text-muted-foreground text-xs">ID</p>
            <p className="text-sm font-medium tabular-nums">DOC-001</p>
          </div>
          <div>
            <p className="text-muted-foreground text-xs">Status</p>
            <Badge variant="success">Active</Badge>
          </div>
          <div>
            <p className="text-muted-foreground text-xs">Updated</p>
            <p className="text-sm tabular-nums">2026-11-11</p>
          </div>
        </CardContent>
      </Card>
      <Tabs defaultValue="items" className="flex min-h-0 flex-1 flex-col">
        <TabsList className="w-fit">
          <TabsTrigger value="items">Items</TabsTrigger>
          <TabsTrigger value="history">History</TabsTrigger>
        </TabsList>
        <TabsContent value="items" className="min-h-0 flex-1 rounded-lg border p-4 text-sm">
          {tc('noData')}
        </TabsContent>
        <TabsContent value="history" className="min-h-0 flex-1 rounded-lg border p-4 text-sm">
          {tc('noData')}
        </TabsContent>
      </Tabs>
    </div>
  )
}
`
}

function buildDashboardClient(pascalName) {
  return `'use client'

import { useTranslations } from 'next-intl'
import { PageHeader } from '@/components/data-table/page-header'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

const METRICS = [
  { key: 'total', label: 'Total', value: '0' },
  { key: 'pending', label: 'Pending', value: '0' },
  { key: 'completed', label: 'Completed', value: '0' },
  { key: 'rate', label: 'Rate', value: '0.00%' },
] as const

export function ${pascalName}Client({ title }: { title: string }) {
  const tc = useTranslations('Common')
  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4">
      <PageHeader title={title} />
      <div className="grid shrink-0 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {METRICS.map((metric) => (
          <Card key={metric.key} className="gap-2">
            <CardHeader>
              <CardTitle className="text-muted-foreground text-xs font-normal">{metric.label}</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-2xl font-semibold tabular-nums">{metric.value}</p>
            </CardContent>
          </Card>
        ))}
      </div>
      <Card className="flex min-h-0 flex-1 flex-col overflow-hidden">
        <CardHeader className="shrink-0">
          <CardTitle className="text-sm font-medium">Queue</CardTitle>
        </CardHeader>
        <CardContent className="text-muted-foreground min-h-0 flex-1 overflow-y-auto text-sm">
          {tc('noData')}
        </CardContent>
      </Card>
    </div>
  )
}
`
}

export function generatePageFiles(slug, kind = 'list') {
  const normalizedSlug = validateRouteSlug(slug)
  if (!SUPPORTED_KINDS.includes(kind)) {
    throw new Error(`不支持的页面类型 "${kind}"，可选：${SUPPORTED_KINDS.join(', ')}`)
  }
  const pascalName = toPascalCase(normalizedSlug)
  const routeUrl = `/${normalizedSlug}`
  const storageKey = normalizedSlug.replaceAll('/', '-')
  const usesListServer = kind === 'list' || kind === 'crud' || kind === 'report'
  const pageTsx = usesListServer
    ? buildListPageServer(pascalName, routeUrl)
    : buildPlainPageServer(pascalName, routeUrl)

  let clientTsx = ''
  if (kind === 'list') clientTsx = buildListClient(pascalName, storageKey)
  else if (kind === 'crud') clientTsx = buildCrudClient(pascalName, storageKey)
  else if (kind === 'report') clientTsx = buildReportClient(pascalName, storageKey)
  else if (kind === 'form') clientTsx = buildFormClient(pascalName, storageKey)
  else if (kind === 'detail') clientTsx = buildDetailClient(pascalName)
  else clientTsx = buildDashboardClient(pascalName)

  return {
    routeUrl,
    pascalName,
    files: {
      'page.tsx': pageTsx,
      'client.tsx': clientTsx,
    },
  }
}

export function scaffoldPage({ slug, kind = 'list', dryRun = false, dashboardDir = DASHBOARD_DIR }) {
  const generated = generatePageFiles(slug, kind)
  const targetDir = join(dashboardDir, ...slug.split('/'))
  const outputs = Object.entries(generated.files).map(([name, content]) => ({
    path: join(targetDir, name),
    content,
  }))

  for (const out of outputs) {
    if (existsSync(out.path)) {
      throw new Error(`目标文件已存在，拒绝覆盖：${out.path}`)
    }
  }

  if (!dryRun) {
    mkdirSync(targetDir, { recursive: true })
    for (const out of outputs) {
      writeFileSync(out.path, out.content, 'utf8')
    }
  }
  return { ...generated, targetDir, outputs }
}

function main() {
  const options = parseCliArgs(process.argv.slice(2))
  const result = scaffoldPage(options)
  const modeLabel = options.dryRun ? '[dry-run] 已校验骨架（未落盘）' : '已生成合规页面骨架'
  console.log(`${modeLabel}：${result.routeUrl} (--kind ${options.kind})`)
  for (const out of result.outputs) {
    console.log(`  - ${out.path}`)
  }
  console.log('后续步骤：1) 在 nav-groups/*.ts 注册导航项与权限；2) 在 AGENTS.md 业务模块地图登记（若为新域）。')
}

const isEntry = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)
if (isEntry) {
  try {
    main()
  } catch (error) {
    console.error(`[new:page] ${error instanceof Error ? error.message : String(error)}`)
    process.exit(1)
  }
}
