'use client'

import { useEffect, useRef } from 'react'
import { usePathname } from 'next/navigation'
import {
  configureTabStorePersistence,
  useTabStore,
} from '@/stores/use-tab-store'
import { canAccessPath, getPageTitle } from '@/lib/nav-utils'
import { recordVisit } from '@/lib/recent-visits'
import { reportNavUsage } from '@/lib/nav-usage'
import { useAuthenticatedBrowserStorage } from '@/components/providers/session-identity-provider'

interface RouteWatcherProps {
  grantedPolicies: Record<string, boolean>
  /** 是否上报导航使用频次。 */
  reportUsage?: boolean
}

type BrowserStorage = ReturnType<typeof useAuthenticatedBrowserStorage>

interface PendingRoute {
  browserStorage: BrowserStorage
  grantedPolicies: Record<string, boolean>
  pathname: string
  reportUsage: boolean
}

function openCurrentRoute({
  browserStorage,
  grantedPolicies,
  pathname,
  reportUsage,
}: PendingRoute): void {
  if (!canAccessPath(pathname, grantedPolicies)) return
  const query = typeof window !== 'undefined' ? window.location.search : ''
  useTabStore.getState().openTab({
    key: pathname,
    title: getPageTitle(pathname),
    path: pathname + query,
  })
  void recordVisit(browserStorage, pathname)
  if (reportUsage) reportNavUsage(pathname)
}

export function RouteWatcher({ grantedPolicies, reportUsage = true }: RouteWatcherProps) {
  const pathname = usePathname()
  const browserStorage = useAuthenticatedBrowserStorage()
  const latestRouteRef = useRef<PendingRoute | null>(null)
  const hydratedStorageRef = useRef<BrowserStorage | null>(null)

  // 恢复未完成时只记录最新路由；同一 identity 已恢复后，后续路由变化才可直接开页签。
  // 身份切换的这一帧 hydratedStorageRef 仍指向旧闭包，因此不会把旧模块态写进新 identity。
  useEffect(() => {
    const route = { browserStorage, grantedPolicies, pathname, reportUsage }
    latestRouteRef.current = route
    if (hydratedStorageRef.current === browserStorage) openCurrentRoute(route)
  }, [browserStorage, grantedPolicies, pathname, reportUsage])

  // configure 在 no-op 持久层下同步清空旧 identity 模块态；随后恢复当前 identity 的存档，
  // 最后补开恢复期间记录的最新路由，避免 openTab 抢跑后又被 rehydrate 覆盖。
  useEffect(() => {
    let cancelled = false
    hydratedStorageRef.current = null
    configureTabStorePersistence(browserStorage)
    void Promise.resolve(useTabStore.persist.rehydrate()).then(() => {
      if (cancelled) return
      hydratedStorageRef.current = browserStorage
      const latestRoute = latestRouteRef.current
      if (latestRoute?.browserStorage === browserStorage) openCurrentRoute(latestRoute)
    })

    return () => {
      cancelled = true
      if (hydratedStorageRef.current === browserStorage) hydratedStorageRef.current = null
    }
  }, [browserStorage])

  return null
}
