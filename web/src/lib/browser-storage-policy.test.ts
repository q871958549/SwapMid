import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  authenticatedBrowserStoragePolicies,
  browserStorageScopeRegistry,
  createAnonymousDraftRawHandle,
  createAuthenticatedBrowserStorage,
  createIdentityDraftRawHandle,
  readSessionIdentityMarker,
  SESSION_IDENTITY_CHANGE_MARKER_KEY,
  SESSION_IDENTITY_MARKER_KEY,
  writeSessionIdentityChangeMarker,
  writeSessionIdentityMarker,
} from './browser-storage-policy'

const isStringArray = (value: unknown): value is string[] =>
  Array.isArray(value) && value.every((item) => typeof item === 'string')

const isRecord = (value: unknown): value is Record<string, unknown> =>
  Boolean(value) && typeof value === 'object' && !Array.isArray(value)

function createStorage(identityVersion: string, storagePrincipalId = 'principal-a') {
  return createAuthenticatedBrowserStorage({ identityVersion, storagePrincipalId })
}

beforeEach(() => {
  vi.useRealTimers()
  window.localStorage.clear()
  window.sessionStorage.clear()
})

describe('browser storage policy registry', () => {
  it('声明四级作用域，并约束已认证 identity/principal 的存储区域', () => {
    expect(Object.keys(browserStorageScopeRegistry)).toEqual([
      'device',
      'anonymous',
      'principal',
      'identity',
    ])

    expect(Object.keys(authenticatedBrowserStoragePolicies)).toEqual([
      'tabs',
      'scroll',
      'authenticatedFormDraft',
      'apiConsoleHistory',
      'recentVisits',
      'etlExecutionRecovery',
    ])

    for (const policy of Object.values(authenticatedBrowserStoragePolicies)) {
      expect(policy.version).toBeGreaterThan(0)
      expect(policy.ttlMs).toBeGreaterThan(0)
      expect(policy.maxBytes).toBeGreaterThan(0)
      expect(policy.description.length).toBeGreaterThan(0)
      expect(policy.area).toBe(policy.scope === 'identity' ? 'session' : 'local')
      if (policy.scope === 'identity') {
        expect(policy.ttlMs).toBeLessThanOrEqual(8 * 60 * 60 * 1_000)
      }
    }

    expect(authenticatedBrowserStoragePolicies.apiConsoleHistory.maxBytes).toBe(32 * 1_024)
    expect(authenticatedBrowserStoragePolicies.apiConsoleHistory.description).toContain(
      'headers and bodies are forbidden',
    )
    expect(authenticatedBrowserStoragePolicies.etlExecutionRecovery).toMatchObject({
      area: 'local',
      scope: 'principal',
      ttlMs: 30 * 24 * 60 * 60 * 1_000,
      maxBytes: 8 * 1_024,
      preserveInvalid: true,
    })
  })
})

describe('session identity control-plane markers', () => {
  it('通过集中策略读写跨标签身份版本与变化信号', () => {
    expect(writeSessionIdentityMarker('identity-current')).toBe(true)
    expect(readSessionIdentityMarker()).toBe('identity-current')
    expect(window.localStorage.getItem(SESSION_IDENTITY_MARKER_KEY)).toBe('identity-current')

    expect(writeSessionIdentityChangeMarker('signal-generation-1')).toBe(true)
    expect(window.localStorage.getItem(SESSION_IDENTITY_CHANGE_MARKER_KEY))
      .toBe('signal-generation-1')
  })

  it('拒绝并清理畸形身份标记，避免旧页面用坏值驱动恢复', () => {
    for (const invalid of [
      'short',
      ' identity-current',
      'identity current',
      `identity-${'a'.repeat(121)}`,
      'identity-current\nnext',
    ]) {
      expect(writeSessionIdentityMarker(invalid)).toBe(false)
      window.localStorage.setItem(SESSION_IDENTITY_MARKER_KEY, invalid)
      expect(readSessionIdentityMarker()).toBeNull()
      expect(window.localStorage.getItem(SESSION_IDENTITY_MARKER_KEY)).toBeNull()
    }
  })
})

