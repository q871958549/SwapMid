'use client'

import { useState } from 'react'
import { Check, Copy, History, Loader2 } from 'lucide-react'
import { useLocale, useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { StatusBadge } from '@/components/ui/status-badge'
import type { CdcLakehouseHistoryDto, CdcStreamDto } from '@/lib/api/types'

export interface CdcLakehouseHistoryState {
  stream: CdcStreamDto
  history: CdcLakehouseHistoryDto
}

function formatByteSize(bytes: number | null | undefined): string {
  const n = Number(bytes ?? 0)
  if (!Number.isFinite(n) || n <= 0) return '0 B'
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / (1024 * 1024)).toFixed(2)} MB`
}

export function CdcLakehouseHistoryDialog({
  state,
  loadingVersion,
  onSelectVersion,
  onClose,
}: {
  state: CdcLakehouseHistoryState | null
  loadingVersion: number | null
  onSelectVersion: (stream: CdcStreamDto, version: number) => void
  onClose: () => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const tc = useTranslations('Common')
  const locale = useLocale()
  const [copiedSql, setCopiedSql] = useState(false)

  const copySql = async (sql: string) => {
    await navigator.clipboard?.writeText(sql)
    setCopiedSql(true)
    setTimeout(() => setCopiedSql(false), 1800)
  }

  return (
    <Dialog open={!!state} onOpenChange={(open) => { if (!open) onClose() }}>
      {state && (
        <ScrollableDialogContent
          title={t('lakehouseHistoryDialogTitle', { name: state.stream.name })}
          description={state.history.summary ?? undefined}
          className="sm:max-w-3xl"
          footer={<Button variant="outline" onClick={onClose}>{tc('close')}</Button>}
        >
          <div className="space-y-4">
            <div className="grid grid-cols-3 gap-2.5">
              <div className="rounded-md border bg-muted/30 px-3 py-2">
                <div className="text-xs text-muted-foreground">{t('historyCurrentVersionLabel')}</div>
                <div className="mt-0.5 font-mono text-sm font-medium">
                  {state.history.currentVersion !== null && state.history.currentVersion !== undefined
                    ? `v${state.history.currentVersion}`
                    : '-'}
                </div>
              </div>
              <div className="rounded-md border bg-muted/30 px-3 py-2">
                <div className="text-xs text-muted-foreground">{t('historySelectedVersionLabel')}</div>
                <div className="mt-0.5 flex items-center gap-1.5 font-mono text-sm font-medium">
                  <History className="size-3.5 text-primary" />
                  <span>
                    {state.history.selectedVersion !== null && state.history.selectedVersion !== undefined
                      ? `v${state.history.selectedVersion}`
                      : '-'}
                  </span>
                </div>
              </div>
              <div className="rounded-md border bg-muted/30 px-3 py-2">
                <div className="text-xs text-muted-foreground">{t('historySnapshotStatsLabel')}</div>
                <div className="mt-0.5 font-mono text-sm font-medium">
                  {`${state.history.selectedActiveFileCount ?? 0} · ${state.history.selectedTotalRecordCount ?? 0} rows (${formatByteSize(state.history.selectedTotalBytes)})`}
                </div>
              </div>
            </div>

            {state.history.deltaLogDirectory && (
              <div className="font-mono text-xs text-muted-foreground break-all">
                {`Delta Log: ${state.history.deltaLogDirectory}`}
              </div>
            )}

            <div className="space-y-1.5">
              <span className="text-xs font-medium text-muted-foreground">{t('historyTimelineTitle')}</span>
              {state.history.commits && state.history.commits.length > 0 ? (
                <div className="divide-y rounded-md border text-xs">
                  {state.history.commits.map((commit) => {
                    const isSelected = commit.version === state.history.selectedVersion
                    const isLoading = loadingVersion === commit.version
                    return (
                      <button
                        key={commit.version}
                        type="button"
                        disabled={loadingVersion !== null}
                        onClick={() => onSelectVersion(state.stream, commit.version ?? 0)}
                        className={`flex w-full items-center justify-between gap-2 px-3 py-2 text-left transition-colors hover:bg-muted/40 ${isSelected ? 'bg-primary/5' : ''}`}
                      >
                        <div className="flex flex-wrap items-center gap-2">
                          <StatusBadge tone={isSelected ? 'info' : 'neutral'}>
                            {`v${commit.version}`}
                          </StatusBadge>
                          <span className="font-mono font-medium">{commit.operation}</span>
                          <span className="text-muted-foreground">
                            {`+${commit.addedFileCount ?? 0} / -${commit.removedFileCount ?? 0} · ${commit.totalRecordCount ?? 0} rows`}
                          </span>
                        </div>
                        <div className="flex items-center gap-2 font-mono text-[11px] text-muted-foreground">
                          {isLoading && <Loader2 className="size-3 animate-spin" />}
                          <span suppressHydrationWarning>
                            {commit.timestampUtc ? new Date(commit.timestampUtc).toLocaleString(locale) : '-'}
                          </span>
                        </div>
                      </button>
                    )
                  })}
                </div>
              ) : (
                <div className="rounded-md border px-3 py-4 text-center text-xs text-muted-foreground">
                  {t('historyEmptyCommits')}
                </div>
              )}
            </div>

            {state.history.activeFiles && state.history.activeFiles.length > 0 && (
              <div className="space-y-1.5">
                <span className="text-xs font-medium text-muted-foreground">
                  {t('historyActiveFilesTitle', { version: state.history.selectedVersion ?? 0 })}
                </span>
                <div className="divide-y rounded-md border text-xs">
                  {state.history.activeFiles.map(file => (
                    <div key={file.objectKey} className="grid grid-cols-12 items-center gap-2 px-3 py-1.5">
                      <span className="col-span-6 truncate font-mono font-medium" title={file.objectKey ?? undefined}>
                        {file.relativePath}
                      </span>
                      <span className="col-span-2 font-mono text-muted-foreground">{`v${file.addedAtVersion}`}</span>
                      <span className="col-span-2 font-mono text-muted-foreground">
                        {`${file.recordCount ?? 0} rows`}
                      </span>
                      <span className="col-span-2 text-right font-mono text-muted-foreground">
                        {formatByteSize(file.sizeBytes)}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {state.history.timeTravelQuerySql && (
              <div className="space-y-1.5">
                <div className="flex items-center justify-between gap-2">
                  <span className="text-xs font-medium text-muted-foreground">{t('historyQuerySqlTitle')}</span>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => void copySql(state.history.timeTravelQuerySql!)}
                  >
                    {copiedSql ? <Check className="mr-1.5 size-3.5" /> : <Copy className="mr-1.5 size-3.5" />}
                    {copiedSql ? t('copiedDdlButton') : t('copyQuerySqlButton')}
                  </Button>
                </div>
                <pre className="max-h-52 overflow-auto rounded-md border bg-muted/40 p-3 font-mono text-xs">
                  {state.history.timeTravelQuerySql}
                </pre>
              </div>
            )}
          </div>
        </ScrollableDialogContent>
      )}
    </Dialog>
  )
}
