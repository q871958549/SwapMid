'use client'

import { useTranslations } from 'next-intl'
import { StatusBadge as UiStatusBadge } from '@/components/ui/status-badge'

/** 后端 ScheduledJobExecutionDto（枚举按 ABP 默认序列化为数字）。 */
export interface JobExecution {
  id: string
  jobKey: string | null
  jobType: string
  jobMethod: string | null
  triggerKind: number // 0=定时 1=手动
  status: number // 0=执行中 1=成功 2=失败 3=中断 4=已跳过
  startedAt: string
  finishedAt: string | null
  durationMs: number | null
  summary: string | null
  error: string | null
  isDryRun: boolean
  argsJson: string | null
  /** 操作人（手动触发时的当前用户）；定时触发 / 老轨记录为空。 */
  creatorId: string | null
}

/** 与后端 JobExecutionStatus/JobTriggerKind 数值枚举语义一致，供两种执行历史视图复用。 */
export const fmt = (value: string | null) => (value ? value.replace('T', ' ').slice(0, 19) : '—')

/** 纯函数不能直接调 hook，触发方式文案由已持有 scheduledJobs 翻译器的调用方传入。 */
export const triggerText = (kind: number, t: ReturnType<typeof useTranslations>) =>
  kind === 0 ? t('triggerLabels.scheduled') : t('triggerLabels.manual')

export const durationText = (ms: number | null) =>
  ms == null ? '—' : ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(1)}s`

const MinimumSuccessfulDurationSamples = 3

/** 最近窗口内成功记录的耗时中位数；少于 3 个有效样本时不建立易误导的异常阈值。 */
export function successfulDurationMedian(rows: JobExecution[], isDryRun?: boolean): number | null {
  const durations = rows
    .filter((row) =>
      row.status === 1 &&
      row.durationMs != null &&
      row.durationMs >= 0 &&
      (isDryRun == null || row.isDryRun === isDryRun))
    .map((row) => row.durationMs as number)
    .sort((a, b) => a - b)

  if (durations.length < MinimumSuccessfulDurationSamples) return null
  const middle = Math.floor(durations.length / 2)
  return durations.length % 2 === 0
    ? (durations[middle - 1] + durations[middle]) / 2
    : durations[middle]
}

export function isDurationAnomaly(durationMs: number | null, medianMs: number | null) {
  return durationMs != null && medianMs != null && medianMs > 0 && durationMs > medianMs * 2
}

export function StatusBadge({ status }: { status: number }) {
  const t = useTranslations('System.scheduledJobs')
  if (status === 1) return <UiStatusBadge tone="success">{t('statusLabels.succeeded')}</UiStatusBadge>
  if (status === 2) return <UiStatusBadge tone="danger">{t('statusLabels.failed')}</UiStatusBadge>
  if (status === 3) return <UiStatusBadge tone="warning">{t('statusLabels.interrupted')}</UiStatusBadge>
  if (status === 4) return <UiStatusBadge tone="neutral">{t('statusLabels.skipped')}</UiStatusBadge>
  if (status === 0) return <UiStatusBadge tone="info">{t('statusLabels.running')}</UiStatusBadge>
  return <UiStatusBadge tone="draft">{t('statusLabels.unknown', { value: status })}</UiStatusBadge>
}
