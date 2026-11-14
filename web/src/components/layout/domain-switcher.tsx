'use client'

import { useState } from 'react'
import { Check, ChevronsUpDown, Settings2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList, CommandSeparator } from '@/components/ui/command'
import { translateNavDescription, translateNavTitle, type NavT, type ResolvedDomain } from '@/lib/nav-utils'
import { buildNavSearchDoc, formatNavSearchValue, navCommandFilter, parseNavSearchValue } from '@/lib/nav-search'
import { DomainPreviewPane } from './domain-preview-pane'
import { WORKBENCH_DOMAIN, type NavDomain, type NavGroup } from './nav-config'

interface DomainSwitcherProps {
  domains: ResolvedDomain[]
  hiddenDomains?: ResolvedDomain[]
  recentDomainKeys?: string[]
  active: NavDomain
  collapsed: boolean
  popoverSide?: 'right' | 'bottom'
  onPick: (key: string) => void
  onManage?: () => void
  onOpen?: () => void
  previewGroupsOf?: (domainKey: string) => NavGroup[]
  t: NavT
  ts: NavT
}

function domainSearchValue(domain: NavDomain, t: NavT, section: string): string {
  return formatNavSearchValue(buildNavSearchDoc({
    title: translateNavTitle(domain, t),
    titleZh: domain.title,
    group: translateNavDescription(domain, t),
    groupZh: domain.description,
    href: `/${section}/${domain.key}`,
  }))
}

function domainKeyOfValue(value: string): string | null {
  const segments = parseNavSearchValue(value).href.split('/')
  return segments.length === 3 ? segments[2]! : null
}

