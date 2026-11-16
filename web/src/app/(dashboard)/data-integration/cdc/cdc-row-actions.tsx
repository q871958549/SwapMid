'use client'

import { Activity, FileCode2, Loader2, PauseCircle, Play, Power, RotateCcw, Unplug } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import type { CdcStreamDto } from '@/lib/api/types'

export const CDC_STATUS_ACTIVE = 1
export const CDC_STATUS_PAUSED = 2
export const CDC_STATUS_ERROR = 3
export const CDC_TARGET_PIPELINE = 1
export const CDC_TARGET_EXTERNAL_SINK = 2

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
