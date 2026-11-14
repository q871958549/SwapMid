'use client'

import type { Dispatch, SetStateAction } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import type { useTranslations } from 'next-intl'
import { AlertTriangle, FlaskConical, Loader2, Play, Save } from 'lucide-react'
import { CronHelper, describeCron } from '@/components/system/cron-helper'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { DropdownButton } from '@/components/data-table/dropdown-button'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import { fmtUtc, fmtUtcFull } from '@/lib/date-format'
import { cn } from '@/lib/utils'
import { ExecutionHistoryButton } from './execution-history-dialog'

/** 与后端 JobParameterType 枚举同序：0=文本 / 1=某天(yyyy-MM-dd) / 2=某月(yyyy-MM)。 */
export interface JobParameterSchema {
  name: string
  label: string
  type: number
  required: boolean
  defaultHint: string | null
}

export interface ScheduledJob {
  key: string
  displayName: string
  description: string | null
  cron: string
  enabled: boolean
  /** 是否已接入统一执行管道（恒 true——全部受管控任务均已迁移）；禁用态下按钮是否仍可点全靠这个。 */
  supportsParameterizedRun: boolean
  /** 带参执行支持的参数定义（F7，前端据此动态渲染表单）；未接入管道或无参任务为空数组。 */
  parameters: JobParameterSchema[]
  /** 危险操作（物理删除数据 / 写外部生产系统）：行内标红 + 立即执行前二次确认。 */
  isDestructive: boolean
  /** 长任务（报表实时查外部库 / 全量同步，数十秒到分钟级）：「立即执行」走后台入队（F6），进度看执行历史。 */
  isLongRunning: boolean
  /** 运行时互斥组；同组任务不会并发执行。只读展示，空值表示仅按自身任务键防重复。 */
  mutexGroup?: string | null
  lastExecution: string | null
  nextExecution: string | null
  lastJobState: string | null
}

function StateBadge({ state }: { state: string | null }) {
  if (!state) return <span className="text-xs text-muted-foreground">—</span>
  const variant = state === 'Succeeded' ? 'success' : state === 'Failed' ? 'destructive' : 'secondary'
  return <Badge variant={variant} className="text-xs">{state}</Badge>
}

export interface ScheduledJobColumnDeps {
  cronDraft: Record<string, string>
  setCronDraft: Dispatch<SetStateAction<Record<string, string>>>
  /** 正在执行操作的任务 key（该行按钮禁用 + 转圈）。 */
  busy: string | null
  onToggle: (job: ScheduledJob, enabled: boolean) => void
  onSaveCron: (job: ScheduledJob) => void
  /** 「立即执行」点击：危险任务先弹二次确认，非危险任务直接执行（父组件按 supportsParameterizedRun 分流 run/trigger）。 */
  onRequestRun: (job: ScheduledJob) => void
  /** 「演练」点击：父组件持有演练目标、渲染受控 DryRunDialog（U3——触发入口在下拉菜单里，菜单项点击即卸载，弹窗不能内嵌自管）。 */
  onRequestDryRun: (job: ScheduledJob) => void
  /** 列定义是纯函数、不能直接调 hook，由持有 useTranslations('System.scheduledJobs') 的父组件传入。 */
  t: ReturnType<typeof useTranslations>
  /** describeCron 绑定 System.cronHelper namespace（与上面 t 的 scheduledJobs 不同）,同理由父组件传入。 */
  cronT: ReturnType<typeof useTranslations>
}

