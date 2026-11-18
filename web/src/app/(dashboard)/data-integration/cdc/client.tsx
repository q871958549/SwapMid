'use client'

import { useMemo, useState } from 'react'
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
import type { PagedResult } from '@/lib/api-client'
import type { CdcStreamDto } from '@/lib/api/types'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import {
  CdcCheckpointDialog,
  CdcDropSlotDialog,
  CdcPreflightDialog,
  CdcSinkTemplateDialog,
} from './cdc-dialogs'
import { CdcFormDialog } from './cdc-form-dialog'
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
            {Number(row.original.sourceType ?? 0) === 2
              ? 'MySQL Binlog'
              : (Number(row.original.sourceType ?? 0) === 1 ? 'SQL Server CT' : 'PostgreSQL')}
          </div>
        </div>
      ),
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
        onClose={() => actions.setSinkTemplateState(null)}
      />
      <DeleteConfirm
        crud={crud}
        entityName={t('entityName')}
        description={target => <>{t('deleteDesc', { name: target.name })}</>}
      />
    </>
  )
}
