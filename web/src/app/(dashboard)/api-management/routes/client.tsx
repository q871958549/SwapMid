'use client'

import { useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { actionsColumn, booleanBadgeColumn, primaryColumn } from '@/components/data-table/column-factories'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select'
import { Combobox } from '@/components/ui/combobox'
import { type PagedResult } from '@/lib/api-client'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { missingLabels } from '@/lib/crud/missing-fields'
import { useCrudResource } from '@/lib/crud/use-crud-resource'

export interface ApiRoute {
  id: string
  groupId: string | null
  groupName: string | null
  name: string
  path: string
  httpMethod: number
  upstreamUrl: string
  description: string | null
  isEnabled: boolean
  order: number
  creationTime: string
}

export interface ApiGroup { id: string; name: string }

const HTTP_METHODS = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH']
const METHOD_COLORS: Record<string, string> = {
  GET: 'bg-chart-1/15 text-chart-1',
  POST: 'bg-success-soft text-success',
  PUT: 'bg-warning-soft text-warning',
  DELETE: 'bg-destructive-soft text-destructive',
  PATCH: 'bg-info-soft text-info',
}

interface FormState {
  groupId: string; name: string; path: string; httpMethod: string
  upstreamUrl: string; description: string; isEnabled: boolean; order: string
}
const defaultForm: FormState = {
  groupId: '', name: '', path: '/', httpMethod: 'GET',
  upstreamUrl: '', description: '', isEnabled: true, order: '0',
}

export function ApiRoutesClient({
  initialData, groups,
  initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '',
}: {
  initialData: PagedResult<ApiRoute>; groups: ApiGroup[]
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState; initialSearch?: string
}) {
  const t = useTranslations('ApiManagement.routes')
  const tc = useTranslations('Common')
  // 服务端分页：翻页/搜索下推后端（/api-routes 支持 skipCount/maxResultCount/Filter）。分组参照保持全量供下拉。
  // page/pageSize/search 经 urlSync 镜像到 URL，切走再切回/刷新可恢复（初值来自 page.tsx 的 searchParams）。
  const list = useCrudResource<ApiRoute>({
    endpoint: 'api-routes',
    initial: initialData,
    errorTitle: t('loadErrorTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    initialSearch,
  })
  const crud = useCrudForm<FormState, ApiRoute>({
    entityName: t('entityName'),
    endpoint: 'api-routes',
    defaultForm,
    toForm: (r) => ({
      groupId: r.groupId ?? '',
      name: r.name,
      path: r.path,
      httpMethod: HTTP_METHODS[r.httpMethod] ?? 'GET',
      upstreamUrl: r.upstreamUrl,
      description: r.description ?? '',
      isEnabled: r.isEnabled,
      order: String(r.order),
    }),
    toBody: (form) => ({
      groupId: form.groupId || null,
      name: form.name,
      path: form.path,
      httpMethod: HTTP_METHODS.indexOf(form.httpMethod),
      upstreamUrl: form.upstreamUrl,
      description: form.description || null,
      isEnabled: form.isEnabled,
      order: Number(form.order) || 0,
    }),
    onSuccess: list.reload,
  })

  const columns: ColumnDef<ApiRoute, unknown>[] = [
    primaryColumn<ApiRoute>('path', t('columnPath'), {
      size: 240,
      className: 'block max-w-full font-mono text-sm font-normal',
      onOpen: crud.openEdit,
      format: (row) => (
        <span className="block leading-tight">
          <span>{row.path}</span>
          <span className="text-muted-foreground block font-mono text-[11px] font-normal">→ /api/gateway{row.path}</span>
        </span>
      ),
    }),
    {
      accessorKey: 'httpMethod',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnMethod')} />,
      meta: { filterVariant: 'select', filterOptions: [{ value: 0, label: 'GET' }, { value: 1, label: 'POST' }, { value: 2, label: 'PUT' }, { value: 3, label: 'DELETE' }, { value: 4, label: 'PATCH' }] },
      size: 90,
      cell: ({ row }) => {
        const m = HTTP_METHODS[row.original.httpMethod] ?? '?'
        return <span className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-semibold ${METHOD_COLORS[m] ?? 'bg-muted text-muted-foreground'}`}>{m}</span>
      },
    },
    {
      accessorKey: 'upstreamUrl',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnUpstreamUrl')} />,
      size: 240,
      cell: ({ row }) => <span className="text-sm text-muted-foreground max-w-48 truncate block">{row.original.upstreamUrl}</span>,
    },
    {
      accessorKey: 'groupName',
      enableSorting: false, // 分组名是后端分页后按 GroupId 查字典回填的派生列，仓储层无对应持久化列可排序
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnGroup')} />,
      size: 120,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.groupName ?? '—'}</span>,
    },
    booleanBadgeColumn<ApiRoute>('isEnabled', tc('status'), { trueLabel: tc('enabled'), falseLabel: t('statusDisabled') }),
    actionsColumn<ApiRoute>({ onEdit: crud.openEdit, onDelete: crud.askDelete }),
  ]

  // 必填项一处声明、`canSubmit` 与缺失项提示两处消费
  const missingFields = missingLabels({
    [tc('name')]: !!crud.form.name,
    [t('columnPath')]: !!crud.form.path,
    [t('columnUpstreamUrl')]: !!crud.form.upstreamUrl,
  })

  return (
    <>
      <PageHeader title={t('pageTitle')} description={t('listDescription', { count: list.total })} action={<CreateButton label={t('createLabel')} onClick={crud.openCreate} />} />
      <DataTable columns={columns} data={list.rows} {...list.tableProps} searchPlaceholder={t('searchPlaceholder')} tableKey="api-routes" />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('createDialogTitle'), edit: t('editDialogTitle') }}
        contentClassName="sm:max-w-[540px]"
        canSubmit={missingFields.length === 0}
        missingFields={missingFields}
      >
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{tc('name')} *</Label>
            <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('namePlaceholder')} />
          </div>
          <div className="space-y-1.5">
            <Label>{t('httpMethodLabel')} *</Label>
            <Select value={crud.form.httpMethod} onValueChange={v => crud.set({ httpMethod: v })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>{HTTP_METHODS.map(m => <SelectItem key={m} value={m}>{m}</SelectItem>)}</SelectContent>
            </Select>
          </div>
        </div>
        <div className="space-y-1.5">
          <Label>{t('columnPath')} *</Label>
          <Input value={crud.form.path} onChange={e => crud.set({ path: e.target.value })} placeholder="/api/example" className="font-mono" />
          <p className="text-muted-foreground text-xs">{t.rich('gatewayHint', { path: crud.form.path || '/...', code: (chunks) => <span className="font-mono">{chunks}</span> })}</p>
        </div>
        <div className="space-y-1.5">
          <Label>{t('columnUpstreamUrl')} *</Label>
          <Input value={crud.form.upstreamUrl} onChange={e => crud.set({ upstreamUrl: e.target.value })} placeholder="https://service.internal/..." />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('groupComboboxLabel')}</Label>
            <Combobox
              options={groups.map(g => ({ value: g.id, label: g.name }))}
              value={crud.form.groupId}
              onChange={v => crud.set({ groupId: v })}
              placeholder={t('groupComboboxPlaceholder')}
              searchPlaceholder={t('groupComboboxSearchPlaceholder')}
              emptyText={t('groupComboboxEmptyText')}
              clearable
            />
          </div>
          <div className="space-y-1.5">
            <Label>{tc('sortOrder')}</Label>
            <Input type="number" value={crud.form.order} onChange={e => crud.set({ order: e.target.value })} />
          </div>
        </div>
        <div className="space-y-1.5">
          <Label>{tc('description')}</Label>
          <Textarea value={crud.form.description} onChange={e => crud.set({ description: e.target.value })} placeholder={t('descriptionPlaceholder')} className="min-h-[60px]" />
        </div>
        <div className="flex items-center gap-2">
          <Switch id="isEnabled" checked={crud.form.isEnabled} onCheckedChange={v => crud.set({ isEnabled: v })} />
          <Label htmlFor="isEnabled">{t('isEnabledLabel')}</Label>
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        crud={crud}
        entityName={t('entityName')}
        description={(target) => <>{t('deleteDescription', { path: target.path })}</>}
      />
    </>
  )
}