/** 列定义依赖父组件的行内编辑状态与操作回调，故以工厂形式导出；cronDraft 变化时随渲染重建闭包。 */
export function buildColumns({
  cronDraft,
  setCronDraft,
  busy,
  onToggle,
  onSaveCron,
  onRequestRun,
  onRequestDryRun,
  t,
  cronT,
}: ScheduledJobColumnDeps): ColumnDef<ScheduledJob, unknown>[] {
  const cronOf = (job: ScheduledJob) => cronDraft[job.key] ?? job.cron

  return [
    {
      accessorKey: 'enabled',
      size: 70,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.enabled')} />,
      meta: {
        pin: 'left' as const,
        filterVariant: 'select' as const,
        filterOptions: [
          { value: true, label: t('columns.enabledOn') },
          { value: false, label: t('columns.enabledOff') },
        ],
      },
      cell: ({ row }) => (
        <Switch
          checked={row.original.enabled}
          disabled={busy === row.original.key}
          onCheckedChange={v => onToggle(row.original, v)}
        />
      ),
    },
    {
      id: 'job',
      // 拼上描述与任务键，让全局搜索能按三者任一命中；排序按拼串（以显示名开头，等效按显示名排）。
      accessorFn: row => `${row.displayName} ${row.description ?? ''} ${row.key}`,
      enableHiding: false,
      meta: { pin: 'left' as const },
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.job')} />,
      cell: ({ row }) => (
        <div>
          <div className="flex items-center gap-1.5">
            <span className="font-medium">{row.original.displayName}</span>
            {row.original.isDestructive && (
              <Badge variant="warning" className="h-4 gap-0.5 px-1 text-[10px] font-normal">
                <AlertTriangle className="size-2.5" />{t('columns.destructiveBadge')}
              </Badge>
            )}
          </div>
          <div className="mt-0.5 max-w-[22rem] text-xs text-muted-foreground">{row.original.description}</div>
          <div className="mt-0.5 flex flex-wrap items-center gap-1.5">
            <span className="font-mono text-[10px] text-muted-foreground/60">{row.original.key}</span>
            {row.original.mutexGroup && (
              <Badge variant="outline" className="h-4 px-1 text-[10px] font-normal text-muted-foreground">
                {t('columns.mutexGroupBadge', { group: row.original.mutexGroup })}
              </Badge>
            )}
          </div>
        </div>
      ),
    },
    {
      accessorKey: 'cron',
      enableSorting: false,
      size: 190,
      header: ({ column }) => <DataTableColumnHeader column={column} title="Cron" />,
      cell: ({ row }) => {
        const job = row.original
        const value = cronOf(job)
        const dirty = value !== job.cron
        return (
          <div>
            <div className="flex items-center gap-1">
              <Input
                value={value}
                onChange={e => setCronDraft(p => ({ ...p, [job.key]: e.target.value }))}
                className={cn('h-7 w-32 font-mono text-xs', dirty && 'border-primary')}
                placeholder={t('columns.cronPlaceholder')}
              />
              <CronHelper value={value} onApply={c => setCronDraft(p => ({ ...p, [job.key]: c }))} />
            </div>
            <p className="text-muted-foreground/70 mt-1 text-[11px]">{describeCron(value, cronT)}</p>
          </div>
        )
      },
    },
    {
      accessorKey: 'lastExecution',
      size: 130,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.lastExecution')} />,
      cell: ({ row }) => (
        <span className="text-xs text-muted-foreground" title={fmtUtcFull(row.original.lastExecution)}>
          {fmtUtc(row.original.lastExecution)}
        </span>
      ),
    },
    {
      accessorKey: 'nextExecution',
      size: 130,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.nextExecution')} />,
      cell: ({ row }) => (
        <span className="text-xs text-muted-foreground" title={row.original.enabled ? fmtUtcFull(row.original.nextExecution) : undefined}>
          {row.original.enabled ? fmtUtc(row.original.nextExecution) : '—'}
        </span>
      ),
    },
    {
      accessorKey: 'lastJobState',
      size: 100,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.lastState')} />,
      meta: {
        filterVariant: 'select' as const,
        filterOptions: [
          { value: 'Succeeded', label: 'Succeeded' },
          { value: 'Failed', label: 'Failed' },
        ],
      },
      cell: ({ row }) => <StateBadge state={row.original.lastJobState} />,
    },
    {
      id: 'actions',
      size: 230,
      enableHiding: false,
      cell: ({ row }) => {
        const job = row.original
        const isBusy = busy === job.key
        const dirty = cronOf(job) !== job.cron
        return (
          <div className="flex items-center justify-end gap-1.5">
            {dirty && (
              <Button
                size="sm"
                variant="outline"
                className="h-7"
                disabled={isBusy}
                onClick={() => onSaveCron(job)}
                title={job.enabled ? undefined : t('columns.saveDisabledTitle')}
              >
                <Save className="mr-1 size-3.5" />{t('columns.save')}
              </Button>
            )}
            {/* U3：立即执行 + 演练收敛为「执行 ▾」下拉（保存是 dirty 时才出现的上下文动作、历史是查看类，均保留独立按钮）。 */}
            <DropdownButton
              className="h-7"
              disabled={isBusy || (!job.enabled && !job.supportsParameterizedRun)}
              icon={isBusy ? <Loader2 className="size-3.5 animate-spin" /> : <Play className="size-3.5" />}
              label={isBusy ? t('columns.executing') : t('columns.execute')}
              items={<>
                <DropdownMenuItem onClick={() => onRequestRun(job)}>
                  <Play className="mr-1.5 size-3.5" />{t('columns.runNow')}
                </DropdownMenuItem>
                {job.supportsParameterizedRun && (
                  <DropdownMenuItem onClick={() => onRequestDryRun(job)}>
                    <FlaskConical className="mr-1.5 size-3.5" />{t('columns.dryRun')}
                  </DropdownMenuItem>
                )}
              </>}
            />
            <ExecutionHistoryButton jobKey={job.key} jobName={job.displayName} disabled={isBusy} />
          </div>
        )
      },
    },
  ]
}
