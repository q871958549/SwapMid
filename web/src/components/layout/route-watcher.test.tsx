import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, waitFor } from '@testing-library/react'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import {
  authenticatedBrowserStoragePolicies,
  createAuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'
import {
  configureTabStorePersistence,
  HOME_KEY,
  HOME_TAB,
  useTabStore,
} from '@/stores/use-tab-store'
import { RouteWatcher } from './route-watcher'

const mockUsePathname = vi.fn<() => string>()
vi.mock('next/navigation', () => ({
  usePathname: () => mockUsePathname(),
}))
vi.mock('@/lib/nav-utils', () => ({
  canAccessPath: () => true,
  getPageTitle: (pathname: string) => `Title ${pathname}`,
}))
vi.mock('@/lib/recent-visits', () => ({
  recordVisit: vi.fn(),
}))
// 使用量上报会真发请求、也依赖完整的 nav-utils：这里只验「打开页面时上报了」
const mockReportNavUsage = vi.fn()
vi.mock('@/lib/nav-usage', () => ({
  reportNavUsage: (pathname: string) => mockReportNavUsage(pathname),
}))

function createStorage(identityVersion: string) {
  return createAuthenticatedBrowserStorage({
    identityVersion,
    storagePrincipalId: `principal-${identityVersion}`,
  })
}

function watcher(identityVersion: string) {
  return (
    <SessionIdentityProvider
      identityVersion={identityVersion}
      storagePrincipalId={`principal-${identityVersion}`}
    >
      <RouteWatcher grantedPolicies={{}} />
    </SessionIdentityProvider>
  )
}

beforeEach(() => {
  window.localStorage.clear()
  window.sessionStorage.clear()
  mockUsePathname.mockReturnValue('/account-a')

  // 通过生产 configure 语义复位跨测试模块单例，不直接 setState 掩盖身份切换边界。
  configureTabStorePersistence(createStorage('test-reset'))
  window.sessionStorage.clear()
})

describe('RouteWatcher identity rehydrate ordering', () => {
  it('恢复新 identity 存档后再打开当前路由，旧模块页签不会混入', async () => {
    const { rerender } = render(watcher('identity-a'))

    await waitFor(() => {
      expect(useTabStore.getState().tabs.map((tab) => tab.key)).toContain('/account-a')
    })
    expect(mockReportNavUsage).toHaveBeenCalledWith('/account-a')

    const nextStorage = createStorage('identity-b')
    nextStorage.write(
      authenticatedBrowserStoragePolicies.tabs,
      JSON.stringify({
        state: {
          tabs: [
            HOME_TAB,
            { key: '/saved-b', title: 'Saved B', path: '/saved-b?filter=mine' },
          ],
          activeKey: '/saved-b',
        },
        version: 0,
      }),
      'state',
    )

    mockUsePathname.mockReturnValue('/account-b')
    window.history.replaceState(null, '', '/account-b?view=current')
    rerender(watcher('identity-b'))

    await waitFor(() => {
      expect(useTabStore.getState().tabs.map((tab) => tab.key)).toEqual([
        HOME_KEY,
        '/saved-b',
        '/account-b',
      ])
    })

    const state = useTabStore.getState()
    expect(state.activeKey).toBe('/account-b')
    expect(state.tabs.some((tab) => tab.key === '/account-a')).toBe(false)
    expect(state.tabs.find((tab) => tab.key === '/account-b')?.path).toBe(
      '/account-b?view=current',
    )
  })

  it('关闭使用量上报时不发导航度量', async () => {
    mockReportNavUsage.mockClear()
    render(
      <SessionIdentityProvider identityVersion="identity-a" storagePrincipalId="principal-identity-a">
        <RouteWatcher grantedPolicies={{}} reportUsage={false} />
      </SessionIdentityProvider>,
    )
    await waitFor(() => {
      expect(useTabStore.getState().tabs.map((tab) => tab.key)).toContain('/account-a')
    })
    expect(mockReportNavUsage).not.toHaveBeenCalled()
  })
})
