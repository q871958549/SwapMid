'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { format, isValid, parse } from 'date-fns'
import { CalendarIcon } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Calendar } from '@/components/ui/calendar'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

// 平台统一日期时间选择器（与 date-picker.tsx 同款交互）：可输入（回车/失焦提交，宽松解析）
// + 日历/时间弹层双通道。值保持 'yyyy-MM-ddTHH:mm' 字符串（空串=未选），与后端 DateTime 绑定兼容。
interface DateTimePickerProps {
  id?: string
  value: string
  onChange: (value: string) => void
  placeholder?: string
  className?: string
  disabled?: boolean
}

const WIRE_FORMAT = "yyyy-MM-dd'T'HH:mm"
const HOURS = Array.from({ length: 24 }, (_, i) => String(i).padStart(2, '0'))
const MINUTES = Array.from({ length: 12 }, (_, i) => String(i * 5).padStart(2, '0'))

function parseValue(value: string): Date | undefined {
  if (!value) return undefined
  const d = parse(value, WIRE_FORMAT, new Date())
  return isValid(d) ? d : undefined
}

// 手输宽松解析：2026-06-12 16:00 / 2026/6/12 16:00 / 2026-06-12（默认 09:00）/ 202606121600
function parseLoose(text: string): Date | undefined {
  const t = text.trim()
  if (!t) return undefined
  if (/^\d{12}$/.test(t)) {
    const d = parse(t, 'yyyyMMddHHmm', new Date())
    return isValid(d) ? d : undefined
  }
  const normalized = t.replace(/[./]+/g, '-').replace(/\s+/g, ' ').replace(' ', 'T').replace('T', ' ')
  for (const fmtStr of ['yyyy-M-d H:m', 'yyyy-M-d H', 'yyyy-M-d']) {
    const d = parse(normalized, fmtStr, new Date())
    if (isValid(d)) {
      if (fmtStr === 'yyyy-M-d') d.setHours(9, 0, 0, 0)
      return d
    }
  }
  return undefined
}