describe('authenticated browser storage isolation', () => {
  it('隔离不同账号，并允许同账号的新 identity 读取 principal 数据', () => {
    const oldIdentity = createStorage('identity-old', 'principal-a')
    const rotatedIdentity = createStorage('identity-new', 'principal-a')
    const otherAccount = createStorage('identity-other', 'principal-b')

    oldIdentity.write(authenticatedBrowserStoragePolicies.tabs, ['old-tab'])
    oldIdentity.write(authenticatedBrowserStoragePolicies.recentVisits, ['/data-integration/pipelines'])

    expect(rotatedIdentity.read(
      authenticatedBrowserStoragePolicies.tabs,
      isStringArray,
    )).toBeNull()
    expect(rotatedIdentity.read(
      authenticatedBrowserStoragePolicies.recentVisits,
      isStringArray,
    )).toEqual(['/data-integration/pipelines'])
    expect(otherAccount.read(
      authenticatedBrowserStoragePolicies.recentVisits,
      isStringArray,
    )).toBeNull()
  })

  it('安全编码 itemKey，避免分隔符逃逸 namespace', () => {
    const storage = createStorage('identity:a/b')
    const key = storage.key(
      authenticatedBrowserStoragePolicies.authenticatedFormDraft,
      '../form:a/b',
    )

    expect(key).toContain('identity%3Aa%2Fb')
    expect(key).toContain('authenticated-form-draft:v1')
    expect(key).toContain('..%2Fform%3Aa%2Fb')
    expect(key).not.toContain('../form')
  })

  it('策略版本进入物理键，旧 reader/initializer/writer 不触碰未来 principal 版本', () => {
    const storage = createStorage('identity-current', 'principal-a')
    const currentPolicy = authenticatedBrowserStoragePolicies.recentVisits
    const futurePolicy = {
      ...currentPolicy,
      version: 2,
    } as unknown as Parameters<typeof storage.key>[0]
    const currentKey = storage.key(currentPolicy)
    const futureKey = storage.key(futurePolicy)
    const futureRaw = JSON.stringify({
      version: 2,
      savedAt: Date.now(),
      value: ['/future'],
    })

    expect(currentKey).toContain('recent-visits:v1')
    expect(futureKey).toContain('recent-visits:v2')
    expect(currentKey).not.toBe(futureKey)
    window.localStorage.setItem(currentKey, '{broken-v1')
    window.localStorage.setItem(futureKey, futureRaw)

    storage.initialize()
    expect(window.localStorage.getItem(currentKey)).toBeNull()
    expect(window.localStorage.getItem(futureKey)).toBe(futureRaw)
    expect(storage.read(currentPolicy, isStringArray)).toBeNull()
    expect(window.localStorage.getItem(futureKey)).toBe(futureRaw)

    expect(storage.write(currentPolicy, ['/current'])).toBe(true)
    expect(window.localStorage.getItem(currentKey)).not.toBeNull()
    expect(window.localStorage.getItem(futureKey)).toBe(futureRaw)
  })

  it('initialize 清同标签旧 identity、保留当前 identity 和所有 principal', () => {
    const oldIdentity = createStorage('identity-old', 'principal-a')
    const currentIdentity = createStorage('identity-current', 'principal-b')
    const oldIdentityKey = oldIdentity.key(authenticatedBrowserStoragePolicies.tabs)
    const currentIdentityKey = currentIdentity.key(authenticatedBrowserStoragePolicies.tabs)
    const firstPrincipalKey = oldIdentity.key(authenticatedBrowserStoragePolicies.recentVisits)
    const secondPrincipalKey = currentIdentity.key(authenticatedBrowserStoragePolicies.recentVisits)

    oldIdentity.write(authenticatedBrowserStoragePolicies.tabs, ['old'])
    currentIdentity.write(authenticatedBrowserStoragePolicies.tabs, ['current'])
    oldIdentity.write(authenticatedBrowserStoragePolicies.recentVisits, ['/a'])
    currentIdentity.write(authenticatedBrowserStoragePolicies.recentVisits, ['/b'])

    currentIdentity.initialize()

    expect(window.sessionStorage.getItem(oldIdentityKey)).toBeNull()
    expect(window.sessionStorage.getItem(currentIdentityKey)).not.toBeNull()
    expect(window.localStorage.getItem(firstPrincipalKey)).not.toBeNull()
    expect(window.localStorage.getItem(secondPrincipalKey)).not.toBeNull()
  })

  it('initialize 回收其他账号已过期的 principal envelope，保留无关 localStorage', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-01-01T00:00:00Z'))
    const oldAccount = createStorage('identity-old', 'principal-old')
    const policy = authenticatedBrowserStoragePolicies.recentVisits
    const oldKey = oldAccount.key(policy)
    oldAccount.write(policy, ['/data-integration/sources'])
    window.localStorage.setItem('unrelated-device-key', 'keep')

    vi.advanceTimersByTime(policy.ttlMs + 1)
    createStorage('identity-current', 'principal-current').initialize()

    expect(window.localStorage.getItem(oldKey)).toBeNull()
    expect(window.localStorage.getItem('unrelated-device-key')).toBe('keep')
  })

  it('迟到的旧闭包只能写回旧 namespace，不能污染新 identity', () => {
    const staleClosure = createStorage('identity-old')
    staleClosure.initialize()
    const current = createStorage('identity-current')
    const staleKey = staleClosure.key(authenticatedBrowserStoragePolicies.tabs)
    const currentKey = current.key(authenticatedBrowserStoragePolicies.tabs)

    current.initialize()
    current.write(authenticatedBrowserStoragePolicies.tabs, ['current'])
    staleClosure.write(authenticatedBrowserStoragePolicies.tabs, ['late-old'])

    expect(staleKey).not.toBe(currentKey)
    expect(window.sessionStorage.getItem(staleKey)).not.toBeNull()
    expect(current.read(authenticatedBrowserStoragePolicies.tabs, isStringArray)).toEqual([
      'current',
    ])
  })
})

