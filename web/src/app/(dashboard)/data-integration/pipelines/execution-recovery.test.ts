import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createAuthenticatedBrowserStorage } from '@/lib/browser-storage-policy'
import {
  ETL_EXECUTION_RECOVERY_POLICY,
  MAX_ETL_EXECUTION_RECOVERY_ENTRIES,
  classifyEtlExecutionPayload,
  isEtlExecutionRecoveryState,
  persistEtlExecutionRecovery,
  readEtlExecutionRecovery,
  readEtlExecutionRecoveryResult,
  subscribeEtlExecutionRecovery,
  upsertEtlExecutionRecovery,
  verifyEtlExecutionRun,
  type EtlExecutionRecoveryEntry,
} from './execution-recovery'

const PIPELINE_ID = '11111111-1111-4111-8111-111111111111'
const RUN_ID = '22222222-2222-4222-8222-222222222222'
const CASE_PIPELINE_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const CASE_RUN_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'

function createStorage(identityVersion: string, storagePrincipalId: string) {
  return createAuthenticatedBrowserStorage({ identityVersion, storagePrincipalId })
}

function entry(overrides: Partial<EtlExecutionRecoveryEntry> = {}): EtlExecutionRecoveryEntry {
  return {
    pipelineId: PIPELINE_ID,
    runId: RUN_ID,
    disposition: 'pending',
    ...overrides,
  }
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  window.localStorage.clear()
  window.sessionStorage.clear()
})

