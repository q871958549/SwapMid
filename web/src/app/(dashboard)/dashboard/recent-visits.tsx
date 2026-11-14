'use client'

import { useCallback, useState, useSyncExternalStore } from 'react'
import { useTranslations } from 'next-intl'
import { History } from 'lucide-react'
import { PageLink } from '@/components/layout/page-link'
import { useAuthenticatedBrowserStorage } from '@/components/providers/session-identity-provider'
import { SegmentedControl } from '@/components/ui/segmented-control'
import { canAccessPath, findNavItemByPath, translateNavTitle, type NavT } from '@/lib/nav-utils'
import {
  getFrequentVisitsSnapshot,
  getRecentVisitsSnapshot,
  getServerSnapshot,
  subscribeRecentVisits,
} from '@/lib/recent-visits'

// 「最近使用 / 常用功能」快捷行：读稳定账号 principal 作用域（RouteWatcher 落记）。
// 已有高频访问记录（count >= 2）时展示轻量「最近 / 常用」切换胶囊；尚无高频项时静默回退纯「最近使用」。
// useSyncExternalStore 的 server 快照恒为空 → SSR/hydration 首帧不渲染，客户端立即以真实数据补上。

type VisitMode = 'recent' | 'frequent'

function resolveAccessibleNavItems(hrefs: string[], grantedPolicies: Record<string, boolean>) {
  return hrefs
    .filter((href) => canAccessPath(href, grantedPolicies))
    .map((href) => ({ href, item: findNavItemByPath(href) }))
    .filter((x): x is { href: string; item: NonNullable<ReturnType<typeof findNavItemByPath>> } => x.item !== null)
}

export function RecentVisits({ grantedPolicies }: { grantedPolicies: Record<string, boolean> }) {
  const t = useTranslations('Nav') as NavT
  const td = useTranslations('Dashboard')
  const browserStorage = useAuthenticatedBrowserStorage()
  const [mode, setMode] = useState<VisitMode>('recent')

  const subscribe = useCallback(
    (callback: () => void) => subscribeRecentVisits(browserStorage, callback),
    [browserStorage],
  )
  const getRecent = useCallback(
    () => getRecentVisitsSnapshot(browserStorage, 8),
    [browserStorage],
  )
  const getFrequent = useCallback(
    () => getFrequentVisitsSnapshot(browserStorage, 8, 2),
    [browserStorage],
  )

  const recentHrefs = useSyncExternalStore(subscribe, getRecent, getServerSnapshot)
  const frequentHrefs = useSyncExternalStore(subscribe, getFrequent, getServerSnapshot)

  const recentItems = resolveAccessibleNavItems(recentHrefs, grantedPolicies)
  const frequentItems = resolveAccessibleNavItems(frequentHrefs, grantedPolicies)

  if (recentItems.length === 0) return null

  const canSwitchFrequent = frequentItems.length > 0
  const effectiveMode: VisitMode = canSwitchFrequent && mode === 'frequent' ? 'frequent' : 'recent'
  const items = effectiveMode === 'frequent' ? frequentItems : recentItems

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {canSwitchFrequent ? (
        <SegmentedControl<VisitMode>
          aria-label={td('visitViewSwitch')}
          value={effectiveMode}
          onValueChange={setMode}
          className="mr-1 h-7"
          options={[
            { value: 'recent', label: td('recentTab') },
            { value: 'frequent', label: td('frequentTab') },
          ]}
        />
      ) : (
        <span className="mr-1 flex items-center gap-1 text-xs text-muted-foreground">
          <History className="size-3.5" />
          {td('recentlyUsed')}
        </span>
      )}
      {items.map(({ href, item }) => {
        const Icon = item.icon
        return (
          <PageLink
            key={href}
            href={href}
            className="flex items-center gap-1.5 rounded-full border bg-card px-2.5 py-1 text-xs text-muted-foreground transition-colors hover:bg-muted/60 hover:text-foreground"
          >
            <Icon className="size-3.5" />
            {translateNavTitle(item, t)}
          </PageLink>
        )
      })}
    </div>
  )
}
