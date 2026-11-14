'use client'

import { useLocale } from 'next-intl'
import { ChevronDown, ChevronLeft, ChevronRight } from 'lucide-react'
import { DayPicker, type DayPickerProps, type DropdownProps } from 'react-day-picker'
import { enUS, zhCN, zhTW } from 'react-day-picker/locale'
import { cn } from '@/lib/utils'
import { buttonVariants } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

// next-intl locale → react-day-picker（date-fns）locale：星期/月份等日历自有文案随语言切换。
const DAY_PICKER_LOCALES: Record<string, typeof zhCN> = {
  'zh-Hans': zhCN,
  'zh-Hant': zhTW,
  en: enUS,
}

// 年/月下拉：用平台 Select（Radix）替换原生 <select>——原生下拉无法定义样式且长列表观感差；
// Radix 列表自带滚动与键盘导航。v9 的 onChange 期望原生事件，这里合成最小事件对象回传。
function CalendarDropdown({ options = [], value, onChange, 'aria-label': ariaLabel, disabled }: DropdownProps) {
  return (
    <Select
      value={String(value)}
      disabled={disabled}
      onValueChange={(next) => {
        onChange?.({ target: { value: next } } as unknown as React.ChangeEvent<HTMLSelectElement>)
      }}
    >
      <SelectTrigger aria-label={ariaLabel} className="h-7 gap-1 rounded-md border-input px-2 py-0 text-sm font-medium">
        <SelectValue />
      </SelectTrigger>
      <SelectContent position="popper" className="max-h-64 min-w-[5.5rem]">
        {options.map((opt) => (
          <SelectItem key={opt.value} value={String(opt.value)} disabled={opt.disabled}>
            {opt.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

// 平台统一日历（react-day-picker v9，shadcn 风格类名，三语随 next-intl locale）。
// 一般不直接用本组件，业务侧用封装好的 <DatePicker>（components/ui/date-picker.tsx）。
export function Calendar({ className, classNames, showOutsideDays = true, ...props }: DayPickerProps) {
  const locale = useLocale()
  return (
    <DayPicker
      locale={DAY_PICKER_LOCALES[locale] ?? zhCN}
      showOutsideDays={showOutsideDays}
      className={cn('p-3', className)}
      classNames={{
        months: 'relative flex flex-col gap-4 sm:flex-row',
        month: 'flex w-full flex-col gap-3',
        // ⚠️ nav 绝对定位铺满标题行、且在 DOM 上层——必须 pointer-events-none 放行点击，
        // 仅两个箭头按钮恢复可点；否则年/月下拉（静态定位、视觉居中）整块点不到，
        // 历史上连换三种下拉实现都"无法操作"，根因全是这层透明遮罩。
        nav: 'pointer-events-none absolute inset-x-0 top-0 flex w-full items-center justify-between',
        button_previous: cn(buttonVariants({ variant: 'ghost' }), 'pointer-events-auto size-7 p-0 opacity-60 hover:opacity-100'),
        button_next: cn(buttonVariants({ variant: 'ghost' }), 'pointer-events-auto size-7 p-0 opacity-60 hover:opacity-100'),
        month_caption: 'flex h-7 items-center justify-center',
        caption_label: 'flex items-center gap-0.5 text-sm font-medium select-none',
        dropdowns: 'flex items-center justify-center gap-1.5',
        month_grid: 'w-full border-collapse',
        weekdays: 'flex',
        weekday: 'w-8 text-[11px] font-normal text-muted-foreground',
        week: 'mt-1.5 flex w-full',
        day: 'flex size-8 items-center justify-center p-0 text-sm',
        day_button: cn(buttonVariants({ variant: 'ghost' }), 'size-8 p-0 font-normal'),
        selected: 'rounded-md bg-primary text-primary-foreground [&>button]:bg-primary [&>button]:text-primary-foreground [&>button]:hover:bg-primary [&>button]:hover:text-primary-foreground',
        today: 'rounded-md bg-accent text-accent-foreground',
        outside: 'text-muted-foreground/50',
        disabled: 'text-muted-foreground opacity-40',
        hidden: 'invisible',
        ...classNames,
      }}
      components={{
        Dropdown: CalendarDropdown,
        // down 方向用于下拉箭头；漏掉会渲染成 ">" 造成"2026 >"的怪异观感
        Chevron: ({ orientation }) =>
          orientation === 'left' ? <ChevronLeft className="size-4" />
            : orientation === 'right' ? <ChevronRight className="size-4" />
            : <ChevronDown className="size-3.5 opacity-60" />,
      }}
      {...props}
    />
  )
}