describe('ETL 执行恢复存储', () => {
  it('同 principal 刷新或 identity 轮换后恢复稳定 RunId 与未知态封锁', () => {
    const firstDocument = createStorage('identity-a1', 'sp1-account-a')
    expect(persistEtlExecutionRecovery(firstDocument, [entry({ disposition: 'outcome-unknown' })]))
      .toBe(true)

    const refreshedDocument = createStorage('identity-a2', 'sp1-account-a')
    expect(readEtlExecutionRecovery(refreshedDocument)).toEqual([
      entry({ disposition: 'outcome-unknown' }),
    ])
  })

  it('读取历史大写 UUID 时规范为小写，避免同一管道生成第二个 RunId', () => {
    const storage = createStorage('identity-a1', 'sp1-account-a')
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)
    window.localStorage.setItem(key, JSON.stringify({
      version: ETL_EXECUTION_RECOVERY_POLICY.version,
      savedAt: Date.now(),
      value: {
        entries: [{
          pipelineId: CASE_PIPELINE_ID.toUpperCase(),
          runId: CASE_RUN_ID.toUpperCase(),
          disposition: 'pending',
        }],
      },
    }))

    expect(readEtlExecutionRecovery(storage)).toEqual([{
      pipelineId: CASE_PIPELINE_ID,
      runId: CASE_RUN_ID,
      disposition: 'pending',
    }])
  })

  it('同账号 identity 变化仍恢复，不同账号不可见', () => {
    const firstIdentity = createStorage('identity-a1', 'sp1-account-a')
    expect(persistEtlExecutionRecovery(firstIdentity, [entry()])).toBe(true)
    const firstKey = firstIdentity.key(ETL_EXECUTION_RECOVERY_POLICY)

    const rotatedIdentity = createStorage('identity-a2', 'sp1-account-a')
    expect(rotatedIdentity.key(ETL_EXECUTION_RECOVERY_POLICY)).toBe(firstKey)
    expect(readEtlExecutionRecovery(rotatedIdentity)).toEqual([entry()])
    expect(window.localStorage.getItem(firstKey)).not.toBeNull()

    const otherAccount = createStorage('identity-b1', 'sp1-account-b')
    expect(readEtlExecutionRecovery(otherAccount)).toEqual([])
  })

  it.each([
    { entries: [{ ...entry(), pipelineId: 'not-a-uuid' }] },
    { entries: [{ ...entry(), runId: '00000000-0000-0000-0000-000000000000' }] },
    { entries: [{ ...entry(), disposition: 'retry' }] },
    { entries: [{ ...entry(), extra: true }] },
    { entries: [entry(), entry()] },
  ])('严格拒绝非法、空 UUID、未知 disposition、额外字段与重复项：%j', (value) => {
    expect(isEtlExecutionRecoveryState(value)).toBe(false)
  })

  it('超过 32 条时在物化新恢复项前拒绝，不驱逐已有安全状态', () => {
    const current = Array.from({ length: MAX_ETL_EXECUTION_RECOVERY_ENTRIES }, (_, index) =>
      entry({
        pipelineId: `11111111-1111-4111-8111-${String(index + 1).padStart(12, '0')}`,
        runId: `22222222-2222-4222-8222-${String(index + 1).padStart(12, '0')}`,
      }))
    expect(isEtlExecutionRecoveryState({ entries: current })).toBe(true)
    expect(upsertEtlExecutionRecovery(current, entry({
      pipelineId: '33333333-3333-4333-8333-333333333333',
      runId: '44444444-4444-4444-8444-444444444444',
    }))).toBeNull()
    expect(current).toHaveLength(MAX_ETL_EXECUTION_RECOVERY_ENTRIES)
  })

  it('非法 envelope 作为 fail-closed 证据保留，结构合法且过期的 envelope 按 TTL 删除', () => {
    const storage = createStorage('identity-current', 'sp1-current')
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)

    window.localStorage.setItem(key, JSON.stringify({
      version: ETL_EXECUTION_RECOVERY_POLICY.version,
      savedAt: Date.now(),
      value: { entries: [{ ...entry(), disposition: 'invalid' }] },
    }))
    expect(readEtlExecutionRecovery(storage)).toEqual([])
    expect(window.localStorage.getItem(key)).not.toBeNull()

    window.localStorage.setItem(key, JSON.stringify({
      version: ETL_EXECUTION_RECOVERY_POLICY.version,
      savedAt: Date.now() - ETL_EXECUTION_RECOVERY_POLICY.ttlMs - 1,
      value: { entries: [entry()] },
    }))
    expect(readEtlExecutionRecovery(storage)).toEqual([])
    expect(window.localStorage.getItem(key)).toBeNull()

    window.localStorage.setItem(key, JSON.stringify({
      version: ETL_EXECUTION_RECOVERY_POLICY.version,
      savedAt: Date.now() - ETL_EXECUTION_RECOVERY_POLICY.ttlMs - 1,
      value: { entries: [{ ...entry(), disposition: 'invalid' }] },
    }))
    expect(readEtlExecutionRecoveryResult(storage)).toEqual({
      status: 'available',
      entries: [],
    })
    expect(window.localStorage.getItem(key)).toBeNull()
  })

  it('严格恢复读取不会把存储异常或非法信封伪装成空恢复集', () => {
    const storage = createStorage('identity-current', 'sp1-current')
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)

    expect(readEtlExecutionRecoveryResult(storage)).toEqual({
      status: 'available',
      entries: [],
    })
    window.localStorage.setItem(key, '{not-json')
    expect(readEtlExecutionRecoveryResult(storage)).toEqual({ status: 'unavailable' })

    vi.spyOn(window.localStorage.__proto__, 'getItem')
      .mockImplementation(() => { throw new DOMException('denied', 'SecurityError') })
    expect(readEtlExecutionRecoveryResult(storage)).toEqual({ status: 'unavailable' })
  })

  it('首次访问时初始化清理的非法恢复信封仍报告 unavailable，而不是伪装成 absent', () => {
    const storage = createStorage('identity-current', 'sp1-current')
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)
    window.localStorage.setItem(key, '{not-json')

    expect(readEtlExecutionRecoveryResult(storage)).toEqual({ status: 'unavailable' })
    expect(window.localStorage.getItem(key)).toBe('{not-json')
  })

  it('非法恢复信封跨账号初始化和跨标签保持 fail closed，直到合法写入或显式删除', () => {
    const firstTab = createStorage('identity-b1', 'sp1-account-b')
    const key = firstTab.key(ETL_EXECUTION_RECOVERY_POLICY)
    window.localStorage.setItem(key, '{not-json')

    createStorage('identity-a1', 'sp1-account-a').initialize()
    expect(window.localStorage.getItem(key)).toBe('{not-json')
    expect(readEtlExecutionRecoveryResult(firstTab)).toEqual({ status: 'unavailable' })

    const secondTab = createStorage('identity-b2', 'sp1-account-b')
    expect(readEtlExecutionRecoveryResult(secondTab)).toEqual({ status: 'unavailable' })
    expect(window.localStorage.getItem(key)).toBe('{not-json')

    expect(persistEtlExecutionRecovery(secondTab, [entry()])).toBe(true)
    expect(readEtlExecutionRecoveryResult(firstTab)).toEqual({
      status: 'available',
      entries: [entry()],
    })

    secondTab.remove(ETL_EXECUTION_RECOVERY_POLICY)
    expect(readEtlExecutionRecoveryResult(firstTab)).toEqual({
      status: 'available',
      entries: [],
    })
  })

  it('后续写失败时保留上一份持久化 last-known-good', () => {
    const storage = createStorage('identity-current', 'sp1-current')
    expect(persistEtlExecutionRecovery(storage, [entry()])).toBe(true)
    const before = readEtlExecutionRecovery(storage)
    const setItem = vi.spyOn(window.localStorage.__proto__, 'setItem')
      .mockImplementation(() => { throw new DOMException('quota', 'QuotaExceededError') })

    expect(persistEtlExecutionRecovery(storage, [entry({ disposition: 'outcome-unknown' })]))
      .toBe(false)
    setItem.mockRestore()
    expect(readEtlExecutionRecovery(storage)).toEqual(before)
  })

  it.each([
    [2, 'terminal'],
    [3, 'terminal'],
    [4, 'terminal'],
    [5, 'outcome-unknown'],
    [0, 'retain'],
    [1, 'retain'],
  ] as const)('GET 原 RunId 状态 %s 的核验结论为 %s', async (status, expected) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: RUN_ID,
      pipelineId: PIPELINE_ID,
      status,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })))

    await expect(verifyEtlExecutionRun(PIPELINE_ID, RUN_ID)).resolves.toBe(expected)
    expect(fetch).toHaveBeenCalledWith(
      `/api/proxy/pipeline-executions/${RUN_ID}`,
      expect.objectContaining({ method: 'GET', cache: 'no-store' }),
    )
  })

  it('仅匹配原 RunId、管道且状态为明确终态的 2xx 响应允许清理', () => {
    expect(classifyEtlExecutionPayload(PIPELINE_ID, RUN_ID, {
      id: RUN_ID,
      pipelineId: PIPELINE_ID,
      status: 2,
    })).toBe('terminal')
    expect(classifyEtlExecutionPayload(PIPELINE_ID, RUN_ID, {
      id: '33333333-3333-4333-8333-333333333333',
      pipelineId: PIPELINE_ID,
      status: 2,
    })).toBe('retain')
    expect(classifyEtlExecutionPayload(PIPELINE_ID, RUN_ID, {
      id: RUN_ID,
      pipelineId: '33333333-3333-4333-8333-333333333333',
      status: 2,
    })).toBe('retain')
    expect(classifyEtlExecutionPayload(PIPELINE_ID, RUN_ID, {
      id: RUN_ID,
      pipelineId: PIPELINE_ID,
      status: 1,
    })).toBe('retain')
    expect(classifyEtlExecutionPayload(PIPELINE_ID, RUN_ID, {
      id: RUN_ID,
      pipelineId: PIPELINE_ID,
      status: 5,
    })).toBe('outcome-unknown')
  })

  it('404、网络错误、畸形或错配响应一律保留且不自动重跑', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 404 }))
      .mockRejectedValueOnce(new TypeError('network down'))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        id: '33333333-3333-4333-8333-333333333333',
        pipelineId: PIPELINE_ID,
        status: 2,
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response('{bad json', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(verifyEtlExecutionRun(PIPELINE_ID, RUN_ID)).resolves.toBe('retain')
    await expect(verifyEtlExecutionRun(PIPELINE_ID, RUN_ID)).resolves.toBe('retain')
    await expect(verifyEtlExecutionRun(PIPELINE_ID, RUN_ID)).resolves.toBe('retain')
    await expect(verifyEtlExecutionRun(PIPELINE_ID, RUN_ID)).resolves.toBe('retain')
    expect(fetchMock).toHaveBeenCalledTimes(4)
    expect(fetchMock.mock.calls.every(([url]) =>
      String(url).includes('/pipeline-executions/') && !String(url).includes('/execute'))).toBe(true)
  })
})

