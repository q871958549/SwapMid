'use client'

import { useMemo, useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { CheckCircle2, Loader2, PlugZap, XCircle } from 'lucide-react'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { actionsColumn, booleanBadgeColumn, primaryColumn } from '@/components/data-table/column-factories'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { type PagedResult } from '@/lib/api-client'
import { cn } from '@/lib/utils'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import {
  ClickHouseConfigFields,
  KafkaConfigFields,
  ObjectStorageConfigFields,
  RelationalConfigFields,
} from './source-config-fields'
import {
  buildDataSourceConfig,
  changeDataSourceType,
  createDefaultDataSourceForm,
  defaultPortForProvider,
  isDataSourceConfigReadyToSave,
  isDataSourceTestable,
  requiresNewCredential,
  toDataSourceBody,
  toDataSourceForm,
  type ClickHouseConfig,
  type DataSource,
  type DataSourceFormState,
  type KafkaConfig,
  type ObjectStorageConfig,
  type RelationalConfig,
} from './source-form'

export type { DataSource } from './source-form'

function getSourceTypes(t: ReturnType<typeof useTranslations>) {
  return [
    { value: 0, label: t('typeRelational') },
    { value: 1, label: t('typeRestApi') },
    { value: 2, label: t('typeFile') },
    { value: 3, label: t('typeKafka') },
    { value: 4, label: t('typeClickHouse') },
    { value: 5, label: t('typeObjectStorage') },
  ]
}

export function DataSourcesClient({
  data,
  initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '',
}: {
  data: PagedResult<DataSource>
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState; initialSearch?: string
}) {
  const t = useTranslations('DataIntegration.source')
  const rc = useTranslations('DataIntegration.common')
  const tc = useTranslations('Common')
  const locale = useLocale()
  const SOURCE_TYPES = useMemo(() => getSourceTypes(t), [t])
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<{ success: boolean; message: string; elapsedMs: number } | null>(null)
  const [defaultForm] = useState(createDefaultDataSourceForm)

  const list = useCrudResource<DataSource>({
    endpoint: 'data-sources',
    initial: data,
    errorTitle: t('loadFailedTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    initialSearch,
  })

  const crud = useCrudForm<DataSourceFormState, DataSource>({
    entityName: t('entityName'),
    endpoint: 'data-sources',
    defaultForm,
    toForm: toDataSourceForm,
    toBody: toDataSourceBody,
    beforeCreate: () => { setTestResult(null) },
    beforeEdit: () => { setTestResult(null) },
    onSuccess: list.reload,
  })

  const f = crud.set
  const editingTarget = crud.mode === 'edit' ? crud.target : null
  const requiresNewSecret = requiresNewCredential(crud.form, editingTarget)
  const canSave = !!crud.form.name.trim() && isDataSourceConfigReadyToSave(crud.form, editingTarget)
  const canTestConnection = isDataSourceTestable(crud.form, editingTarget)
  const showTestButton = ['0', '3', '4', '5'].includes(crud.form.type)

  function relational(patch: Partial<RelationalConfig>) {
    crud.set({ relational: { ...crud.form.relational, ...patch } })
  }

  function clickhouse(patch: Partial<ClickHouseConfig>) {
    crud.set({ clickhouse: { ...crud.form.clickhouse, ...patch } })
  }

  function objectStorage(patch: Partial<ObjectStorageConfig>) {
    crud.set({ objectStorage: { ...crud.form.objectStorage, ...patch } })
  }

  function kafka(patch: Partial<KafkaConfig>) {
    crud.set({ kafka: { ...crud.form.kafka, ...patch } })
  }

  function changeProvider(provider: string) {
    setTestResult(null)
    const cur = crud.form.relational
    const prevDefault = defaultPortForProvider(cur.provider)
    const nextDefault = defaultPortForProvider(provider)
    const port = cur.port.trim() === '' || cur.port.trim() === prevDefault
      ? nextDefault
      : cur.port
    crud.set({ relational: { ...cur, provider, port } })
  }

  async function testConnection() {
    setTesting(true); setTestResult(null)
    try {
      const url = crud.mode === 'edit' && crud.target
        ? `/api/proxy/data-sources/${crud.target.id}/test-connection`
        : '/api/proxy/data-sources/test-connection'
      const res = await fetch(url, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          type: Number(crud.form.type),
          config: buildDataSourceConfig(crud.form),
        }),
      })
      if (!res.ok) throw new Error(await res.text())
      setTestResult(await res.json())
    } catch (e) {
      setTestResult({ success: false, message: e instanceof Error ? e.message : t('testFailedFallback'), elapsedMs: 0 })
    } finally { setTesting(false) }
  }

  const columns = useMemo<ColumnDef<DataSource, unknown>[]>(() => [
    primaryColumn<DataSource>('name', tc('name'), { size: 180, onOpen: crud.openEdit }),
    {
      accessorKey: 'type',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('type')} />,
      meta: { filterVariant: 'select', filterOptions: SOURCE_TYPES.map(o => ({ value: o.value, label: o.label })) },
      size: 140,
      cell: ({ row }) => <Badge variant="outline">{SOURCE_TYPES.find(o => o.value === row.original.type)?.label ?? rc('unknown')}</Badge>,
    },
    {
      accessorKey: 'description',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('description')} />,
      size: 240,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.description ?? '—'}</span>,
    },
    booleanBadgeColumn<DataSource>('isEnabled', tc('status'), { trueLabel: rc('enabledLabel'), falseLabel: rc('disabledLabel') }),
    {
      accessorKey: 'creationTime',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('creationTime')} />,
      size: 160,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{new Date(row.original.creationTime).toLocaleDateString(locale)}</span>,
    },
    actionsColumn<DataSource>({ onEdit: crud.openEdit, onDelete: crud.askDelete }),
  ],
    [SOURCE_TYPES, crud.askDelete, crud.openEdit, locale, rc, tc],
  )

  return (
    <>
      <PageHeader title={t('pageTitle')} description={t('countDescription', { count: list.total })} action={<CreateButton label={t('createButtonLabel')} onClick={crud.openCreate} />} />
      <DataTable columns={columns} data={list.rows} {...list.tableProps} searchPlaceholder={t('searchPlaceholder')} tableKey="data-sources" />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('createTitle'), edit: t('editTitle') }}
        size="lg"
        canSubmit={canSave}
        footerExtra={
          <>
            {showTestButton && (
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={testConnection}
                disabled={testing || !canTestConnection}
              >
                {testing ? <Loader2 className="mr-1.5 size-3.5 animate-spin" /> : <PlugZap className="mr-1.5 size-3.5" />}{t('testConnectionButton')}
              </Button>
            )}
            {testResult && (
              <span
                title={testResult.message}
                className={cn('flex min-w-0 items-center gap-1 text-xs', testResult.success ? 'text-success' : 'text-destructive')}
              >
                {testResult.success ? <CheckCircle2 className="size-3.5 shrink-0" /> : <XCircle className="size-3.5 shrink-0" />}
                <span className="truncate">{testResult.success ? t('testSuccessResult', { ms: testResult.elapsedMs }) : testResult.message}</span>
              </span>
            )}
          </>
        }
      >
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('nameFieldLabel')}</Label>
            <Input value={crud.form.name} onChange={e => f({ name: e.target.value })} placeholder={t('namePlaceholder')} />
          </div>
          <div className="space-y-1.5">
            <Label>{t('typeFieldLabel')}</Label>
            <Select
              value={crud.form.type}
              onValueChange={(value) => {
                setTestResult(null)
                f(changeDataSourceType(crud.form, value))
              }}
            >
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>{SOURCE_TYPES.map(o => <SelectItem key={o.value} value={String(o.value)}>{o.label}</SelectItem>)}</SelectContent>
            </Select>
          </div>
        </div>
        {crud.form.type === '0' ? (
          <RelationalConfigFields
            value={crud.form.relational}
            hasSecret={crud.form.hasSecret}
            requiresNewPassword={requiresNewSecret}
            onChangeProvider={changeProvider}
            onPatch={relational}
          />
        ) : crud.form.type === '4' ? (
          <ClickHouseConfigFields
            value={crud.form.clickhouse}
            hasSecret={crud.form.hasSecret}
            requiresNewPassword={requiresNewSecret}
            onPatch={clickhouse}
          />
        ) : crud.form.type === '5' ? (
          <ObjectStorageConfigFields
            value={crud.form.objectStorage}
            hasSecret={crud.form.hasSecret}
            requiresNewSecretKey={requiresNewSecret}
            onPatch={objectStorage}
          />
        ) : crud.form.type === '3' ? (
          <KafkaConfigFields
            value={crud.form.kafka}
            hasSecret={crud.form.hasSecret}
            requiresNewPassword={requiresNewSecret}
            onPatch={kafka}
          />
        ) : (
          <div className="space-y-1.5">
            <Label>{t('configJsonFieldLabel')}</Label>
            <Textarea value={crud.form.config} onChange={e => f({ config: e.target.value })} placeholder={'{\n  "url": "https://api.example.com"\n}'} className="font-mono text-xs min-h-[100px]" />
          </div>
        )}
        <div className="space-y-1.5">
          <Label>{tc('description')}</Label>
          <Textarea value={crud.form.description} onChange={e => f({ description: e.target.value })} placeholder={rc('descriptionPlaceholder')} className="min-h-[60px]" />
        </div>
        <div className="flex items-center gap-2">
          <Switch id="ds-isEnabled" checked={crud.form.isEnabled} onCheckedChange={v => f({ isEnabled: v })} />
          <Label htmlFor="ds-isEnabled">{t('enableSwitchLabel')}</Label>
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        crud={crud}
        entityName={t('entityName')}
        description={(target) => <>{t('deleteDesc', { name: target.name })}</>}
      />
    </>
  )
}
