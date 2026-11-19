'use client'

import { Loader2, RefreshCw, RotateCcw } from 'lucide-react'
import { useLocale, useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { StatusBadge, type StatusTone } from '@/components/ui/status-badge'
import type {
  CdcStreamDto,
  CdcWebhookDeliveryListDto,
  CdcWebhookDeliveryRecordDto,
} from '@/lib/api/types'

export interface CdcWebhookDeliveriesState {
  stream: CdcStreamDto
  deliveries: CdcWebhookDeliveryListDto
}

function formatPayloadSize(bytes: number | null | undefined): string {
  const n = Number(bytes ?? 0)
  if (!Number.isFinite(n) || n <= 0) return '0 B'
  if (n < 1024) return `${n} B`
  return `${(n / 1024).toFixed(1)} KB`
}

function getOutcomeMeta(
  outcome: number | undefined,
  t: ReturnType<typeof useTranslations>,
): { label: string; tone: StatusTone } {
  switch (Number(outcome)) {
    case 0:
      return { label: t('webhookOutcomeAccepted'), tone: 'success' }
    case 2:
      return { label: t('webhookOutcomeUnauthorized'), tone: 'danger' }
    case 3:
      return { label: t('webhookOutcomeInactive'), tone: 'warning' }
    case 4:
      return { label: t('webhookOutcomeInvalidPayload'), tone: 'warning' }
    case 5:
      return { label: t('webhookOutcomePayloadTooLarge'), tone: 'warning' }
    case 6:
      return { label: t('webhookOutcomeBusy'), tone: 'warning' }
    default:
      return { label: t('webhookOutcomeFailed'), tone: 'danger' }
  }
}

export function CdcWebhookDeliveriesDialog({
  state,
  loading,
  replayingId,
  canManage,
  onRefresh,
  onReplay,
  onClose,
}: {
  state: CdcWebhookDeliveriesState | null
  loading: boolean
  replayingId: string | null
  canManage: boolean
  onRefresh: (stream: CdcStreamDto) => void
  onReplay: (stream: CdcStreamDto, delivery: CdcWebhookDeliveryRecordDto) => void
  onClose: () => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const tc = useTranslations('Common')
  const locale = useLocale()

  return (
    <Dialog open={!!state} onOpenChange={(open) => { if (!open) onClose() }}>
      {state && (
        <ScrollableDialogContent
          title={t('webhookDeliveriesDialogTitle', { name: state.stream.name })}
          description={t('webhookDeliveriesDialogDesc')}
          className="sm:max-w-3xl"
          footer={(
            <div className="flex w-full items-center justify-between gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={loading || replayingId !== null}
                onClick={() => onRefresh(state.stream)}
              >
                {loading
                  ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                  : <RefreshCw className="mr-1.5 size-3.5" />}
                {t('webhookDeliveriesRefreshButton')}
              </Button>
              <Button variant="outline" onClick={onClose}>{tc('close')}</Button>
            </div>
          )}
        >
          <div className="space-y-4">
            <div className="grid grid-cols-3 gap-2.5">
              <div className="rounded-md border bg-muted/30 px-3 py-2">
                <div className="text-xs text-muted-foreground">{t('webhookDeliveriesTotalLabel')}</div>
                <div className="mt-0.5 font-mono text-sm font-medium">
                  {state.deliveries.totalCount ?? 0}
                </div>
              </div>
              <div className="rounded-md border bg-muted/30 px-3 py-2">
                <div className="text-xs text-muted-foreground">{t('webhookDeliveriesReplayableLabel')}</div>
                <div className="mt-0.5 font-mono text-sm font-medium">
                  {state.deliveries.replayableCount ?? 0}
                </div>
              </div>
              <div className="rounded-md border bg-muted/30 px-3 py-2">
                <div className="text-xs text-muted-foreground">{t('webhookDeliveriesEndpointLabel')}</div>
                <div
                  className="mt-0.5 truncate font-mono text-xs"
                  title={`/api/v1/cdc-webhooks/${state.stream.id}`}
                >
                  {`/api/v1/cdc-webhooks/${state.stream.id}`}
                </div>
              </div>
            </div>

            {state.deliveries.items && state.deliveries.items.length > 0 ? (
              <div className="divide-y rounded-md border text-xs">
                {state.deliveries.items.map((item) => {
                  const meta = getOutcomeMeta(item.outcome, t)
                  const isReplaying = replayingId === item.id
                  return (
                    <div key={item.id} className="space-y-1.5 px-3 py-2.5">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="flex flex-wrap items-center gap-2">
                          <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>
                          <span className="font-mono text-[11px] text-muted-foreground">
                            {`HTTP ${item.httpStatusCode ?? '-'}`}
                          </span>
                          <span className="font-mono font-medium">{item.deliveryId}</span>
                          {Number(item.replayCount ?? 0) > 0 && (
                            <StatusBadge tone="info">
                              {t('webhookDeliveriesReplayedCount', { count: item.replayCount ?? 0 })}
                            </StatusBadge>
                          )}
                        </div>
                        <div className="flex items-center gap-2">
                          <span className="font-mono text-[11px] text-muted-foreground" suppressHydrationWarning>
                            {item.receivedAtUtc ? new Date(item.receivedAtUtc).toLocaleString(locale) : '-'}
                          </span>
                          {canManage && item.canReplay && (
                            <Button
                              size="sm"
                              variant="outline"
                              disabled={loading || replayingId !== null}
                              onClick={() => onReplay(state.stream, item)}
                            >
                              {isReplaying
                                ? <Loader2 className="mr-1.5 size-3 animate-spin" />
                                : <RotateCcw className="mr-1.5 size-3" />}
                              {isReplaying ? t('webhookDeliveriesReplayingButton') : t('webhookDeliveriesReplayButton')}
                            </Button>
                          )}
                        </div>
                      </div>
                      <div className="flex flex-wrap items-center gap-3 font-mono text-[11px] text-muted-foreground">
                        <span>{`events: ${item.acceptedEventCount ?? 0} · delivered: ${item.deliveredCount ?? 0}`}</span>
                        <span>{`lsn: ${item.checkpointLsn || '-'}`}</span>
                        <span>{`size: ${formatPayloadSize(item.payloadSizeBytes)}`}</span>
                      </div>
                      {item.errorSummary && (
                        <div className="rounded bg-muted/50 px-2 py-1 font-mono text-[11px] text-muted-foreground break-all">
                          {item.errorCode ? `[${item.errorCode}] ${item.errorSummary}` : item.errorSummary}
                        </div>
                      )}
                    </div>
                  )
                })}
              </div>
            ) : (
              <div className="rounded-md border px-3 py-6 text-center text-xs text-muted-foreground">
                {t('webhookDeliveriesEmpty')}
              </div>
            )}
          </div>
        </ScrollableDialogContent>
      )}
    </Dialog>
  )
}
