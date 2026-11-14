import { useTranslations } from 'next-intl'
import { AlertTriangle, CheckCircle2, Loader2 } from 'lucide-react'
import Link from 'next/link'
import { cn } from '@/lib/utils'

export interface ScheduledJobStats {
  enabledCount: number
  totalCount: number
  failed24hCount: number
  failed24hStartedFrom?: string
  failed24hMeasuredAt?: string
  runningCount: number
}

export function buildFailedExecutionsHref(windowStartedAt: string, windowEndedAt: string) {
  const params = new URLSearchParams({ status: '2', windowStartedAt, windowEndedAt })
  return `/system/scheduled-jobs/executions?${params.toString()}`
}

/** 管控页顶部统计卡（U1）：启用数 / 近 24h 失败数 / 执行中数一眼可见。 */
export function StatsCards({ stats }: { stats: ScheduledJobStats | null }) {
  const t = useTranslations('System.scheduledJobs.statsCards')
  const tiles = [
    {
      key: 'enabled',
      label: t('enabled'),
      value: stats ? `${stats.enabledCount} / ${stats.totalCount}` : '—',
      icon: CheckCircle2,
      tone: 'text-success ',
    },
    {
      key: 'failed24h',
      label: t('failed24h'),
      value: stats ? String(stats.failed24hCount) : '—',
      icon: AlertTriangle,
      tone: stats && stats.failed24hCount > 0 ? 'text-destructive' : 'text-muted-foreground',
      href: stats?.failed24hStartedFrom && stats.failed24hMeasuredAt
        ? buildFailedExecutionsHref(stats.failed24hStartedFrom, stats.failed24hMeasuredAt)
        : undefined,
    },
    {
      key: 'running',
      label: t('running'),
      value: stats ? String(stats.runningCount) : '—',
      icon: Loader2,
      tone: 'text-primary',
      spin: !!stats && stats.runningCount > 0,
    },
  ]

  return (
    <div className="mb-4 grid shrink-0 grid-cols-3 gap-3">
      {tiles.map(tile => {
        const content = (
          <>
            <tile.icon className={cn('size-5 shrink-0', tile.tone, tile.spin && 'animate-spin')} />
            <div className="min-w-0">
              <p className="text-muted-foreground text-xs">{tile.label}</p>
              <p className={cn('text-lg font-semibold tabular-nums', tile.tone)}>{tile.value}</p>
            </div>
          </>
        )

        return tile.href ? (
          <Link
            key={tile.key}
            href={tile.href}
            className="flex items-center gap-3 rounded-lg border p-3 transition-colors hover:bg-muted/50 focus-visible:border-ring focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color) focus-visible:outline-none"
          >
            {content}
          </Link>
        ) : (
          <div key={tile.key} className="flex items-center gap-3 rounded-lg border p-3">
            {content}
          </div>
        )
      })}
    </div>
  )
}
