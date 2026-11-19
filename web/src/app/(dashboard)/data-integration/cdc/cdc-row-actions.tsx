'use client'

import { Activity, ClipboardCheck, DatabaseBackup, FileCode2, History, Loader2, PauseCircle, Play, Power, RefreshCw, RotateCcw, ShieldCheck, Unplug } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { StatusBadge, type StatusTone } from '@/components/ui/status-badge'
import type { CdcStreamDto } from '@/lib/api/types'

export const CDC_STATUS_ACTIVE = 1
export const CDC_STATUS_PAUSED = 2
export const CDC_STATUS_ERROR = 3
export const CDC_TARGET_PIPELINE = 1
export const CDC_TARGET_EXTERNAL_SINK = 2
export const CDC_TARGET_RELATIONAL = 3

export function getCdcStatusMeta(
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

export function CdcStatusCell({ stream }: { stream: CdcStreamDto }) {
  const t = useTranslations('DataIntegration.cdc')
  const rc = useTranslations('DataIntegration.common')
  const meta = getCdcStatusMeta(stream.status, t, rc)
  const snapStatus = Number(stream.snapshotStatus ?? 0)
  const showSnap = stream.initialSnapshotEnabled || snapStatus > 0
  const snapLabel = snapStatus === 1
    ? t('snapshotCompletedBadge', { count: stream.snapshotRowCount ?? 0 })
    : snapStatus === 2
      ? t('snapshotFailedBadge')
      : t('snapshotPendingBadge')

  return (
    <div className="space-y-1">
      <div className="flex flex-wrap items-center gap-1">
        <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
        {stream.schemaDriftDetected && (
          <StatusBadge tone="danger" title={stream.schemaDriftSummary ?? undefined}>
            {t('schemaDriftDetectedBadge')}
          </StatusBadge>
        )}
        {Number(stream.contractStatus ?? 0) === 1 && (
          <StatusBadge tone="success">{t('contractHealthyBadge')}</StatusBadge>
        )}
        {Number(stream.contractStatus ?? 0) === 2 && (
          <StatusBadge tone="warning" title={stream.contractViolationSummary ?? undefined}>
            {t('contractWarningBadge')}
          </StatusBadge>
        )}
        {Number(stream.contractStatus ?? 0) === 3 && (
          <StatusBadge tone="danger" title={stream.contractViolationSummary ?? undefined}>
            {t('contractBreachedBadge')}
          </StatusBadge>
        )}
      </div>
      {showSnap && <div className="text-xs text-muted-foreground">{snapLabel}</div>}
    </div>
  )
}

export function CdcTargetCell({ stream }: { stream: CdcStreamDto }) {
  const t = useTranslations('DataIntegration.cdc')
  const mode = Number(stream.targetMode)

  if (mode === CDC_TARGET_PIPELINE) {
    return (
      <div className="space-y-0.5 text-xs">
        <div className="font-medium">{t('targetModePipeline')}</div>
        <div className="font-mono text-muted-foreground">{stream.targetPipelineName || '-'}</div>
      </div>
    )
  }

  if (mode === CDC_TARGET_EXTERNAL_SINK || mode === CDC_TARGET_RELATIONAL) {
    const modeLabel = mode === CDC_TARGET_RELATIONAL
      ? t('targetModeRelationalTable')
      : t('targetModeExternalSink')
    const mappingCount = stream.targetFieldMappings?.length ?? 0
    const isSoftDelete = mode === CDC_TARGET_RELATIONAL && Number(stream.targetDeletePolicy) === 2
    return (
      <div className="space-y-0.5 text-xs">
        <div className="font-medium">{modeLabel}</div>
        <div className="font-mono text-muted-foreground">
          {`${stream.targetSinkName || '-'} · ${stream.targetSinkDestination || '-'}`}
        </div>
        {stream.targetSinkKeyColumns && (
          <div className="font-mono text-[11px] text-muted-foreground">
            {`Key: ${stream.targetSinkKeyColumns}`}
          </div>
        )}
        {(isSoftDelete || mappingCount > 0 || (mode === CDC_TARGET_EXTERNAL_SINK && Number(stream.lakehouseTableFormat ?? 0) === 1) || (mode === CDC_TARGET_EXTERNAL_SINK && Number(stream.catalogType ?? 0) > 0)) && (
          <div className="flex flex-wrap gap-1 pt-0.5">
            {mode === CDC_TARGET_EXTERNAL_SINK && Number(stream.lakehouseTableFormat ?? 0) === 1 && (
              <StatusBadge tone="info">Delta Lake</StatusBadge>
            )}
            {mode === CDC_TARGET_EXTERNAL_SINK && Number(stream.catalogType ?? 0) > 0 && (
              <StatusBadge tone="neutral" title={stream.catalogLastSyncStatus ?? undefined}>
                {Number(stream.catalogType) === 1
                  ? (stream.catalogLastSyncedVersion !== null && stream.catalogLastSyncedVersion !== undefined
                    ? `UC · v${stream.catalogLastSyncedVersion}`
                    : 'Unity Catalog')
                  : (stream.catalogLastSyncedVersion !== null && stream.catalogLastSyncedVersion !== undefined
                    ? `Iceberg · v${stream.catalogLastSyncedVersion}`
                    : 'Iceberg REST')}
              </StatusBadge>
            )}
            {isSoftDelete && (
              <StatusBadge tone="info">
                {t('softDeleteCellBadge', { col: stream.softDeleteColumn || 'is_deleted' })}
              </StatusBadge>
            )}
            {mappingCount > 0 && (
              <StatusBadge tone="neutral">
                {t('fieldMappingsCellBadge', { count: mappingCount })}
              </StatusBadge>
            )}
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="space-y-0.5 text-xs">
      <div className="font-medium">{t('targetModeTopic')}</div>
      <div className="font-mono text-muted-foreground">{stream.targetTopicName || '-'}</div>
    </div>
  )
}

export function CdcRowActions({
  row,
  busyKind,
  canManage,
  canDelete,
  onSinkTemplate,
  onLakehouseHistory,
  onSyncCatalog,
  onEvaluateContract,
  onPreflight,
  onLifecycle,
  onCaptureNow,
  onSnapshot,
  onCheckpointReset,
  onAcceptSchema,
  onDropSlot,
}: {
  row: CdcStreamDto
  busyKind?: string
  canManage: boolean
  canDelete: boolean
  onSinkTemplate: (row: CdcStreamDto) => void
  onLakehouseHistory?: (row: CdcStreamDto) => void
  onSyncCatalog?: (row: CdcStreamDto) => void
  onEvaluateContract?: (row: CdcStreamDto) => void
  onPreflight: (row: CdcStreamDto) => void
  onLifecycle: (row: CdcStreamDto, action: 'activate' | 'pause') => void
  onCaptureNow: (row: CdcStreamDto) => void
  onSnapshot: (row: CdcStreamDto) => void
  onCheckpointReset: (row: CdcStreamDto) => void
  onAcceptSchema?: (row: CdcStreamDto) => void
  onDropSlot: (row: CdcStreamDto) => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const busy = !!busyKind
  const isActive = Number(row.status) === CDC_STATUS_ACTIVE
  const isExternalSink = Number(row.targetMode) === CDC_TARGET_EXTERNAL_SINK
  const isDeltaLake = isExternalSink && Number(row.lakehouseTableFormat ?? 0) === 1 && row.targetSinkType === 5
  const hasExternalCatalog = isExternalSink && Number(row.catalogType ?? 0) > 0

  if (!canManage && !isExternalSink) return null

  return (
    <>
      {isExternalSink && (
        <DropdownMenuItem disabled={busy} onClick={() => onSinkTemplate(row)}>
          {busyKind === 'sink-template'
            ? <Loader2 className="mr-2 size-3.5 animate-spin" />
            : <FileCode2 className="mr-2 size-3.5" />}
          {t('sinkTemplateMenuItem')}
        </DropdownMenuItem>
      )}
      {isDeltaLake && onLakehouseHistory && (
        <DropdownMenuItem disabled={busy} onClick={() => onLakehouseHistory(row)}>
          {busyKind === 'history'
            ? <Loader2 className="mr-2 size-3.5 animate-spin" />
            : <History className="mr-2 size-3.5" />}
          {t('timeTravelMenuItem')}
        </DropdownMenuItem>
      )}
      {canManage && hasExternalCatalog && onSyncCatalog && (
        <DropdownMenuItem disabled={busy} onClick={() => onSyncCatalog(row)}>
          {busyKind === 'sync-catalog'
            ? <Loader2 className="mr-2 size-3.5 animate-spin" />
            : <RefreshCw className="mr-2 size-3.5" />}
          {t('syncCatalogMenuItem')}
        </DropdownMenuItem>
      )}
      {canManage && onEvaluateContract && (
        <DropdownMenuItem disabled={busy} onClick={() => onEvaluateContract(row)}>
          {busyKind === 'evaluate-contract'
            ? <Loader2 className="mr-2 size-3.5 animate-spin" />
            : <ClipboardCheck className="mr-2 size-3.5" />}
          {t('evaluateContractMenuItem')}
        </DropdownMenuItem>
      )}
      {canManage && (
        <>
          <DropdownMenuItem disabled={busy} onClick={() => onPreflight(row)}>
            {busyKind === 'preflight'
              ? <Loader2 className="mr-2 size-3.5 animate-spin" />
              : <Activity className="mr-2 size-3.5" />}
            {t('preflightMenuItem')}
          </DropdownMenuItem>
          {isActive ? (
            <DropdownMenuItem disabled={busy} onClick={() => onLifecycle(row, 'pause')}>
              {busyKind === 'pause'
                ? <Loader2 className="mr-2 size-3.5 animate-spin" />
                : <PauseCircle className="mr-2 size-3.5" />}
              {t('pauseMenuItem')}
            </DropdownMenuItem>
          ) : (
            <DropdownMenuItem disabled={busy} onClick={() => onLifecycle(row, 'activate')}>
              {busyKind === 'activate'
                ? <Loader2 className="mr-2 size-3.5 animate-spin" />
                : <Power className="mr-2 size-3.5" />}
              {t('activateMenuItem')}
            </DropdownMenuItem>
          )}
          <DropdownMenuItem
            disabled={busy || !isActive}
            onClick={() => onCaptureNow(row)}
          >
            {busyKind === 'capture'
              ? <Loader2 className="mr-2 size-3.5 animate-spin" />
              : <Play className="mr-2 size-3.5" />}
            {isActive ? t('captureNowMenuItem') : t('captureRequiresActiveMenuItem')}
          </DropdownMenuItem>
          <DropdownMenuItem disabled={busy} onClick={() => onSnapshot(row)}>
            {busyKind === 'snapshot'
              ? <Loader2 className="mr-2 size-3.5 animate-spin" />
              : <DatabaseBackup className="mr-2 size-3.5" />}
            {t('snapshotMenuItem')}
          </DropdownMenuItem>
          <DropdownMenuItem disabled={busy} onClick={() => onCheckpointReset(row)}>
            <RotateCcw className="mr-2 size-3.5" />
            {t('resetCheckpointMenuItem')}
          </DropdownMenuItem>
          {onAcceptSchema && (row.schemaDriftDetected || Number(row.schemaDriftPolicy ?? 0) > 0) && (
            <DropdownMenuItem disabled={busy} onClick={() => onAcceptSchema(row)}>
              {busyKind === 'accept-schema'
                ? <Loader2 className="mr-2 size-3.5 animate-spin" />
                : <ShieldCheck className="mr-2 size-3.5" />}
              {t('acceptSchemaMenuItem')}
            </DropdownMenuItem>
          )}
          {canDelete && !isActive && Number(row.sourceType ?? 0) === 0 && (
            <DropdownMenuItem disabled={busy} onClick={() => onDropSlot(row)}>
              <Unplug className="mr-2 size-3.5" />
              {t('dropSlotMenuItem')}
            </DropdownMenuItem>
          )}
        </>
      )}
    </>
  )
}
