'use client'

import { useRef, type KeyboardEvent, type ReactNode } from 'react'
import { cn } from '@/lib/utils'

export interface SegmentedOption<T extends string> {
  value: T
  label: ReactNode
  /** 选项后的数量，如「待补档 12」。 */
  count?: number
  disabled?: boolean
}

/**
 * 这一组在选什么，读屏用，二选一：没有可见标题时传 `aria-label`（如「状态」）；
 * 表单里已有可见标签时传 `aria-labelledby` 指向它，读屏名与看到的标签同源。
 */
type SegmentedLabel =
  | { 'aria-label': string; 'aria-labelledby'?: never }
  | { 'aria-labelledby': string; 'aria-label'?: never }

type SegmentedControlProps<T extends string> = SegmentedLabel & {
  value: T
  onValueChange: (value: T) => void
  options: SegmentedOption<T>[]
  /** `sm`：32px，筛选条与工具栏；`lg`：44px、等分撑满一行，手机表单里的二选一。 */
  size?: 'sm' | 'lg'
  className?: string
  /** 作表单字段时用：`id` 供错误定位，校验失败时轨道描红并关联错误说明。 */
  id?: string
  'aria-invalid'?: boolean
  'aria-describedby'?: string
}

/**
 * 互斥单选的分段切换（全部 / 待处理 / 已完成）。
 *
 * 样式沿用平台页签：灰轨道 + 白滑块，选中项加粗。不要再用 `variant={选中 ? 'default' : 'outline'}`
 * 手写——黑底只给本区域的主操作，选中态借用它会和主按钮抢焦点（规范 §1.1、蓝图 §3.5）。
 * 键盘：方向键切换并立即生效，Home / End 到首尾（WAI-ARIA radiogroup）。
 */
export function SegmentedControl<T extends string>({
  value,
  onValueChange,
  options,
  size = 'sm',
  className,
  id,
  'aria-label': ariaLabel,
  'aria-labelledby': ariaLabelledBy,
  'aria-invalid': ariaInvalid,
  'aria-describedby': ariaDescribedBy,
}: SegmentedControlProps<T>) {
  const refs = useRef<(HTMLButtonElement | null)[]>([])
  const enabled = options.map((option, index) => ({ option, index })).filter(({ option }) => !option.disabled)
  // 表单里可以一项都没选（如「性别」未填）；这时让第一个可用项进 Tab 顺序，否则键盘进不来。
  const tabStop = enabled.some(({ option }) => option.value === value) ? value : enabled[0]?.option.value

  function select(index: number) {
    refs.current[index]?.focus()
    onValueChange(options[index].value)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const at = enabled.findIndex((item) => item.index === index)
    const last = enabled.length - 1
    let next: number | null = null
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') next = at === last ? 0 : at + 1
    else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') next = at <= 0 ? last : at - 1
    else if (event.key === 'Home') next = 0
    else if (event.key === 'End') next = last
    if (next === null || !enabled[next]) return
    event.preventDefault()
    select(enabled[next].index)
  }

  return (
    <div
      id={id}
      role="radiogroup"
      aria-label={ariaLabel}
      aria-labelledby={ariaLabelledBy}
      aria-invalid={ariaInvalid}
      aria-describedby={ariaDescribedBy}
      className={cn(
        'items-center gap-0.5 rounded-lg bg-muted p-0.5 text-muted-foreground aria-invalid:ring-2 aria-invalid:ring-destructive/40',
        size === 'lg' ? 'grid h-11 w-full auto-cols-fr grid-flow-col' : 'inline-flex h-8',
        className,
      )}
    >
      {options.map((option, index) => {
        const checked = option.value === value
        return (
          <button
            key={option.value}
            ref={(element) => { refs.current[index] = element }}
            type="button"
            role="radio"
            aria-checked={checked}
            tabIndex={option.value === tabStop ? 0 : -1}
            disabled={option.disabled}
            onClick={() => onValueChange(option.value)}
            onKeyDown={(event) => handleKeyDown(event, index)}
            className={cn(
              'inline-flex cursor-pointer items-center justify-center gap-1.5 rounded-md text-sm font-medium outline-none transition-colors',
              // lg 等分一行，手机上一格可能只有六七十像素：允许折行居中，别让长选项挤出格子。
              size === 'lg' ? 'h-10 min-w-0 px-1.5 text-center leading-tight' : 'h-7 whitespace-nowrap px-2.5',
              'focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color) disabled:cursor-not-allowed disabled:opacity-50',
              checked ? 'bg-background text-foreground shadow-xs' : 'hover:text-foreground',
            )}
          >
            {option.label}
            {option.count !== undefined && <span className="text-xs tabular-nums text-muted-foreground">{option.count}</span>}
          </button>
        )
      })}
    </div>
  )
}
