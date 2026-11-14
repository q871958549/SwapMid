'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { Home, List, X } from 'lucide-react'
import { cn } from '@/lib/utils'
import { findNavItemByPath, resolveTabTitle, type NavT } from '@/lib/nav-utils'
import { HOME_KEY, type Tab } from '@/stores/use-tab-store'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command'

interface TabListMenuProps {
  tabs: Tab[]
  activeKey: string
  onSelect: (tab: Tab) => void
  onClose: (key: string) => void
  onCloseOthers: () => void
  onCloseAll: () => void
}

// 页签栏右端常驻的「全部页签」下拉：不依赖横向滚动即可直达任意页签，
// 并把右键菜单里的「关闭其他 / 关闭全部」提为可见入口（右键可发现性差）。
export function TabListMenu({ tabs, activeKey, onSelect, onClose, onCloseOthers, onCloseAll }: TabListMenuProps) {
  const [open, setOpen] = useState(false)
  const t = useTranslations('Nav') as NavT

  const closableCount = tabs.filter(o => o.key !== HOME_KEY).length
  const othersCount = tabs.filter(o => o.key !== HOME_KEY && o.key !== activeKey).length

  function handleSelect(tab: Tab) {
    setOpen(false)
    onSelect(tab)
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="ghost"
          size="sm"
          aria-label={t('tabBar.allTabsAriaLabel', { count: tabs.length })}
          title={t('tabBar.allTabsTitle')}
          className="h-7 gap-1 px-1.5 text-muted-foreground hover:text-foreground"
        >
          <List className="size-4" />
          <span className="min-w-4 rounded-full bg-muted px-1 text-center text-[11px] tabular-nums leading-4">
            {tabs.length}
          </span>
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-64 overflow-hidden p-0">
        <Command>
          <CommandInput placeholder={t('tabBar.searchPlaceholder')} />
          <CommandList
            className="max-h-[min(18.75rem,calc(var(--radix-popover-content-available-height,999px)-5.5rem))]"
            onWheel={(e) => {
              const el = e.currentTarget
              if (el.scrollHeight > el.clientHeight) e.stopPropagation()
            }}
          >
            <CommandEmpty>{t('tabBar.noMatch')}</CommandEmpty>
            <CommandGroup>
              {tabs.map((tab) => {
                const isActive = tab.key === activeKey
                const isHome = tab.key === HOME_KEY
                const Icon = isHome ? Home : findNavItemByPath(tab.key)?.icon
                const title = resolveTabTitle(tab, t)

                return (
                  <CommandItem
                    key={tab.key}
                    // key 保唯一（标题可能重名），搜索仍按标题命中
                    value={`${title} ${tab.key}`}
                    onSelect={() => handleSelect(tab)}
                    className="group"
                  >
                    {Icon ? (
                      <Icon className={cn('size-4 shrink-0', isActive ? 'text-primary' : 'text-muted-foreground')} />
                    ) : (
                      <span className="size-4 shrink-0" />
                    )}
                    <span className={cn('flex-1 truncate', isActive && 'font-medium')}>{title}</span>
                    {isActive && (
                      <span className="shrink-0 rounded bg-primary/10 px-1 text-[11px] leading-4 text-primary">{t('tabBar.current')}</span>
                    )}
                    {!isHome && (
                      <button
                        tabIndex={-1}
                        aria-label={t('tabBar.closeTabAriaLabel', { title })}
                        onClick={(e) => {
                          e.stopPropagation()
                          onClose(tab.key)
                        }}
                        className="flex size-4 shrink-0 items-center justify-center rounded opacity-0 transition-opacity hover:bg-muted group-hover:opacity-60 hover:!opacity-100"
                      >
                        <X className="size-3" />
                      </button>
                    )}
                  </CommandItem>
                )
              })}
            </CommandGroup>
          </CommandList>
          <div className="flex items-center gap-1 border-t p-1">
            <Button
              variant="ghost"
              size="sm"
              className="h-7 flex-1 text-xs text-muted-foreground hover:text-foreground"
              disabled={othersCount === 0}
              onClick={() => { setOpen(false); onCloseOthers() }}
            >
              {t('tabBar.closeOthers')}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              className="h-7 flex-1 text-xs text-muted-foreground hover:text-destructive"
              disabled={closableCount === 0}
              onClick={() => { setOpen(false); onCloseAll() }}
            >
              {t('tabBar.closeAll')}
            </Button>
          </div>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
