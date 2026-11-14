'use client'

import { useMemo } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { actionsColumn, primaryColumn } from '@/components/data-table/column-factories'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { type PagedResult } from '@/lib/api-client'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'

interface ApiGroup {
  id: string
  name: string
  description: string | null
  routeCount: number
  creationTime: string
}

interface FormState { name: string; description: string }
const defaultForm: FormState = { name: '', description: '' }

interface ApiGroupsClientProps {
  data: PagedResult<ApiGroup>
  initialPage?: number
  initialPageSize?: number; initialSorting?: SortingState
  initialSearch?: string
}

export function ApiGroupsClient({ data, initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '' }: ApiGroupsClientProps) {
  const t = useTranslations('ApiManagement.groups')
  const tc = useTranslations('Common')
  const locale = useLocale()
  // 服务端分页：翻页/搜索下推后端（/api-groups 支持 skipCount/maxResultCount/Filter）。
  // page/pageSize/search 经 urlSync 镜像到 URL，切走再切回/刷新可恢复（初值来自 page.tsx 的 searchParams）。
  const list = useCrudResource<ApiGroup>({
    endpoint: 'api-groups',
    initial: data,
    errorTitle: t('loadErrorTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    initialSearch,
  })
  const crud = useCrudForm<FormState, ApiGroup>({
    entityName: t('entityName'),
    endpoint: 'api-groups',
    defaultForm,
    toForm: (row) => ({ name: row.name, description: row.description ?? '' }),
    toBody: (form) => ({ name: form.name.trim(), description: form.description || null }),
    onSuccess: list.reload,
  })

  const columns = useMemo<ColumnDef<ApiGroup, unknown>[]>(() => [
    primaryColumn<ApiGroup>('name', t('columnName'), { size: 180, onOpen: crud.openEdit }),
    {
      accessorKey: 'description',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('description')} />,
      size: 260,
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{row.original.description ?? '—'}</span>
      ),
    },
    {
      accessorKey: 'routeCount',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('routeCount')} />,
      size: 120,
      meta: { numeric: true },
      cell: ({ row }) => <Badge variant="secondary" className="tabular-nums">{row.original.routeCount ?? 0}</Badge>,
    },
    {
      accessorKey: 'creationTime',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('creationTime')} />,
      size: 160,
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {new Date(row.original.creationTime).toLocaleDateString(locale)}
        </span>
      ),
    },
    actionsColumn<ApiGroup>({ onEdit: crud.openEdit, onDelete: crud.askDelete }),
  ], [t, tc, locale, crud.openEdit, crud.askDelete])

  return (
    <>
      <PageHeader
        title={t('pageTitle')}
        description={t('listDescription', { count: list.total })}
        action={<CreateButton label={t('createLabel')} onClick={crud.openCreate} />}
      />
      <DataTable
        columns={columns}
        data={list.rows}
        {...list.tableProps}
        searchPlaceholder={t('searchPlaceholder')}
        tableKey="api-groups"
      />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('createDialogTitle'), edit: t('editDialogTitle') }}
        contentClassName="sm:max-w-md"
        canSubmit={!!crud.form.name.trim()}
      >
        <div className="space-y-1.5">
          <Label htmlFor="name">{t('columnName')} <span className="text-destructive">*</span></Label>
          <Input
            id="name"
            value={crud.form.name}
            onChange={(e) => crud.set({ name: e.target.value })}
            placeholder={t('namePlaceholder')}
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="desc">{tc('description')}</Label>
          <Input
            id="desc"
            value={crud.form.description}
            onChange={(e) => crud.set({ description: e.target.value })}
            placeholder={t('descriptionPlaceholder')}
          />
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        crud={crud}
        entityName={t('entityName')}
        description={(target) => <>{t('deleteDescription', { name: target.name })}</>}
      />
    </>
  )
}
