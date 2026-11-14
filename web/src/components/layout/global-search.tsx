'use client'

import { useEffect, useMemo, useState } from 'react'
import { useRouter } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { Search } from 'lucide-react'
import { IconButton } from '@/components/ui/icon-button'
import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command'
import { navCatalog } from '@/lib/nav-catalog'
import { resolveStoredPagePath } from '@/stores/use-tab-store'
import { isNavItemVisible, translateNavTitle, type NavT } from '@/lib/nav-utils'
import { buildNavSearchDoc, formatNavSearchValue, navCommandFilter } from '@/lib/nav-search'

interface GlobalSearchProps {
  grantedPolicies: Record<string, boolean>
}

export function GlobalSearch({ grantedPolicies }: GlobalSearchProps) {
  const router = useRouter()
  const [open, setOpen] = useState(false)
  const t = useTranslations('Nav') as NavT
  const tc = useTranslations('Common')
  const ts = useTranslations('GlobalSearch')

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key?.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey)) {
        event.preventDefault()
        setOpen((value) => !value)
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [])

  const menus = useMemo(() => {
    const { groups, domains } = navCatalog()
    return groups.flatMap((group) => {
      const domain = domains.find((candidate) => candidate.groupKeys.includes(group.key))
      const domainTitle = domain ? translateNavTitle(domain, t) : ''
      const groupTitle = translateNavTitle(group, t)
      return group.items
        .filter((item) => isNavItemVisible(item, grantedPolicies))
        .map((item) => {
          const itemTitle = translateNavTitle(item, t)
          return {
            title: itemTitle,
            href: item.href,
            // 「域 › 组」：同名组（如多个域都有「基础数据」）靠域名区分
            path: domainTitle ? `${domainTitle} › ${groupTitle}` : groupTitle,
            icon: item.icon,
            // 中文原文与译文分字段索引，拼音按字段连续匹配，不跨分组/路径借字母
            searchValue: formatNavSearchValue(buildNavSearchDoc({
              title: itemTitle,
              titleZh: item.title,
              group: groupTitle,
              groupZh: group.title,
              domain: domainTitle,
              domainZh: domain?.title,
              href: item.href,
            })),
          }
        })
    })
  }, [grantedPolicies, t])

  function runCommand(href: string) {
    setOpen(false)
    // 回到已开页面时走页签存量路径（含筛选/分页 query），与点页签行为一致
    router.push(resolveStoredPagePath(href))
  }

  return (
    <>
      <IconButton
        label={ts('openLabel')}
        className="size-9 shrink-0 md:hidden"
        onClick={() => setOpen(true)}
      >
        <Search className="size-4" />
      </IconButton>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="hidden h-8 w-full max-w-sm items-center gap-2 rounded-md border border-input bg-muted/40 px-3 text-sm text-muted-foreground shadow-sm transition-colors hover:bg-accent hover:text-accent-foreground md:flex"
      >
        <Search className="size-3.5 shrink-0" />
        <span className="min-w-0 flex-1 truncate text-left">{tc('search')}</span>
        <kbd className="pointer-events-none hidden h-5 select-none items-center gap-1 rounded border bg-background px-1.5 font-mono text-[10px] font-medium text-muted-foreground sm:flex">
          Ctrl K
        </kbd>
      </button>

      <CommandDialog open={open} onOpenChange={setOpen} filter={navCommandFilter}>
        <CommandInput placeholder={ts('inputPlaceholder')} />
        <CommandList>
          <CommandEmpty>{ts('empty')}</CommandEmpty>
          <CommandGroup heading={ts('heading')}>
            {menus.map((menu) => {
              const Icon = menu.icon
              return (
                <CommandItem
                  key={menu.href}
                  value={`${menu.searchValue} ${menu.href}`}
                  onSelect={() => runCommand(menu.href)}
                  className="justify-between gap-3"
                >
                  <span className="flex min-w-0 items-center gap-2">
                    <Icon className="size-4 text-muted-foreground" />
                    <span className="truncate">{menu.title}</span>
                  </span>
                  <span className="shrink-0 text-xs text-muted-foreground">{menu.path}</span>
                </CommandItem>
              )
            })}
          </CommandGroup>
        </CommandList>
      </CommandDialog>
    </>
  )
}
