'use client'

import { useEffect, useState } from 'react'
import { useTranslations } from 'next-intl'
import { Wand2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { fmtUtc } from '@/lib/date-format'

// 定时任务 cron 助手：标准 5 段（分 时 日 月 周，Hangfire/Cronos 口径）。
// 常用模板一键套用 + 按模式可视化生成 + 当前表达式描述（随语言翻译，namespace System.cronHelper）。

/** describeCron/getWeekdayNames/getPresets 均为纯函数（非组件、非 hook），文案由持有
 * useTranslations('System.cronHelper') 的调用方传入——本文件内的 CronHelper 组件自行调用，
 * 外部消费方（system/scheduled-jobs/columns.tsx 的 describeCron(value, cronT)）由其父组件
 * client.tsx 新增一个同 namespace 的 hook 实例传入，二者互不影响。 */
type CronHelperT = ReturnType<typeof useTranslations>

function getWeekdayNames(t: CronHelperT): string[] {
  return [
    t('weekdaySunday'), t('weekdayMonday'), t('weekdayTuesday'), t('weekdayWednesday'),
    t('weekdayThursday'), t('weekdayFriday'), t('weekdaySaturday'),
  ]
}

/** 常用模板：除「每小时整点」外，标签全部复用 describeCron 同款模板函数生成——
 * 单一措辞来源，避免按钮标签与动态描述文本各写一份、日后措辞漂移。 */
function getPresets(t: CronHelperT): { label: string; cron: string }[] {
  const names = getWeekdayNames(t)
  return [
    { label: t('descEveryMinute'), cron: '* * * * *' },
    { label: t('descEveryNMinutes', { n: 5 }), cron: '*/5 * * * *' },
    { label: t('descEveryNMinutes', { n: 30 }), cron: '*/30 * * * *' },
    { label: t('presetHourly'), cron: '0 * * * *' },
    { label: t('descEveryDayAt', { time: '02:00' }), cron: '0 2 * * *' },
    { label: t('descEveryDayAt', { time: '08:30' }), cron: '30 8 * * *' },
    { label: t('descEveryWeekdayAt', { days: names[1], time: '08:00' }), cron: '0 8 * * 1' },
    { label: t('descEveryMonthDayAt', { day: 1, time: '00:00' }), cron: '0 0 1 * *' },
  ]
}

function pad2(n: number) { return String(n).padStart(2, '0') }

// 常见形态的描述；识别不了的归为"自定义表达式"（不报错，Hangfire 侧才是最终裁判）
export function describeCron(expr: string, t: CronHelperT): string {
  const parts = expr.trim().split(/\s+/)
  if (parts.length !== 5) return t('descErrorFormat')
  const [min, hour, dom, month, dow] = parts

  const everyN = /^\*\/(\d+)$/.exec(min)
  if (everyN && hour === '*' && dom === '*' && month === '*' && dow === '*') return t('descEveryNMinutes', { n: everyN[1] })
  if (min === '*' && hour === '*' && dom === '*' && month === '*' && dow === '*') return t('descEveryMinute')

  const m = /^(\d+)$/.exec(min)
  const h = /^(\d+)$/.exec(hour)
  if (m && hour === '*' && dom === '*' && month === '*' && dow === '*') return t('descEveryHourAtMinute', { minute: m[1] })
  if (m && h) {
    const time = `${pad2(Number(h[1]))}:${pad2(Number(m[1]))}`
    if (dom === '*' && month === '*' && dow === '*') return t('descEveryDayAt', { time })
    if (dom === '*' && month === '*' && /^[0-6](,[0-6])*$/.test(dow)) {
      const names = getWeekdayNames(t)
      const days = dow.split(',').map(d => names[Number(d)]).join(t('listJoiner'))
      return t('descEveryWeekdayAt', { days, time })
    }
    if (/^\d+$/.test(dom) && month === '*' && dow === '*') return t('descEveryMonthDayAt', { day: dom, time })
  }
  return t('descCustom')
}

/** 未来执行时间预览：debounce 请求后端 preview-cron（后端同时兼做 cron 语法校验，无效表达式回填 error）。
 * t 由 NextRunsPreview 传入（非直接调 useTranslations——本 hook 只在该组件内使用，避免重复实例化）。 */
function useNextRuns(cron: string, active: boolean, t: CronHelperT) {
  const [state, setState] = useState<{ loading: boolean; times: string[]; error: string | null }>({
    loading: false, times: [], error: null,
  })

  useEffect(() => {
    // 不满足条件时不发起请求，也不必同步清空状态——消费方 NextRunsPreview 在 cron 为空时本就不渲染，
    // active=false（弹层关闭）多数情况下组件已随 Radix Popover 卸载，state 自然丢弃。
    if (!active || !cron.trim()) return
    let cancelled = false
    // loading 状态延到 timer 回调里才置位（而非效果体顶层同步 setState）——避开
    // react-hooks/set-state-in-effect，顺带也不会在快速输入期间每次按键都闪一下"计算中"。
    const timer = setTimeout(() => {
      if (cancelled) return
      setState(p => ({ ...p, loading: true, error: null }))
      fetch(`/api/proxy/scheduled-jobs/preview-cron?cron=${encodeURIComponent(cron.trim())}`)
        .then(async res => {
          if (!res.ok) throw new Error(await res.text())
          return (await res.json()) as string[]
        })
        .then(times => { if (!cancelled) setState({ loading: false, times, error: null }) })
        .catch(() => { if (!cancelled) setState({ loading: false, times: [], error: t('invalidExpression') }) })
    }, 400)
    return () => { cancelled = true; clearTimeout(timer) }
  }, [cron, active, t])

  return state
}

function NextRunsPreview({ cron, active, t }: { cron: string; active: boolean; t: CronHelperT }) {
  const { loading, times, error } = useNextRuns(cron, active, t)
  if (!cron.trim()) return null
  if (loading) return <p className="text-muted-foreground mt-1 text-[11px]">{t('computingNextRuns')}</p>
  if (error) return <p className="text-destructive mt-1 text-[11px]">{error}</p>
  if (times.length === 0) return null
  return (
    <p className="text-muted-foreground mt-1 text-[11px]">
      {t('nextRuns', { times: times.map(fmtUtc).join(t('listJoiner')) })}
    </p>
  )
}

type Mode = 'minutes' | 'hourly' | 'daily' | 'weekly' | 'monthly'

export function CronHelper({ value, onApply }: { value: string; onApply: (cron: string) => void }) {
  const t = useTranslations('System.cronHelper')
  const [open, setOpen] = useState(false)
  const [mode, setMode] = useState<Mode>('daily')
  const [everyMinutes, setEveryMinutes] = useState(5)
  const [time, setTime] = useState('02:00')
  const [weekday, setWeekday] = useState('1')
  const [dayOfMonth, setDayOfMonth] = useState(1)
  const weekdayNames = getWeekdayNames(t)
  const presets = getPresets(t)

  function compose(): string {
    const [hh = '0', mm = '0'] = time.split(':')
    const h = Number(hh), m = Number(mm)
    switch (mode) {
      case 'minutes': return `*/${Math.min(59, Math.max(1, everyMinutes))} * * * *`
      case 'hourly': return `${m} * * * *`
      case 'daily': return `${m} ${h} * * *`
      case 'weekly': return `${m} ${h} * * ${weekday}`
      case 'monthly': return `${m} ${h} ${Math.min(31, Math.max(1, dayOfMonth))} * *`
    }
  }

  function apply(cron: string) {
    onApply(cron)
    setOpen(false)
  }

  const preview = compose()

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button size="sm" variant="ghost" className="h-7 px-2 text-xs" title={t('tooltip')}>
          <Wand2 className="size-3.5" />
        </Button>
      </PopoverTrigger>
      {/* 面板比触发点上、下剩余空间都高时，翻转仍会顶出视口。可用高度由定位器写入，
          封顶后中间滚动；当前表达式与「应用」贴在滚动区两端，矮屏也能点到。 */}
      <PopoverContent
        align="end"
        collisionPadding={12}
        className="w-80 max-h-[var(--radix-popover-content-available-height)] overflow-y-auto overscroll-contain p-0"
      >
        <div className="bg-popover sticky top-0 z-10 space-y-1 border-b px-3 py-2.5">
          <p className="text-sm font-medium">{t('panelTitle')}</p>
          <p className="text-muted-foreground break-all text-xs">
            {t('currentLabel')}<code className="bg-muted rounded px-1 font-mono">{value || '—'}</code> · {describeCron(value, t)}
          </p>
          <NextRunsPreview cron={value} active={open} t={t} />
        </div>

        <div className="space-y-3 px-3 py-2.5">
          <div className="space-y-1.5">
            <Label className="text-xs">{t('presetsLabel')}</Label>
            {/* 单列：标签+完整表达式并排展示不截断——双列下长表达式会溢出按钮。
                注意：JSX 注释内禁写 cron 原文，星号+斜杠组合会提前终止注释块 */}
            <div className="grid grid-cols-1 gap-1">
              {presets.map(p => (
                <Button key={p.cron} size="sm" variant="outline" className="h-7 w-full justify-between gap-2 px-2 text-xs" onClick={() => apply(p.cron)}>
                  <span className="shrink-0">{p.label}</span>
                  <code className="text-muted-foreground font-mono text-[10px]">{p.cron}</code>
                </Button>
              ))}
            </div>
          </div>

          <div className="space-y-2 rounded-md border p-2.5">
            <Label className="text-xs">{t('modeGenLabel')}</Label>
            <Select value={mode} onValueChange={v => setMode(v as Mode)}>
              <SelectTrigger className="h-8 text-sm"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="minutes">{t('modeMinutes')}</SelectItem>
                <SelectItem value="hourly">{t('modeHourly')}</SelectItem>
                <SelectItem value="daily">{t('modeDaily')}</SelectItem>
                <SelectItem value="weekly">{t('modeWeekly')}</SelectItem>
                <SelectItem value="monthly">{t('modeMonthly')}</SelectItem>
              </SelectContent>
            </Select>

            <div className="flex flex-wrap items-center gap-2">
              {mode === 'minutes' && (
                <>
                  <span className="text-sm">{t('perLabel')}</span>
                  <Input type="number" min={1} max={59} value={everyMinutes} onChange={e => setEveryMinutes(Number(e.target.value) || 1)} className="h-8 w-16 text-right" />
                  <span className="text-sm">{t('minutesUnit')}</span>
                </>
              )}
              {mode === 'weekly' && (
                <Select value={weekday} onValueChange={setWeekday}>
                  <SelectTrigger className="h-8 w-24 text-sm"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    {weekdayNames.map((name, i) => <SelectItem key={i} value={String(i)}>{name}</SelectItem>)}
                  </SelectContent>
                </Select>
              )}
              {mode === 'monthly' && (
                <>
                  <Input type="number" min={1} max={31} value={dayOfMonth} onChange={e => setDayOfMonth(Number(e.target.value) || 1)} className="h-8 w-16 text-right" />
                  <span className="text-sm">{t('dayUnit')}</span>
                </>
              )}
              {mode !== 'minutes' && (
                <Input type="time" value={time} onChange={e => setTime(e.target.value)} className="h-8 w-28" />
              )}
            </div>
          </div>
        </div>

        <div className="bg-popover sticky bottom-0 z-10 flex items-center justify-between gap-2 border-t px-3 py-2">
          <p className="text-muted-foreground min-w-0 truncate text-xs">
            <code className="bg-muted rounded px-1 font-mono">{preview}</code> · {describeCron(preview, t)}
          </p>
          <Button size="sm" className="h-7 shrink-0 px-3 text-xs" onClick={() => apply(preview)}>{t('applyButton')}</Button>
        </div>
      </PopoverContent>
    </Popover>
  )
}
