'use client'

import { useMemo, useState } from 'react'
import { GitBranch, Network } from 'lucide-react'
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
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { toast } from '@/components/ui/use-toast'
import type { PagedResult } from '@/lib/api-client'
import { readApiErrorMessage } from '@/lib/api-error'
import type { CdcStreamDto, IntegrationLineageGraphDto } from '@/lib/api/types'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import {
  CdcCheckpointDialog,
  CdcDropSlotDialog,
  CdcPreflightDialog,
  CdcSinkTemplateDialog,
} from './cdc-dialogs'
import { CdcFormDialog } from './cdc-form-dialog'
import { CdcGitOpsBundleDialog } from './cdc-gitops-bundle-dialog'
import { CdcLakehouseHistoryDialog } from './cdc-lakehouse-history-dialog'
import { CdcLineageGraphDialog } from './cdc-lineage-graph-dialog'
import { CdcWebhookDeliveriesDialog } from './cdc-webhook-deliveries-dialog'
import {
  type CdcFormState,
  type CdcPipelineOption,
  type CdcSourceOption,
  type CdcTopicOption,
  defaultCdcForm,
  toCdcBody,
  toCdcForm,
} from './cdc-form-state'
import {
  CdcRowActions,
  CdcStatusCell,
  CdcTargetCell,
  getCdcStatusMeta,
} from './cdc-row-actions'
import { useCdcStreamActions } from './use-cdc-stream-actions'