describe('legacy cleanup boundary', () => {
  it('首次 read/write/remove/StateStorage 使用会先执行幂等 initialize', () => {
    const factories = [
      (storage: ReturnType<typeof createStorage>) =>
        storage.read(authenticatedBrowserStoragePolicies.tabs, isStringArray),
      (storage: ReturnType<typeof createStorage>) =>
        storage.write(authenticatedBrowserStoragePolicies.tabs, []),
      (storage: ReturnType<typeof createStorage>) =>
        storage.remove(authenticatedBrowserStoragePolicies.tabs),
      (storage: ReturnType<typeof createStorage>) =>
        storage.createStateStorage(authenticatedBrowserStoragePolicies.tabs),
    ]

    for (const [index, useStorage] of factories.entries()) {
      const legacyKey = 'swapmid-api-console-history'
      window.localStorage.setItem(legacyKey, `legacy-${index}`)
      useStorage(createStorage(`identity-${index}`))
      expect(window.localStorage.getItem(legacyKey)).toBeNull()
    }
  })

  it('仅删除明确的已认证旧键，保护匿名流程数据', () => {
    const localKeys = [
      'swapmid-api-console-history',
      'swapmid-recent-visits',
    ]
    const sessionKeys = ['swapmid-tabs', 'swapmid-scroll']
    const protectedAnonymousKeys = [
      'swapmid-draft:unrelated-form',
    ]

    for (const key of localKeys) window.localStorage.setItem(key, 'legacy')
    for (const key of [...sessionKeys, ...protectedAnonymousKeys]) {
      window.sessionStorage.setItem(key, 'legacy')
    }

    createStorage('identity-current').initialize()

    for (const key of localKeys) expect(window.localStorage.getItem(key)).toBeNull()
    for (const key of sessionKeys) {
      expect(window.sessionStorage.getItem(key)).toBeNull()
    }
    for (const key of protectedAnonymousKeys) {
      expect(window.sessionStorage.getItem(key)).toBe('legacy')
    }
  })

  it('storage 跨标签事件会再次清理被旧页面写回的 local 精确键', () => {
    const storage = createStorage('identity-current')
    const dispose = storage.installBoundary()

    window.localStorage.setItem('swapmid-recent-visits', 'late-legacy-value')
    window.dispatchEvent(new StorageEvent('storage', { key: 'another-key' }))

    expect(window.localStorage.getItem('swapmid-recent-visits')).toBeNull()
    dispose()

    window.localStorage.setItem('swapmid-recent-visits', 'after-dispose')
    window.dispatchEvent(new StorageEvent('storage', { key: 'another-key' }))
    expect(window.localStorage.getItem('swapmid-recent-visits')).toBe('after-dispose')
  })
})

