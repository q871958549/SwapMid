'use client'

import { useState } from 'react'
import { GitMerge, Loader2, Tag, Trash2 } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { StatusBadge } from '@/components/ui/status-badge'
import { Switch } from '@/components/ui/switch'
import { toast } from '@/components/ui/use-toast'
import { readApiErrorMessage } from '@/lib/api-error'
import type {
  CdcLakehouseBranchPromoteResultDto,
  CdcLakehouseExpireResultDto,
  CdcLakehouseRefDto,
  CdcLakehouseTagResultDto,
  CdcStreamDto,
} from '@/lib/api/types'

export function CdcIcebergMaintenanceSection({
  stream,
  selectedVersion,
  refs,
  canManage,
  onMutated,
}: {
  stream: CdcStreamDto
  selectedVersion: number | null | undefined
  refs: CdcLakehouseRefDto[]
  canManage: boolean
  onMutated: () => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const [tagName, setTagName] = useState('')
  const [promoteAuditTag, setPromoteAuditTag] = useState('')
  const [retainLast, setRetainLast] = useState('2')
  const [deleteOrphans, setDeleteOrphans] = useState(false)
  const [busy, setBusy] = useState<'tag' | 'dryRun' | 'expire' | 'promote' | null>(null)
  const [expireResult, setExpireResult] = useState<CdcLakehouseExpireResultDto | null>(null)

  const mainRef = refs.find(r => r.name === 'main')
  const pendingBranches = refs.filter(
    r => r.type === 'branch' && r.name && r.name !== 'main' && r.snapshotId !== mainRef?.snapshotId,
  )

  const handlePromoteBranch = async (branchName: string) => {
    setBusy('promote')
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${stream.id}/promote-lakehouse-branch`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          branchName,
          requireContractPass: true,
          createAuditTagName: promoteAuditTag.trim() || null,
          dryRun: false,
        }),
      })
      if (!res.ok) {
        const msg = await readApiErrorMessage(res, t('icebergPromoteFailedTitle'))
        toast({ variant: 'destructive', title: t('icebergPromoteFailedTitle'), description: msg })
        return
      }
      const data = await res.json() as CdcLakehouseBranchPromoteResultDto
      toast({ variant: 'success', title: t('icebergPromoteSuccessTitle'), description: data.message ?? undefined })
      setPromoteAuditTag('')
      onMutated()
    } finally {
      setBusy(null)
    }
  }

  const handleCreateTag = async () => {
    const trimmed = tagName.trim()
    if (!trimmed) return
    setBusy('tag')
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${stream.id}/lakehouse-tags`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          tagName: trimmed,
          snapshotVersion: selectedVersion ?? null,
        }),
      })
      if (!res.ok) {
        const msg = await readApiErrorMessage(res, t('icebergTagFailedTitle'))
        toast({ variant: 'destructive', title: t('icebergTagFailedTitle'), description: msg })
        return
      }
      const data = await res.json() as CdcLakehouseTagResultDto
      toast({ variant: 'success', title: t('icebergTagSuccessTitle'), description: data.message ?? undefined })
      setTagName('')
      onMutated()
    } finally {
      setBusy(null)
    }
  }

  const handleExpire = async (dryRun: boolean) => {
    const parsedRetain = Number.parseInt(retainLast, 10)
    const clampedRetain = Number.isFinite(parsedRetain) ? Math.min(100, Math.max(1, parsedRetain)) : 2
    setBusy(dryRun ? 'dryRun' : 'expire')
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${stream.id}/expire-lakehouse-snapshots`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          retainLast: clampedRetain,
          olderThanMinutes: 0,
          deleteOrphanedFiles: deleteOrphans,
          dryRun,
        }),
      })
      if (!res.ok) {
        const msg = await readApiErrorMessage(res, t('icebergExpireFailedTitle'))
        toast({ variant: 'destructive', title: t('icebergExpireFailedTitle'), description: msg })
        return
      }
      const data = await res.json() as CdcLakehouseExpireResultDto
      setExpireResult(data)
      if (!dryRun) {
        toast({ variant: 'success', title: t('icebergExpireSuccessTitle'), description: data.summary ?? undefined })
        onMutated()
      }
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="space-y-3 rounded-md border bg-muted/20 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-xs font-medium text-muted-foreground">{t('icebergRefsSectionTitle')}</span>
        <div className="flex flex-wrap items-center gap-1.5">
          {refs.length > 0 ? refs.map(r => (
            <StatusBadge key={r.name} tone={r.type === 'branch' ? 'info' : 'success'}>
              {`${r.type === 'branch' ? 'branch' : 'tag'}:${r.name}${r.version ? ` · v${r.version}` : ''}`}
            </StatusBadge>
          )) : (
            <span className="text-xs text-muted-foreground">-</span>
          )}
        </div>
      </div>

      {stream.lakehouseMaintenanceCron && (
        <div className="flex flex-wrap items-center justify-between gap-2 rounded border bg-background px-2.5 py-1.5 text-xs">
          <span className="font-mono text-muted-foreground">
            {`${t('lakehouseMaintenanceCronFieldLabel')}: ${stream.lakehouseMaintenanceCron}`}
          </span>
          {stream.lakehouseLastMaintenanceSummary && (
            <span className="text-muted-foreground">{stream.lakehouseLastMaintenanceSummary}</span>
          )}
        </div>
      )}

      {canManage && pendingBranches.length > 0 && (
        <div className="space-y-2 border-t pt-2.5">
          {pendingBranches.map(branch => (
            <div key={branch.name} className="flex flex-wrap items-center justify-between gap-2 rounded border bg-background px-2.5 py-2">
              <div className="flex flex-wrap items-center gap-1.5 text-xs">
                <StatusBadge tone="warning">
                  {`branch:${branch.name}${branch.version ? ` · v${branch.version}` : ''} → main`}
                </StatusBadge>
                <Input
                  value={promoteAuditTag}
                  onChange={e => setPromoteAuditTag(e.target.value)}
                  placeholder={t('icebergPromoteTagPlaceholder')}
                  className="h-7 max-w-[165px] font-mono text-xs"
                />
              </div>
              <Button
                size="sm"
                variant="outline"
                disabled={busy !== null}
                onClick={() => void handlePromoteBranch(branch.name!)}
              >
                {busy === 'promote'
                  ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                  : <GitMerge className="mr-1.5 size-3.5" />}
                {t('icebergPromoteBranchButton')}
              </Button>
            </div>
          ))}
        </div>
      )}

      {canManage && (
        <div className="space-y-2.5 border-t pt-2.5">
          <div className="flex flex-wrap items-center gap-2">
            <Input
              value={tagName}
              onChange={e => setTagName(e.target.value)}
              placeholder="audit-2026-11"
              className="h-8 max-w-[200px] font-mono text-xs"
            />
            <Button
              size="sm"
              variant="outline"
              disabled={busy !== null || !tagName.trim()}
              onClick={() => void handleCreateTag()}
            >
              {busy === 'tag'
                ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                : <Tag className="mr-1.5 size-3.5" />}
              {t('icebergPinTagButton')}
            </Button>
            <span className="text-xs text-muted-foreground">
              {selectedVersion ? t('icebergPinTagTargetHint', { version: selectedVersion }) : ''}
            </span>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex flex-wrap items-center gap-2 text-xs">
              <span className="text-muted-foreground">{t('icebergRetainLastLabel')}</span>
              <Input
                type="number"
                min={1}
                max={100}
                value={retainLast}
                onChange={e => setRetainLast(e.target.value)}
                className="h-8 w-[70px] font-mono text-xs"
              />
              <label className="flex items-center gap-1.5 text-muted-foreground">
                <Switch checked={deleteOrphans} onCheckedChange={setDeleteOrphans} disabled={busy !== null} />
                {t('icebergDeleteOrphansLabel')}
              </label>
            </div>
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant="outline"
                disabled={busy !== null}
                onClick={() => void handleExpire(true)}
              >
                {busy === 'dryRun' && <Loader2 className="mr-1.5 size-3.5 animate-spin" />}
                {t('icebergExpireDryRunButton')}
              </Button>
              <Button
                size="sm"
                variant="destructive-outline"
                disabled={busy !== null}
                onClick={() => void handleExpire(false)}
              >
                {busy === 'expire'
                  ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                  : <Trash2 className="mr-1.5 size-3.5" />}
                {t('icebergExpireExecuteButton')}
              </Button>
            </div>
          </div>

          {expireResult && (
            <div className="rounded border bg-background px-3 py-2 text-xs text-muted-foreground">
              {expireResult.summary}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
