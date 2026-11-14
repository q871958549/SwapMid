'use client'

import { useRef, useState } from 'react'
import Link from 'next/link'
import { useTranslations } from 'next-intl'
import { Check, ChevronsUpDown, X } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import {
  Command, CommandEmpty, CommandGroup, CommandInput,
  CommandItem, CommandList,
} from '@/components/ui/command'

export interface ComboboxOption {
  value: string
  label: string
  /** Optional secondary text shown below label */
  description?: string
  /** Optional search haystack (cmdk filters by this or label). */
  keywords?: string
  /** 设置后点击打开该地址，不写入当前选择。 */
  href?: string
  /** href 项右侧文案，如「已关联·打开」。 */
  hrefLabel?: string
}

interface ComboboxProps {
  id?: string
  ariaLabel?: string
  options: ComboboxOption[]
  value?: string
  onChange?: (value: string) => void
  placeholder?: string
  searchPlaceholder?: string
  emptyText?: string
  disabled?: boolean
  clearable?: boolean
  className?: string
  /**
   * Show search input when options count exceeds this threshold (default: 8).
   * Always shows search if undefined.
   */
  searchThreshold?: number
  /** href 选项被选中（含键盘）时的站内跳转。鼠标单击同时走选项里的链接。 */
  onNavigate?: (href: string) => void
}

export function Combobox({
  id,
  ariaLabel,
  options,
  value,
  onChange,
  placeholder,
  searchPlaceholder,
  emptyText,
  disabled = false,
  clearable = false,
  className,
  searchThreshold = 8,
  onNavigate,
}: ComboboxProps) {
  const t = useTranslations('Common')
  const resolvedPlaceholder = placeholder ?? t('selectPlaceholder')
  const resolvedSearchPlaceholder = searchPlaceholder ?? t('searchPlaceholder')
  const resolvedEmptyText = emptyText ?? t('noMatchingResult')
  const [open, setOpen] = useState(false)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const showSearch = options.length > searchThreshold

  const selected = options.find(o => o.value === value)
  const showClear = clearable && !!selected && !disabled

  function handleSelect(optionValue: string) {
    onChange?.(optionValue === value ? '' : optionValue)
    setOpen(false)
  }

  function openHref(href: string) {
    onNavigate?.(href)
    setOpen(false)
  }

  function handleClear() {
    onChange?.('')
    triggerRef.current?.focus() // 清空钮随即卸载,焦点归还触发器,防丢到 body
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <div className={cn('relative', className)}>
        <PopoverTrigger asChild>
          <Button
            type="button"
            id={id}
            ref={triggerRef}
            variant="outline"
            role="combobox"
            aria-label={ariaLabel}
            aria-expanded={open}
            disabled={disabled}
            className={cn(
              // 字段型触发器与 Input 一致，焦点环画在框内，免得贴着滚动容器时被裁。
              'h-9 w-full justify-between px-3 font-normal ring-inset',
              !selected && 'text-muted-foreground',
              showClear && 'pr-12',
            )}
          >
            <span className="truncate">{selected?.label ?? resolvedPlaceholder}</span>
            <ChevronsUpDown className="ml-1 size-3.5 shrink-0 opacity-50" />
          </Button>
        </PopoverTrigger>
        {showClear && (
          <button
            type="button"
            aria-label={t('clearSelection')}
            className="absolute right-7 top-1/2 -translate-y-1/2 rounded-sm p-1 text-muted-foreground outline-none transition-colors hover:text-foreground focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)"
            onClick={handleClear}
          >
            <X className="size-3.5" />
          </button>
        )}
      </div>
      <PopoverContent
        className="w-[var(--radix-popover-trigger-width)] overflow-hidden p-0"
        align="start"
        onWheel={(e) => e.stopPropagation()}
      >
        <Command>
          {showSearch && <CommandInput placeholder={resolvedSearchPlaceholder} />}
          <CommandList
            onWheel={(e) => {
              const el = e.currentTarget
              const canScroll = el.scrollHeight > el.clientHeight
              if (canScroll) e.stopPropagation()
            }}
          >
            <CommandEmpty>{resolvedEmptyText}</CommandEmpty>
            <CommandGroup>
              {options.map(option => (
                <CommandItem
                  key={option.value}
                  value={`${option.value} ${option.keywords ?? option.label}`}
                  onSelect={() => {
                    if (option.href) {
                      openHref(option.href)
                      return
                    }
                    handleSelect(option.value)
                  }}
                >
                  {option.href ? (
                    <Link
                      href={option.href}
                      className="flex min-w-0 flex-1 items-center gap-2"
                      onClick={(event) => {
                        if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) return
                        event.preventDefault()
                        openHref(option.href!)
                      }}
                    >
                      <span className="flex min-w-0 flex-1 flex-col">
                        <span className="truncate">{option.label}</span>
                        {option.description && (
                          <span className="truncate text-xs text-muted-foreground">{option.description}</span>
                        )}
                      </span>
                      {option.hrefLabel && (
                        <span className="shrink-0 text-xs text-primary">{option.hrefLabel}</span>
                      )}
                    </Link>
                  ) : (
                    <>
                      <Check
                        className={cn('size-4 shrink-0', value === option.value ? 'opacity-100' : 'opacity-0')}
                      />
                      <span className="flex flex-col">
                        <span>{option.label}</span>
                        {option.description && (
                          <span className="text-xs text-muted-foreground">{option.description}</span>
                        )}
                      </span>
                    </>
                  )}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
