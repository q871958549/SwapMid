'use client'

import { useCallback, useMemo, useRef, useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { Activity, Loader2, PauseCircle, Play, Power, RotateCcw, Unplug } from 'lucide-react'
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
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { StatusBadge, type StatusTone } from '@/components/ui/status-badge'
import { toast } from '@/components/ui/use-toast'
import type { PagedResult } from '@/lib/api-client'
import { getApiErrorMessage } from '@/lib/api-error'
import type {
  CdcCaptureResultDto,
  CdcDropSlotResultDto,
  CdcPreflightResultDto,
  CdcStreamDto,
} from '@/lib/api/types'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import { CdcCheckpointDialog, CdcDropSlotDialog, CdcPreflightDialog, type CdcPreflightState } from './cdc-dialogs'
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

const CDC_STATUS_ACTIVE = 1
const CDC_STATUS_PAUSED = 2
const CDC_STATUS_ERROR = 3
const CDC_TARGET_PIPELINE = 1
const CDC_TARGET_EXTERNAL_SINK = 2

function getStatusMeta(
  status: number | undefined,
  t: ReturnType<typeof useTranslations>,
  rc: ReturnType<typeof useTranslations>,
): { label: string; tone: StatusTone } {
  switch (Number(status)) {
    case 0:
      return { label: t('statusDraft'), tone: 'draft' }
    case CDC_STATUS_ACTIVE:
      return { label: t('statusActive'), tone: 'success' }
    case CDC_STATUS_PAUSED:
      return { label: t('statusPaused'), tone: 'warning' }
    case CDC_STATUS_ERROR:
      return { label: t('statusError'), tone: 'danger' }
    default:
      return { label: rc('unknown'), tone: 'neutral' }
  }
}

export function CdcStreamsClient({
  data,
  sources,
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

  const handlePreflight = useCallback(async (row: CdcStreamDto) => {
    if (busyActionsRef.current[row.id]) return
    setBusy(row.id, 'preflight')
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${row.id}/preflight`, { method: 'POST' })
      if (!res.ok) {
        toast({
          variant: 'destructive',
          title: t('preflightFailedTitle'),
          description: await getApiErrorMessage(res, t('preflightFailedFallback')),
        })
        return
      }
      const result = await res.json() as CdcPreflightResultDto
      setPreflightState({ stream: row, result })
    } finally {
      setBusy(row.id, null)
    }
  }, [setBusy, t])

  const handleLifecycle = useCallback(async (row: CdcStreamDto, action: 'activate' | 'pause') => {
    if (busyActionsRef.current[row.id]) return
    setBusy(row.id, action)
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${row.id}/${action}`, { method: 'PUT' })
      if (!res.ok) {
        toast({
          variant: 'destructive',
          title: action === 'activate' ? t('activateFailedTitle') : t('pauseFailedTitle'),
          description: await getApiErrorMessage(res, t('lifecycleFailedFallback')),
        })
        return
      }
      toast({
        variant: 'success',
        title: action === 'activate' ? t('activateSuccessTitle') : t('pauseSuccessTitle'),
        description: row.name,
      })
      await list.reloadAndRefresh()
    } finally {
      setBusy(row.id, null)
    }
  }, [list, setBusy, t])

  const handleCaptureNow = useCallback(async (row: CdcStreamDto) => {
    if (busyActionsRef.current[row.id]) return
    setBusy(row.id, 'capture')
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${row.id}/capture`, { method: 'POST' })
      if (!res.ok) {
        toast({
          variant: 'destructive',
          title: t('captureFailedTitle'),
          description: await getApiErrorMessage(res, t('captureFailedFallback')),
        })
        return
      }
      const result = await res.json() as CdcCaptureResultDto
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
    } finally {
      setBusy(row.id, null)
    }
  }, [list, setBusy, t])

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
        setCheckpointError(await getApiErrorMessage(res, t('checkpointFailedFallback')))
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
        setDropSlotError(await getApiErrorMessage(res, t('dropSlotFailedFallback')))
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
      cell: ({ row }) => {
        const mode = Number(row.original.targetMode)
        if (mode === CDC_TARGET_PIPELINE) {
          return (
            <div className="space-y-0.5 text-xs">
              <div className="font-medium">{t('targetModePipeline')}</div>
              <div className="font-mono text-muted-foreground">{row.original.targetPipelineName || '-'}</div>
            </div>
          )
        }
        if (mode === CDC_TARGET_EXTERNAL_SINK) {
          return (
            <div className="space-y-0.5 text-xs">
              <div className="font-medium">{t('targetModeExternalSink')}</div>
              <div className="font-mono text-muted-foreground">
                {`${row.original.targetSinkName || '-'} · ${row.original.targetSinkDestination || '-'}`}
              </div>
            </div>
          )
        }
        return (
          <div className="space-y-0.5 text-xs">
            <div className="font-medium">{t('targetModeTopic')}</div>
            <div className="font-mono text-muted-foreground">{row.original.targetTopicName || '-'}</div>
          </div>
        )
      },
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
      cell: ({ row }) => {
        const meta = getStatusMeta(row.original.status, t, rc)
        return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
      },
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
      extraItems: canManage
        ? (row) => {
            const busyKind = busyActions[row.id]
            const busy = !!busyKind
            const isActive = Number(row.status) === CDC_STATUS_ACTIVE
            return (
              <>
                <DropdownMenuItem disabled={busy} onClick={() => void handlePreflight(row)}>
                  {busyKind === 'preflight'
                    ? <Loader2 className="mr-2 size-3.5 animate-spin" />
                    : <Activity className="mr-2 size-3.5" />}
                  {t('preflightMenuItem')}
                </DropdownMenuItem>
                {isActive ? (
                  <DropdownMenuItem disabled={busy} onClick={() => void handleLifecycle(row, 'pause')}>
                    {busyKind === 'pause'
                      ? <Loader2 className="mr-2 size-3.5 animate-spin" />
                      : <PauseCircle className="mr-2 size-3.5" />}
                    {t('pauseMenuItem')}
                  </DropdownMenuItem>
                ) : (
                  <DropdownMenuItem disabled={busy} onClick={() => void handleLifecycle(row, 'activate')}>
                    {busyKind === 'activate'
                      ? <Loader2 className="mr-2 size-3.5 animate-spin" />
                      : <Power className="mr-2 size-3.5" />}
                    {t('activateMenuItem')}
                  </DropdownMenuItem>
                )}
                <DropdownMenuItem
                  disabled={busy || !isActive}
                  onClick={() => void handleCaptureNow(row)}
                >
                  {busyKind === 'capture'
                    ? <Loader2 className="mr-2 size-3.5 animate-spin" />
                    : <Play className="mr-2 size-3.5" />}
                  {isActive ? t('captureNowMenuItem') : t('captureRequiresActiveMenuItem')}
                </DropdownMenuItem>
                <DropdownMenuItem disabled={busy} onClick={() => openCheckpointDialog(row)}>
                  <RotateCcw className="mr-2 size-3.5" />
                  {t('resetCheckpointMenuItem')}
                </DropdownMenuItem>
                {canDelete && !isActive && (
                  <DropdownMenuItem disabled={busy} onClick={() => openDropSlotDialog(row)}>
                    <Unplug className="mr-2 size-3.5" />
                    {t('dropSlotMenuItem')}
                  </DropdownMenuItem>
                )}
              </>
            )
          }
        : undefined,
    }),
  ], [
    busyActions,
    canDelete,
    canManage,
    canUpdate,
    crud,
    handleCaptureNow,
    handleLifecycle,
    handlePreflight,
    locale,
    openCheckpointDialog,
    openDropSlotDialog,
    rc,
    t,
  ])

  const chips: FilterBarChip[] = statusFilter === 'all'
    ? []
    : [{
        key: 'status',
        label: `${t('colStatus')}: ${getStatusMeta(Number(statusFilter), t, rc).label}`,
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

      <CdcFormDialog crud={crud} sources={sources} sinks={sinks} pipelines={pipelines} topics={topics} />
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
      <DeleteConfirm
        crud={crud}
        entityName={t('entityName')}
        description={target => <>{t('deleteDesc', { name: target.name })}</>}
      />
    </>
  )
}
