import { useLayoutEffect, type ReactNode } from 'react'
import { renderToString } from 'react-dom/server'
import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import { createAuthenticatedBrowserStorage } from '@/lib/browser-storage-policy'
import {
  ETL_EXECUTION_RECOVERY_POLICY,
  persistEtlExecutionRecovery,
} from './execution-recovery'
import { useEtlExecutionRecovery } from './use-etl-execution-recovery'

const PIPELINE_ID = '11111111-1111-4111-8111-111111111111'
const RUN_ID = '22222222-2222-4222-8222-222222222222'
const RUN_ID_2 = '33333333-3333-4333-8333-333333333333'
const PIPELINE_ID_2 = '44444444-4444-4444-8444-444444444444'
const RUN_ID_3 = '55555555-5555-4555-8555-555555555555'
const CASE_PIPELINE_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const CASE_RUN_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
let originalLocksDescriptor: PropertyDescriptor | undefined

interface RecordedVerificationRequest {
  url: string
  signal: AbortSignal | null | undefined
}

function installPendingFetchRecorder() {
  const requests: RecordedVerificationRequest[] = []
  const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    requests.push({ url: String(input), signal: init?.signal })
    return new Promise<Response>(() => undefined)
  })
  vi.stubGlobal('fetch', fetchMock)
  return { fetchMock, requests }
}

function createLocalStorageEvent(key: string | null) {
  return new StorageEvent('storage', {
    key,
    storageArea: window.localStorage,
    url: window.location.href,
  })
}

function dispatchLocalStorageEvent(key: string | null) {
  window.dispatchEvent(createLocalStorageEvent(key))
}

function requestForRun(
  requests: readonly RecordedVerificationRequest[],
  runId: string,
): RecordedVerificationRequest {
  const request = requests.find(candidate => candidate.url.endsWith(`/${runId}`))
  expect(request).toBeDefined()
  return request!
}

function setLockManager(value: LockManager | undefined) {
  if (value === undefined) {
    Reflect.deleteProperty(window.navigator, 'locks')
    return
  }
  Object.defineProperty(window.navigator, 'locks', {
    configurable: true,
    value,
  })
}

function installSerialLockManager() {
  const tails = new Map<string, Promise<void>>()
  const request = vi.fn((
    name: string,
    _options: LockOptions,
    callback: (lock: Lock | null) => unknown,
  ) => {
    const tail = tails.get(name) ?? Promise.resolve()
    const result = tail.then(() => callback({ name, mode: 'exclusive' } as Lock))
    tails.set(name, result.then(() => undefined, () => undefined))
    return result
  })
  setLockManager({ request } as unknown as LockManager)
  return request
}

function identityWrapper(identityVersion = 'identity-a1', storagePrincipalId = 'sp1-account-a') {
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <SessionIdentityProvider
        identityVersion={identityVersion}
        storagePrincipalId={storagePrincipalId}
      >
        {children}
      </SessionIdentityProvider>
    )
  }
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  window.localStorage.clear()
  window.sessionStorage.clear()
  originalLocksDescriptor = Object.getOwnPropertyDescriptor(window.navigator, 'locks')
  installSerialLockManager()
})

afterEach(() => {
  if (originalLocksDescriptor) {
    Object.defineProperty(window.navigator, 'locks', originalLocksDescriptor)
  } else {
    setLockManager(undefined)
  }
})

