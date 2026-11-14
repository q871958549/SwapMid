import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  recoverSessionToLogin,
  recoverSessionToLoginWithIdentityChange,
  requestIdentityBoundLogout,
} from './client-session-recovery'
import {
  SESSION_IDENTITY_CHANGE_MARKER_KEY,
  SESSION_IDENTITY_MARKER_KEY,
} from './browser-storage-policy'
import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
  SESSION_STATE_IDENTITY_CHANGED,
  SESSION_STATE_UNAVAILABLE,
} from './session-availability'

const IDENTITY_VERSION = 'identity-v1'

function makeResponse(status: number, headers?: HeadersInit): Response {
  return new Response(null, { status, headers })
}

describe('client session recovery', () => {
  beforeEach(() => {
    vi.useRealTimers()
    window.localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('根边界先读取权威版本，再以完全相同版本执行 logout，成功后才广播并导航', async () => {
    const fetchImpl = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(makeResponse(200, {
        [SESSION_IDENTITY_VERSION_HEADER]: IDENTITY_VERSION,
      }))
      .mockResolvedValueOnce(makeResponse(200))
    const announce = vi.fn()
    const navigate = vi.fn()

    const outcome = await recoverSessionToLogin({
      fetchImpl,
      announce,
      navigate,
      createGeneration: () => 'generation-1',
    })

    expect(outcome).toBe('logged-out')
    expect(fetchImpl).toHaveBeenCalledTimes(2)
    expect(fetchImpl.mock.calls[0][0]).toBe('/api/auth/session-identity-version')
    expect(fetchImpl.mock.calls[0][1]).toMatchObject({ cache: 'no-store' })
    expect(fetchImpl.mock.calls[1][0]).toBe('/api/auth/logout')
    expect(fetchImpl.mock.calls[1][1]).toMatchObject({ method: 'POST', cache: 'no-store' })
    expect(new Headers(fetchImpl.mock.calls[1][1]?.headers).get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe(IDENTITY_VERSION)
    expect(fetchImpl.mock.calls[1][1]?.signal).toBe(fetchImpl.mock.calls[0][1]?.signal)
    expect(announce).toHaveBeenCalledWith('logged-out-generation-1')
    expect(navigate).toHaveBeenCalledWith('/login')
  })


  it('权威探针返回 401 时不再调用 logout，但允许进入登录页', async () => {
    const fetchImpl = vi.fn<typeof fetch>().mockResolvedValue(makeResponse(401))
    const announce = vi.fn()
    const navigate = vi.fn()

    const outcome = await recoverSessionToLogin({
      fetchImpl,
      announce,
      navigate,
      createGeneration: () => 'generation-2',
    })

    expect(outcome).toBe('session-missing')
    expect(fetchImpl).toHaveBeenCalledTimes(1)
    expect(announce).toHaveBeenCalledWith('logged-out-generation-2')
    expect(navigate).toHaveBeenCalledWith('/login')
  })

  it('登出广播写入失败时仍离开已确认删除会话的旧文档', async () => {
    const fetchImpl = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(makeResponse(200, {
        [SESSION_IDENTITY_VERSION_HEADER]: IDENTITY_VERSION,
      }))
      .mockResolvedValueOnce(makeResponse(200))
    const navigate = vi.fn()

    const outcome = await recoverSessionToLogin({
      fetchImpl,
      announce: () => { throw new Error('storage denied') },
      navigate,
    })

    expect(outcome).toBe('logged-out')
    expect(navigate).toHaveBeenCalledWith('/login')
  })

  it.each([
    ['带状态头的探针 503', makeResponse(503, {
      [SESSION_STATE_HEADER]: SESSION_STATE_UNAVAILABLE,
    }), 'unavailable'],
    ['缺状态头的探针 503', makeResponse(503), 'failed'],
    ['带矛盾状态头的探针 401', makeResponse(401, {
      [SESSION_STATE_HEADER]: SESSION_STATE_IDENTITY_CHANGED,
    }), 'failed'],
    ['探针未知错误', makeResponse(500), 'failed'],
    ['探针非契约 201', makeResponse(201, {
      [SESSION_IDENTITY_VERSION_HEADER]: IDENTITY_VERSION,
    }), 'failed'],
    ['探针成功但缺版本头', makeResponse(200), 'failed'],
    ['探针返回过短版本', makeResponse(200, { [SESSION_IDENTITY_VERSION_HEADER]: 'short' }), 'failed'],
  ] as const)('%s 时保持原页且不调用 logout', async (_name, authority, expected) => {
    const fetchImpl = vi.fn<typeof fetch>().mockResolvedValue(authority)
    const navigate = vi.fn()

    const outcome = await recoverSessionToLogin({ fetchImpl, navigate })

    expect(outcome).toBe(expected)
    expect(fetchImpl).toHaveBeenCalledTimes(1)
    expect(navigate).not.toHaveBeenCalled()
  })

  it('探针网络错误保持 fail-closed', async () => {
    const fetchImpl = vi.fn<typeof fetch>().mockRejectedValue(new TypeError('offline'))
    const announce = vi.fn()
    const navigate = vi.fn()

    const outcome = await recoverSessionToLogin({ fetchImpl, announce, navigate })

    expect(outcome).toBe('failed')
    expect(announce).not.toHaveBeenCalled()
    expect(navigate).not.toHaveBeenCalled()
  })

  it.each([
    ['普通 CAS 冲突 409', makeResponse(409), 'failed'],
    ['身份变化 409 缺权威版本', makeResponse(409, {
      [SESSION_STATE_HEADER]: SESSION_STATE_IDENTITY_CHANGED,
    }), 'failed'],
    ['身份变化 409 携带非法权威版本', makeResponse(409, {
      [SESSION_STATE_HEADER]: SESSION_STATE_IDENTITY_CHANGED,
      [SESSION_IDENTITY_VERSION_HEADER]: 'short',
    }), 'failed'],
    ['带状态头的 logout 503', makeResponse(503, {
      [SESSION_STATE_HEADER]: SESSION_STATE_UNAVAILABLE,
    }), 'unavailable'],
    ['缺状态头的 logout 503', makeResponse(503), 'failed'],
    ['logout 428', makeResponse(428), 'failed'],
    ['logout 非契约 204', makeResponse(204), 'failed'],
    ['2xx 却声明身份已变化', makeResponse(200, {
      [SESSION_STATE_HEADER]: SESSION_STATE_IDENTITY_CHANGED,
    }), 'failed'],
  ] as const)('%s 不导航也不追逐新版本重试', async (_name, logout, expected) => {
    const fetchImpl = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(makeResponse(200, {
        [SESSION_IDENTITY_VERSION_HEADER]: IDENTITY_VERSION,
      }))
      .mockResolvedValueOnce(logout)
    const announce = vi.fn()
    const navigate = vi.fn()

    const outcome = await recoverSessionToLogin({ fetchImpl, announce, navigate })

    expect(outcome).toBe(expected)
    expect(fetchImpl).toHaveBeenCalledTimes(2)
    expect(announce).not.toHaveBeenCalled()
    expect(navigate).not.toHaveBeenCalled()
  })

  it('合法 identity-changed 只广播无值变化信号，不写入响应版本也不导航登录页', async () => {
    const fetchImpl = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(makeResponse(200, {
        [SESSION_IDENTITY_VERSION_HEADER]: IDENTITY_VERSION,
      }))
      .mockResolvedValueOnce(makeResponse(409, {
        [SESSION_STATE_HEADER]: SESSION_STATE_IDENTITY_CHANGED,
        [SESSION_IDENTITY_VERSION_HEADER]: 'identity-v2',
      }))
    const announce = vi.fn()
    const navigate = vi.fn()
    const onIdentityChanged = vi.fn()

    const outcome = await recoverSessionToLoginWithIdentityChange({
      fetchImpl,
      announce,
      navigate,
      onIdentityChanged,
    })

    expect(outcome).toBe('identity-changed')
    expect(onIdentityChanged).toHaveBeenCalledTimes(1)
    expect(announce).not.toHaveBeenCalled()
    expect(navigate).not.toHaveBeenCalled()
    expect(window.localStorage.getItem(SESSION_IDENTITY_MARKER_KEY)).toBeNull()
    expect(window.localStorage.getItem(SESSION_IDENTITY_CHANGE_MARKER_KEY)).toBeTruthy()
  })

  it('identity-changed 的异步文档动作拒绝时返回 failed，允许入口恢复重试', async () => {
    const fetchImpl = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(makeResponse(200, {
        [SESSION_IDENTITY_VERSION_HEADER]: IDENTITY_VERSION,
      }))
      .mockResolvedValueOnce(makeResponse(409, {
        [SESSION_STATE_HEADER]: SESSION_STATE_IDENTITY_CHANGED,
        [SESSION_IDENTITY_VERSION_HEADER]: 'identity-v2',
      }))

    await expect(recoverSessionToLoginWithIdentityChange({
      fetchImpl,
      onIdentityChanged: async () => { throw new Error('navigation blocked') },
    })).resolves.toBe('failed')
  })

  it('logout 网络错误不导航', async () => {
    const fetchImpl = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(makeResponse(200, {
        [SESSION_IDENTITY_VERSION_HEADER]: IDENTITY_VERSION,
      }))
      .mockRejectedValueOnce(new TypeError('offline'))
    const navigate = vi.fn()

    expect(await recoverSessionToLogin({ fetchImpl, navigate })).toBe('failed')
    expect(fetchImpl).toHaveBeenCalledTimes(2)
    expect(navigate).not.toHaveBeenCalled()
  })

  it('已知首屏版本时跳过探针，并直接发起一次版本绑定 logout', async () => {
    const fetchImpl = vi.fn<typeof fetch>().mockResolvedValue(makeResponse(200))

    const outcome = await requestIdentityBoundLogout({
      identityVersion: IDENTITY_VERSION,
      fetchImpl,
    })

    expect(outcome).toBe('logged-out')
    expect(fetchImpl).toHaveBeenCalledTimes(1)
    expect(fetchImpl.mock.calls[0][0]).toBe('/api/auth/logout')
    expect(new Headers(fetchImpl.mock.calls[0][1]?.headers).get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe(IDENTITY_VERSION)
  })

  it('一个绝对 deadline 覆盖探针与 logout，并在超时时取消在途请求', async () => {
    vi.useFakeTimers()
    let observedSignal: AbortSignal | undefined
    const fetchImpl = vi.fn<typeof fetch>((_input, init) => {
      observedSignal = init?.signal ?? undefined
      return new Promise<Response>((_resolve, reject) => {
        observedSignal?.addEventListener('abort', () => {
          reject(new DOMException('aborted', 'AbortError'))
        })
      })
    })

    const pending = requestIdentityBoundLogout({ fetchImpl, deadlineMs: 50 })
    await vi.advanceTimersByTimeAsync(50)

    await expect(pending).resolves.toBe('failed')
    expect(observedSignal?.aborted).toBe(true)
  })

  it('fetch wrapper 即使完全忽略 AbortSignal，deadline 到达后也会返回', async () => {
    vi.useFakeTimers()
    let observedSignal: AbortSignal | undefined
    const fetchImpl = vi.fn<typeof fetch>((_input, init) => {
      observedSignal = init?.signal ?? undefined
      return new Promise<Response>(() => undefined)
    })

    const pending = requestIdentityBoundLogout({ fetchImpl, deadlineMs: 50 })
    await vi.advanceTimersByTimeAsync(50)

    await expect(pending).resolves.toBe('failed')
    expect(observedSignal?.aborted).toBe(true)
  })

  it('探针与 logout 共享同一个绝对预算，不会给第二阶段重新计时', async () => {
    vi.useFakeTimers()
    const fetchImpl = vi.fn<typeof fetch>()
      .mockImplementationOnce(() => new Promise<Response>((resolve) => {
        setTimeout(() => resolve(makeResponse(200, {
          [SESSION_IDENTITY_VERSION_HEADER]: IDENTITY_VERSION,
        })), 90)
      }))
      .mockImplementationOnce(() => new Promise<Response>(() => undefined))

    const pending = requestIdentityBoundLogout({ fetchImpl, deadlineMs: 100 })
    await vi.advanceTimersByTimeAsync(90)
    expect(fetchImpl).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(10)

    await expect(pending).resolves.toBe('failed')
    expect(fetchImpl).toHaveBeenCalledTimes(2)
  })

  it('导航异常后返回 failed，允许错误页再次探测已删除的会话', async () => {
    const fetchImpl = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(makeResponse(200, {
        [SESSION_IDENTITY_VERSION_HEADER]: IDENTITY_VERSION,
      }))
      .mockResolvedValueOnce(makeResponse(200))

    const outcome = await recoverSessionToLogin({
      fetchImpl,
      announce: vi.fn(),
      navigate: () => { throw new Error('navigation blocked') },
    })

    expect(outcome).toBe('failed')
  })
})
