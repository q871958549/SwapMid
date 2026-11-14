'use client'

import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { useRouter } from 'next/navigation'
import { useOptionalAuthenticatedBrowserStorage } from '@/components/providers/session-identity-provider'
import { toast } from '@/components/ui/use-toast'
import { resolveStoredPagePath } from '@/stores/use-tab-store'
import { DASHBOARD_PATH, findNavDomainKeyByPath, type NavT, type ResolvedDomain } from '@/lib/nav-utils'
import { NAV_ORDER_MAX_LENGTH } from '@/lib/nav-order'
import { getRecentVisitsSnapshot, getServerSnapshot, subscribeRecentVisits } from '@/lib/recent-visits'
import {
  deriveRecentDomainKeys, findResumeHref, parseDomainPrefs, serializeDomainPrefs, splitDomains, type DomainPrefs,
} from '@/lib/domain-prefs'
import { navDomains, WORKBENCH_DOMAIN } from './nav-config'

const DEFAULT_DOMAIN_KEYS = navDomains.map((domain) => domain.key)
// 最近访问最多 30 条（recent-visits.ts 的 MAX_ENTRIES），全取：要在其中找每个域最近的一页
const RECENT_LIMIT = 30
const EMPTY_HREFS: string[] = []

interface UseDomainNavOptions {
  domains: ResolvedDomain[]
  grantedPolicies: Record<string, boolean>
  initialDomainPrefs: string | null | undefined
  activeDomainKey: string
  /** 只换侧栏、不跳页时用（与切域器原行为一致）。 */
  onPick: (key: string) => void
  /** 是否监听 Alt+1…9。抽屉侧栏与常驻栏可能同时挂载，只让常驻栏监听，避免一次按键切两次。 */
  shortcuts: boolean
  /** 为 false 时排序只留在本地，不请求导航偏好接口。 */
  persistRemote?: boolean
  ts: NavT
}

/**
 * 业务域栏的可操作性（蓝图 P1-1/2/3/6）：个人排序与隐藏、最近用过的域、切域即回到上次页面、Alt+数字快捷键。
 * 从 sidebar.tsx 拆出：侧栏组件本身已近千行，这几项又彼此共用「最近访问」和「个人顺序」两份数据。
 */
export function useDomainNav({
  domains, grantedPolicies, initialDomainPrefs, activeDomainKey, onPick, shortcuts, persistRemote = false, ts,
}: UseDomainNavOptions) {
  const router = useRouter()
  const [prefs, setPrefs] = useState<DomainPrefs>(() => parseDomainPrefs(initialDomainPrefs))

  // 测试与匿名壳层没有 SessionIdentityProvider：拿不到存储就当没有最近访问记录
  const storage = useOptionalAuthenticatedBrowserStorage()
  const subscribe = useCallback(
    (callback: () => void) => (storage ? subscribeRecentVisits(storage, callback) : () => {}),
    [storage],
  )
  const getSnapshot = useCallback(
    () => (storage ? getRecentVisitsSnapshot(storage, RECENT_LIMIT) : EMPTY_HREFS),
    [storage],
  )
  const recentHrefs = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot)

  const { shown, hidden } = splitDomains(domains, prefs, (d) => d.domain.key)
  const recentDomainKeys = deriveRecentDomainKeys(recentHrefs, shown.concat(hidden).map((d) => d.domain.key), activeDomainKey)

  function goToDomain(key: string) {
    if (key === WORKBENCH_DOMAIN.key) {
      onPick(key)
      router.push(resolveStoredPagePath(DASHBOARD_PATH))
      return
    }
    onPick(key)
    // 已经在这个域的页面上：只是把侧栏切回来，不跳页
    if (key === findNavDomainKeyByPath(window.location.pathname)) return
    // 决策 Q1：回到该域上次访问的页面；页签还开着时 resolveStoredPagePath 会带回它的筛选 query
    const resumeHref = findResumeHref(recentHrefs, key, grantedPolicies)
    if (resumeHref) router.push(resolveStoredPagePath(resumeHref))
  }

  // Alt+N 对应「常用」段第 N 个域；用 e.code 而不是 e.key，免受键盘布局与 Alt 组合字符影响
  const goToDomainRef = useRef(goToDomain)
  const shownKeysRef = useRef<string[]>([])
  useEffect(() => {
    goToDomainRef.current = goToDomain
    shownKeysRef.current = shown.map((d) => d.domain.key)
  })
  useEffect(() => {
    if (!shortcuts) return
    function onKeyDown(e: KeyboardEvent) {
      if (!e.altKey || e.ctrlKey || e.metaKey || e.shiftKey || e.repeat) return
      const match = /^Digit([1-9])$/.exec(e.code)
      if (!match) return
      const key = shownKeysRef.current[Number(match[1]) - 1]
      if (!key) return
      e.preventDefault()
      goToDomainRef.current(key)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [shortcuts])

  function saveDomainPrefs(next: DomainPrefs): boolean {
    const value = serializeDomainPrefs(next, DEFAULT_DOMAIN_KEYS)
    if (value.length > NAV_ORDER_MAX_LENGTH) {
      toast({ variant: 'destructive', title: ts('domainPrefsSaveFailed'), description: ts('orderTooLong') })
      return false
    }
    const previous = prefs
    setPrefs(parseDomainPrefs(value))
    if (!persistRemote) return true
    void fetch('/api/proxy/nav-preference/domain-prefs', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ value }),
    })
      .then((res) => { if (!res.ok) throw new Error(String(res.status)) })
      .catch(() => {
        setPrefs(previous)
        toast({ variant: 'destructive', title: ts('domainPrefsSaveFailed'), description: ts('orderSaveRetry') })
      })
    return true
  }

  return { prefs, shownDomains: shown, hiddenDomains: hidden, recentDomainKeys, goToDomain, saveDomainPrefs }
}
