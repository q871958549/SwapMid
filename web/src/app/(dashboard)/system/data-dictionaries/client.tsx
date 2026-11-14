'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { actionsColumn, booleanBadgeColumn, primaryColumn } from '@/components/data-table/column-factories'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { type PagedResult } from '@/lib/api-client'
import { DictItemsDialog } from './dict-items-dialog'
import { useHasPermission } from '@/components/providers/permissions-provider'

export interface DataDictionaryDto {
  id: string
  code: string
  name: string
  description: string | null
  isEnabled: boolean
  itemCount: number
  creationTime: string
}

interface FormState {
  code: string
  name: string
  description: string
  isEnabled: boolean
}

const defaultForm: FormState = { code: '', name: '', description: '', isEnabled: true }

export function DataDictionariesClient({
  data, initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '',
}: {
  data: PagedResult<DataDictionaryDto>
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState; initialSearch?: string
}) {
  const t = useTranslations('System.dataDictionaries')
  const tc = useTranslations('Common')
  const sc = useTranslations('System.common')
  const canRead = useHasPermission('SwapMid.SystemManagement.DataDictionaries')
  const createAllowed = useHasPermission('SwapMid.SystemManagement.DataDictionaries.Create')
  const updateAllowed = useHasPermission('SwapMid.SystemManagement.DataDictionaries.Update')
  const deleteAllowed = useHasPermission('SwapMid.SystemManagement.DataDictionaries.Delete')
  const canCreate = canRead && createAllowed
  const canUpdate = canRead && updateAllowed
  const canDelete = canRead && deleteAllowed

  const list = useCrudResource<DataDictionaryDto>({
    endpoint: 'data-dictionaries',
    initial: data,
    errorTitle: t('errorTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    initialSearch,
  })
  const crud = useCrudForm<FormState, DataDictionaryDto>({
    entityName: t('entityName'),
    endpoint: 'data-dictionaries',
    defaultForm,
    getName: (item) => item.name,
    toForm: (d) => ({
      code: d.code,
      name: d.name,
      description: d.description ?? '',
      isEnabled: d.isEnabled,
    }),
    toBody: (form) => ({
      code: form.code,
      name: form.name,
      description: form.description || null,
      isEnabled: form.isEnabled,
    }),
    onSuccess: list.reload,
  })

  const [itemsDict, setItemsDict] = useState<DataDictionaryDto | null>(null)

  const columns: ColumnDef<DataDictionaryDto, unknown>[] = [
    primaryColumn<DataDictionaryDto>('code', t('columns.code'), {
      size: 180,
      className: 'font-mono text-sm font-semibold',
      onOpen: canUpdate ? crud.openEdit : undefined,
    }),
    primaryColumn<DataDictionaryDto>('name', t('columns.name'), {
      size: 160,
      onOpen: canUpdate ? crud.openEdit : undefined,
    }),
    {
      accessorKey: 'description',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('description')} />,
      size: 220,
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{row.original.description || '—'}</span>
      ),
    },
    {
      accessorKey: 'itemCount',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.itemCount')} />,
      size: 90,
      enableSorting: false,
      cell: ({ row }) => (
        <Badge variant="outline" className="tabular-nums">{t('columns.itemCountValue', { count: row.original.itemCount })}</Badge>
      ),
    },
    booleanBadgeColumn<DataDictionaryDto>('isEnabled', tc('status'), { trueLabel: tc('enabled'), falseLabel: sc('disabledLabel') }),
    actionsColumn<DataDictionaryDto>({
      onEdit: canUpdate ? crud.openEdit : undefined,
      extraItems: canRead ? (row) => (
        <>
          <DropdownMenuItem onClick={() => setItemsDict(row)}>{t('configureItemsMenuItem')}</DropdownMenuItem>
          {canDelete && <DropdownMenuItem variant="destructive" onClick={() => crud.askDelete(row)}>{tc('delete')}</DropdownMenuItem>}
        </>
      ) : undefined,
    }),
  ]

  return (
    <>
      <PageHeader
        title={t('pageTitle')}
        description={t('pageDescriptionTemplate', { count: list.total })}
        action={canCreate ? <CreateButton label={t('createButton')} onClick={crud.openCreate} /> : undefined}
      />
      <DataTable
        columns={columns}
        data={list.rows}
        {...list.tableProps}
        searchPlaceholder={t('searchPlaceholder')}
        tableKey="data-dictionaries"
        showAuditColumns={false}
      />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('dialog.createTitle'), edit: t('dialog.editTitle') }}
        canSubmit={(crud.mode === 'create' ? canCreate : canUpdate) && !!crud.form.code && !!crud.form.name}
        contentClassName="sm:max-w-[480px]"
      >
        <div className="space-y-1.5">
          <Label>{t('dialog.codeLabel')}</Label>
          <Input
            value={crud.form.code}
            onChange={e => crud.set({ code: e.target.value })}
            placeholder="gender / order_status"
            className="font-mono"
          />
          <p className="text-xs text-muted-foreground">{t('dialog.codeHint')}</p>
        </div>
        <div className="space-y-1.5">
          <Label>{t('dialog.nameLabel')}</Label>
          <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('dialog.namePlaceholder')} />
        </div>
        <div className="space-y-1.5">
          <Label>{tc('description')}</Label>
          <Input value={crud.form.description} onChange={e => crud.set({ description: e.target.value })} placeholder={t('dialog.descriptionPlaceholder')} />
        </div>
        <div className="flex items-center gap-2 pt-1">
          <Switch id="dict-enabled" checked={crud.form.isEnabled} onCheckedChange={v => crud.set({ isEnabled: v })} />
          <Label htmlFor="dict-enabled">{t('dialog.enabledLabel')}</Label>
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        crud={crud}
        entityName={t('delete.entityName')}
        description={(target) => <>{t('delete.descriptionPrefixTemplate', { name: target.name })}<strong>{t('delete.descriptionBoldWord')}</strong>{t('delete.descriptionSuffix')}</>}
      />

      {itemsDict && (
        <DictItemsDialog
          dict={itemsDict}
          open={!!itemsDict}
          onOpenChange={(o) => {
            if (!o) {
              setItemsDict(null)
              void list.reload()
            }
          }}
        />
      )}
    </>
  )
}
