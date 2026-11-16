'use client'

import { Activity, DatabaseBackup, FileCode2, Loader2, PauseCircle, Play, Power, RotateCcw, Unplug } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { StatusBadge, type StatusTone } from '@/components/ui/status-badge'
import type { CdcStreamDto } from '@/lib/api/types'

export const CDC_STATUS_ACTIVE = 1
export const CDC_STATUS_PAUSED = 2
export const CDC_STATUS_ERROR = 3
export const CDC_TARGET_PIPELINE = 1
export const CDC_TARGET_EXTERNAL_SINK = 2

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
      <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
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

  if (mode === CDC_TARGET_EXTERNAL_SINK) {
    return (
      <div className="space-y-0.5 text-xs">
        <div className="font-medium">{t('targetModeExternalSink')}</div>
        <div className="font-mono text-muted-foreground">
          {`${stream.targetSinkName || '-'} · ${stream.targetSinkDestination || '-'}`}
        </div>
        {stream.targetSinkKeyColumns && (
          <div className="font-mono text-[11px] text-muted-foreground">
            {`Key: ${stream.targetSinkKeyColumns}`}
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
  onPreflight,
  onLifecycle,
  onCaptureNow,
  onSnapshot,
  onCheckpointReset,
  onDropSlot,
}: {
  row: CdcStreamDto
  busyKind?: string
  canManage: boolean
  canDelete: boolean
  onSinkTemplate: (row: CdcStreamDto) => void
  onPreflight: (row: CdcStreamDto) => void
  onLifecycle: (row: CdcStreamDto, action: 'activate' | 'pause') => void
  onCaptureNow: (row: CdcStreamDto) => void
  onSnapshot: (row: CdcStreamDto) => void
  onCheckpointReset: (row: CdcStreamDto) => void
  onDropSlot: (row: CdcStreamDto) => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const busy = !!busyKind
  const isActive = Number(row.status) === CDC_STATUS_ACTIVE
  const isExternalSink = Number(row.targetMode) === CDC_TARGET_EXTERNAL_SINK

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
          {canDelete && !isActive && (
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
