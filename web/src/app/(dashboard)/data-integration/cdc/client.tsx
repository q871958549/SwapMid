'use client'

import { useCallback, useMemo, useRef, useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import {
  actionsColumn,
  numberColumn,
  primaryColumn,
} from '@/components/data-table/column-factories'
import { CreateButton } from '@/components/data-table/create-button'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { FilterBar, type FilterBarChip } from '@/components/data-table/filter-bar'
import { ListPageShell } from '@/components/data-table/list-page-shell'
import { PageHeader } from '@/components/data-table/page-header'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { toast } from '@/components/ui/use-toast'
import type { PagedResult } from '@/lib/api-client'
import { parseOptionalJsonResponse, readApiErrorMessage } from '@/lib/api-error'
import type {
  CdcAcceptSchemaResultDto,
  CdcCaptureResultDto,
  CdcDropSlotResultDto,
  CdcPreflightResultDto,
  CdcSinkTemplateDto,
  CdcSnapshotResultDto,
  CdcStreamDto,
} from '@/lib/api/types'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import {
  CdcCheckpointDialog,
  CdcDropSlotDialog,
  CdcPreflightDialog,
  CdcSinkTemplateDialog,
  type CdcPreflightState,
  type CdcSinkTemplateState,
} from './cdc-dialogs'
import {
  CdcFormDialog,
  defaultCdcForm,
  toCdcBody,
  toCdcForm,
  type CdcFormState,
  type CdcPipelineOption,
  type CdcSourceOption,
  type CdcTopicOption,
} from './cdc-form-dialog'
import {
  CdcRowActions,
  CdcStatusCell,
  CdcTargetCell,
  getCdcStatusMeta,
} from './cdc-row-actions'

export function CdcStreamsClient({
  data,
  sources,
  relationalTargets,
  sinks = [],
  pipelines,
  topics,
  initialPage = 0,
  initialPageSize = 15,
  initialSorting = [],
  initialSearch = '',
  initialStatus = 'all',
  canCreate = false,
  canUpdate = false,
  canDelete = false,
  canManage = false,
}: {
  data: PagedResult<CdcStreamDto>
  sources: CdcSourceOption[]
  relationalTargets?: CdcSourceOption[]
  sinks?: CdcSourceOption[]
  pipelines: CdcPipelineOption[]
  topics: CdcTopicOption[]
  initialPage?: number
  initialPageSize?: number
  initialSorting?: SortingState
  initialSearch?: string
  initialStatus?: string
  canCreate?: boolean
  canUpdate?: boolean
  canDelete?: boolean
  canManage?: boolean
}) {
  const t = useTranslations('DataIntegration.cdc')
  const rc = useTranslations('DataIntegration.common')
  const locale = useLocale()

  const [statusFilter, setStatusFilter] = useState(initialStatus)
  const [busyActions, setBusyActions] = useState<Record<string, string>>({})
  const busyActionsRef = useRef<Record<string, string>>({})
  const [preflightState, setPreflightState] = useState<CdcPreflightState | null>(null)
  const [checkpointTarget, setCheckpointTarget] = useState<CdcStreamDto | null>(null)
  const [checkpointInput, setCheckpointInput] = useState('')
  const [checkpointSubmitting, setCheckpointSubmitting] = useState(false)
  const [checkpointError, setCheckpointError] = useState<string | null>(null)
  const [dropSlotTarget, setDropSlotTarget] = useState<CdcStreamDto | null>(null)
  const [dropSlotSubmitting, setDropSlotSubmitting] = useState(false)
  const [dropSlotError, setDropSlotError] = useState<string | null>(null)
  const [sinkTemplateState, setSinkTemplateState] = useState<CdcSinkTemplateState | null>(null)
  const [provisionSubmitting, setProvisionSubmitting] = useState(false)

  const list = useCrudResource<CdcStreamDto>({
    endpoint: 'cdc-streams',
    initial: data,
    errorTitle: t('loadFailedTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    initialSearch,
    extraDeps: [statusFilter],
    buildParams: (qs) => {
      if (statusFilter !== 'all') {
        qs.set('status', statusFilter)
      }
    },
  })

  const crud = useCrudForm<CdcFormState, CdcStreamDto>({
    entityName: t('entityName'),
    endpoint: 'cdc-streams',
    defaultForm: defaultCdcForm,
    toForm: toCdcForm,
    toBody: toCdcBody,
    onSuccess: list.reloadAndRefresh,
  })

  const setBusy = useCallback((id: string, kind: string | null) => {
    if (kind === null) {
      delete busyActionsRef.current[id]
      setBusyActions((prev) => {
        const next = { ...prev }
        delete next[id]
        return next
      })
      return
    }
    busyActionsRef.current[id] = kind
    setBusyActions(prev => ({ ...prev, [id]: kind }))
  }, [])

  const runStreamAction = useCallback(async <T,>(
    row: CdcStreamDto,
    kind: string,
    url: string,
    method: 'GET' | 'POST' | 'PUT',
    failTitle: string,
    failFallback: string,
    onOk: (data: T) => void | Promise<void>,
  ) => {
    if (busyActionsRef.current[row.id]) return
    setBusy(row.id, kind)
    try {
      const res = method === 'GET' ? await fetch(url) : await fetch(url, { method })
      if (!res.ok) {
        toast({
          variant: 'destructive',
          title: failTitle,
          description: await readApiErrorMessage(res, failFallback),
        })
        // 捕获/快照/生命周期失败可能已由后端触发 Schema 漂移自动暂停或状态变更，刷新列表以同步徽章
        if (method !== 'GET') await Promise.resolve(list.reloadAndRefresh()).catch(() => {})
        return
      }
      await onOk(await parseOptionalJsonResponse<T>(res))
    } finally {
      setBusy(row.id, null)
    }
  }, [list, setBusy])

  const handlePreflight = useCallback((row: CdcStreamDto) => runStreamAction<CdcPreflightResultDto>(
    row, 'preflight', `/api/proxy/cdc-streams/${row.id}/preflight`, 'POST',
    t('preflightFailedTitle'), t('preflightFailedFallback'),
    result => setPreflightState({ stream: row, result }),
  ), [runStreamAction, t])

  const handleLifecycle = useCallback((row: CdcStreamDto, action: 'activate' | 'pause') => runStreamAction<void>(
    row, action, `/api/proxy/cdc-streams/${row.id}/${action}`, 'PUT',
    action === 'activate' ? t('activateFailedTitle') : t('pauseFailedTitle'), t('lifecycleFailedFallback'),
    async () => {
      toast({
        variant: 'success',
        title: action === 'activate' ? t('activateSuccessTitle') : t('pauseSuccessTitle'),
        description: row.name,
      })
      await list.reloadAndRefresh()
    },
  ), [list, runStreamAction, t])

  const handleCaptureNow = useCallback((row: CdcStreamDto) => runStreamAction<CdcCaptureResultDto>(
    row, 'capture', `/api/proxy/cdc-streams/${row.id}/capture`, 'POST',
    t('captureFailedTitle'), t('captureFailedFallback'),
    async (result) => {
      toast({
        variant: 'success',
        title: t('captureSuccessTitle'),
        description: t('captureSuccessDesc', {
          captured: result.capturedEventCount ?? 0,
          delivered: result.deliveredCount ?? 0,
          lsn: result.newCheckpointLsn || '-',
        }),
      })
      await list.reloadAndRefresh()
    },
  ), [list, runStreamAction, t])

  const openCheckpointDialog = useCallback((row: CdcStreamDto) => {
    setCheckpointTarget(row)
    setCheckpointInput(row.checkpointLsn ?? '')
    setCheckpointError(null)
  }, [])

  const submitCheckpointReset = useCallback(async () => {
    if (!checkpointTarget) return
    setCheckpointSubmitting(true)
    setCheckpointError(null)
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${checkpointTarget.id}/checkpoint`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ checkpointLsn: checkpointInput.trim() || null }),
      })
      if (!res.ok) {
        setCheckpointError(await readApiErrorMessage(res, t('checkpointFailedFallback')))
        return
      }
      toast({
        variant: 'success',
        title: t('checkpointSuccessTitle'),
        description: checkpointTarget.name,
      })
      setCheckpointTarget(null)
      await list.reloadAndRefresh()
    } finally {
      setCheckpointSubmitting(false)
    }
  }, [checkpointInput, checkpointTarget, list, t])

  const openDropSlotDialog = useCallback((row: CdcStreamDto) => {
    setDropSlotTarget(row)
    setDropSlotError(null)
  }, [])

  const submitDropSlot = useCallback(async () => {
    if (!dropSlotTarget) return
    setDropSlotSubmitting(true)
    setDropSlotError(null)
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${dropSlotTarget.id}/drop-slot`, { method: 'POST' })
      if (!res.ok) {
        setDropSlotError(await readApiErrorMessage(res, t('dropSlotFailedFallback')))
        return
      }
      const result = await res.json() as CdcDropSlotResultDto
      toast({
        variant: 'success',
        title: t('dropSlotSuccessTitle'),
        description: result.message || result.slotName,
      })
      setDropSlotTarget(null)
      await list.reloadAndRefresh()
    } finally {
      setDropSlotSubmitting(false)
    }
  }, [dropSlotTarget, list, t])

  const handleSinkTemplate = useCallback((row: CdcStreamDto) => runStreamAction<CdcSinkTemplateDto>(
    row, 'sink-template', `/api/proxy/cdc-streams/${row.id}/sink-template`, 'GET',
    t('sinkTemplateFailedTitle'), t('sinkTemplateFailedFallback'),
    template => setSinkTemplateState({ stream: row, template }),
  ), [runStreamAction, t])

  const handleSnapshot = useCallback((row: CdcStreamDto) => runStreamAction<CdcSnapshotResultDto>(
    row, 'snapshot', `/api/proxy/cdc-streams/${row.id}/snapshot`, 'POST',
    t('snapshotFailedTitle'), t('snapshotFailedFallback'),
    async (result) => {
      toast({
        variant: 'success',
        title: t('snapshotSuccessTitle'),
        description: t('snapshotSuccessDesc', {
          rows: result.snapshotRowCount ?? 0,
          delivered: result.deliveredCount ?? 0,
          lsn: result.consistentPointLsn || '-',
        }),
      })
      await list.reloadAndRefresh()
    },
  ), [list, runStreamAction, t])

  const handleAcceptSchema = useCallback((row: CdcStreamDto) => runStreamAction<CdcAcceptSchemaResultDto>(
    row, 'accept-schema', `/api/proxy/cdc-streams/${row.id}/accept-schema`, 'POST',
    t('acceptSchemaFailedTitle'), t('acceptSchemaFailedFallback'),
    async (result) => {
      toast({
        variant: 'success',
        title: t('acceptSchemaSuccessTitle'),
        description: result.message || row.name,
      })
      await list.reloadAndRefresh()
    },
  ), [list, runStreamAction, t])

  const handleProvisionSink = useCallback(async () => {
    if (!sinkTemplateState) return
    setProvisionSubmitting(true)
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${sinkTemplateState.stream.id}/provision-sink`, {
        method: 'POST',
      })
      if (!res.ok) {
        toast({
          variant: 'destructive',
          title: t('provisionClickHouseFailedTitle'),
          description: await readApiErrorMessage(res, t('provisionClickHouseFailedFallback')),
        })
        return
      }
      const updated = await res.json() as CdcSinkTemplateDto
      setSinkTemplateState({ stream: sinkTemplateState.stream, template: updated })
      toast({
        variant: 'success',
        title: t('provisionClickHouseSuccessTitle'),
        description: updated.destination,
      })
    } finally {
      setProvisionSubmitting(false)
    }
  }, [sinkTemplateState, t])

  const columns: ColumnDef<CdcStreamDto, unknown>[] = useMemo(() => [
    primaryColumn<CdcStreamDto>('name', t('colName'), {
      onOpen: canUpdate ? crud.openEdit : undefined,
      size: 160,
    }),
    {
      accessorKey: 'sourceName',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('colSource')} />,
      cell: ({ row }) => row.original.sourceName || '-',
    },
    {
      accessorKey: 'slotName',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('colSlotPublication')} />,
      cell: ({ row }) => (
        <div className="space-y-0.5 font-mono text-xs">
          <div>{row.original.slotName}</div>
          <div className="text-muted-foreground">{row.original.publicationName}</div>
        </div>
      ),
    },
    {
      id: 'target',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('colTarget')} />,
      cell: ({ row }) => <CdcTargetCell stream={row.original} />,
    },
    {
      accessorKey: 'checkpointLsn',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('colCheckpointLsn')} />,
      cell: ({ row }) => <span className="font-mono text-xs">{row.original.checkpointLsn || '-'}</span>,
    },
    numberColumn<CdcStreamDto>('lastCapturedCount', t('colLastCaptured'), {
      sortable: false,
      fractionDigits: 0,
      size: 105,
    }),
    numberColumn<CdcStreamDto>('totalCapturedCount', t('colTotalCaptured'), {
      sortable: false,
      fractionDigits: 0,
      size: 105,
    }),
    {
      accessorKey: 'status',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('colStatus')} />,
      cell: ({ row }) => <CdcStatusCell stream={row.original} />,
    },
    {
      accessorKey: 'lastCapturedAt',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('colLastCapturedAt')} />,
      cell: ({ row }) => row.original.lastCapturedAt
        ? new Date(row.original.lastCapturedAt).toLocaleString(locale)
        : '-',
    },
    actionsColumn<CdcStreamDto>({
      onEdit: canUpdate ? crud.openEdit : undefined,
      onDelete: canDelete ? crud.askDelete : undefined,
      extraItems: row => (
        <CdcRowActions
          row={row}
          busyKind={busyActions[row.id]}
          canManage={canManage}
          canDelete={canDelete}
          onSinkTemplate={item => void handleSinkTemplate(item)}
          onPreflight={item => void handlePreflight(item)}
          onLifecycle={(item, action) => void handleLifecycle(item, action)}
          onCaptureNow={item => void handleCaptureNow(item)}
          onSnapshot={item => void handleSnapshot(item)}
          onCheckpointReset={openCheckpointDialog}
          onAcceptSchema={item => void handleAcceptSchema(item)}
          onDropSlot={openDropSlotDialog}
        />
      ),
    }),
  ], [
    busyActions,
    canDelete,
    canManage,
    canUpdate,
    crud,
    handleAcceptSchema,
    handleCaptureNow,
    handleLifecycle,
    handlePreflight,
    handleSinkTemplate,
    handleSnapshot,
    locale,
    openCheckpointDialog,
    openDropSlotDialog,
    t,
  ])

  const chips: FilterBarChip[] = statusFilter === 'all'
    ? []
    : [{
        key: 'status',
        label: `${t('colStatus')}: ${getCdcStatusMeta(Number(statusFilter), t, rc).label}`,
        onRemove: () => setStatusFilter('all'),
      }]

  return (
    <>
      <ListPageShell
        header={(
          <PageHeader
            title={t('pageTitle')}
            description={t('countDescription', { count: list.total })}
            primaryAction={canCreate
              ? <CreateButton label={t('createButtonLabel')} onClick={crud.openCreate} />
              : undefined}
          />
        )}
        filters={(
          <FilterBar chips={chips}>
            <Select value={statusFilter} onValueChange={setStatusFilter}>
              <SelectTrigger aria-label={t('colStatus')} className="h-8 w-[140px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">{t('statusAll')}</SelectItem>
                <SelectItem value="0">{t('statusDraft')}</SelectItem>
                <SelectItem value="1">{t('statusActive')}</SelectItem>
                <SelectItem value="2">{t('statusPaused')}</SelectItem>
                <SelectItem value="3">{t('statusError')}</SelectItem>
              </SelectContent>
            </Select>
          </FilterBar>
        )}
        table={(
          <DataTable
            columns={columns}
            data={list.rows}
            {...list.tableProps}
            searchPlaceholder={t('searchPlaceholder')}
            tableKey="data-cdc-streams"
          />
        )}
      />

      <CdcFormDialog
        crud={crud}
        sources={sources}
        relationalTargets={relationalTargets}
        sinks={sinks}
        pipelines={pipelines}
        topics={topics}
      />
      <CdcPreflightDialog state={preflightState} onClose={() => setPreflightState(null)} />
      <CdcCheckpointDialog
        target={checkpointTarget}
        value={checkpointInput}
        submitting={checkpointSubmitting}
        error={checkpointError}
        onValueChange={setCheckpointInput}
        onClose={() => setCheckpointTarget(null)}
        onSubmit={() => void submitCheckpointReset()}
      />
      <CdcDropSlotDialog
        target={dropSlotTarget}
        submitting={dropSlotSubmitting}
        error={dropSlotError}
        onClose={() => setDropSlotTarget(null)}
        onSubmit={() => void submitDropSlot()}
      />
      <CdcSinkTemplateDialog
        state={sinkTemplateState}
        canManage={canManage}
        provisioning={provisionSubmitting}
        onProvision={() => void handleProvisionSink()}
        onClose={() => setSinkTemplateState(null)}
      />
      <DeleteConfirm
        crud={crud}
        entityName={t('entityName')}
        description={target => <>{t('deleteDesc', { name: target.name })}</>}
      />
    </>
  )
}