describe('useEtlExecutionRecovery', () => {
  it('挂载恢复 effect 完成前保持未就绪，执行入口可据此禁用', () => {
    function Probe() {
      const recovery = useEtlExecutionRecovery()
      return <span>{recovery.status}</span>
    }

    expect(renderToString(
      <SessionIdentityProvider identityVersion="identity-a1" storagePrincipalId="sp1-account-a">
        <Probe />
      </SessionIdentityProvider>,
    )).toContain('loading')
  })

  it('刷新重挂载后恢复同一 RunId，并保持 OutcomeUnknown 封锁', async () => {
    const wrapper = identityWrapper()
    const first = renderHook(() => useEtlExecutionRecovery(), { wrapper })
    await waitFor(() => expect(first.result.current.ready).toBe(true))
    expect(first.result.current.status).toBe('ready')

    await act(async () => {
      expect(await first.result.current.retainRunId(
        PIPELINE_ID,
        RUN_ID,
        'outcome-unknown',
      )).toBe(true)
    })
    first.unmount()

    const refreshed = renderHook(() => useEtlExecutionRecovery(), { wrapper })
    await waitFor(() => expect(refreshed.result.current.ready).toBe(true))
    expect(refreshed.result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID)
    expect(refreshed.result.current.isBlocked(PIPELINE_ID)).toBe(true)
  })

  it('同 principal 两标签并发 claim 时在同一把锁内复用唯一 RunId', async () => {
    const request = installSerialLockManager()
    const first = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper('identity-a1', 'sp1-account-a'),
    })
    const second = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper('identity-a2', 'sp1-account-a'),
    })
    await waitFor(() => expect(first.result.current.ready).toBe(true))
    await waitFor(() => expect(second.result.current.ready).toBe(true))

    let claims: Awaited<ReturnType<typeof first.result.current.claimRunId>>[] = []
    await act(async () => {
      claims = await Promise.all([
        first.result.current.claimRunId(PIPELINE_ID),
        second.result.current.claimRunId(PIPELINE_ID),
      ])
    })

    expect(claims[0]?.runId).toBe(claims[1]?.runId)
    expect(claims.map(claim => claim?.isReplay).sort()).toEqual([false, true])
    expect(first.result.current.getRunId(PIPELINE_ID))
      .toBe(second.result.current.getRunId(PIPELINE_ID))
    const expectedLockName = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    }).key(ETL_EXECUTION_RECOVERY_POLICY)
    expect(request).toHaveBeenCalledTimes(2)
    expect(request.mock.calls.map(([name]) => name)).toEqual([
      expectedLockName,
      expectedLockName,
    ])
  })

  it('无 Web Locks 时禁止新 RunId，但可从持久层安全重放已有 ID', async () => {
    setLockManager(undefined)
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))

    await expect(result.current.claimRunId(PIPELINE_ID)).resolves.toBeNull()
    expect(window.localStorage.getItem(storage.key(ETL_EXECUTION_RECOVERY_POLICY))).toBeNull()

    expect(persistEtlExecutionRecovery(storage, [{
      pipelineId: PIPELINE_ID,
      runId: RUN_ID,
      disposition: 'pending',
    }])).toBe(true)
    let replay: Awaited<ReturnType<typeof result.current.claimRunId>> = null
    await act(async () => {
      replay = await result.current.claimRunId(PIPELINE_ID)
    })
    expect(replay).toEqual({
      runId: RUN_ID,
      disposition: 'pending',
      isReplay: true,
    })
  })

  it('收到其他标签的 storage 事件后刷新内存 RunId 快照', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })))
    const first = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper('identity-a1', 'sp1-account-a'),
    })
    const second = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper('identity-a2', 'sp1-account-a'),
    })
    await waitFor(() => expect(first.result.current.ready).toBe(true))
    await waitFor(() => expect(second.result.current.ready).toBe(true))

    await act(async () => {
      expect(await first.result.current.retainRunId(PIPELINE_ID, RUN_ID)).toBe(true)
    })
    expect(second.result.current.getRunId(PIPELINE_ID)).toBeUndefined()

    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a2',
      storagePrincipalId: 'sp1-account-a',
    })
    dispatchLocalStorageEvent(storage.key(ETL_EXECUTION_RECOVERY_POLICY))

    await waitFor(() => expect(second.result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID))
  })

  it('其他标签 clear localStorage 后清空恢复快照并中止旧 RunId 核验', async () => {
    const { fetchMock, requests } = installPendingFetchRecorder()
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    expect(persistEtlExecutionRecovery(storage, [{
      pipelineId: PIPELINE_ID,
      runId: RUN_ID,
      disposition: 'outcome-unknown',
    }])).toBe(true)

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledOnce())
    const request = requestForRun(requests, RUN_ID)

    act(() => {
      window.localStorage.clear()
      dispatchLocalStorageEvent(null)
    })

    expect(fetchMock).toHaveBeenCalledOnce()
    expect(request.signal?.aborted).toBe(true)
    expect(result.current.status).toBe('ready')
    expect(result.current.pendingRunIds).toEqual({})
    expect(result.current.blockedPipelines).toEqual({})
  })

  it('ready 标签收到新增恢复项时只核验新增 RunId，未变在途请求不重启', async () => {
    const { fetchMock, requests } = installPendingFetchRecorder()
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)
    expect(persistEtlExecutionRecovery(storage, [
      { pipelineId: PIPELINE_ID, runId: RUN_ID, disposition: 'pending' },
      { pipelineId: CASE_PIPELINE_ID, runId: CASE_RUN_ID, disposition: 'pending' },
    ])).toBe(true)

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    const firstRequest = requestForRun(requests, RUN_ID)
    const secondRequest = requestForRun(requests, CASE_RUN_ID)

    act(() => {
      expect(persistEtlExecutionRecovery(storage, [
        { pipelineId: PIPELINE_ID, runId: RUN_ID, disposition: 'pending' },
        { pipelineId: CASE_PIPELINE_ID, runId: CASE_RUN_ID, disposition: 'pending' },
        { pipelineId: PIPELINE_ID_2, runId: RUN_ID_3, disposition: 'pending' },
      ])).toBe(true)
      dispatchLocalStorageEvent(key)
    })

    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(firstRequest.signal?.aborted).toBe(false)
    expect(secondRequest.signal?.aborted).toBe(false)
    expect(requestForRun(requests, RUN_ID_3).signal?.aborted).toBe(false)
    expect(result.current.getRunId(PIPELINE_ID_2)).toBe(RUN_ID_3)
  })

  it('ready 标签收到恢复项删除时只中止被删项，未变项不重启', async () => {
    const { fetchMock, requests } = installPendingFetchRecorder()
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)
    expect(persistEtlExecutionRecovery(storage, [
      { pipelineId: PIPELINE_ID, runId: RUN_ID, disposition: 'pending' },
      { pipelineId: CASE_PIPELINE_ID, runId: CASE_RUN_ID, disposition: 'pending' },
    ])).toBe(true)

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    const removedRequest = requestForRun(requests, RUN_ID)
    const retainedRequest = requestForRun(requests, CASE_RUN_ID)

    act(() => {
      expect(persistEtlExecutionRecovery(storage, [
        { pipelineId: CASE_PIPELINE_ID, runId: CASE_RUN_ID, disposition: 'pending' },
      ])).toBe(true)
      dispatchLocalStorageEvent(key)
    })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(removedRequest.signal?.aborted).toBe(true)
    expect(retainedRequest.signal?.aborted).toBe(false)
    expect(result.current.getRunId(PIPELINE_ID)).toBeUndefined()
    expect(result.current.getRunId(CASE_PIPELINE_ID)).toBe(CASE_RUN_ID)
  })

  it('ready 标签仅核验替换后的 RunId，重复同值事件不会重启新请求', async () => {
    const { fetchMock, requests } = installPendingFetchRecorder()
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)
    expect(persistEtlExecutionRecovery(storage, [
      { pipelineId: PIPELINE_ID, runId: RUN_ID, disposition: 'pending' },
    ])).toBe(true)

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledOnce())
    const oldRequest = requestForRun(requests, RUN_ID)

    act(() => {
      expect(persistEtlExecutionRecovery(storage, [
        { pipelineId: PIPELINE_ID, runId: RUN_ID_2, disposition: 'pending' },
      ])).toBe(true)
      dispatchLocalStorageEvent(key)
    })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    const replacementRequest = requestForRun(requests, RUN_ID_2)
    expect(oldRequest.signal?.aborted).toBe(true)
    expect(replacementRequest.signal?.aborted).toBe(false)
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID_2)

    act(() => dispatchLocalStorageEvent(key))

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(replacementRequest.signal?.aborted).toBe(false)
  })

  it('ready 标签只收到 disposition 变化时更新 blocked，但不重启同 RunId GET', async () => {
    const { fetchMock, requests } = installPendingFetchRecorder()
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)
    expect(persistEtlExecutionRecovery(storage, [
      { pipelineId: PIPELINE_ID, runId: RUN_ID, disposition: 'pending' },
    ])).toBe(true)

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledOnce())
    const request = requestForRun(requests, RUN_ID)

    act(() => {
      expect(persistEtlExecutionRecovery(storage, [
        { pipelineId: PIPELINE_ID, runId: RUN_ID, disposition: 'outcome-unknown' },
      ])).toBe(true)
      dispatchLocalStorageEvent(key)
    })

    expect(fetchMock).toHaveBeenCalledOnce()
    expect(request.signal?.aborted).toBe(false)
    expect(result.current.isBlocked(PIPELINE_ID)).toBe(true)
  })

  it('ready layout 事件先启动核验时，后续 ready effect 不重复同 RunId GET', async () => {
    const { fetchMock, requests } = installPendingFetchRecorder()
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)
    expect(persistEtlExecutionRecovery(storage, [
      { pipelineId: PIPELINE_ID, runId: RUN_ID, disposition: 'pending' },
    ])).toBe(true)
    let injected = false

    const { result } = renderHook(() => {
      const recovery = useEtlExecutionRecovery()
      useLayoutEffect(() => {
        if (!recovery.ready || injected) return
        injected = true
        expect(persistEtlExecutionRecovery(storage, [
          { pipelineId: PIPELINE_ID, runId: RUN_ID, disposition: 'pending' },
          { pipelineId: CASE_PIPELINE_ID, runId: CASE_RUN_ID, disposition: 'pending' },
        ])).toBe(true)
        dispatchLocalStorageEvent(key)
      }, [recovery.ready])
      return recovery
    }, { wrapper: identityWrapper() })

    await waitFor(() => expect(result.current.getRunId(CASE_PIPELINE_ID)).toBe(CASE_RUN_ID))
    await act(async () => {
      await new Promise(resolve => setTimeout(resolve, 0))
    })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(requestForRun(requests, RUN_ID).signal?.aborted).toBe(false)
    expect(requestForRun(requests, CASE_RUN_ID).signal?.aborted).toBe(false)
  })

  it('大写历史恢复项与小写 API Id 共用同一规范键和原 RunId', async () => {
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    window.localStorage.setItem(
      storage.key(ETL_EXECUTION_RECOVERY_POLICY),
      JSON.stringify({
        version: ETL_EXECUTION_RECOVERY_POLICY.version,
        savedAt: Date.now(),
        value: {
          entries: [{
            pipelineId: CASE_PIPELINE_ID.toUpperCase(),
            runId: CASE_RUN_ID.toUpperCase(),
            disposition: 'pending',
          }],
        },
      }),
    )
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: CASE_RUN_ID.toUpperCase(),
      pipelineId: CASE_PIPELINE_ID.toUpperCase(),
      status: 1,
    }), { status: 200 })))

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())

    expect(result.current.getRunId(CASE_PIPELINE_ID)).toBe(CASE_RUN_ID)
    expect(result.current.getRunId(CASE_PIPELINE_ID.toUpperCase())).toBe(CASE_RUN_ID)
    expect(result.current.pendingRunIds).toEqual({
      [CASE_PIPELINE_ID]: CASE_RUN_ID,
    })
  })

  it('持久化写失败时保留当前页面的内存 last-known-good', async () => {
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))
    vi.spyOn(window.localStorage.__proto__, 'setItem')
      .mockImplementation(() => { throw new DOMException('quota', 'QuotaExceededError') })

    await act(async () => {
      expect(await result.current.retainRunId(PIPELINE_ID, RUN_ID)).toBe(false)
    })
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID)
    expect(result.current.pendingRunIds[PIPELINE_ID]).toBe(RUN_ID)
  })

  it('clearRunId 精确命中并持久化成功时返回 removed', async () => {
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))
    await act(async () => {
      expect(await result.current.retainRunId(PIPELINE_ID, RUN_ID)).toBe(true)
      expect(await result.current.clearRunId(PIPELINE_ID, RUN_ID)).toBe('removed')
    })

    expect(result.current.getRunId(PIPELINE_ID)).toBeUndefined()
  })

  it('clearRunId 在恢复项本就不存在时返回 already-absent', async () => {
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))

    await expect(result.current.clearRunId(PIPELINE_ID, RUN_ID))
      .resolves.toBe('already-absent')
  })

  it('clearRunId 遇到更新后的 RunId 时返回 superseded 且不删除新值', async () => {
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))
    await act(async () => {
      expect(await result.current.retainRunId(PIPELINE_ID, RUN_ID_2)).toBe(true)
    })
    const setItem = vi.spyOn(window.localStorage.__proto__, 'setItem')

    await expect(result.current.clearRunId(PIPELINE_ID, RUN_ID))
      .resolves.toBe('superseded')
    expect(setItem).not.toHaveBeenCalled()
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID_2)
  })

  it.each([
    ['非法 pipelineId', 'not-a-uuid', RUN_ID],
    ['非法 expectedRunId', PIPELINE_ID, 'not-a-uuid'],
  ])('clearRunId 输入%s时返回 unavailable', async (_caseName, pipelineId, runId) => {
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))

    await expect(result.current.clearRunId(pipelineId, runId))
      .resolves.toBe('unavailable')
  })

  it('clearRunId 在恢复尚未 ready 时返回 unavailable', async () => {
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })

    expect(result.current.ready).toBe(false)
    expect(result.current.status).toBe('loading')
    await expect(result.current.clearRunId(PIPELINE_ID, RUN_ID))
      .resolves.toBe('unavailable')
  })

  it('clearRunId 在身份文档切换的未就绪窗口返回 unavailable', async () => {
    let identityVersion = 'identity-a1'
    let storagePrincipalId = 'sp1-account-a'
    function Wrapper({ children }: { children: ReactNode }) {
      return (
        <SessionIdentityProvider
          identityVersion={identityVersion}
          storagePrincipalId={storagePrincipalId}
        >
          {children}
        </SessionIdentityProvider>
      )
    }
    const { result, rerender } = renderHook(() => useEtlExecutionRecovery(), { wrapper: Wrapper })
    await waitFor(() => expect(result.current.ready).toBe(true))
    await act(async () => {
      expect(await result.current.retainRunId(
        PIPELINE_ID,
        RUN_ID,
        'outcome-unknown',
      )).toBe(true)
    })

    identityVersion = 'identity-b1'
    storagePrincipalId = 'sp1-account-b'
    rerender()

    expect(result.current.ready).toBe(false)
    await expect(result.current.clearRunId(PIPELINE_ID, RUN_ID))
      .resolves.toBe('unavailable')
    const oldStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    expect(oldStorage.readResult(
      ETL_EXECUTION_RECOVERY_POLICY,
      (value): value is { entries: unknown[] } => (
        typeof value === 'object' && value !== null && 'entries' in value
      ),
    ).status).toBe('found')
  })

  it('clearRunId 在 Web Locks 不可用或请求抛错时返回 unavailable', async () => {
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))
    await act(async () => {
      expect(await result.current.retainRunId(
        PIPELINE_ID,
        RUN_ID,
        'outcome-unknown',
      )).toBe(true)
    })

    setLockManager(undefined)
    await expect(result.current.clearRunId(PIPELINE_ID, RUN_ID))
      .resolves.toBe('unavailable')
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID)
    expect(result.current.isBlocked(PIPELINE_ID)).toBe(true)

    setLockManager({
      request: vi.fn().mockRejectedValue(new Error('lock failed')),
    } as unknown as LockManager)
    await expect(result.current.clearRunId(PIPELINE_ID, RUN_ID))
      .resolves.toBe('unavailable')
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID)
    expect(result.current.isBlocked(PIPELINE_ID)).toBe(true)
  })

  it('clearRunId 持久化删除失败时返回 unavailable 并保留 last-known-good', async () => {
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))
    await act(async () => {
      expect(await result.current.retainRunId(
        PIPELINE_ID,
        RUN_ID,
        'outcome-unknown',
      )).toBe(true)
    })
    vi.spyOn(window.localStorage.__proto__, 'setItem')
      .mockImplementation(() => { throw new DOMException('quota', 'QuotaExceededError') })

    await expect(result.current.clearRunId(PIPELINE_ID, RUN_ID))
      .resolves.toBe('unavailable')
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID)
    expect(result.current.isBlocked(PIPELINE_ID)).toBe(true)
  })

  it('clearRunId 锁内读取失败时返回 unavailable，并保留内存中的 blocked last-known-good', async () => {
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))
    await act(async () => {
      expect(await result.current.retainRunId(
        PIPELINE_ID,
        RUN_ID,
        'outcome-unknown',
      )).toBe(true)
    })
    vi.spyOn(window.localStorage.__proto__, 'getItem')
      .mockImplementation(() => { throw new DOMException('denied', 'SecurityError') })

    await expect(result.current.clearRunId(PIPELINE_ID, RUN_ID))
      .resolves.toBe('unavailable')
    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID)
    expect(result.current.isBlocked(PIPELINE_ID)).toBe(true)
  })

  it('已 ready 后收到跨标签损坏恢复信封时降级 unavailable，并保留内存 last-known-good', async () => {
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.status).toBe('ready'))
    await act(async () => {
      expect(await result.current.retainRunId(
        PIPELINE_ID,
        RUN_ID,
        'outcome-unknown',
      )).toBe(true)
    })
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)
    window.localStorage.setItem(key, '{not-json')

    act(() => dispatchLocalStorageEvent(key))

    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    expect(result.current.ready).toBe(false)
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID)
    expect(result.current.isBlocked(PIPELINE_ID)).toBe(true)
    await expect(result.current.claimRunId(PIPELINE_ID)).resolves.toBeNull()
  })

  it('降级 unavailable 后收到同 principal 另一标签的合法写入时自动恢复 ready', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })))
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.status).toBe('ready'))
    await act(async () => {
      expect(await result.current.retainRunId(
        PIPELINE_ID,
        RUN_ID,
        'outcome-unknown',
      )).toBe(true)
    })

    const currentStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const otherTabStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a2',
      storagePrincipalId: 'sp1-account-a',
    })
    const key = currentStorage.key(ETL_EXECUTION_RECOVERY_POLICY)
    expect(otherTabStorage.key(ETL_EXECUTION_RECOVERY_POLICY)).toBe(key)
    window.localStorage.setItem(key, '{not-json')
    act(() => dispatchLocalStorageEvent(key))

    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID)
    expect(result.current.isBlocked(PIPELINE_ID)).toBe(true)

    expect(persistEtlExecutionRecovery(otherTabStorage, [{
      pipelineId: PIPELINE_ID,
      runId: RUN_ID_2,
      disposition: 'pending',
    }])).toBe(true)
    act(() => dispatchLocalStorageEvent(key))

    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current.ready).toBe(true)
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID_2)
    expect(result.current.isBlocked(PIPELINE_ID)).toBe(false)
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    expect(fetch).toHaveBeenCalledWith(
      `/api/proxy/pipeline-executions/${RUN_ID_2}`,
      expect.objectContaining({ method: 'GET', cache: 'no-store' }),
    )
  })

  it('首次恢复 unavailable 后收到同 principal 另一标签的合法删除时自动恢复为空 ready', async () => {
    vi.stubGlobal('fetch', vi.fn())
    const currentStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const otherTabStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a2',
      storagePrincipalId: 'sp1-account-a',
    })
    const key = currentStorage.key(ETL_EXECUTION_RECOVERY_POLICY)
    window.localStorage.setItem(key, '{not-json')
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })

    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    otherTabStorage.remove(ETL_EXECUTION_RECOVERY_POLICY)
    expect(window.localStorage.getItem(key)).toBeNull()
    act(() => dispatchLocalStorageEvent(key))

    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current.ready).toBe(true)
    expect(result.current.pendingRunIds).toEqual({})
    expect(result.current.blockedPipelines).toEqual({})
    expect(fetch).not.toHaveBeenCalled()
  })

  it('首次严格读取判坏到状态提交之间的合法修复事件不会丢失', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })))
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const key = storage.key(ETL_EXECUTION_RECOVERY_POLICY)
    window.localStorage.setItem(key, '{not-json')

    const nativeSetTimeout = globalThis.setTimeout
    let restoreCallback: (() => void) | null = null
    vi.spyOn(globalThis, 'setTimeout').mockImplementation((handler, timeout, ...args) => {
      if (restoreCallback === null && timeout === 0 && typeof handler === 'function') {
        restoreCallback = () => handler(...args)
        return 1 as unknown as ReturnType<typeof setTimeout>
      }
      return nativeSetTimeout(handler, timeout, ...args)
    })

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    expect(restoreCallback).not.toBeNull()

    act(() => {
      restoreCallback?.()
      expect(persistEtlExecutionRecovery(storage, [{
        pipelineId: PIPELINE_ID,
        runId: RUN_ID_2,
        disposition: 'pending',
      }])).toBe(true)
      dispatchLocalStorageEvent(key)
    })

    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID_2)
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
  })

  it('账号切换后旧 principal 的合法事件不能修复新 principal 的 unavailable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })))
    let identityVersion = 'identity-a1'
    let storagePrincipalId = 'sp1-account-a'
    function Wrapper({ children }: { children: ReactNode }) {
      return (
        <SessionIdentityProvider
          identityVersion={identityVersion}
          storagePrincipalId={storagePrincipalId}
        >
          {children}
        </SessionIdentityProvider>
      )
    }

    const oldStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const newStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-b1',
      storagePrincipalId: 'sp1-account-b',
    })
    const oldKey = oldStorage.key(ETL_EXECUTION_RECOVERY_POLICY)
    const newKey = newStorage.key(ETL_EXECUTION_RECOVERY_POLICY)
    expect(newKey).not.toBe(oldKey)
    window.localStorage.setItem(oldKey, '{not-json')
    window.localStorage.setItem(newKey, '{not-json')

    const { result, rerender } = renderHook(() => useEtlExecutionRecovery(), { wrapper: Wrapper })
    await waitFor(() => expect(result.current.status).toBe('unavailable'))

    identityVersion = 'identity-b1'
    storagePrincipalId = 'sp1-account-b'
    rerender()
    expect(result.current.status).toBe('loading')
    await waitFor(() => expect(result.current.status).toBe('unavailable'))

    oldStorage.remove(ETL_EXECUTION_RECOVERY_POLICY)
    act(() => dispatchLocalStorageEvent(oldKey))
    expect(result.current.status).toBe('unavailable')
    expect(result.current.ready).toBe(false)
    expect(result.current.pendingRunIds).toEqual({})
    expect(fetch).not.toHaveBeenCalled()

    expect(persistEtlExecutionRecovery(newStorage, [{
      pipelineId: PIPELINE_ID,
      runId: RUN_ID_2,
      disposition: 'pending',
    }])).toBe(true)
    act(() => dispatchLocalStorageEvent(newKey))

    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current.ready).toBe(true)
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID_2)
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
  })

  it('新账号 commit 后旧 listener 在 passive cleanup 前触发也不能污染新账号 LKG', async () => {
    vi.stubGlobal('fetch', vi.fn())
    const storageListeners: EventListener[] = []
    const originalAddEventListener = window.addEventListener.bind(window)
    vi.spyOn(window, 'addEventListener').mockImplementation((
      type: string,
      listener: EventListenerOrEventListenerObject,
      options?: boolean | AddEventListenerOptions,
    ) => {
      if (type === 'storage' && typeof listener === 'function') {
        storageListeners.push(listener)
      }
      originalAddEventListener(type, listener, options)
    })

    let identityVersion = 'identity-a1'
    let storagePrincipalId = 'sp1-account-a'
    let staleListeners: readonly EventListener[] = []
    function Wrapper({ children }: { children: ReactNode }) {
      return (
        <SessionIdentityProvider
          identityVersion={identityVersion}
          storagePrincipalId={storagePrincipalId}
        >
          {children}
        </SessionIdentityProvider>
      )
    }

    const oldStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const newStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-b1',
      storagePrincipalId: 'sp1-account-b',
    })
    const oldKey = oldStorage.key(ETL_EXECUTION_RECOVERY_POLICY)
    const newKey = newStorage.key(ETL_EXECUTION_RECOVERY_POLICY)
    window.localStorage.setItem(oldKey, '{not-json')
    window.localStorage.setItem(newKey, '{not-json')

    const { result, rerender } = renderHook(({ invokeStaleListeners }) => {
      const recovery = useEtlExecutionRecovery()
      useLayoutEffect(() => {
        if (!invokeStaleListeners) return
        const event = createLocalStorageEvent(oldKey)
        for (const listener of staleListeners) listener(event)
      }, [invokeStaleListeners])
      return recovery
    }, {
      initialProps: { invokeStaleListeners: false },
      wrapper: Wrapper,
    })
    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    staleListeners = [...storageListeners]
    expect(staleListeners.length).toBeGreaterThan(0)

    expect(persistEtlExecutionRecovery(oldStorage, [{
      pipelineId: PIPELINE_ID,
      runId: RUN_ID,
      disposition: 'outcome-unknown',
    }])).toBe(true)
    identityVersion = 'identity-b1'
    storagePrincipalId = 'sp1-account-b'
    rerender({ invokeStaleListeners: true })

    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    expect(result.current.ready).toBe(false)
    expect(result.current.pendingRunIds).toEqual({})
    expect(result.current.blockedPipelines).toEqual({})
    expect(result.current.getRunId(PIPELINE_ID)).toBeUndefined()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('新账号 commit 后旧 restore timer 在 passive cleanup 前触发也不读取旧 principal', async () => {
    let identityVersion = 'identity-a1'
    let storagePrincipalId = 'sp1-account-a'
    function Wrapper({ children }: { children: ReactNode }) {
      return (
        <SessionIdentityProvider
          identityVersion={identityVersion}
          storagePrincipalId={storagePrincipalId}
        >
          {children}
        </SessionIdentityProvider>
      )
    }

    const oldStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    const oldKey = oldStorage.key(ETL_EXECUTION_RECOVERY_POLICY)
    window.localStorage.setItem(oldKey, '{not-json')

    const nativeSetTimeout = globalThis.setTimeout
    let staleRestoreCallback: (() => void) | null = null
    vi.spyOn(globalThis, 'setTimeout').mockImplementation((handler, timeout, ...args) => {
      if (staleRestoreCallback === null && timeout === 0 && typeof handler === 'function') {
        staleRestoreCallback = () => handler(...args)
        return 1 as unknown as ReturnType<typeof setTimeout>
      }
      return nativeSetTimeout(handler, timeout, ...args)
    })

    const nativeGetItem = window.localStorage.getItem.bind(window.localStorage)
    let invokingStaleRestore = false
    let staleOldKeyReads = 0
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation((key: string) => {
      if (invokingStaleRestore && key === oldKey) staleOldKeyReads += 1
      return nativeGetItem(key)
    })

    const { result, rerender } = renderHook(({ invokeStaleRestore }) => {
      const recovery = useEtlExecutionRecovery()
      useLayoutEffect(() => {
        if (!invokeStaleRestore) return
        invokingStaleRestore = true
        try {
          staleRestoreCallback?.()
        } finally {
          invokingStaleRestore = false
        }
      }, [invokeStaleRestore])
      return recovery
    }, {
      initialProps: { invokeStaleRestore: false },
      wrapper: Wrapper,
    })
    expect(staleRestoreCallback).not.toBeNull()

    identityVersion = 'identity-b1'
    storagePrincipalId = 'sp1-account-b'
    rerender({ invokeStaleRestore: true })

    expect(staleOldKeyReads).toBe(0)
    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current.pendingRunIds).toEqual({})
    expect(result.current.blockedPipelines).toEqual({})
  })

  it('首次恢复读取不可用时保持未就绪，且不生成或覆盖 RunId', async () => {
    const setItem = vi.spyOn(window.localStorage.__proto__, 'setItem')
    vi.spyOn(window.localStorage.__proto__, 'getItem')
      .mockImplementation(() => { throw new DOMException('denied', 'SecurityError') })
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })

    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    expect(result.current.ready).toBe(false)
    await expect(result.current.claimRunId(PIPELINE_ID)).resolves.toBeNull()
    expect(setItem).not.toHaveBeenCalled()
  })

  it('Provider 初始化先清理非法恢复信封时仍保持未就绪，且不生成新 RunId', async () => {
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    window.localStorage.setItem(
      storage.key(ETL_EXECUTION_RECOVERY_POLICY),
      '{not-json',
    )

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })

    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    expect(result.current.ready).toBe(false)
    await expect(result.current.claimRunId(PIPELINE_ID)).resolves.toBeNull()
    expect(window.localStorage.getItem(
      storage.key(ETL_EXECUTION_RECOVERY_POLICY),
    )).toBe('{not-json')
  })

  it('verifyRun 明确终态但清理不可用时仍保留 blocked 恢复项', async () => {
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    expect(persistEtlExecutionRecovery(storage, [{
      pipelineId: PIPELINE_ID,
      runId: RUN_ID,
      disposition: 'outcome-unknown',
    }])).toBe(true)
    setLockManager(undefined)
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(
      new Response(JSON.stringify({
        id: RUN_ID,
        pipelineId: PIPELINE_ID,
        status: 2,
      }), { status: 200 }),
    )))
    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))

    await expect(result.current.verifyRun(PIPELINE_ID, RUN_ID))
      .resolves.toBe('terminal')
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID)
    expect(result.current.isBlocked(PIPELINE_ID)).toBe(true)
  })

  it.each([2, 3, 4])('刷新后 GET 原 RunId 明确终态 %s 才清理恢复项', async (status) => {
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    expect(persistEtlExecutionRecovery(storage, [{
      pipelineId: PIPELINE_ID,
      runId: RUN_ID,
      disposition: 'pending',
    }])).toBe(true)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: RUN_ID,
      pipelineId: PIPELINE_ID,
      status,
    }), { status: 200 })))

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.ready).toBe(true))
    await waitFor(() => expect(result.current.getRunId(PIPELINE_ID)).toBeUndefined())
  })

  it('刷新核验得到 OutcomeUnknown 时保留 RunId 并升级为 blocked', async () => {
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    expect(persistEtlExecutionRecovery(storage, [{
      pipelineId: PIPELINE_ID,
      runId: RUN_ID,
      disposition: 'pending',
    }])).toBe(true)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: RUN_ID,
      pipelineId: PIPELINE_ID,
      status: 5,
    }), { status: 200 })))

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(result.current.isBlocked(PIPELINE_ID)).toBe(true))
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID)
  })

  it('迟到的旧 RunId 核验响应不能清除同身份下更新后的 RunId', async () => {
    let resolveFetch!: (response: Response) => void
    const fetchPromise = new Promise<Response>((resolve) => { resolveFetch = resolve })
    vi.stubGlobal('fetch', vi.fn().mockReturnValue(fetchPromise))
    const storage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    expect(persistEtlExecutionRecovery(storage, [{
      pipelineId: PIPELINE_ID,
      runId: RUN_ID,
      disposition: 'pending',
    }])).toBe(true)

    const { result } = renderHook(() => useEtlExecutionRecovery(), {
      wrapper: identityWrapper(),
    })
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    await act(async () => {
      expect(await result.current.clearRunId(PIPELINE_ID, RUN_ID)).toBe('removed')
      expect(await result.current.retainRunId(PIPELINE_ID, RUN_ID_2)).toBe(true)
    })
    await act(async () => {
      resolveFetch(new Response(JSON.stringify({
        id: RUN_ID,
        pipelineId: PIPELINE_ID,
        status: 2,
      }), { status: 200 }))
      await fetchPromise
    })
    expect(result.current.getRunId(PIPELINE_ID)).toBe(RUN_ID_2)
  })

  it('账号切换后旧账号迟到响应不能清除新账号的恢复项', async () => {
    let resolveFetch!: (response: Response) => void
    const fetchPromise = new Promise<Response>((resolve) => { resolveFetch = resolve })
    vi.stubGlobal('fetch', vi.fn().mockReturnValue(fetchPromise))
    const firstStorage = createAuthenticatedBrowserStorage({
      identityVersion: 'identity-a1',
      storagePrincipalId: 'sp1-account-a',
    })
    expect(persistEtlExecutionRecovery(firstStorage, [{
      pipelineId: PIPELINE_ID,
      runId: RUN_ID,
      disposition: 'pending',
    }])).toBe(true)

    function Probe() {
      const recovery = useEtlExecutionRecovery()
      return (
        <button
          type="button"
          onClick={() => { void recovery.retainRunId(PIPELINE_ID, RUN_ID_2) }}
        >
          {recovery.ready ? (recovery.getRunId(PIPELINE_ID) ?? 'none') : 'locked'}
        </button>
      )
    }

    const view = render(
      <SessionIdentityProvider identityVersion="identity-a1" storagePrincipalId="sp1-account-a">
        <Probe />
      </SessionIdentityProvider>,
    )
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    view.rerender(
      <SessionIdentityProvider identityVersion="identity-b1" storagePrincipalId="sp1-account-b">
        <Probe />
      </SessionIdentityProvider>,
    )
    await waitFor(
      () => expect(screen.getByRole('button')).toHaveTextContent('none'),
      { timeout: 10_000 },
    )
    fireEvent.click(screen.getByRole('button'))
    await waitFor(
      () => expect(screen.getByRole('button')).toHaveTextContent(RUN_ID_2),
      { timeout: 10_000 },
    )

    await act(async () => {
      resolveFetch(new Response(JSON.stringify({
        id: RUN_ID,
        pipelineId: PIPELINE_ID,
        status: 2,
      }), { status: 200 }))
      await fetchPromise
    })
    expect(screen.getByRole('button')).toHaveTextContent(RUN_ID_2)
  }, 20_000)
})
