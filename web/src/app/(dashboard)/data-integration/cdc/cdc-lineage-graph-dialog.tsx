'use client'

import { useMemo, useState } from 'react'
import { ArrowRight, Download, Loader2, Network, RefreshCw, Search } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { StatusBadge, type StatusTone } from '@/components/ui/status-badge'
import { toast } from '@/components/ui/use-toast'
import { readApiErrorMessage } from '@/lib/api-error'
import type {
  IntegrationLineageGraphDto,
  IntegrationLineageImpactDto,
  IntegrationLineageNodeDto,
  OpenLineageExportDto,
} from '@/lib/api/types'

function statusTone(status: string | null | undefined): StatusTone {
  switch ((status ?? '').toLowerCase()) {
    case 'active':
      return 'success'
    case 'draft':
    case 'disabled':
      return 'neutral'
    case 'paused':
    case 'driftdetected':
      return 'warning'
    case 'breached':
    case 'error':
      return 'danger'
    default:
      return 'info'
  }
}

function riskTone(risk: string | null | undefined): StatusTone {
  switch ((risk ?? '').toLowerCase()) {
    case 'high':
      return 'danger'
    case 'medium':
      return 'warning'
    default:
      return 'success'
  }
}

const STAGE_COLUMNS = [
  { key: 'sources', kinds: ['DataSource'], labelKey: 'lineageStageSources' },
  { key: 'engines', kinds: ['DataPipeline', 'CdcStream'], labelKey: 'lineageStageEngines' },
  { key: 'mesh', kinds: ['LakehouseTable', 'MessageTopic'], labelKey: 'lineageStageMesh' },
  { key: 'consumers', kinds: ['MessageSubscription', 'ApiGroup', 'ApiRoute'], labelKey: 'lineageStageConsumers' },
] as const

