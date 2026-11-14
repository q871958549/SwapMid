'use client'

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslations } from 'next-intl'
import { Check, ChevronsUpDown, Loader2, X } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Command, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'

export interface RemoteComboboxOption {
  value: string
  label: string
  /** 选项下方的次要文本 */
  description?: string
}

function isAbortError(error: unknown): boolean {
  return typeof error === 'object' && error !== null && 'name' in error && error.name === 'AbortError'
}

interface RemoteComboboxProps {
  id?: string
  required?: boolean
  value?: string
  /**
   * 已选项的显示文本。远程搜索会不断替换选项列表，选中项可能不在当前列表里，
   * 故由外部保存已选 label，保证收起后触发器始终显示正确的选中项。
   */
  selectedLabel?: string
  onChange?: (value: string, option?: RemoteComboboxOption) => void
  /** 输入关键字（debounce 后）拉取选项；空串表示初始 / 默认列表。需透传 signal 到实际请求。 */
  fetchOptions: (query: string, signal: AbortSignal) => Promise<RemoteComboboxOption[]>
  placeholder?: string
  searchPlaceholder?: string
  emptyText?: ReactNode
  loadingText?: string
  disabled?: boolean
  clearable?: boolean
  className?: string
  /** 输入防抖毫秒，默认 300；初次打开（空 query）立即拉取不防抖 */
  debounceMs?: number
  /**
   * 远程范围变化时（如部门切换）触发重新拉取。不要把 fetchOptions 本身放进 effect 依赖——
   * 父组件每次 render 新函数引用会打出重复请求。
   */
  scopeKey?: string
}

/**
 * 远程搜索下拉：搜索词受控、debounce 后回调 fetchOptions 拉取，关闭 cmdk 客户端过滤。
 * 适合候选项成百上千、无法一次性全量加载的场景（角色、员工等）。
 */
