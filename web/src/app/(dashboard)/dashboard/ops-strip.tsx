import Link from 'next/link'
import { getTranslations } from 'next-intl/server'
import { HeartPulse, History, MessageSquare } from 'lucide-react'
import { apiFetch, type PagedResult } from '@/lib/api-client'

// 「平台运行」快照条（管理员）：体检红绿灯 + 定时任务失败数 + 近 7 天失败消息数，点击直达详情页。
// 与「我的待办」不同的显隐哲学：待办=行动项（无事不扰、全 0 隐藏），运营=状态监控（正常也要可见、常显绿灯）。
// 三项各按权限独立门控，全无权限整条不渲染；数据面全部复用现有接口（体检/定时任务/消息记录），零新后端。

/** /system-health 的轻量视图（完整 DTO 在体检页模块内，跨模块不引，此处只取用到的字段） */
interface SystemHealthLite {
  /** 0 健康 / 1 警告 / 2 严重 / 3 未知 */
  overallStatus: number
}

interface ScheduledJobLite {
  lastJobState: string | null
}

type DashboardT = Awaited<ReturnType<typeof getTranslations<'Dashboard'>>>

function getHealthStyle(t: DashboardT): Record<number, { label: string; dot: string; text: string }> {
  return {
    0: { label: t('ops.healthNormal'), dot: 'bg-success', text: 'text-success ' },
    1: { label: t('ops.healthWarning'), dot: 'bg-warning', text: 'text-warning ' },
    2: { label: t('ops.healthCritical'), dot: 'bg-destructive', text: 'text-destructive ' },
    3: { label: t('ops.healthUnknown'), dot: 'bg-muted-foreground/40', text: 'text-muted-foreground' },
  }
}

async function loadOps(grantedPolicies: Record<string, boolean>, t: DashboardT) {
  const canHealth = grantedPolicies['SwapMid.SystemManagement.SystemHealth'] === true
  const canJobs = grantedPolicies['SwapMid.SystemManagement.ScheduledJobs'] === true
  const canMessages = grantedPolicies['SwapMid.MessageBus.Records'] === true
  if (!canHealth && !canJobs && !canMessages) return null

  const weekAgo = new Date(Date.now() - 7 * 86_400_000).toISOString()
  const [health, jobs, failedMessages] = await Promise.all([
    canHealth
      ? apiFetch<SystemHealthLite>('/system-health').catch(() => null)
      : Promise.resolve(null),
    canJobs
      ? apiFetch<ScheduledJobLite[]>('/scheduled-jobs').catch(() => null)
      : Promise.resolve(null),
    canMessages
      ? apiFetch<PagedResult<unknown>>(`/message-records?status=2&startTime=${encodeURIComponent(weekAgo)}&maxResultCount=1`)
          .then((r) => r.totalCount).catch(() => null)
      : Promise.resolve(null),
  ])
  const healthStyles = getHealthStyle(t)
  return {
    healthStyle: health ? (healthStyles[health.overallStatus] ?? healthStyles[3]) : null,
    failedJobs: jobs ? jobs.filter((j) => j.lastJobState === 'Failed').length : null,
    failedMessages,
  }
}

export async function OpsStrip({ grantedPolicies }: { grantedPolicies: Record<string, boolean> }) {
  const t = await getTranslations('Dashboard')
  const ops = await loadOps(grantedPolicies, t)
  if (!ops) return null
  const { healthStyle, failedJobs, failedMessages } = ops

  // 三项全取数失败（接口异常）也不渲染，避免一排「未知」噪音
  if (!healthStyle && failedJobs === null && failedMessages === null) return null

  const pill = 'flex items-center gap-2 rounded-lg border bg-card px-3.5 py-2 text-sm transition-colors hover:bg-muted/60'
  const alertPill = 'flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/5 px-3.5 py-2 text-sm transition-colors hover:bg-destructive/10'

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="mr-1 text-xs text-muted-foreground">{t('ops.platformStatus')}</span>

      {healthStyle && (
        <Link href="/system/health" className={pill}>
          <HeartPulse className="size-4 shrink-0 text-muted-foreground" />
          <span className="text-xs text-muted-foreground">{t('ops.systemHealth')}</span>
          <span className={`size-2 rounded-full ${healthStyle.dot}`} />
          <span className={`text-xs font-medium ${healthStyle.text}`}>{healthStyle.label}</span>
        </Link>
      )}

      {failedJobs !== null && (
        <Link href="/system/scheduled-jobs" className={failedJobs > 0 ? alertPill : pill}>
          <History className={failedJobs > 0 ? 'size-4 shrink-0 text-destructive' : 'size-4 shrink-0 text-muted-foreground'} />
          <span className={failedJobs > 0 ? 'text-xs text-destructive/90' : 'text-xs text-muted-foreground'}>
            {failedJobs > 0 ? t('ops.jobsFailed', { count: failedJobs }) : t('ops.jobsNormal')}
          </span>
        </Link>
      )}

      {failedMessages !== null && (
        <Link href="/message-bus/records" className={failedMessages > 0 ? alertPill : pill}>
          <MessageSquare className={failedMessages > 0 ? 'size-4 shrink-0 text-destructive' : 'size-4 shrink-0 text-muted-foreground'} />
          <span className={failedMessages > 0 ? 'text-xs text-destructive/90' : 'text-xs text-muted-foreground'}>
            {failedMessages > 0 ? t('ops.messagesFailed', { count: failedMessages }) : t('ops.messagesNormal')}
          </span>
        </Link>
      )}
    </div>
  )
}

export function OpsStripSkeleton() {
  return (
    <div className="flex gap-2">
      {[0, 1].map((i) => (
        <div key={i} className="h-[34px] w-32 animate-pulse rounded-lg border bg-muted/40" />
      ))}
    </div>
  )
}
