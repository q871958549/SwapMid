import { type NavDomain, type NavGroup, type NavItem } from '@/components/layout/nav-config'
import { navCatalog } from '@/lib/nav-catalog'

export const DASHBOARD_PATH = '/dashboard'

export type NavT = (key: string, values?: Record<string, string | number>) => string

export function translateNavTitle(item: { title: string; titleKey?: string }, t?: NavT): string {
  if (t && item.titleKey) return t(item.titleKey)
  return item.title
}

export function translateNavDescription(domain: { description: string; descriptionKey?: string }, t?: NavT): string {
  if (t && domain.descriptionKey) return t(domain.descriptionKey)
  return domain.description
}

const EXTRA_ROUTE_TITLES: Record<string, { key: string; title: string }> = {
  '/profile': { key: 'pages.profile', title: '个人中心' },
  '/system/scheduled-jobs/executions': { key: 'pages.scheduledJobExecutions', title: '执行历史' },
}

export function getPageTitle(pathname: string, t?: NavT): string {
  if (pathname === DASHBOARD_PATH) return t ? t('pages.dashboard') : '仪表盘'

  const extra = EXTRA_ROUTE_TITLES[pathname]
  if (extra) return t ? t(extra.key) : extra.title

  const item = findNavItemByPath(pathname)
  if (item) return translateNavTitle(item, t)

  const last = pathname.split('/').filter(Boolean).pop()
  return last ? decodeURIComponent(last) : (t ? t('pages.page') : '页面')
}

export function resolveTabTitle(tab: { key: string; title: string }, t?: NavT): string {
  const staticBaseline = getPageTitle(tab.key)
  if (tab.title !== staticBaseline) return tab.title
  return getPageTitle(tab.key, t)
}

export function pathMatchesHref(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(`${href}/`)
}

export function findNavItemByPath(pathname: string): NavItem | null {
  let best: NavItem | null = null
  for (const group of navCatalog().groups) {
    for (const item of group.items) {
      if (pathMatchesHref(pathname, item.href)) {
        if (!best || item.href.length > best.href.length) {
          best = item
        }
      }
    }
  }
  return best
}

function longestCommonPathPrefix(hrefs: readonly string[]): string | null {
  if (hrefs.length === 0) return null
  const parts = hrefs.map((href) => href.split('/').filter(Boolean))
  const first = parts[0]
  if (!first?.length) return null
  let count = 0
  while (count < first.length && parts.every((p) => p[count] === first[count])) {
    count++
  }
  if (count === 0) return null
  return `/${first.slice(0, count).join('/')}`
}

let prefixCache: { groups: readonly NavGroup[]; map: ReadonlyMap<string, string> } | null = null

function uniquePrefixByGroupKey(): ReadonlyMap<string, string> {
  const groups = navCatalog().groups
  if (prefixCache?.groups === groups) return prefixCache.map
  const map = new Map<string, string>()
  for (const group of groups) {
    const prefix = longestCommonPathPrefix(group.items.map((item) => item.href))
    if (!prefix) continue
    const conflicts = groups.some(
      (other) =>
        other.key !== group.key
        && other.items.some((item) => item.href === prefix || item.href.startsWith(`${prefix}/`)),
    )
    if (!conflicts) map.set(group.key, prefix)
  }
  prefixCache = { groups, map }
  return map
}

export function uniqueGroupPathPrefix(group: Pick<NavGroup, 'key'>): string | null {
  return uniquePrefixByGroupKey().get(group.key) ?? null
}

export function findSidebarHighlightItem(pathname: string): NavItem | null {
  return findNavItemByPath(pathname)
}

export function findNavGroupByPath(pathname: string, groups: readonly NavGroup[] = navCatalog().groups): NavGroup | null {
  const highlight = findSidebarHighlightItem(pathname)
  if (highlight) {
    const owner = groups.find((group) => group.items.some((item) => item.href === highlight.href))
    if (owner) return owner
  }

  let best: { group: NavGroup; prefix: string } | null = null
  for (const group of groups) {
    const prefix = uniqueGroupPathPrefix(group)
    if (!prefix || !pathMatchesHref(pathname, prefix)) continue
    if (!best || prefix.length > best.prefix.length) {
      best = { group, prefix }
    }
  }
  return best?.group ?? null
}

export function isPathInNavGroup(pathname: string, group: NavGroup): boolean {
  return findNavGroupByPath(pathname)?.key === group.key
}

export function findNavDomainKeyByPath(pathname: string): string | null {
  const group = findNavGroupByPath(pathname)
  if (!group) return null
  return navCatalog().domains.find((domain) => domain.groupKeys.includes(group.key))?.key ?? null
}

export function isNavItemVisible(item: NavItem, grantedPolicies: Record<string, boolean>): boolean {
  return item.permissionAny
    ? item.permissionAny.some((permission) => grantedPolicies[permission] === true)
    : item.authenticated === true || (!!item.permission && grantedPolicies[item.permission] === true)
}

export function canAccessPath(pathname: string, grantedPolicies: Record<string, boolean>): boolean {
  pathname = pathname.split(/[?#]/, 1)[0]
  if (!pathname || pathname === DASHBOARD_PATH) return true
  const item = findNavItemByPath(pathname)
  if (!item) return true
  return isNavItemVisible(item, grantedPolicies)
}

export interface ResolvedDomain {
  domain: NavDomain
  groups: NavGroup[]
}

export function alsoInGroupsOf(domainKey: string): NavGroup[] {
  const own = navCatalog().domains.find((domain) => domain.key === domainKey)?.groupKeys ?? []
  return navCatalog().groups
    .filter((group) => !own.includes(group.key))
    .map((group) => ({
      ...group,
      key: `${group.key}@${domainKey}`,
      alsoOf: group.key,
      items: group.items.filter((item) => item.alsoIn?.includes(domainKey)),
    }))
    .filter((group) => group.items.length > 0)
}

export function allAlsoInGroups(): NavGroup[] {
  return navCatalog().domains.flatMap((domain) => alsoInGroupsOf(domain.key))
}

export function resolveDomains(
  grantedPolicies: Record<string, boolean>,
  options: { includeAlsoIn?: boolean } = {},
): ResolvedDomain[] {
  const result: ResolvedDomain[] = []

  for (const domain of navCatalog().domains) {
    const groups: NavGroup[] = []
    for (const key of domain.groupKeys) {
      const group = navCatalog().groups.find((g) => g.key === key)
      if (!group) continue
      const items = group.items.filter((item) => isNavItemVisible(item, grantedPolicies))
      if (items.length > 0) groups.push({ ...group, items })
    }
    if (options.includeAlsoIn && groups.length > 0) {
      for (const group of alsoInGroupsOf(domain.key)) {
        const items = group.items.filter((item) => isNavItemVisible(item, grantedPolicies))
        if (items.length > 0) groups.push({ ...group, items })
      }
    }
    if (groups.length > 0) result.push({ domain, groups })
  }

  return result
}
