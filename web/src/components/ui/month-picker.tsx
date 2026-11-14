'use client'

import { useMemo, useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { CalendarIcon, ChevronLeft, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { IconButton } from '@/components/ui/icon-button'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'

// 平台统一月份选择器：值 'yyyy-MM'（空串=未选）。可输入（回车/失焦提交，宽松解析）+ 年份切换 + 12 月网格弹层，
// 风格与 DatePicker 一致。按月的页面用它，不要再用原生 <input type="month">（UI 与平台不适配）。
interface MonthPickerProps {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  className?: string
  disabled?: boolean
  max?: string // 最大可选月份 yyyy-MM（含）；超过的月份禁用、手输超过则回退。不传=不限制
}

function parseYM(value: string): { year: number; month: number } | null {
  const m = /^(\d{4})-(\d{1,2})$/.exec(value.trim())
  if (!m) return null
  const month = Number(m[2])
  if (month < 1 || month > 12) return null
  return { year: Number(m[1]), month }
}

export function MonthPicker({ value, onChange, placeholder, className, disabled, max }: MonthPickerProps) {
  const t = useTranslations('Common')
  const resolvedPlaceholder = placeholder ?? t('selectMonth')
  const locale = useLocale()
  // 12 月网格按当前 next-intl locale 现场求月份短名（Intl 原生本地化，无需接 date-fns locale）。
  const months = useMemo(() => {
    const formatter = new Intl.DateTimeFormat(locale, { month: 'short' })
    return Array.from({ length: 12 }, (_, i) => formatter.format(new Date(2024, i, 1)))
  }, [locale])
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState(value)
  const [focused, setFocused] = useState(false)
  const parsed = parseYM(value)
  const [viewYear, setViewYear] = useState(parsed?.year ?? new Date().getFullYear())

  // adjust-state-during-render：外部值变化（换月/重置）时同步草稿与视图年份；输入中除外
  const [lastValue, setLastValue] = useState(value)
  if (lastValue !== value) {
    setLastValue(value)
    if (!focused) setDraft(value)
    if (parsed) setViewYear(parsed.year)
  }

  // 宽松解析：2026-6 / 2026/06 / 2026.6 / 202606
  function commit(text: string) {
    const trimmed = text.trim()
    if (!trimmed) {
      setDraft('')
      if (value) onChange('')
      return
    }
    const norm = trimmed.replace(/[./]/g, '-')
    let m = /^(\d{4})-(\d{1,2})$/.exec(norm)
    if (!m) m = /^(\d{4})(\d{2})$/.exec(trimmed)
    if (m) {
      const y = Number(m[1])
      const mo = Number(m[2])
      if (y >= 1900 && mo >= 1 && mo <= 12) {
        const next = `${y}-${String(mo).padStart(2, '0')}`
        if (max && next > max) { setDraft(value); return } // 超过最大可选月：回退
        setDraft(next)
        if (next !== value) onChange(next)
        return
      }
    }
    setDraft(value) // 解析失败：回退
  }

  function selectMonth(mo: number) {
    const next = `${viewYear}-${String(mo).padStart(2, '0')}`
    setDraft(next)
    onChange(next)
    setOpen(false)
  }

  function selectThisMonth() {
    const d = new Date()
    const next = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
    setViewYear(d.getFullYear())
    setDraft(next)
    onChange(next)
    setOpen(false)
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <div className={cn('relative h-8 w-40', className)}>
        <Input
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
            aria-label={t('openMonthPicker')}
            className="text-muted-foreground hover:text-foreground absolute top-1/2 right-2 -translate-y-1/2 transition-colors disabled:opacity-50"
          >
            <CalendarIcon className="size-3.5" />
          </button>
        </PopoverTrigger>
      </div>
      <PopoverContent className="w-56 p-0" align="start">
        <div className="flex items-center justify-between border-b px-2 py-1.5">
          <IconButton type="button" className="size-7 p-0" label={t('prevYear')} onClick={() => setViewYear((y) => y - 1)}>
            <ChevronLeft className="size-4" />
          </IconButton>
          <span className="text-sm font-medium">{t('yearCaption', { year: viewYear })}</span>
          <IconButton type="button" className="size-7 p-0" label={t('nextYear')} onClick={() => setViewYear((y) => y + 1)}>
            <ChevronRight className="size-4" />
          </IconButton>
        </div>
        <div className="grid grid-cols-3 gap-1 p-2">
          {months.map((label, i) => {
            const mo = i + 1
            const ymStr = `${viewYear}-${String(mo).padStart(2, '0')}`
            const overMax = max ? ymStr > max : false
            const active = parsed?.year === viewYear && parsed?.month === mo
            return (
              <Button
                key={mo}
                type="button"
                variant={active ? 'default' : 'ghost'}
                size="sm"
                disabled={overMax}
                className="h-8 text-xs"
                onClick={() => selectMonth(mo)}
              >
                {label}
              </Button>
            )
          })}
        </div>
        <div className="flex items-center justify-between border-t px-3 py-2">
          <Button type="button" variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={() => { onChange(''); setDraft(''); setOpen(false) }}>
            {t('clear')}
          </Button>
          <Button type="button" variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={selectThisMonth}>
            {t('thisMonth')}
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  )
}
