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

// 平台统一日期选择器：可输入（回车/失焦提交，宽松解析）+ 日历弹层双通道。
// 值保持 'yyyy-MM-dd' 字符串（空串=未选），与原生 input 的 value 完全兼容。
interface DatePickerProps {
  id?: string
  value: string
  onChange: (value: string) => void
  placeholder?: string
  className?: string
  disabled?: boolean
  /** 日历年份下拉起始年。默认 2015，适合近期业务日；出生日期请传 1950。 */
  fromYear?: number
  /** 日历年份下拉结束年。默认明年。出生日期请配合 toDate（当天减满 16 岁）。 */
  toYear?: number
  /** 最早可选日（含）。传入后该日之前禁用，年份下拉从该年起。 */
  fromDate?: Date
  /** 最晚可选日（含）。传入后年份下拉收到该年，该日之后禁用；「今天」若越界则隐藏。 */
  toDate?: Date
}

const WIRE_FORMAT = 'yyyy-MM-dd'

function parseValue(value: string): Date | undefined {
  if (!value) return undefined
  const d = parse(value, WIRE_FORMAT, new Date())
  return isValid(d) ? d : undefined
}

// 手输宽松解析：2026-06-11 / 2026/6/3 / 2026.6.3 / 20260611
function parseLoose(text: string): Date | undefined {
  const t = text.trim()
  if (!t) return undefined
  if (/^\d{8}$/.test(t)) {
    const d = parse(t, 'yyyyMMdd', new Date())
    return isValid(d) ? d : undefined
  }
  const normalized = t.replace(/[./\s]+/g, '-')
  const d = parse(normalized, 'yyyy-M-d', new Date())
  return isValid(d) ? d : undefined
}

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}

function isInRange(d: Date, min: Date, max: Date): boolean {
  const t = startOfDay(d).getTime()
  return t >= startOfDay(min).getTime() && t <= startOfDay(max).getTime()
}

export function DatePicker({ id, value, onChange, placeholder, className, disabled, fromYear, toYear, fromDate, toDate }: DatePickerProps) {
  const t = useTranslations('Common')
  const resolvedPlaceholder = placeholder ?? t('selectOrInputDate')
  const [open, setOpen] = useState(false)
  // 输入草稿与外部值分离：敲键不逐字提交，回车/失焦才解析；无效输入回退上一个有效值
  const [draft, setDraft] = useState(value)
  const [focused, setFocused] = useState(false)
  // 外部值变化（日历选择/调用方重置）同步进输入框——输入中除外（render-adjust 模式）
  const [lastValue, setLastValue] = useState(value)
  if (lastValue !== value) {
    setLastValue(value)
    if (!focused) setDraft(value)
  }

  const date = parseValue(value)
  const minDate = fromDate ? startOfDay(fromDate) : new Date(fromYear ?? 2015, 0, 1)
  const maxDate = toDate
    ? startOfDay(toDate)
    : new Date(toYear ?? new Date().getFullYear() + 1, 11, 31)
  const today = startOfDay(new Date())
  const todayInRange = isInRange(today, minDate, maxDate)

  function commit(text: string) {
    const trimmed = text.trim()
    if (!trimmed) {
      setDraft('')
      if (value) onChange('')
      return
    }
    const d = parseLoose(trimmed)
    if (d && isInRange(d, minDate, maxDate)) {
      const next = format(d, WIRE_FORMAT)
      setDraft(next)
      if (next !== value) onChange(next)
    } else {
      setDraft(value) // 解析失败或越界：回退
    }
  }

  function select(d: Date | undefined) {
    const next = d ? format(d, WIRE_FORMAT) : ''
    setDraft(next)
    onChange(next)
    setOpen(false)
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <div className={cn('relative h-8 w-40', className)}>
        <Input
          id={id}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onFocus={() => setFocused(true)}
          onBlur={(e) => { setFocused(false); commit(e.target.value) }}
          onKeyDown={(e) => {
            if (e.key === 'Enter') { e.preventDefault(); commit(e.currentTarget.value) }
            if (e.key === 'Escape') setDraft(value)
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
          selected={date}
          onSelect={select}
          defaultMonth={date}
          captionLayout="dropdown"
          startMonth={new Date(minDate.getFullYear(), minDate.getMonth())}
          endMonth={new Date(maxDate.getFullYear(), maxDate.getMonth())}
          disabled={{ before: minDate, after: maxDate }}
          autoFocus
        />
        <div className="flex items-center justify-between border-t px-3 py-2">
          <Button type="button" variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={() => select(undefined)}>
            {t('clear')}
          </Button>
          {todayInRange && (
            <Button type="button" variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={() => select(today)}>
              {t('today')}
            </Button>
          )}
        </div>
      </PopoverContent>
    </Popover>
  )
}
