'use client'

import { useCallback, useMemo, useState, useSyncExternalStore } from 'react'
import { Flame, Search } from 'lucide-react'
import { useOptionalAuthenticatedBrowserStorage } from '@/components/providers/session-identity-provider'
import { Input } from '@/components/ui/input'
import { buildNavSearchDoc, scoreNavSearch } from '@/lib/nav-search'
import { translateNavDescription, translateNavTitle, type NavT } from '@/lib/nav-utils'
import { getFrequentVisitsSnapshot, getServerSnapshot, subscribeRecentVisits } from '@/lib/recent-visits'
import { PageLink } from './page-link'
import type { NavDomain, NavGroup, NavItem } from './nav-config'

const DOMAIN_FILTER_THRESHOLD = 15
const NOOP_UNSUBSCRIBE = () => {}

export function DomainPreviewPane({ domain, groups, onNavigate, t, ts }: {
  domain: NavDomain | null
  groups: NavGroup[]
  onNavigate: () => void
  t: NavT
  ts: NavT
}) {
  const browserStorage = useOptionalAuthenticatedBrowserStorage()
  const [filterState, setFilterState] = useState<{ domainKey: string; text: string }>({
    domainKey: '',
    text: '',
  })

  const subscribe = useCallback(
    (callback: () => void) => (browserStorage ? subscribeRecentVisits(browserStorage, callback) : NOOP_UNSUBSCRIBE),
    [browserStorage],
  )
  const getFrequent = useCallback(
    () => (browserStorage ? getFrequentVisitsSnapshot(browserStorage, 30, 2) : getServerSnapshot()),
    [browserStorage],
  )
  const frequentHrefs = useSyncExternalStore(subscribe, getFrequent, getServerSnapshot)

  const totalItems = useMemo(
    () => groups.reduce((sum, group) => sum + group.items.length, 0),
    [groups],
  )

  const domainFrequentItems = useMemo(() => {
    if (frequentHrefs.length === 0 || groups.length === 0) return []
    const byHref = new Map<string, NavItem>()
    for (const group of groups) {
      for (const item of group.items) {
        if (!byHref.has(item.href)) byHref.set(item.href, item)
      }
    }
    const matched: NavItem[] = []
    for (const href of frequentHrefs) {
      const found = byHref.get(href)
      if (!found) continue
      matched.push(found)
      if (matched.length >= 4) break
    }
    return matched
  }, [frequentHrefs, groups])

  if (!domain || groups.length === 0) {
    return (
      <div className="flex h-full items-center justify-center p-4 text-center text-xs text-muted-foreground">
        {ts('previewHint')}
      </div>
    )
  }

  const query = filterState.domainKey === domain.key ? filterState.text : ''
  const trimmedQuery = query.trim()
  const showFilter = totalItems > DOMAIN_FILTER_THRESHOLD

  const visibleGroups = trimmedQuery
    ? groups
      .map((group) => ({
        ...group,
        items: group.items.filter((item) => scoreNavSearch(buildNavSearchDoc({
          title: translateNavTitle(item, t),
          titleZh: item.title,
          group: translateNavTitle(group, t),
          groupZh: group.title,
          href: item.href,
        }), trimmedQuery) > 0),
      }))
      .filter((group) => group.items.length > 0)
    : groups

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="shrink-0 border-b px-3 py-2">
        <p className="truncate text-[13px] font-medium">{translateNavTitle(domain, t)}</p>
        <p className="truncate text-[11px] text-muted-foreground">{translateNavDescription(domain, t)}</p>
        {showFilter && (
          <div className="relative mt-2">
            <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" />
            <Input
              value={query}
              onChange={(event) => setFilterState({ domainKey: domain.key, text: event.target.value })}
              placeholder={ts('filterDomainItems')}
              aria-label={ts('filterDomainItems')}
              className="h-7 pl-7 text-xs"
            />
          </div>
        )}
      </div>
      <div className="scrollbar-fade min-h-0 flex-1 overflow-y-auto p-1.5">
        {!trimmedQuery && domainFrequentItems.length > 0 && (
          <section className="mb-2 border-b pb-2">
            <h3 className="flex items-center gap-1 px-2 pt-1 pb-1 text-xs font-medium text-muted-foreground">
              <Flame className="size-3 shrink-0 text-muted-foreground" />
              <span className="truncate">{ts('domainFrequent')}</span>
            </h3>
            <div className="flex flex-wrap gap-1 px-1.5">
              {domainFrequentItems.map((item) => {
                const Icon = item.icon
                return (
                  <PageLink
                    key={`freq:${item.href}`}
                    href={item.href}
                    onClick={onNavigate}
                    className="flex max-w-full items-center gap-1 rounded-full border bg-muted/40 px-2 py-0.5 text-xs text-foreground/90 transition-colors hover:bg-accent hover:text-foreground"
                  >
                    <Icon className="size-3 shrink-0 text-muted-foreground" />
                    <span className="truncate">{translateNavTitle(item, t)}</span>
                  </PageLink>
                )
              })}
            </div>
          </section>
        )}
        {visibleGroups.length === 0 ? (
          <p className="px-2 py-6 text-center text-xs text-muted-foreground">{ts('noItemMatch')}</p>
        ) : (
          visibleGroups.map((group) => (
            <section key={group.key} className="mb-1.5">
              <h3 className="flex items-center gap-1.5 px-2 pt-1.5 pb-1 text-[11px] font-medium text-muted-foreground">
                <span className="min-w-0 flex-1 truncate">{translateNavTitle(group, t)}</span>
              </h3>
              {group.items.map((item) => {
                const Icon = item.icon
                return (
                  <PageLink
                    key={item.href}
                    href={item.href}
                    onClick={onNavigate}
                    className="flex items-center gap-2 rounded-md px-2 py-1.5 text-[13px] text-foreground/80 transition-colors hover:bg-accent hover:text-foreground"
                  >
                    <Icon className="size-3.5 shrink-0 text-muted-foreground" />
                    <span className="truncate">{translateNavTitle(item, t)}</span>
                  </PageLink>
                )
              })}
            </section>
          ))
        )}
      </div>
    </div>
  )
}
