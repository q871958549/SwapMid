'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { History, Loader2, Play, RefreshCw } from 'lucide-react'
import { ConfirmDialog } from '@/components/data-table/confirm-dialog'
import { DataTable } from '@/components/data-table/data-table'
import { PageHeader } from '@/components/data-table/page-header'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'
import { buildColumns, type ScheduledJob } from './columns'
import { DryRunDialog } from './dry-run-dialog'
import { buildArgsJson, isParamsValid, ParamFields } from './param-fields'
import { StatsCards, type ScheduledJobStats } from './stats-cards'

export type { ScheduledJob } from './columns'

export function ScheduledJobsClient({
  initial,
  initialStats,
}: {
  initial: ScheduledJob[]
  initialStats: ScheduledJobStats | null
}) {
  const t = useTranslations('System.scheduledJobs')
  const tc = useTranslations('Common')
  const sc = useTranslations('System.common')
  // CronHelper 组件自行调 useTranslations('System.cronHelper')；这里额外起一份同 namespace 实例
  // 只为传给纯函数 describeCron（cell 内联渲染，见 columns.tsx）——纯函数不能直接调 hook。
  const cronT = useTranslations('System.cronHelper')
  const [jobs, setJobs] = useState<ScheduledJob[]>(initial)
  const [stats, setStats] = useState<ScheduledJobStats | null>(initialStats)
  const [cronDraft, setCronDraft] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState<string | null>(null)
  const router = useRouter()
  const [refreshing, setRefreshing] = useState(false)
  // 危险任务「立即执行」前的二次确认目标（非危险任务不经此弹窗，直接执行）。
  const [confirmTarget, setConfirmTarget] = useState<ScheduledJob | null>(null)
  // 带参任务「立即执行」的参数收集目标（F7：有参数定义才弹此框，无参任务仍直接执行）。
  const [paramsTarget, setParamsTarget] = useState<ScheduledJob | null>(null)
  const [paramsValues, setParamsValues] = useState<Record<string, string>>({})
  // 「演练」目标（U3：入口收进「执行 ▾」下拉后弹窗受控化，由本组件持有）。
  const [dryRunTarget, setDryRunTarget] = useState<ScheduledJob | null>(null)

  const cronOf = (job: ScheduledJob) => cronDraft[job.key] ?? job.cron

  async function refresh() {
    setRefreshing(true)
    try {
      const [jobsRes, statsRes] = await Promise.all([
        fetch('/api/proxy/scheduled-jobs'),
        fetch('/api/proxy/scheduled-jobs/stats'),
      ])
      if (!jobsRes.ok) throw new Error(await jobsRes.text())
      setJobs((await jobsRes.json()) as ScheduledJob[])
      setCronDraft({})
      if (statsRes.ok) setStats((await statsRes.json()) as ScheduledJobStats)
    } catch (e) {
      console.error(e)
    } finally {
      setRefreshing(false)
    }
  }

  async function post(url: string, body?: unknown) {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body ? JSON.stringify(body) : undefined,
    })
    if (!res.ok) throw new Error(await res.text())
  }

  async function toggle(job: ScheduledJob, enabled: boolean) {
    setBusy(job.key)
    try {
      await post('/api/proxy/scheduled-jobs/set-enabled', { key: job.key, enabled, cron: cronOf(job) })
      toast({ variant: 'success', title: enabled ? t('toastEnabledTitle') : t('toastDisabledTitle'), description: job.displayName })
      await refresh(); router.refresh() // 本地刷 + 失效 Router Cache（jobs 来自服务端 initial，防切页签显旧态）
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('toastToggleFailedTitle'), description: getApiErrorMessage(e, t('toastToggleFailedFallback')) })
    } finally { setBusy(null) }
  }

  // 保存不改变启用状态（此前误写死 enabled:true，导致「保存」会顺带把停用任务悄悄启用）；
  // 停用任务保存=暂存草稿（后端 D3 方案 A），启用任务保存=同时更新 Hangfire 注册的 cron。
  async function saveCron(job: ScheduledJob) {
    setBusy(job.key)
    try {
      await post('/api/proxy/scheduled-jobs/set-enabled', { key: job.key, enabled: job.enabled, cron: cronOf(job) })
      toast({
        variant: 'success',
        title: t('toastCronSavedTitle'),
        description: job.enabled ? `${job.displayName}：${cronOf(job)}` : `${job.displayName}：${cronOf(job)}${t('toastCronSavedDisabledSuffix')}`,
      })
      await refresh(); router.refresh()
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('toastCronSaveFailedTitle'), description: getApiErrorMessage(e, t('toastCronSaveFailedFallback')) })
    } finally { setBusy(null) }
  }

  // 已接入统一执行管道的任务：正式立即执行走 run 端点（生成落库 + 广播 + 记历史）。
  // argsJson 留空=按 handler 默认值（如材料超耗率默认上月）；带参弹窗收集到值时经此传入。
  // 长任务（isLongRunning：报表实时查外部库 / 全量同步，实测材料超耗率单次 51s）传 runInBackground
  // 后台入队立即返回（F6），进度与结果看执行历史——同步等待会顶着 HTTP 超时；秒级任务仍同步直返摘要。
  async function runFormal(job: ScheduledJob, argsJson?: string | null) {
    setBusy(job.key)
    try {
      const res = await fetch('/api/proxy/scheduled-jobs/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          key: job.key,
          dryRun: false,
          argsJson: argsJson ?? null,
          runInBackground: job.isLongRunning,
        }),
      })
      if (!res.ok) throw new Error(await res.text())
      const result = (await res.json()) as {
        success: boolean
        summary: string | null
        error: string | null
        queued: boolean
        status?: number | null
      }
      if (!result.success) throw new Error(result.error ?? t('runFailedDefaultMessage'))
      toast(
        result.queued
          ? { title: t('toastQueuedTitle'), description: t('toastQueuedDescription', { name: job.displayName }) }
          : result.status === 4
            ? { variant: 'warning', title: t('statusLabels.skipped'), description: `${job.displayName}：${result.summary ?? t('toastRunSkippedDescriptionFallback')}` }
          : { variant: 'success', title: t('toastRunSuccessTitle'), description: `${job.displayName}：${result.summary ?? t('toastRunSuccessDescriptionFallback')}` }
      )
      await refresh(); router.refresh()
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('toastRunFailedTitle'), description: getApiErrorMessage(e, t('toastRunFailedFallback')) })
    } finally { setBusy(null) }
  }

  // 全部受管控任务均已接入统一执行管道；危险任务经 requestRun 先过确认弹窗再到这里。
  function runOne(job: ScheduledJob) {
    void runFormal(job)
  }

  // 「立即执行」入口：有参数定义先弹框收集（可留空走默认）；危险任务无参数时走二次确认；两者都没有就直接执行。
  function requestRun(job: ScheduledJob) {
    if (job.parameters.length > 0) { setParamsValues({}); setParamsTarget(job); return }
    if (job.isDestructive) { setConfirmTarget(job); return }
    runOne(job)
  }

  function confirmRun() {
    if (!confirmTarget) return
    const job = confirmTarget
    setConfirmTarget(null)
    runOne(job)
  }

  function submitParamsRun() {
    if (!paramsTarget) return
    const job = paramsTarget
    setParamsTarget(null)
    void runFormal(job, buildArgsJson(job.parameters, paramsValues))
  }

  const columns = buildColumns({
    cronDraft,
    setCronDraft,
    busy,
    onToggle: (job, enabled) => void toggle(job, enabled),
    onSaveCron: job => void saveCron(job),
    onRequestRun: requestRun,
    onRequestDryRun: setDryRunTarget,
    t,
    cronT,
  })

  return (
    <>
      <PageHeader
        title={t('pageTitle')}
        description={t('pageDescription')}
        extra={
          <Button asChild variant="outline" size="sm">
            <Link href="/system/scheduled-jobs/executions"><History className="mr-1.5 size-4" />{t('allExecutionHistory')}</Link>
          </Button>
        }
        action={
          <Button variant="outline" size="sm" disabled={refreshing} onClick={() => void refresh()}>
            {refreshing ? <Loader2 className="mr-1.5 size-4 animate-spin" /> : <RefreshCw className="mr-1.5 size-4" />}
            {sc('refresh')}
          </Button>
        }
      />
      <StatsCards stats={stats} />
      <DryRunDialog
        target={dryRunTarget}
        onClose={() => setDryRunTarget(null)}
        onDone={() => void refresh()}
      />
      <ConfirmDialog
        open={!!confirmTarget}
        onOpenChange={o => { if (!o) setConfirmTarget(null) }}
        title={t('confirmRunTitleTemplate', { name: confirmTarget?.displayName ?? '' })}
        description={
          <>
            {t('confirmRunDescriptionPrefix')}<strong>{t('confirmRunDescriptionDangerWord')}</strong>（{confirmTarget?.description ?? t('confirmRunDescriptionFallback')}），
            {t('confirmRunDescriptionSuffix')}
          </>
        }
        onConfirm={confirmRun}
        confirmText={t('confirmRunConfirmText')}
        destructive
        busy={busy === confirmTarget?.key}
      />
      <Dialog open={!!paramsTarget} onOpenChange={o => { if (!o) setParamsTarget(null) }}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>{t('paramsDialogTitleTemplate', { name: paramsTarget?.displayName ?? '' })}</DialogTitle>
            <DialogDescription>{t('paramsDialogDescription')}</DialogDescription>
          </DialogHeader>
          <div className="py-2">
            {paramsTarget && (
              <ParamFields
                parameters={paramsTarget.parameters}
                values={paramsValues}
                onChange={(name, value) => setParamsValues(p => ({ ...p, [name]: value }))}
              />
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setParamsTarget(null)}>{tc('cancel')}</Button>
            <Button
              onClick={submitParamsRun}
              disabled={!paramsTarget || !isParamsValid(paramsTarget.parameters, paramsValues)}
            >
              <Play className="mr-1.5 size-4" />{t('paramsDialogSubmit')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DataTable
        columns={columns}
        data={jobs}
        tableKey="scheduled-jobs"
        searchPlaceholder={t('searchPlaceholder')}
        showAuditColumns={false}
        enableExport={false}
      />
    </>
  )
}
