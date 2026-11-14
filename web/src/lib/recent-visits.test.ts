import { beforeEach, describe, expect, it } from 'vitest'
import {
  authenticatedBrowserStoragePolicies,
  createAuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'
import {
  getFrequentVisitsSnapshot,
  getRecentVisitsSnapshot,
} from '@/lib/recent-visits'

describe('recent-visits 频次与最近快照', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('按最近访问时间与按访问频次分别输出稳定引用快照，并支持 minCount 过滤', () => {
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'v1',
      storagePrincipalId: 'user-freq-test',
    })

    storage.write(authenticatedBrowserStoragePolicies.recentVisits, {
      '/data-integration/pipelines': { count: 5, lastAt: 1_000 },
      '/enterprise-management/employees': { count: 3, lastAt: 2_000 },
      '/message-bus/topics': { count: 1, lastAt: 3_000 },
      '/system/code-rules': { count: 3, lastAt: 2_500 },
    })

    const recent1 = getRecentVisitsSnapshot(storage, 8)
    const recent2 = getRecentVisitsSnapshot(storage, 8)
    expect(recent1).toEqual([
      '/message-bus/topics',
      '/system/code-rules',
      '/enterprise-management/employees',
      '/data-integration/pipelines',
    ])
    expect(recent2).toBe(recent1)

    const frequent1 = getFrequentVisitsSnapshot(storage, 8, 2)
    const frequent2 = getFrequentVisitsSnapshot(storage, 8, 2)
    expect(frequent1).toEqual([
      '/data-integration/pipelines',
      '/system/code-rules',
      '/enterprise-management/employees',
    ])
    expect(frequent2).toBe(frequent1)
  })
})