export function DateTimePicker({ id, value, onChange, placeholder, className, disabled }: DateTimePickerProps) {
  const t = useTranslations('Common')
  const resolvedPlaceholder = placeholder ?? t('selectOrInputDateTime')
  const [open, setOpen] = useState(false)
  // 输入草稿与外部值分离：敲键不逐字提交，回车/失焦才解析；无效输入回退上一个有效值
  const [draft, setDraft] = useState(displayOf(value))
  // 外部值变化（弹层选择 / 调用方一键填入）必须同步草稿。
  // 不能用「聚焦中就跳过」：Dialog 会把第一个输入框自动聚焦，跳过会导致一键填入看起来没填上，失焦还会把已写入的值清掉。
  const [lastValue, setLastValue] = useState(value)
  const date = parseValue(value)
  const [pending, setPending] = useState<Date | undefined>(date)
  if (lastValue !== value) {
    setLastValue(value)
    setDraft(displayOf(value))
    if (!open) setPending(parseValue(value))
  }

  function displayOf(v: string): string {
    return v ? v.replace('T', ' ') : ''
  }

  function emit(d: Date | undefined) {
    const next = d ? format(d, WIRE_FORMAT) : ''
    setDraft(next ? next.replace('T', ' ') : '')
    if (next !== value) onChange(next)
  }

  function preview(d: Date | undefined) {
    setPending(d)
    setDraft(d ? format(d, WIRE_FORMAT).replace('T', ' ') : '')
  }

  function commit(text: string) {
    const trimmed = text.trim()
    if (!trimmed) {
      emit(undefined)
      return
    }
    const d = parseLoose(trimmed)
    if (d) emit(d)
    else setDraft(displayOf(value)) // 解析失败：回退
  }

  // 日历/时间先改弹层草稿，点「确认」才写入；点「现在」曾在确认位上极易误触。
  function selectDate(d: Date | undefined) {
    if (!d) {
      preview(undefined)
      return
    }
    const next = new Date(d)
    const source = pending ?? date
    if (source) next.setHours(source.getHours(), source.getMinutes(), 0, 0)
    else next.setHours(9, 0, 0, 0)
    preview(next)
  }

  function selectTime(part: 'hour' | 'minute', raw: string) {
    const base = pending ?? date ?? (() => { const n = new Date(); n.setHours(9, 0, 0, 0); return n })()
    const next = new Date(base)
    if (part === 'hour') next.setHours(Number(raw))
    else next.setMinutes(Number(raw))
    preview(next)
  }

  function handleOpenChange(nextOpen: boolean) {
    if (nextOpen) {
      const current = parseValue(value)
      setPending(current)
      setDraft(displayOf(value))
    } else {
      setPending(parseValue(value))
      setDraft(displayOf(value))
    }
    setOpen(nextOpen)
  }

  return (
    <Popover open={open} onOpenChange={handleOpenChange}>
      <div className={cn('relative h-8 w-full', className)}>
        <Input
          id={id}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onBlur={(e) => { commit(e.target.value) }}
          onKeyDown={(e) => {
            if (e.key === 'Enter') { e.preventDefault(); commit(e.currentTarget.value) }
            if (e.key === 'Escape') setDraft(displayOf(value))
          }}
          placeholder={resolvedPlaceholder}
          disabled={disabled}
          className="h-full w-full pr-8 text-sm"
        />
        <PopoverTrigger asChild>
          <button
            type="button"
            disabled={disabled}
            aria-label={t('openCalendar')}
            className="text-muted-foreground hover:text-foreground absolute top-1/2 right-2 -translate-y-1/2 transition-colors disabled:opacity-50"
          >
            <CalendarIcon className="size-3.5" />
          </button>
        </PopoverTrigger>
      </div>
      <PopoverContent className="w-auto p-0" align="end">
        <Calendar
          mode="single"
          selected={pending}
          onSelect={selectDate}
          defaultMonth={pending ?? date}
          captionLayout="dropdown"
          startMonth={new Date(2015, 0)}
          endMonth={new Date(new Date().getFullYear() + 1, 11)}
          autoFocus
        />
        <div className="flex items-center gap-1.5 border-t px-3 py-2">
          <span className="text-muted-foreground shrink-0 text-xs">{t('time')}</span>
          <Select value={pending ? String(pending.getHours()).padStart(2, '0') : undefined} onValueChange={(v) => selectTime('hour', v)}>
            <SelectTrigger className="h-7! w-[4.5rem] min-w-[4.5rem] shrink-0 px-2 text-xs tabular-nums *:data-[slot=select-value]:overflow-visible">
              <SelectValue placeholder="--" />
            </SelectTrigger>
            <SelectContent className="max-h-60 min-w-[4.5rem]">
              {HOURS.map(h => <SelectItem key={h} value={h} className="text-xs tabular-nums">{h}</SelectItem>)}
            </SelectContent>
          </Select>
          <span className="text-muted-foreground shrink-0 text-xs">{t('hourPlaceholder')}</span>
          <Select value={pending ? String(pending.getMinutes()).padStart(2, '0') : undefined} onValueChange={(v) => selectTime('minute', v)}>
            <SelectTrigger className="h-7! w-[4.5rem] min-w-[4.5rem] shrink-0 px-2 text-xs tabular-nums *:data-[slot=select-value]:overflow-visible">
              <SelectValue placeholder="--" />
            </SelectTrigger>
            <SelectContent className="max-h-60 min-w-[4.5rem]">
              {MINUTES.map(m => <SelectItem key={m} value={m} className="text-xs tabular-nums">{m}</SelectItem>)}
            </SelectContent>
          </Select>
          <span className="text-muted-foreground shrink-0 text-xs">{t('minutePlaceholder')}</span>
        </div>
        <div className="flex items-center justify-between border-t px-3 py-2">
          <Button type="button" variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={() => { emit(undefined); setPending(undefined); setOpen(false) }}>
            {t('clear')}
          </Button>
          <Button type="button" size="sm" className="h-7 px-3 text-xs" disabled={!pending} onClick={() => { emit(pending); setOpen(false) }}>
            {t('confirm')}
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  )
}
