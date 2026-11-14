'use client'

import { useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import {
  CheckCircle2, AlertTriangle, XCircle, HelpCircle, RefreshCw, Loader2,
  Lightbulb, TrendingDown, type LucideIcon,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { PageHeader } from '@/components/data-table/page-header'
import { cn } from '@/lib/utils'

// 与后端 HealthStatus 枚举一致（序列化为数字）：Healthy=0 Warning=1 Critical=2 Unknown=3。
export interface HealthItemDto {
  name: string
  status: number
  conclusion: string
  impact?: string | null
  suggestion?: string | null
  detail?: string | null
}

export interface SystemHealthDto {
  overallStatus: number
  overallConclusion: string
  checkedAt: string
  items: HealthItemDto[]
}

interface StatusStyle {
  label: string
  icon: LucideIcon
  dot: string          // 状态色点
  badgeBg: string      // 卡片状态徽标背景
  badgeText: string
  cardBorder: string   // 卡片左侧强调边
  bannerBg: string     // 顶部总状态横幅背景
  bannerBorder: string
  bannerText: string
  bannerIcon: string
}

// 四态配色（浅/深色都可读）。绿=正常、黄=需留意、红=异常、灰=未知。
// 模块级常量含 UI 文案（label），改为由持有 useTranslations 的组件调用的工厂函数（纯函数不能直接调 hook）。
function getStatusStyles(t: ReturnType<typeof useTranslations>): Record<number, StatusStyle> {
  return {
    0: {
      label: t('statusLabels.healthy'), icon: CheckCircle2,
      dot: 'bg-success',
      badgeBg: 'bg-success-soft ', badgeText: 'text-success ',
      cardBorder: 'border-l-success/40',
      bannerBg: 'bg-success-soft ', bannerBorder: 'border-success/40 ',
      bannerText: 'text-success ', bannerIcon: 'text-success ',
    },
    1: {
      label: t('statusLabels.warning'), icon: AlertTriangle,
      dot: 'bg-warning',
      badgeBg: 'bg-warning-soft ', badgeText: 'text-warning ',
      cardBorder: 'border-l-warning/40',
      bannerBg: 'bg-warning-soft ', bannerBorder: 'border-warning/40 ',
      bannerText: 'text-warning ', bannerIcon: 'text-warning ',
    },
    2: {
      label: t('statusLabels.critical'), icon: XCircle,
      dot: 'bg-destructive',
      badgeBg: 'bg-destructive/10 ', badgeText: 'text-destructive ',
      cardBorder: 'border-l-destructive/40',
      bannerBg: 'bg-destructive/10 ', bannerBorder: 'border-destructive/40 ',
      bannerText: 'text-destructive ', bannerIcon: 'text-destructive ',
    },
    3: {
      label: t('statusLabels.unknown'), icon: HelpCircle,
      dot: 'bg-muted-foreground',
      badgeBg: 'bg-muted', badgeText: 'text-muted-foreground',
      cardBorder: 'border-l-border',
      bannerBg: 'bg-muted', bannerBorder: 'border-border',
      bannerText: 'text-foreground', bannerIcon: 'text-muted-foreground',
    },
  }
}

function formatTime(iso: string, locale: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleString(locale, { hour12: false })
}

export function SystemHealthClient({ initial }: { initial: SystemHealthDto }) {
  const t = useTranslations('System.health')
  const sc = useTranslations('System.common')
  const locale = useLocale()
  const STATUS_STYLES = getStatusStyles(t)
  const styleOf = (status: number): StatusStyle => STATUS_STYLES[status] ?? STATUS_STYLES[3]
  const [data, setData] = useState<SystemHealthDto>(initial)
  const [loading, setLoading] = useState(false)

  async function refresh() {
    setLoading(true)
    try {
      const r = await fetch('/api/proxy/system-health', { cache: 'no-store' })
      if (r.ok) {
        setData(await r.json())
      }
    } catch (e) {
      console.error('[system-health refresh]', e)
    } finally {
      setLoading(false)
    }
  }

  const overall = styleOf(data.overallStatus)
  const OverallIcon = overall.icon

  // 分状态统计，给管理者一个一眼概览。
  const counts = { critical: 0, warning: 0, healthy: 0, unknown: 0 }
  for (const it of data.items) {
    if (it.status === 2) counts.critical++
    else if (it.status === 1) counts.warning++
    else if (it.status === 0) counts.healthy++
    else counts.unknown++
  }

  return (
    <>
      <PageHeader
        title={t('pageTitle')}
        description={t('pageDescription')}
        action={
          <Button onClick={() => void refresh()} disabled={loading} variant="outline">
            {loading ? <Loader2 className="mr-1.5 size-4 animate-spin" /> : <RefreshCw className="mr-1.5 size-4" />}
            {sc('refresh')}
          </Button>
        }
      />

      {/* 顶部总状态横幅：整体状态文案按后端数字枚举（overallStatus）判断，不比较已翻译的 label 字符串——
          翻译后 overall.label 不再是固定的中文字面量，逻辑分支须锚定枚举数值。 */}
      <div className={cn('mb-6 flex items-start gap-4 rounded-xl border p-5', overall.bannerBg, overall.bannerBorder)}>
        <OverallIcon className={cn('mt-0.5 size-10 shrink-0', overall.bannerIcon)} />
        <div className="min-w-0 flex-1">
          <div className={cn('text-lg font-semibold', overall.bannerText)}>
            {data.overallStatus === 0 ? t('bannerOverallHealthy') : data.overallStatus === 3 ? t('bannerOverallUnknown') : t('bannerOverallTemplate', { status: overall.label })}
          </div>
          <p className={cn('mt-1 text-sm', overall.bannerText, 'opacity-90')}>{data.overallConclusion}</p>
          <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
            <span>{t('checkedAtLabel', { time: formatTime(data.checkedAt, locale) })}</span>
            {counts.critical > 0 && <span className="text-destructive">{t('countCriticalTemplate', { count: counts.critical })}</span>}
            {counts.warning > 0 && <span className="text-warning">{t('countWarningTemplate', { count: counts.warning })}</span>}
            {counts.healthy > 0 && <span className="text-success">{t('countHealthyTemplate', { count: counts.healthy })}</span>}
            {counts.unknown > 0 && <span>{t('countUnknownTemplate', { count: counts.unknown })}</span>}
          </div>
        </div>
      </div>

      {/* 检查项卡片网格：item.name/conclusion/impact/suggestion/detail 是后端体检服务生成的检查结果文案（业务数据），不翻译，只翻 UI 标签/前缀。 */}
      {data.items.length === 0 ? (
        <Card>
          <CardContent className="py-10 text-center text-sm text-muted-foreground">
            {t('emptyState')}
          </CardContent>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
          {data.items.map((item) => {
            const s = styleOf(item.status)
            const ItemIcon = s.icon
            return (
              <Card key={item.name} className={cn('gap-3 border-l-4 py-4', s.cardBorder)}>
                <CardContent className="space-y-2.5">
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex min-w-0 items-center gap-2">
                      <span className={cn('size-2.5 shrink-0 rounded-full', s.dot)} aria-hidden />
                      <span className="truncate font-medium">{item.name}</span>
                    </div>
                    <span className={cn('flex shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium', s.badgeBg, s.badgeText)}>
                      <ItemIcon className="size-3.5" />
                      {s.label}
                    </span>
                  </div>

                  <p className="text-sm text-foreground/90">
                    {item.conclusion}
                    {item.detail && <span className="ml-1 text-xs text-muted-foreground">（{item.detail}）</span>}
                  </p>

                  {item.impact && (
                    <div className="flex items-start gap-1.5 text-xs text-muted-foreground">
                      <TrendingDown className="mt-0.5 size-3.5 shrink-0" />
                      <span><span className="font-medium">{t('impactLabel')}</span>{item.impact}</span>
                    </div>
                  )}
                  {item.suggestion && (
                    <div className="flex items-start gap-1.5 text-xs text-muted-foreground">
                      <Lightbulb className="mt-0.5 size-3.5 shrink-0" />
                      <span><span className="font-medium">{t('suggestionLabel')}</span>{item.suggestion}</span>
                    </div>
                  )}
                </CardContent>
              </Card>
            )
          })}
        </div>
      )}
    </>
  )
}
