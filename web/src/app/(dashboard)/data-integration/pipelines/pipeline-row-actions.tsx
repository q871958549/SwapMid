'use client'

import { AlertTriangle, Loader2, PauseCircle, Play, Power, RotateCcw } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { StatusBadge, type StatusTone } from '@/components/ui/status-badge'

export interface PipelineLifecycleTarget {
  id: string
  name: string
  status?: number | null
  isEnabled?: boolean
}

export type PipelineLifecycleAction = 'activate' | 'pause'

export function resolvePipelineStatus(pipeline: Pick<PipelineLifecycleTarget, 'status' | 'isEnabled'>): number {
  if (typeof pipeline.status === 'number' && !Number.isNaN(pipeline.status)) {
    return pipeline.status
  }
  return pipeline.isEnabled ? 1 : 0
}

export function getPipelineStatusMeta(
  status: number,
  t: ReturnType<typeof useTranslations>,
  rc: ReturnType<typeof useTranslations>,
): { label: string; tone: StatusTone } {
  switch (status) {
    case 0:
      return { label: t('statusDraft'), tone: 'draft' }
    case 1:
      return { label: t('statusActive'), tone: 'success' }
    case 2:
      return { label: t('statusPaused'), tone: 'warning' }
    case 3:
      return { label: t('statusArchived'), tone: 'neutral' }
    default:
      return { label: rc('unknown'), tone: 'neutral' }
  }
}

export function PipelineStatusBadge({ status }: { status: number }) {
  const t = useTranslations('DataIntegration.pipeline')
  const rc = useTranslations('DataIntegration.common')
  const meta = getPipelineStatusMeta(status, t, rc)
  return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
}

export function PipelineRowExtraActions({
  row,
  canExecute,
  canUpdate,
  recoveryReady,
  recoveryStatus,
  blocked,
  executing,
  statusChanging,
  retainedRunId,
  onExecute,
  onTransitionStatus,
}: {
  row: PipelineLifecycleTarget
  canExecute: boolean
  canUpdate: boolean
  recoveryReady: boolean
  recoveryStatus: 'loading' | 'ready' | 'unavailable'
  blocked: boolean
  executing: boolean
  statusChanging: boolean
  retainedRunId?: string
  onExecute: () => void
  onTransitionStatus: (action: PipelineLifecycleAction) => void
}) {
  const t = useTranslations('DataIntegration.pipeline')
  const rc = useTranslations('DataIntegration.common')

  if (!canExecute && !canUpdate) return null

  const status = resolvePipelineStatus(row)
  const canActivate = canUpdate && (status === 0 || status === 2)
  const canPause = canUpdate && status === 1
  const isActive = status === 1

  return (
    <>
      {canActivate && (
        <DropdownMenuItem
          disabled={statusChanging || executing}
          onClick={() => onTransitionStatus('activate')}
        >
          {statusChanging
            ? <Loader2 className="mr-2 size-3.5 animate-spin" />
            : <Power className="mr-2 size-3.5" />}
          {statusChanging ? t('activatingMenuItem') : t('activateMenuItem')}
        </DropdownMenuItem>
      )}
      {canPause && (
        <DropdownMenuItem
          disabled={statusChanging || executing}
          onClick={() => onTransitionStatus('pause')}
        >
          {statusChanging
            ? <Loader2 className="mr-2 size-3.5 animate-spin" />
            : <PauseCircle className="mr-2 size-3.5" />}
          {statusChanging ? t('pausingMenuItem') : t('pauseMenuItem')}
        </DropdownMenuItem>
      )}
      {canExecute && (!recoveryReady ? (
        <DropdownMenuItem disabled>
          {recoveryStatus === 'unavailable'
            ? <AlertTriangle className="mr-2 size-3.5 text-destructive" />
            : <Loader2 className="mr-2 size-3.5 animate-spin" />}
          {recoveryStatus === 'unavailable'
            ? rc('recoveryStorageUnavailableTitle')
            : t('executionRecoveryLoadingMenuItem')}
        </DropdownMenuItem>
      ) : blocked ? (
        <DropdownMenuItem disabled>
          <AlertTriangle className="mr-2 size-3.5 text-destructive" />
          {t('executeOutcomeUnknownMenuItem')}
        </DropdownMenuItem>
      ) : !isActive ? (
        <DropdownMenuItem disabled>
          <Play className="mr-2 size-3.5 text-muted-foreground" />
          {t('executeRequiresActiveMenuItem')}
        </DropdownMenuItem>
      ) : (
        <DropdownMenuItem disabled={executing || statusChanging} onClick={onExecute}>
          {executing
            ? <Loader2 className="mr-2 size-3.5 animate-spin" />
            : retainedRunId
              ? <RotateCcw className="mr-2 size-3.5" />
              : <Play className="mr-2 size-3.5" />}
          {executing
            ? t('executingMenuItem')
            : retainedRunId
              ? t('retrySameRunMenuItem')
              : t('executeMenuItem')}
        </DropdownMenuItem>
      ))}
    </>
  )
}
