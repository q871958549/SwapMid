'use client'

import { useState } from 'react'
import { Check, Copy, History, Loader2, TableProperties } from 'lucide-react'
import { useLocale, useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { StatusBadge } from '@/components/ui/status-badge'
import type { CdcLakehouseHistoryDto, CdcLakehousePreviewDto, CdcStreamDto } from '@/lib/api/types'

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
  preview,
  previewLoading,
  onSelectVersion,
  onLoadPreview,
  onClose,
}: {
  state: CdcLakehouseHistoryState | null
  loadingVersion: number | null
  preview?: CdcLakehousePreviewDto | null
  previewLoading?: boolean
  onSelectVersion: (stream: CdcStreamDto, version: number) => void
  onLoadPreview?: (stream: CdcStreamDto, version: number) => void
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
      {state && (() => {
        const isIceberg = Number(state.history.tableFormat ?? state.stream.lakehouseTableFormat ?? 0) === 2
        return (
          <ScrollableDialogContent
            title={isIceberg
              ? t('icebergHistoryDialogTitle', { name: state.stream.name })
              : t('lakehouseHistoryDialogTitle', { name: state.stream.name })}
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
                <div className="flex flex-wrap items-center gap-2 font-mono text-xs text-muted-foreground break-all">
                  <span>{`${isIceberg ? 'Iceberg Metadata' : 'Delta Log'}: ${state.history.deltaLogDirectory}`}</span>
                  {isIceberg && Number(state.history.partitionStrategy ?? state.stream.lakehousePartitionStrategy ?? 0) === 1 && (
                    <StatusBadge tone="info">day(commit_timestamp_utc)</StatusBadge>
                  )}
                </div>
              )}

              <div className="space-y-1.5">
                <span className="text-xs font-medium text-muted-foreground">
                  {isIceberg ? t('icebergHistoryTimelineTitle') : t('historyTimelineTitle')}
                </span>
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

            {onLoadPreview && state.history.selectedVersion !== null && state.history.selectedVersion !== undefined && (
              <div className="space-y-2 rounded-md border bg-muted/20 p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-medium text-muted-foreground">
                      {t('lakehousePreviewSectionTitle', { version: state.history.selectedVersion })}
                    </span>
                    {preview && (
                      <StatusBadge tone={preview.rowCount && preview.rowCount > 0 ? 'success' : 'warning'}>
                        {`${preview.rowCount ?? 0} rows`}
                      </StatusBadge>
                    )}
                  </div>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={previewLoading || loadingVersion !== null}
                    onClick={() => onLoadPreview(state.stream, state.history.selectedVersion ?? 0)}
                  >
                    {previewLoading
                      ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                      : <TableProperties className="mr-1.5 size-3.5" />}
                    {previewLoading ? t('lakehousePreviewLoading') : t('lakehousePreviewButtonLabel')}
                  </Button>
                </div>
                {preview && (
                  <div className="space-y-2">
                    <p className="text-xs text-muted-foreground">{preview.summary}</p>
                    {preview.columns && preview.columns.length > 0 && preview.rows && preview.rows.length > 0 && (
                      <div className="max-h-60 overflow-auto rounded border bg-background">
                        <table className="w-full border-collapse text-left font-mono text-[11px]">
                          <thead className="sticky top-0 bg-muted/80">
                            <tr className="border-b">
                              {preview.columns.map(col => (
                                <th key={col} className="whitespace-nowrap px-2 py-1 font-medium text-muted-foreground">
                                  {col}
                                </th>
                              ))}
                            </tr>
                          </thead>
                          <tbody className="divide-y">
                            {preview.rows.map((row, rowIdx) => (
                              <tr key={rowIdx} className="hover:bg-muted/30">
                                {preview.columns!.map(col => {
                                  const cell = row[col]
                                  return (
                                    <td
                                      key={col}
                                      className="max-w-[240px] truncate whitespace-nowrap px-2 py-1"
                                      title={cell ?? ''}
                                    >
                                      {cell ?? '-'}
                                    </td>
                                  )
                                })}
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )}
                  </div>
                )}
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
        )
      })()}
    </Dialog>
  )
}