export function CdcStreamsClient({
  data,
  sources,
  relationalTargets,
  sinks = [],
  catalogSources = [],
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
  catalogSources?: CdcSourceOption[]
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
  const [gitOpsOpen, setGitOpsOpen] = useState(false)
  const [lineageOpen, setLineageOpen] = useState(false)
  const [lineageLoading, setLineageLoading] = useState(false)
  const [lineageGraph, setLineageGraph] = useState<IntegrationLineageGraphDto | null>(null)

  const loadLineageGraph = async () => {
    setLineageLoading(true)
    try {
      const res = await fetch('/api/proxy/integration-lineage')
      if (!res.ok) {
        const msg = await readApiErrorMessage(res, t('lineageLoadFailedTitle'))
        toast({ variant: 'destructive', title: t('lineageLoadFailedTitle'), description: msg })
        return
      }
      setLineageGraph(await res.json() as IntegrationLineageGraphDto)
    } finally {
      setLineageLoading(false)
    }
  }

  const handleOpenLineage = () => {
    setLineageOpen(true)
    void loadLineageGraph()
  }

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

  const actions = useCdcStreamActions(list.reloadAndRefresh)

  const columns: ColumnDef<CdcStreamDto, unknown>[] = useMemo(() => [
    primaryColumn<CdcStreamDto>('name', t('colName'), {
      onOpen: canUpdate ? crud.openEdit : undefined,
      size: 160,
    }),
    {
      accessorKey: 'sourceName',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('colSource')} />,
      cell: ({ row }) => (
        <div className="space-y-0.5">
          <div>{row.original.sourceName || '-'}</div>
          <div className="font-mono text-[11px] text-muted-foreground">
            {Number(row.original.sourceType ?? 0) === 4
              ? 'Kafka Debezium'
              : (Number(row.original.sourceType ?? 0) === 3
                  ? 'Push Webhook'
                  : (Number(row.original.sourceType ?? 0) === 2
                      ? 'MySQL Binlog'
                      : (Number(row.original.sourceType ?? 0) === 1 ? 'SQL Server CT' : 'PostgreSQL')))}
          </div>
        </div>
      ),
    },
    {
      accessorKey: 'slotName',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('colSlotPublication')} />,
      cell: ({ row }) => Number(row.original.sourceType ?? 0) === 3
        ? (
            <div className="space-y-0.5 font-mono text-xs">
              <div className="max-w-[200px] truncate" title={`/api/v1/cdc-webhooks/${row.original.id}`}>
                {`/api/v1/cdc-webhooks/${row.original.id}`}
              </div>
              <div className="text-muted-foreground">HMAC-SHA256</div>
            </div>
          )
        : (Number(row.original.sourceType ?? 0) === 4
            ? (
                <div className="space-y-0.5 font-mono text-xs">
                  <div className="max-w-[200px] truncate" title={row.original.publicationName}>
                    {row.original.publicationName}
                  </div>
                  <div className="text-muted-foreground">{row.original.slotName}</div>
                </div>
              )
            : (
                <div className="space-y-0.5 font-mono text-xs">
                  <div>{row.original.slotName}</div>
                  <div className="text-muted-foreground">{row.original.publicationName}</div>
                </div>
              )),
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
      cell: ({ row }) => (
        <span suppressHydrationWarning>
          {row.original.lastCapturedAt ? new Date(row.original.lastCapturedAt).toLocaleString(locale) : '-'}
        </span>
      ),
    },
    actionsColumn<CdcStreamDto>({
      onEdit: canUpdate ? crud.openEdit : undefined,
      onDelete: canDelete ? crud.askDelete : undefined,
      extraItems: row => (
        <CdcRowActions
          row={row}
          busyKind={actions.busyActions[row.id]}
          canManage={canManage}
          canDelete={canDelete}
          onSinkTemplate={item => void actions.handleSinkTemplate(item)}
          onLakehouseHistory={item => void actions.handleOpenHistory(item)}
          onWebhookDeliveries={item => void actions.handleOpenDeliveries(item)}
          onSyncCatalog={item => void actions.handleSyncCatalog(item)}
          onEvaluateContract={item => void actions.handleEvaluateContract(item)}
          onPreflight={item => void actions.handlePreflight(item)}
          onLifecycle={(item, action) => void actions.handleLifecycle(item, action)}
          onCaptureNow={item => void actions.handleCaptureNow(item)}
          onSnapshot={item => void actions.handleSnapshot(item)}
          onCheckpointReset={actions.openCheckpointDialog}
          onAcceptSchema={item => void actions.handleAcceptSchema(item)}
          onDropSlot={actions.openDropSlotDialog}
        />
      ),
    }),
  ], [actions, canDelete, canManage, canUpdate, crud, locale, t])

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
            secondaryActions={[
              <Button
                key="lineage"
                size="sm"
                variant="outline"
                onClick={handleOpenLineage}
              >
                <Network className="mr-1.5 size-3.5" />
                {t('lineageButtonLabel')}
              </Button>,
              <Button
                key="gitops"
                size="sm"
                variant="outline"
                onClick={() => setGitOpsOpen(true)}
              >
                <GitBranch className="mr-1.5 size-3.5" />
                {t('gitOpsButtonLabel')}
              </Button>,
            ]}
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
        catalogSources={catalogSources}
        pipelines={pipelines}
        topics={topics}
      />
      <CdcPreflightDialog state={actions.preflightState} onClose={() => actions.setPreflightState(null)} />
      <CdcCheckpointDialog
        target={actions.checkpointTarget}
        value={actions.checkpointInput}
        submitting={actions.checkpointSubmitting}
        error={actions.checkpointError}
        onValueChange={actions.setCheckpointInput}
        onClose={() => actions.setCheckpointTarget(null)}
        onSubmit={() => void actions.submitCheckpointReset()}
      />
      <CdcDropSlotDialog
        target={actions.dropSlotTarget}
        submitting={actions.dropSlotSubmitting}
        error={actions.dropSlotError}
        onClose={() => actions.setDropSlotTarget(null)}
        onSubmit={() => void actions.submitDropSlot()}
      />
      <CdcSinkTemplateDialog
        state={actions.sinkTemplateState}
        canManage={canManage}
        provisioning={actions.provisionSubmitting}
        compacting={actions.compactSubmitting}
        onProvision={() => void actions.handleProvisionSink()}
        onCompact={() => void actions.handleCompactLakehouse()}
        onOpenHistory={() => {
          const stream = actions.sinkTemplateState?.stream
          actions.setSinkTemplateState(null)
          if (stream) void actions.handleOpenHistory(stream)
        }}
        onClose={() => actions.setSinkTemplateState(null)}
      />
      <CdcLakehouseHistoryDialog
        state={actions.historyState}
        loadingVersion={actions.historyLoadingVersion}
        preview={actions.previewState}
        previewLoading={actions.previewLoading}
        canManage={canManage}
        onSelectVersion={(stream, version) => void actions.handleOpenHistory(stream, version)}
        onLoadPreview={(stream, version) => void actions.handleLoadPreview(stream, version)}
        onRefresh={stream => void actions.handleOpenHistory(stream)}
        onClose={actions.closeHistory}
      />
      <CdcWebhookDeliveriesDialog
        state={actions.deliveriesState}
        loading={actions.deliveriesLoading}
        replayingId={actions.replayingDeliveryId}
        canManage={canManage}
        onRefresh={stream => void actions.handleOpenDeliveries(stream)}
        onReplay={(stream, delivery) => void actions.handleReplayDelivery(stream, delivery)}
        onClose={actions.closeDeliveries}
      />
      <CdcGitOpsBundleDialog
        open={gitOpsOpen}
        canManage={canManage}
        onApplied={list.reloadAndRefresh}
        onClose={() => setGitOpsOpen(false)}
      />
      <CdcLineageGraphDialog
        open={lineageOpen}
        graph={lineageGraph}
        loading={lineageLoading}
        onRefresh={() => void loadLineageGraph()}
        onClose={() => setLineageOpen(false)}
      />
      <DeleteConfirm
        crud={crud}
        entityName={t('entityName')}
        description={target => <>{t('deleteDesc', { name: target.name })}</>}
      />
    </>
  )
}