describe('ETL 执行恢复 storage 订阅', () => {
  function subscribe() {
    const storage = createStorage('identity-a1', 'sp1-account-a')
    const callback = vi.fn()
    const unsubscribe = subscribeEtlExecutionRecovery(storage, callback)
    return {
      callback,
      key: storage.key(ETL_EXECUTION_RECOVERY_POLICY),
      unsubscribe,
    }
  }

  function dispatchStorageEvent(key: string | null, storageArea: Storage | null) {
    window.dispatchEvent(new StorageEvent('storage', {
      key,
      storageArea,
      url: window.location.href,
    }))
  }

  it('接受目标键的 localStorage 原生语义事件', () => {
    const { callback, key, unsubscribe } = subscribe()
    dispatchStorageEvent(key, window.localStorage)
    expect(callback).toHaveBeenCalledOnce()
    unsubscribe()
  })

  it('接受 localStorage.clear 的 key=null 事件', () => {
    const { callback, unsubscribe } = subscribe()
    dispatchStorageEvent(null, window.localStorage)
    expect(callback).toHaveBeenCalledOnce()
    unsubscribe()
  })

  it('拒绝 sessionStorage 的同名键事件', () => {
    const { callback, key, unsubscribe } = subscribe()
    dispatchStorageEvent(key, window.sessionStorage)
    expect(callback).not.toHaveBeenCalled()
    unsubscribe()
  })

  it('拒绝 localStorage 的无关键事件', () => {
    const { callback, unsubscribe } = subscribe()
    dispatchStorageEvent('swapmid:browser-storage:v1:unrelated', window.localStorage)
    expect(callback).not.toHaveBeenCalled()
    unsubscribe()
  })

  it('拒绝 sessionStorage.clear 的 key=null 事件', () => {
    const { callback, unsubscribe } = subscribe()
    dispatchStorageEvent(null, window.sessionStorage)
    expect(callback).not.toHaveBeenCalled()
    unsubscribe()
  })

  it('拒绝缺失 storageArea 的 synthetic 同名键事件', () => {
    const { callback, key, unsubscribe } = subscribe()
    dispatchStorageEvent(key, null)
    expect(callback).not.toHaveBeenCalled()
    unsubscribe()
  })

  it('localStorage getter 异常时通知调用方进入 fail-closed 读取', () => {
    const storageArea = window.localStorage
    const { callback, key, unsubscribe } = subscribe()
    const getter = vi.spyOn(window, 'localStorage', 'get')
      .mockImplementation(() => { throw new DOMException('denied', 'SecurityError') })

    expect(() => dispatchStorageEvent(key, storageArea)).not.toThrow()
    expect(callback).toHaveBeenCalledOnce()

    getter.mockRestore()
    unsubscribe()
  })

  it('取消订阅后不再处理相关事件', () => {
    const { callback, key, unsubscribe } = subscribe()
    unsubscribe()
    dispatchStorageEvent(key, window.localStorage)
    expect(callback).not.toHaveBeenCalled()
  })
})