describe('envelope validation and limits', () => {
  it('严格读取区分有效值、确实不存在、非法信封与存储不可用', () => {
    const storage = createStorage('identity-current')
    const policy = authenticatedBrowserStoragePolicies.apiConsoleHistory
    const key = storage.key(policy)

    expect(storage.readResult(policy, isRecord)).toEqual({ status: 'absent' })
    expect(storage.write(policy, { status: 'ok' })).toBe(true)
    expect(storage.readResult(policy, isRecord)).toEqual({
      status: 'found',
      value: { status: 'ok' },
    })

    window.sessionStorage.setItem(key, '{not-json')
    expect(storage.readResult(policy, isRecord)).toEqual({ status: 'invalid' })
    expect(window.sessionStorage.getItem(key)).toBeNull()

    const getItem = vi.spyOn(window.sessionStorage.__proto__, 'getItem')
      .mockImplementation(() => { throw new DOMException('denied', 'SecurityError') })
    expect(storage.readResult(policy, isRecord)).toEqual({ status: 'unavailable' })
    getItem.mockRestore()
  })

  it('TTL 到期后返回 null 并清除值', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-01-01T00:00:00Z'))
    const storage = createStorage('identity-current')
    const policy = authenticatedBrowserStoragePolicies.apiConsoleHistory
    const key = storage.key(policy)

    storage.write(policy, { status: 'ok' })
    vi.advanceTimersByTime(policy.ttlMs + 1)

    expect(storage.read(policy, isRecord)).toBeNull()
    expect(window.sessionStorage.getItem(key)).toBeNull()
  })

  it.each([
    ['非法 JSON', '{not-json'],
    ['版本不匹配', JSON.stringify({ version: 99, savedAt: Date.now(), value: ['tab'] })],
    [
      '未来时钟',
      JSON.stringify({ version: 1, savedAt: Date.now() + 60_000, value: ['tab'] }),
    ],
    ['值不满足 validator', JSON.stringify({ version: 1, savedAt: Date.now(), value: 42 })],
  ])('%s envelope 会被清除', (_label, raw) => {
    const storage = createStorage('identity-current')
    const policy = authenticatedBrowserStoragePolicies.tabs
    const key = storage.key(policy)
    window.sessionStorage.setItem(key, raw)

    expect(storage.read(policy, isStringArray)).toBeNull()
    expect(window.sessionStorage.getItem(key)).toBeNull()
  })

  it('拒绝超过 policy 容量的 UTF-8 envelope，并保留上一版有效值', () => {
    const storage = createStorage('identity-current')
    const policy = authenticatedBrowserStoragePolicies.apiConsoleHistory
    const key = storage.key(policy)

    expect(storage.write(policy, { status: 'old' })).toBe(true)
    expect(storage.write(policy, '汉'.repeat(policy.maxBytes))).toBe(false)
    expect(window.sessionStorage.getItem(key)).not.toBeNull()
    expect(storage.read(policy, isRecord)).toEqual({ status: 'old' })
  })

  it('setItem 配额失败时不主动删除上一版有效值', () => {
    const storage = createStorage('identity-current')
    const policy = authenticatedBrowserStoragePolicies.apiConsoleHistory
    const key = storage.key(policy)
    storage.write(policy, { status: 'last-known-good' })
    const rawBefore = window.sessionStorage.getItem(key)
    const setItemSpy = vi.spyOn(window.sessionStorage.__proto__, 'setItem')
      .mockImplementation(() => {
        throw new DOMException('quota exceeded', 'QuotaExceededError')
      })

    expect(storage.write(policy, { status: 'new' })).toBe(false)
    expect(window.sessionStorage.getItem(key)).toBe(rawBefore)

    setItemSpy.mockRestore()
  })
})

describe('state storage and raw draft handles', () => {
  const isSerializedCounter = (value: string) => {
    try {
      const parsed = JSON.parse(value) as unknown
      return isRecord(parsed) && typeof parsed.count === 'number'
    } catch {
      return false
    }
  }

  it('StateStorage 拒绝非法新写并保留旧值，非法存量在读取时清除', () => {
    const storage = createStorage('identity-current')
    const policy = authenticatedBrowserStoragePolicies.tabs
    const stateStorage = storage.createStateStorage(policy, isSerializedCounter)
    const key = storage.key(policy, 'tab-store')

    stateStorage.setItem('tab-store', '{"count":1}')
    expect(stateStorage.getItem('tab-store')).toBe('{"count":1}')

    stateStorage.setItem('tab-store', '{"wrong":true}')
    expect(stateStorage.getItem('tab-store')).toBe('{"count":1}')

    storage.write(policy, '{"wrong":true}', 'tab-store')
    expect(stateStorage.getItem('tab-store')).toBeNull()
    expect(window.sessionStorage.getItem(key)).toBeNull()
  })

  it('匿名草稿保持原始键和 raw string，认证初始化不会误删', () => {
    const anonymous = createAnonymousDraftRawHandle('form:token-a')

    expect(anonymous.key).toBe('swapmid-draft:form:token-a')
    anonymous.setItem('{"answer":"A"}')
    createStorage('identity-current').initialize()

    expect(anonymous.getItem()).toBe('{"answer":"A"}')
    anonymous.removeItem()
    expect(anonymous.getItem()).toBeNull()
  })

  it('身份草稿 handle 在 identity namespace 中统一读写 raw JSON string', () => {
    const storage = createStorage('identity-current')
    const draft = createIdentityDraftRawHandle(storage, 'pipeline-draft:42', (value) => {
      try {
        return isRecord(JSON.parse(value))
      } catch {
        return false
      }
    })

    expect(draft.key).toBe(storage.key(
      authenticatedBrowserStoragePolicies.authenticatedFormDraft,
      'pipeline-draft:42',
    ))
    draft.setItem('{"name":"draft"}')
    expect(draft.getItem()).toBe('{"name":"draft"}')

    draft.setItem('{bad-json')
    expect(draft.getItem()).toBe('{"name":"draft"}')
  })
})