export function CdcLineageGraphDialog({
  open,
  graph,
  loading,
  onRefresh,
  onClose,
}: {
  open: boolean
  graph: IntegrationLineageGraphDto | null
  loading: boolean
  onRefresh: () => void
  onClose: () => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const tc = useTranslations('Common')
  const [search, setSearch] = useState('')
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [impact, setImpact] = useState<IntegrationLineageImpactDto | null>(null)
  const [impactLoading, setImpactLoading] = useState(false)
  const [exportingOl, setExportingOl] = useState(false)

  const impactedColumnEdges = useMemo(
    () => (impact?.impactedEdges ?? []).filter(e => (e.columnMappings?.length ?? 0) > 0),
    [impact],
  )

  const handleExportOpenLineage = async () => {
    setExportingOl(true)
    try {
      const res = await fetch('/api/proxy/integration-lineage/openlineage')
      if (!res.ok) {
        const msg = await readApiErrorMessage(res, t('lineageExportOpenLineageFailedTitle'))
        toast({ variant: 'destructive', title: t('lineageExportOpenLineageFailedTitle'), description: msg })
        return
      }
      const data = await res.json() as OpenLineageExportDto
      const blob = new Blob([data.contentJson ?? '{}'], { type: 'application/json;charset=utf-8' })
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = `swapmid-openlineage-v${data.specVersion ?? '2-0-2'}.json`
      anchor.click()
      URL.revokeObjectURL(url)
      toast({
        variant: 'success',
        title: t('lineageExportOpenLineageSuccessTitle'),
        description: data.summary ?? undefined,
      })
    } finally {
      setExportingOl(false)
    }
  }

  const impactedIdSet = useMemo(() => {
    const set = new Set<string>()
    if (impact?.rootNodeId) set.add(impact.rootNodeId)
    for (const item of impact?.impactedNodes ?? []) {
      if (item.node?.id) set.add(item.node.id)
    }
    return set
  }, [impact])

  const filteredNodes = useMemo(() => {
    const all = graph?.nodes ?? []
    const q = search.trim().toLowerCase()
    if (!q) return all
    return all.filter(n =>
      (n.name ?? '').toLowerCase().includes(q) ||
      (n.kind ?? '').toLowerCase().includes(q) ||
      (n.subType ?? '').toLowerCase().includes(q),
    )
  }, [graph, search])

  const handleInspectNode = async (node: IntegrationLineageNodeDto) => {
    if (!node.id) return
    setSelectedNodeId(node.id)
    setImpactLoading(true)
    try {
      const qs = new URLSearchParams({
        nodeId: node.id,
        direction: 'both',
        maxDepth: '5',
      })
      const res = await fetch(`/api/proxy/integration-lineage/impact?${qs.toString()}`)
      if (!res.ok) {
        const msg = await readApiErrorMessage(res, t('lineageImpactFailedTitle'))
        toast({ variant: 'destructive', title: t('lineageImpactFailedTitle'), description: msg })
        return
      }
      setImpact(await res.json() as IntegrationLineageImpactDto)
    } finally {
      setImpactLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => { if (!next) onClose() }}>
      <ScrollableDialogContent
        title={t('lineageDialogTitle')}
        description={t('lineageDialogDesc')}
        className="sm:max-w-4xl"
        footer={(
          <div className="flex w-full items-center justify-between gap-2">
            <span className="font-mono text-xs text-muted-foreground">
              {graph ? graph.summary : ''}
            </span>
            <div className="flex items-center gap-2">
              <Button variant="outline" disabled={loading || exportingOl} onClick={() => void handleExportOpenLineage()}>
                {exportingOl
                  ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                  : <Download className="mr-1.5 size-3.5" />}
                {t('lineageExportOpenLineageButton')}
              </Button>
              <Button variant="outline" disabled={loading} onClick={onRefresh}>
                {loading
                  ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                  : <RefreshCw className="mr-1.5 size-3.5" />}
                {tc('refreshData')}
              </Button>
              <Button variant="outline" onClick={onClose}>{tc('close')}</Button>
            </div>
          </div>
        )}
      >
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="relative max-w-xs flex-1">
              <Search className="pointer-events-none absolute left-2.5 top-2 size-3.5 text-muted-foreground" />
              <Input
                value={search}
                onChange={e => setSearch(e.target.value)}
                placeholder={t('lineageSearchPlaceholder')}
                className="h-8 pl-8 text-xs"
              />
            </div>
            <div className="flex flex-wrap items-center gap-1.5">
              <StatusBadge tone="info">
                {t('lineageNodeCountBadge', { count: graph?.nodeCount ?? 0 })}
              </StatusBadge>
              <StatusBadge tone="neutral">
                {t('lineageEdgeCountBadge', { count: graph?.edgeCount ?? 0 })}
              </StatusBadge>
            </div>
          </div>

          {loading && !graph ? (
            <div className="flex items-center justify-center py-12 text-sm text-muted-foreground">
              <Loader2 className="mr-2 size-4 animate-spin" />
              {tc('processing')}
            </div>
          ) : (filteredNodes.length === 0 ? (
            <EmptyState
              icon={<Network className="size-8 text-muted-foreground" />}
              title={t('lineageEmptyTitle')}
              description={t('lineageEmptyDesc')}
            />
          ) : (
            <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
              {STAGE_COLUMNS.map((col) => {
                const colNodes = filteredNodes.filter(n =>
                  (col.kinds as readonly string[]).includes(n.kind ?? ''),
                )
                return (
                  <div key={col.key} className="space-y-2 rounded-md border bg-muted/20 p-2.5">
                    <div className="flex items-center justify-between border-b pb-1.5 text-xs font-medium">
                      <span>{t(col.labelKey)}</span>
                      <span className="font-mono text-muted-foreground">{colNodes.length}</span>
                    </div>
                    <div className="max-h-64 space-y-1.5 overflow-auto pr-0.5">
                      {colNodes.length === 0 ? (
                        <div className="py-4 text-center text-xs text-muted-foreground">-</div>
                      ) : colNodes.map((node) => {
                        const isSelected = selectedNodeId === node.id
                        const isRelated = impactedIdSet.size > 0 && node.id && impactedIdSet.has(node.id)
                        return (
                          <button
                            key={node.id}
                            type="button"
                            onClick={() => void handleInspectNode(node)}
                            className={`w-full rounded border bg-background p-2 text-left text-xs transition-colors hover:border-primary ${
                              isSelected
                                ? 'border-primary ring-1 ring-primary'
                                : (isRelated ? 'border-primary/60' : 'border-border')
                            }`}
                          >
                            <div className="flex items-center justify-between gap-1">
                              <span className="truncate font-mono font-medium" title={node.name ?? ''}>
                                {node.name}
                              </span>
                              <StatusBadge tone={statusTone(node.status)}>{node.status}</StatusBadge>
                            </div>
                            <div className="mt-0.5 truncate font-mono text-[11px] text-muted-foreground">
                              {`${node.kind}${node.subType ? ` · ${node.subType}` : ''}`}
                            </div>
                          </button>
                        )
                      })}
                    </div>
                  </div>
                )
              })}
            </div>
          ))}

          {(impactLoading || impact) && (
            <div className="space-y-2.5 rounded-md border bg-muted/20 p-3">
              {impactLoading ? (
                <div className="flex items-center gap-2 text-xs text-muted-foreground">
                  <Loader2 className="size-3.5 animate-spin" />
                  {t('lineageAnalyzingText')}
                </div>
              ) : impact && (
                <>
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-xs font-medium">{t('lineageImpactResultTitle')}</span>
                      <span className="font-mono text-xs">{`${impact.rootNodeName} (${impact.rootNodeKind})`}</span>
                      <StatusBadge tone={riskTone(impact.riskLevel)}>
                        {t('lineageRiskBadge', { level: (impact.riskLevel ?? 'low').toUpperCase() })}
                      </StatusBadge>
                    </div>
                    <div className="flex items-center gap-1.5 font-mono text-xs text-muted-foreground">
                      <span>{t('lineageUpstreamCount', { count: impact.upstreamCount ?? 0 })}</span>
                      <span>·</span>
                      <span>{t('lineageDownstreamCount', { count: impact.downstreamCount ?? 0 })}</span>
                    </div>
                  </div>

                  {(impact.impactedNodes?.length ?? 0) > 0 && (
                    <div className="max-h-44 divide-y overflow-auto rounded border bg-background text-xs">
                      {impact.impactedNodes!.map((item, idx) => (
                        <div key={`${item.node?.id}-${idx}`} className="flex flex-wrap items-center justify-between gap-2 px-3 py-1.5">
                          <div className="flex flex-wrap items-center gap-1.5">
                            <StatusBadge tone={item.direction === 'downstream' ? 'warning' : 'info'}>
                              {`${item.direction === 'downstream' ? '↓' : '↑'} d${item.depth}`}
                            </StatusBadge>
                            <span className="font-mono font-medium">{item.node?.name}</span>
                            <span className="font-mono text-muted-foreground">{item.node?.kind}</span>
                          </div>
                          <div className="flex items-center gap-1 font-mono text-[11px] text-muted-foreground">
                            {(item.pathLabels ?? []).map((seg, sIdx) => (
                              <span key={`${seg}-${sIdx}`} className="inline-flex items-center gap-1">
                                {sIdx > 0 && <ArrowRight className="size-3" />}
                                {seg}
                              </span>
                            ))}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}

                  {impactedColumnEdges.length > 0 && (
                    <div className="space-y-1.5 rounded border bg-background px-3 py-2 text-xs">
                      <div className="font-medium text-muted-foreground">{t('lineageColumnMappingsTitle')}</div>
                      <div className="flex flex-wrap gap-1.5">
                        {impactedColumnEdges.flatMap((edge, eIdx) =>
                          (edge.columnMappings ?? []).map((col, cIdx) => (
                            <StatusBadge key={`${eIdx}-${cIdx}-${col.sourceColumn}-${col.targetColumn}`} tone="info">
                              {col.sourceColumn === col.targetColumn
                                ? `${col.targetColumn} (${col.transformType})`
                                : `${col.sourceColumn} → ${col.targetColumn} (${col.transformType})`}
                            </StatusBadge>
                          )),
                        )}
                      </div>
                    </div>
                  )}

                  {(impact.recommendations?.length ?? 0) > 0 && (
                    <ul className="space-y-1 rounded border bg-background px-3 py-2 text-xs text-muted-foreground">
                      {impact.recommendations!.map((rec, rIdx) => (
                        <li key={rIdx}>{`• ${rec}`}</li>
                      ))}
                    </ul>
                  )}
                </>
              )}
            </div>
          )}
        </div>
      </ScrollableDialogContent>
    </Dialog>
  )
}