export function RemoteCombobox({
  id,
  required,
  value,
  selectedLabel,
  onChange,
  fetchOptions,
  placeholder,
  searchPlaceholder,
  emptyText,
  loadingText,
  disabled = false,
  clearable = false,
  className,
  debounceMs = 300,
  scopeKey,
}: RemoteComboboxProps) {
  const t = useTranslations('Common')
  const resolvedPlaceholder = placeholder ?? t('selectPlaceholder')
  const resolvedSearchPlaceholder = searchPlaceholder ?? t('remoteSearchPlaceholder')
  const resolvedEmptyText = emptyText ?? t('noMatchingResult')
  const resolvedLoadingText = loadingText ?? t('searching')
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [options, setOptions] = useState<RemoteComboboxOption[]>([])
  const [loading, setLoading] = useState(false)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const abortControllerRef = useRef<AbortController | null>(null)

  // 用 ref 持有最新 fetchOptions，避免父组件每次渲染传入新函数引用触发重复请求
  const fetchRef = useRef(fetchOptions)
  useEffect(() => {
    fetchRef.current = fetchOptions
  })

  // 竞态保护：取消能节省后端资源；generation 仍是最终防线，确保忽略 signal 的消费者也无法回写旧结果。
  const reqIdRef = useRef(0)
  const invalidatePendingRequest = useCallback(() => {
    reqIdRef.current += 1
    abortControllerRef.current?.abort()
    abortControllerRef.current = null
  }, [])

  // 打开后 + 搜索词变化 → debounce 拉取
  useEffect(() => {
    if (!open) return
    const handle = window.setTimeout(
      () => {
        const controller = new AbortController()
        const reqId = ++reqIdRef.current
        abortControllerRef.current?.abort()
        abortControllerRef.current = controller
        setLoading(true)
        fetchRef
          .current(query, controller.signal)
          .then(result => {
            if (reqId === reqIdRef.current) setOptions(result)
          })
          .catch(error => {
            if (isAbortError(error)) return
            if (reqId === reqIdRef.current) setOptions([])
          })
          .finally(() => {
            if (reqId === reqIdRef.current) {
              if (abortControllerRef.current === controller) abortControllerRef.current = null
              setLoading(false)
            }
          })
      },
      query ? debounceMs : 0
    )
    return () => {
      window.clearTimeout(handle)
      invalidatePendingRequest()
    }
  }, [open, query, debounceMs, invalidatePendingRequest, scopeKey])

  const displayLabel = selectedLabel || options.find(o => o.value === value)?.label
  const showClear = clearable && !!value && !disabled

  // 关闭时清空搜索词，下次打开回到默认列表（在事件处理里清，避免在 effect 内同步 setState）
  function handleOpenChange(next: boolean) {
    if (!next) {
      invalidatePendingRequest()
      setLoading(false)
      setQuery('')
    }
    setOpen(next)
  }

  function handleQueryChange(nextQuery: string) {
    if (nextQuery === query) return
    // 不等 debounce effect 重跑：切词当下就隐藏旧选项、终止旧请求并使其 generation 失效。
    setLoading(true)
    invalidatePendingRequest()
    setQuery(nextQuery)
  }

  function handleSelect(option: RemoteComboboxOption) {
    onChange?.(option.value === value ? '' : option.value, option.value === value ? undefined : option)
    handleOpenChange(false)
  }

  function handleClear() {
    onChange?.('', undefined)
    triggerRef.current?.focus() // 清空钮随即卸载，焦点归还触发器，防丢到 body
  }

  return (
    <Popover open={open} onOpenChange={handleOpenChange}>
      <div className={cn('relative', className)}>
        <PopoverTrigger asChild>
          <Button
            ref={triggerRef}
            id={id}
            variant="outline"
            role="combobox"
            aria-expanded={open}
            aria-required={required || undefined}
            disabled={disabled}
            className={cn(
              // 字段型触发器与 Input 一致，焦点环画在框内，免得贴着滚动容器时被裁。
              'h-9 w-full justify-between px-3 font-normal ring-inset',
              !displayLabel && 'text-muted-foreground',
              showClear && 'pr-12',
            )}
          >
            <span className="truncate">{displayLabel ?? resolvedPlaceholder}</span>
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
        onCloseAutoFocus={event => {
          // 退出动画结束时下一个控件可能已接管焦点；旧面板不得回焦并关闭新弹层。
          // event.target保留本次卸载面板，避免ref已清空而误判仍在旧面板内的焦点。
          const panel = event.target
          const active = document.activeElement
          if (panel instanceof HTMLElement && active instanceof HTMLElement && active.isConnected &&
            active !== document.body && active !== document.documentElement &&
            !panel.contains(active) && !triggerRef.current?.contains(active)) event.preventDefault()
        }}
        onWheel={e => e.stopPropagation()}
      >
        {/* shouldFilter={false}：关闭 cmdk 客户端过滤，完全交给远程搜索 */}
        <Command shouldFilter={false}>
          <CommandInput placeholder={resolvedSearchPlaceholder} value={query} onValueChange={handleQueryChange} />
          <CommandList
            onWheel={e => {
              const el = e.currentTarget
              if (el.scrollHeight > el.clientHeight) e.stopPropagation()
            }}
          >
            {loading ? (
              <div className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground">
                <Loader2 className="size-4 animate-spin" />
                {resolvedLoadingText}
              </div>
            ) : options.length === 0 ? (
              <div className="py-6 text-center text-sm text-muted-foreground">{resolvedEmptyText}</div>
            ) : (
              <CommandGroup>
                {options.map(option => (
                  <CommandItem key={option.value} value={option.value} onSelect={() => handleSelect(option)}>
                    <Check className={cn('size-4 shrink-0', value === option.value ? 'opacity-100' : 'opacity-0')} />
                    <span className="flex flex-col">
                      <span>{option.label}</span>
                      {option.description && <span className="text-xs text-muted-foreground">{option.description}</span>}
                    </span>
                  </CommandItem>
                ))}
              </CommandGroup>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
