'use client'

import { useState } from 'react'
import { Check, Copy, Download, FileCode2, GitCompare, Loader2, Play } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { StatusBadge, type StatusTone } from '@/components/ui/status-badge'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { toast } from '@/components/ui/use-toast'
import { readApiErrorMessage } from '@/lib/api-error'
import type {
  IntegrationBundleApplyResultDto,
  IntegrationBundleDiffResultDto,
  IntegrationBundleExportResultDto,
} from '@/lib/api/types'

function actionTone(action: string | null | undefined): StatusTone {
  switch ((action ?? '').toLowerCase()) {
    case 'create':
    case 'created':
      return 'success'
    case 'update':
    case 'updated':
      return 'info'
    case 'needssecret':
    case 'skipped':
      return 'warning'
    case 'failed':
      return 'danger'
    default:
      return 'neutral'
  }
}

export function CdcGitOpsBundleDialog({
  open,
  canManage,
  onApplied,
  onClose,
}: {
  open: boolean
  canManage: boolean
  onApplied: () => Promise<void>
  onClose: () => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const tc = useTranslations('Common')

  const [format, setFormat] = useState<'yaml' | 'json'>('yaml')
  const [content, setContent] = useState('')
  const [resourceCount, setResourceCount] = useState<number | null>(null)
  const [allowUpdateExisting, setAllowUpdateExisting] = useState(false)
  const [busy, setBusy] = useState<'export' | 'diff' | 'dryRun' | 'apply' | null>(null)
  const [copied, setCopied] = useState(false)
  const [diffResult, setDiffResult] = useState<IntegrationBundleDiffResultDto | null>(null)
  const [applyResult, setApplyResult] = useState<IntegrationBundleApplyResultDto | null>(null)

  const handleExport = async (targetFormat: 'yaml' | 'json' = format) => {
    setBusy('export')
    setApplyResult(null)
    try {
      const res = await fetch(`/api/proxy/integration-bundles/export?format=${targetFormat}`)
      if (!res.ok) {
        const msg = await readApiErrorMessage(res, t('gitOpsExportFailed'))
        toast({ variant: 'destructive', title: t('gitOpsExportFailed'), description: msg })
        return
      }
      const data = await res.json() as IntegrationBundleExportResultDto
      setContent(data.content ?? '')
      setResourceCount(data.resourceCount ?? 0)
      setDiffResult(null)
    } finally {
      setBusy(null)
    }
  }

  const handleDiff = async () => {
    if (!content.trim()) return
    setBusy('diff')
    setApplyResult(null)
    try {
      const res = await fetch('/api/proxy/integration-bundles/diff', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content }),
      })
      if (!res.ok) {
        const msg = await readApiErrorMessage(res, t('gitOpsDiffFailed'))
        toast({ variant: 'destructive', title: t('gitOpsDiffFailed'), description: msg })
        return
      }
      setDiffResult(await res.json() as IntegrationBundleDiffResultDto)
    } finally {
      setBusy(null)
    }
  }

  const handleApply = async (dryRun: boolean) => {
    if (!content.trim()) return
    setBusy(dryRun ? 'dryRun' : 'apply')
    try {
      const res = await fetch('/api/proxy/integration-bundles/apply', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content, dryRun, allowUpdateExisting }),
      })
      if (!res.ok) {
        const msg = await readApiErrorMessage(res, t('gitOpsApplyFailed'))
        toast({ variant: 'destructive', title: t('gitOpsApplyFailed'), description: msg })
        return
      }
      const data = await res.json() as IntegrationBundleApplyResultDto
      setApplyResult(data)
      if (!dryRun) {
        toast({
          variant: data.failedCount && data.failedCount > 0 ? 'destructive' : 'success',
          title: t('gitOpsApplyDoneTitle'),
          description: data.summary ?? undefined,
        })
        await onApplied()
      }
    } finally {
      setBusy(null)
    }
  }

  const handleCopy = async () => {
    if (!content) return
    await navigator.clipboard?.writeText(content)
    setCopied(true)
    setTimeout(() => setCopied(false), 1800)
  }

  const handleDownload = () => {
    if (!content) return
    const ext = format === 'json' ? 'json' : 'yaml'
    const blob = new Blob([content], { type: format === 'json' ? 'application/json' : 'text/yaml' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `swapmid-bundle.${ext}`
    a.click()
    URL.revokeObjectURL(url)
  }

  const resultItems = applyResult?.items ?? diffResult?.items ?? []

  return (
    <Dialog open={open} onOpenChange={(next) => { if (!next) onClose() }}>
      <ScrollableDialogContent
        title={t('gitOpsDialogTitle')}
        description={t('gitOpsDialogDesc')}
        className="sm:max-w-3xl"
        footer={(
          <div className="flex w-full flex-wrap items-center justify-between gap-2">
            {canManage ? (
              <label className="flex items-center gap-2 text-xs text-muted-foreground">
                <Switch
                  checked={allowUpdateExisting}
                  onCheckedChange={setAllowUpdateExisting}
                  disabled={busy !== null}
                />
                {t('gitOpsAllowUpdateLabel')}
              </label>
            ) : <span />}
            <div className="flex items-center gap-2">
              <Button variant="outline" onClick={onClose}>{tc('close')}</Button>
              <Button
                variant="outline"
                disabled={busy !== null || !content.trim()}
                onClick={() => void handleDiff()}
              >
                {busy === 'diff'
                  ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                  : <GitCompare className="mr-1.5 size-3.5" />}
                {t('gitOpsDiffButton')}
              </Button>
              {canManage && (
                <>
                  <Button
                    variant="outline"
                    disabled={busy !== null || !content.trim()}
                    onClick={() => void handleApply(true)}
                  >
                    {busy === 'dryRun' && <Loader2 className="mr-1.5 size-3.5 animate-spin" />}
                    {t('gitOpsDryRunButton')}
                  </Button>
                  <Button
                    disabled={busy !== null || !content.trim()}
                    onClick={() => void handleApply(false)}
                  >
                    {busy === 'apply'
                      ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                      : <Play className="mr-1.5 size-3.5" />}
                    {t('gitOpsApplyButton')}
                  </Button>
                </>
              )}
            </div>
          </div>
        )}
      >
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <Select
                value={format}
                onValueChange={v => setFormat(v === 'json' ? 'json' : 'yaml')}
              >
                <SelectTrigger className="h-8 w-[120px]"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="yaml">YAML</SelectItem>
                  <SelectItem value="json">JSON</SelectItem>
                </SelectContent>
              </Select>
              <Button
                size="sm"
                variant="outline"
                disabled={busy !== null}
                onClick={() => void handleExport(format)}
              >
                {busy === 'export'
                  ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                  : <FileCode2 className="mr-1.5 size-3.5" />}
                {t('gitOpsExportButton')}
              </Button>
              {resourceCount !== null && (
                <StatusBadge tone="info">{t('gitOpsResourceCountBadge', { count: resourceCount })}</StatusBadge>
              )}
            </div>
            <div className="flex items-center gap-2">
              <Button size="sm" variant="outline" disabled={!content} onClick={() => void handleCopy()}>
                {copied ? <Check className="mr-1.5 size-3.5" /> : <Copy className="mr-1.5 size-3.5" />}
                {copied ? t('gitOpsCopiedButton') : t('gitOpsCopyButton')}
              </Button>
              <Button size="sm" variant="outline" disabled={!content} onClick={handleDownload}>
                <Download className="mr-1.5 size-3.5" />
                {t('gitOpsDownloadButton')}
              </Button>
            </div>
          </div>

          <Textarea
            value={content}
            onChange={e => setContent(e.target.value)}
            placeholder={t('gitOpsBundlePlaceholder')}
            className="min-h-[240px] font-mono text-xs"
          />

          {(applyResult || diffResult) && (
            <div className="space-y-2 rounded-md border bg-muted/20 p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="text-xs font-medium">
                  {applyResult
                    ? (applyResult.dryRun ? t('gitOpsDryRunResultTitle') : t('gitOpsApplyResultTitle'))
                    : t('gitOpsDiffResultTitle')}
                </span>
                <span className="font-mono text-xs text-muted-foreground">
                  {applyResult?.summary ?? diffResult?.summary}
                </span>
              </div>
              {resultItems.length > 0 && (
                <div className="max-h-52 divide-y overflow-auto rounded border bg-background text-xs">
                  {resultItems.map((item, idx) => (
                    <div key={`${item.kind}-${item.name}-${idx}`} className="flex flex-wrap items-center justify-between gap-2 px-3 py-1.5">
                      <div className="flex items-center gap-2">
                        <StatusBadge tone={actionTone(item.action)}>{item.action}</StatusBadge>
                        <span className="font-mono text-muted-foreground">{item.kind}</span>
                        <span className="font-mono font-medium">{item.name}</span>
                      </div>
                      {item.note && (
                        <span className="text-muted-foreground">{item.note}</span>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </ScrollableDialogContent>
    </Dialog>
  )
}