export function DomainSwitcher({
  domains,
  hiddenDomains = [],
  recentDomainKeys = [],
  active,
  collapsed,
  popoverSide = 'right',
  onPick,
  onManage,
  onOpen,
  previewGroupsOf,
  t,
  ts,
}: DomainSwitcherProps) {
  const [open, setOpen] = useState(false)
  const [selectedValue, setSelectedValue] = useState('')
  const ActiveIcon = active.icon
  const activeTitle = translateNavTitle(active, t)

  const face = (
    <>
      <span className={cn('flex size-6 shrink-0 items-center justify-center rounded-md', collapsed ? 'text-primary' : 'bg-primary/10 text-primary')}>
        <ActiveIcon className="size-3.5" />
      </span>
      {!collapsed && <span className="flex-1 truncate text-left text-[13px] font-medium">{activeTitle}</span>}
    </>
  )
  const faceClass = cn(
    'flex w-full items-center rounded-lg pointer-coarse:py-2.5',
    collapsed ? 'justify-center py-2' : 'gap-2 border bg-background/40 px-2.5 py-2',
  )

  if (domains.length + hiddenDomains.length <= 1) {
    return (
      <div className={faceClass} title={collapsed ? `${ts('domain')}${activeTitle}` : undefined}>
        {face}
      </div>
    )
  }

  function pick(domain: NavDomain) {
    setOpen(false)
    onPick(domain.key)
  }

  const allDomains = [...domains, ...hiddenDomains]

  function handleOpenChange(next: boolean) {
    setOpen(next)
    if (!next) return
    onOpen?.()
    const current = allDomains.find((d) => d.domain.key === active.key)?.domain
    setSelectedValue(current ? domainSearchValue(current, t, 'all') : '')
  }

  const previewKey = domainKeyOfValue(selectedValue)
  const previewDomain = previewKey ? allDomains.find((d) => d.domain.key === previewKey)?.domain ?? null : null
  const showPreview = !!previewGroupsOf
  const recentDomains = recentDomainKeys
    .map((key) => allDomains.find((d) => d.domain.key === key)?.domain)
    .filter((domain): domain is NavDomain => !!domain)

  function renderItem(domain: NavDomain, section: string, shortcut?: number) {
    const Icon = domain.icon
    const isActive = domain.key === active.key
    return (
      <CommandItem
        key={domain.key}
        value={domainSearchValue(domain, t, section)}
        onSelect={() => pick(domain)}
        aria-current={isActive ? 'true' : undefined}
        className="items-start gap-2 px-2.5"
      >
        <Icon className={cn('mt-0.5', isActive ? 'text-primary' : 'text-muted-foreground')} />
        <span className="flex min-w-0 flex-1 flex-col">
          <span className={cn('text-[13px] font-medium', isActive && 'text-primary')}>{translateNavTitle(domain, t)}</span>
          <span className="truncate text-[11px] text-muted-foreground" title={translateNavDescription(domain, t)}>
            {translateNavDescription(domain, t)}
          </span>
        </span>
        {isActive
          ? <Check className="mt-0.5 text-primary" />
          : shortcut !== undefined && (
              <kbd className="mt-0.5 shrink-0 font-mono text-[10px] text-muted-foreground/70">Alt+{shortcut}</kbd>
            )}
      </CommandItem>
    )
  }

  return (
    <Popover open={open} onOpenChange={handleOpenChange}>
      <PopoverTrigger asChild>
        <button
          type="button"
          title={collapsed ? `${ts('domain')}${activeTitle}` : undefined}
          className={cn(faceClass, 'transition-colors hover:bg-sidebar-accent/40')}
        >
          {face}
          {!collapsed && <ChevronsUpDown className="size-3.5 shrink-0 text-muted-foreground/60" />}
        </button>
      </PopoverTrigger>
      <PopoverContent
        side={popoverSide}
        align="start"
        sideOffset={popoverSide === 'right' ? 10 : 6}
        className={cn(
          'flex overflow-hidden p-0 select-none',
          showPreview ? 'h-[min(30rem,var(--radix-popover-content-available-height))] w-[34rem]' : 'w-64',
        )}
      >
        <Command
          filter={navCommandFilter}
          loop
          value={selectedValue}
          onValueChange={setSelectedValue}
          className={cn(showPreview && 'w-64 shrink-0 border-r')}
        >
          <CommandInput placeholder={ts('searchDomain')} aria-label={ts('switchDomain')} />
          <CommandList className={cn(showPreview && 'max-h-none min-h-0 flex-1')}>
            <CommandEmpty>{ts('noDomainMatch')}</CommandEmpty>
            <CommandGroup>{renderItem(WORKBENCH_DOMAIN, 'workbench')}</CommandGroup>
            {recentDomains.length > 0 && (
              <CommandGroup heading={ts('recentDomains')}>
                {recentDomains.map((domain) => renderItem(domain, 'recent'))}
              </CommandGroup>
            )}
            <CommandSeparator />
            <CommandGroup heading={ts('switchDomain')}>
              {domains.map(({ domain }, index) => renderItem(domain, 'all', index < 9 ? index + 1 : undefined))}
            </CommandGroup>
            {hiddenDomains.length > 0 && (
              <CommandGroup heading={ts('moreDomains')}>
                {hiddenDomains.map(({ domain }) => renderItem(domain, 'hidden'))}
              </CommandGroup>
            )}
          </CommandList>
          {onManage && (
            <button
              type="button"
              onClick={() => { setOpen(false); onManage() }}
              className="flex shrink-0 items-center gap-2 border-t px-4 py-2.5 text-left text-[13px] text-muted-foreground outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:bg-accent focus-visible:text-accent-foreground"
            >
              <Settings2 className="size-4" />
              {ts('manageDomains')}
            </button>
          )}
        </Command>
        {showPreview && (
          <div className="min-h-0 min-w-0 flex-1">
            <DomainPreviewPane
              domain={previewDomain}
              groups={previewDomain ? previewGroupsOf(previewDomain.key) : []}
              onNavigate={() => setOpen(false)}
              t={t}
              ts={ts}
            />
          </div>
        )}
      </PopoverContent>
    </Popover>
  )
}
