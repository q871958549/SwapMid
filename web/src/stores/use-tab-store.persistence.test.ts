import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  authenticatedBrowserStoragePolicies,
  createAuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'
import {
  configureTabStorePersistence,
  HOME_KEY,
  HOME_TAB,
  useTabStore,
} from './use-tab-store'

function createStorage(identityVersion: string, storagePrincipalId: string) {
  return createAuthenticatedBrowserStorage({ identityVersion, storagePrincipalId })
}

function resetTabs() {
  useTabStore.setState({ tabs: [HOME_TAB], activeKey: HOME_KEY })
}

beforeEach(() => {
  vi.useRealTimers()
  window.localStorage.clear()
  window.sessionStorage.clear()
  resetTabs()
})

describe('useTabStore 身份作用域持久化', () => {
  it('切换身份先同步清空模块态，再恢复新 identity 存档且不串入旧页签', async () => {
    const first = createStorage('identity-a', 'sp1-account-a')
    configureTabStorePersistence(first)
    useTabStore.getState().openTab({
      key: '/account-a',
      title: 'Account A',
      path: '/account-a?status=pending',
    })

    const firstKey = first.key(authenticatedBrowserStoragePolicies.tabs, 'state')
    expect(window.sessionStorage.getItem(firstKey)).not.toBeNull()

    const second = createStorage('identity-b', 'sp1-account-b')
    const secondTabs = [
      HOME_TAB,
      { key: '/account-b', title: 'Account B', path: '/account-b?view=mine' },
    ]
    second.write(
      authenticatedBrowserStoragePolicies.tabs,
      JSON.stringify({
        state: { tabs: secondTabs, activeKey: '/account-b' },
        version: 0,
      }),
      'state',
    )

    configureTabStorePersistence(second)
    // configure 的同步边界必须已经清掉旧模块态；测试不得手工 reset 掩盖窗口。
    expect(useTabStore.getState().tabs).toEqual([HOME_TAB])
    expect(useTabStore.getState().activeKey).toBe(HOME_KEY)

    await useTabStore.persist.rehydrate()

    expect(useTabStore.getState().tabs).toEqual(secondTabs)
    expect(useTabStore.getState().activeKey).toBe('/account-b')
    expect(window.sessionStorage.getItem(firstKey)).toBeNull()
    expect(window.sessionStorage.getItem(
      second.key(authenticatedBrowserStoragePolicies.tabs, 'state'),
    )).not.toBeNull()
  })

  it('损坏或形状非法的 Zustand 存档会被拒绝并清除', async () => {
    const storage = createStorage('identity-current', 'sp1-current')
    configureTabStorePersistence(storage)

    const policy = authenticatedBrowserStoragePolicies.tabs
    storage.write(
      policy,
      JSON.stringify({
        state: {
          tabs: 'not-an-array',
          activeKey: '/dashboard',
        },
        version: 0,
      }),
      'state',
    )

    await useTabStore.persist.rehydrate()

    expect(useTabStore.getState().tabs).toEqual([HOME_TAB])
    expect(window.sessionStorage.getItem(storage.key(policy, 'state'))).toBeNull()
  })
})
