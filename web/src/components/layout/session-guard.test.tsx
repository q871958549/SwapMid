import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const effectState = vi.hoisted(() => ({
  cleanup: undefined as undefined | (() => void),
}))

vi.mock('react', () => ({
  useLayoutEffect: (effect: () => void | (() => void)) => {
    effectState.cleanup = effect() ?? undefined
  },
}))

import {
  BACKGROUND_REQUEST_HEADER,
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
  SESSION_STATE_IDENTITY_CHANGED,
} from '@/lib/session-availability'
import { SessionGuard } from './session-guard'

type Listener = (event: never) => void

let originalFetch: ReturnType<typeof vi.fn>
let storage: Map<string, string>
let windowListeners: Map<string, Set<Listener>>
let documentListeners: Map<string, Set<Listener>>
let reload: ReturnType<typeof vi.fn>
let storageSetItem: ReturnType<typeof vi.fn>
let testWindow: Window & typeof globalThis

function dispatchWindow(type: string, event: unknown = {}) {
  for (const listener of windowListeners.get(type) ?? []) {
    listener(event as never)
  }
}

function dispatchDocument(type: string, event: unknown = {}) {
  for (const listener of documentListeners.get(type) ?? []) {
    listener(event as never)
  }
}

function mount(identityVersion = 'identity-a') {
  SessionGuard({ identityVersion })
}

beforeEach(() => {
  vi.clearAllMocks()
  storage = new Map([['swapmid:identity-version', 'identity-a']])
  windowListeners = new Map()
  documentListeners = new Map()
  reload = vi.fn()
  storageSetItem = vi.fn((key: string, value: string) => {
    storage.set(key, value)
  })
  originalFetch = vi.fn().mockResolvedValue(new Response(null, { status: 200 }))

  const location = {
    href: 'http://localhost/data-integration/pipelines?tab=detail',
    pathname: '/data-integration/pipelines',
    search: '?tab=detail',
    reload,
  }
  testWindow = {
    fetch: originalFetch as unknown as typeof fetch,
    location,
    localStorage: {
      getItem: (key: string) => storage.get(key) ?? null,
      setItem: storageSetItem,
    },
    addEventListener: (type: string, listener: EventListenerOrEventListenerObject) => {
      const set = windowListeners.get(type) ?? new Set()
      set.add(listener as Listener)
      windowListeners.set(type, set)
    },
    removeEventListener: (type: string, listener: EventListenerOrEventListenerObject) => {
      windowListeners.get(type)?.delete(listener as Listener)
    },
  } as unknown as Window & typeof globalThis

  vi.stubGlobal('window', testWindow)
  vi.stubGlobal('location', location)
  vi.stubGlobal('document', {
    visibilityState: 'visible',
    addEventListener: (type: string, listener: EventListenerOrEventListenerObject) => {
      const set = documentListeners.get(type) ?? new Set()
      set.add(listener as Listener)
      documentListeners.set(type, set)
    },
    removeEventListener: (type: string, listener: EventListenerOrEventListenerObject) => {
      documentListeners.get(type)?.delete(listener as Listener)
    },
  })
})

afterEach(() => {
  effectState.cleanup?.()
  effectState.cleanup = undefined
  vi.unstubAllGlobals()
})

describe('SessionGuard · 请求身份围栏', () => {
  it('给全部同源 fetch 注入首屏身份版本，跨域请求保持原样', async () => {
    mount()

    await testWindow.fetch('/dashboard?__rsc=1', {
      headers: { 'X-Caller': 'string' },
    })
    await testWindow.fetch(new URL('/api/proxy/employees', location.href))
    await testWindow.fetch('https://evil.example/api/proxy/employees')

    const sameOriginInit = originalFetch.mock.calls[0][1] as RequestInit
    expect(new Headers(sameOriginInit.headers).get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-a')
    expect(new Headers(sameOriginInit.headers).get('x-caller')).toBe('string')

    const urlInit = originalFetch.mock.calls[1][1] as RequestInit
    expect(new Headers(urlInit.headers).get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-a')
    expect(originalFetch.mock.calls[2][1]).toBeUndefined()
  })

  it('Request + init 合并时保留 Request 本体，并由 init 覆盖同名头', async () => {
    mount()
    const request = new Request('http://localhost/api/proxy/employees', {
      method: 'POST',
      body: 'payload',
      headers: {
        'X-From-Request': 'yes',
        'X-Override': 'request',
      },
    })

    await testWindow.fetch(request, {
      headers: {
        'X-From-Init': 'yes',
        'X-Override': 'init',
      },
    })

    expect(originalFetch.mock.calls[0][0]).toBe(request)
    const headers = new Headers((originalFetch.mock.calls[0][1] as RequestInit).headers)
    expect(headers.get('x-from-request')).toBe('yes')
    expect(headers.get('x-from-init')).toBe('yes')
    expect(headers.get('x-override')).toBe('init')
    expect(headers.get(SESSION_IDENTITY_VERSION_HEADER)).toBe('identity-a')
  })

  it('外层 fetch wrapper 存在时，重挂 Guard 只使用最新身份而不串入旧闭包', async () => {
    mount('identity-a')
    const stableDispatcher = testWindow.fetch
    const outerFetch = vi.fn<typeof fetch>((...args) => stableDispatcher(...args))
    testWindow.fetch = outerFetch
    const cleanupIdentityA = effectState.cleanup

    cleanupIdentityA?.()
    effectState.cleanup = undefined
    storage.set('swapmid:identity-version', 'identity-b')
    mount('identity-b')

    await testWindow.fetch('/api/proxy/employees')

    expect(outerFetch).toHaveBeenCalledOnce()
    expect(originalFetch).toHaveBeenCalledOnce()
    const headers = new Headers((originalFetch.mock.calls[0][1] as RequestInit).headers)
    expect(headers.get(SESSION_IDENTITY_VERSION_HEADER)).toBe('identity-b')
    expect(reload).not.toHaveBeenCalled()
  })
})

describe('SessionGuard · 会话响应与跨标签页收敛', () => {
  it('/api/proxy 401 清理失效会话；带状态头 503 保留 cookie 进入故障页', async () => {
    originalFetch
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, {
        status: 503,
        headers: { [SESSION_STATE_HEADER]: 'unavailable' },
      }))
    mount()

    await testWindow.fetch('/api/proxy/employees')
    expect(testWindow.location.href).toBe(
      '/api/auth/session-expired'
      + '?redirect=%2Fdata-integration%2Fpipelines%3Ftab%3Ddetail'
      + '&_swapmidIdentityVersion=identity-a',
    )

    testWindow.location.href = 'http://localhost/data-integration/pipelines?tab=detail'
    await testWindow.fetch('/api/proxy/data-sources')
    expect(testWindow.location.href).toBe(
      '/api/auth/session-unavailable'
      + '?redirect=%2Fdata-integration%2Fpipelines%3Ftab%3Ddetail',
    )
  })

  it('任一同源 identity-changed 响应只发布变化信号，不写回可能迟到的版本', async () => {
    originalFetch.mockResolvedValue(new Response(null, {
      status: 409,
      headers: {
        [SESSION_STATE_HEADER]: 'identity-changed',
        [SESSION_IDENTITY_VERSION_HEADER]: 'identity-b',
      },
    }))
    mount()

    await testWindow.fetch('/dashboard?__rsc=1')

    expect(storage.get('swapmid:identity-version')).toBe('identity-a')
    expect(storage.get('swapmid:identity-change-signal')).toBeTruthy()
    expect(reload).toHaveBeenCalledOnce()
  })

  it('多个并发 identity-changed 响应只广播并刷新一次', async () => {
    originalFetch.mockResolvedValue(new Response(null, {
      status: 409,
      headers: {
        [SESSION_STATE_HEADER]: 'identity-changed',
        [SESSION_IDENTITY_VERSION_HEADER]: 'identity-stale',
      },
    }))
    mount()

    await Promise.all([
      testWindow.fetch('/api/proxy/a'),
      testWindow.fetch('/api/proxy/b'),
      testWindow.fetch('/api/proxy/c'),
    ])

    expect(storageSetItem.mock.calls.filter(
      ([key]) => key === 'swapmid:identity-change-signal',
    )).toHaveLength(1)
    expect(reload).toHaveBeenCalledOnce()
  })

  it('2xx 与 identity-changed 状态头矛盾时不广播也不刷新', async () => {
    originalFetch.mockResolvedValue(new Response(null, {
      status: 200,
      headers: { [SESSION_STATE_HEADER]: 'identity-changed' },
    }))
    mount()

    await testWindow.fetch('/dashboard?__rsc=1')

    expect(storage.get('swapmid:identity-change-signal')).toBeUndefined()
    expect(reload).not.toHaveBeenCalled()
  })

  it('logout 与 authority 恢复端点不触发 Guard 的隐式广播或导航', async () => {
    const initialHref = testWindow.location.href
    originalFetch
      .mockResolvedValueOnce(new Response(null, {
        status: 409,
        headers: { [SESSION_STATE_HEADER]: 'identity-changed' },
      }))
      .mockResolvedValueOnce(new Response(null, {
        status: 503,
        headers: { [SESSION_STATE_HEADER]: 'unavailable' },
      }))
    mount()

    await testWindow.fetch('/api/auth/logout', {
      method: 'POST',
      headers: { [SESSION_IDENTITY_VERSION_HEADER]: 'identity-authority' },
    })
    await testWindow.fetch('/api/auth/session-identity-version')

    expect(testWindow.location.href).toBe(initialHref)
    expect(storage.get('swapmid:identity-change-signal')).toBeUndefined()
    expect(reload).not.toHaveBeenCalled()
    expect(new Headers((originalFetch.mock.calls[0][1] as RequestInit).headers)
      .get(SESSION_IDENTITY_VERSION_HEADER)).toBe('identity-authority')
    expect(new Headers((originalFetch.mock.calls[1][1] as RequestInit | undefined)?.headers)
      .has(SESSION_IDENTITY_VERSION_HEADER)).toBe(false)
  })

  it('身份切换入口不是 auth-control：会注入身份世代，且 409 identity-changed 会硬刷新', async () => {
    originalFetch.mockResolvedValue(new Response(null, {
      status: 409,
      headers: { [SESSION_STATE_HEADER]: SESSION_STATE_IDENTITY_CHANGED },
    }))

    for (const path of ['/api/auth/start-impersonation', '/api/auth/stop-impersonation']) {
      originalFetch.mockClear()
      reload.mockClear()
      effectState.cleanup?.()
      mount()

      await testWindow.fetch(path, { method: 'POST' })

      expect(new Headers((originalFetch.mock.calls[0][1] as RequestInit).headers)
        .get(SESSION_IDENTITY_VERSION_HEADER)).toBe('identity-a')
      expect(reload).toHaveBeenCalledOnce()
    }
  })

  it('身份切换入口的 200 identityChanged 不触发 Guard 硬刷新', async () => {
    originalFetch.mockResolvedValue(new Response(JSON.stringify({
      identityChanged: true,
      identityVersion: 'identity-b',
    }), { status: 200 }))
    mount()

    await testWindow.fetch('/api/auth/start-impersonation', { method: 'POST' })
    await testWindow.fetch('/api/auth/stop-impersonation', { method: 'POST' })

    expect(reload).not.toHaveBeenCalled()
  })

  it('旁路请求的 401 / 503 / 409 都不触发会话导航', async () => {
    const initialHref = testWindow.location.href
    originalFetch
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 503, headers: { [SESSION_STATE_HEADER]: 'unavailable' } }))
      .mockResolvedValueOnce(new Response(null, { status: 409, headers: { [SESSION_STATE_HEADER]: 'identity-changed' } }))
    mount()

    const background = { headers: { [BACKGROUND_REQUEST_HEADER]: '1' } }
    await testWindow.fetch('/api/proxy/data-sources', background)
    await testWindow.fetch('/api/proxy/nav-usage', { method: 'POST', ...background })
    expect(testWindow.location.href).toBe(initialHref)

    await testWindow.fetch('/api/proxy/data-sources', background)
    expect(reload).not.toHaveBeenCalled()
  })

  it('普通错误、非精确 proxy 路径和跨域同名路径不触发会话导航', async () => {
    const initialHref = testWindow.location.href
    originalFetch.mockResolvedValue(new Response(null, { status: 503 }))
    mount()

    await testWindow.fetch('/api/proxy/employees')
    await testWindow.fetch('/api/proxy-evil')
    await testWindow.fetch('https://evil.example/api/proxy/employees')

    expect(testWindow.location.href).toBe(initialHref)
    expect(reload).not.toHaveBeenCalled()
  })

  it('本地标记不一致时以服务端版本为准，旧文档不能覆盖新标记', async () => {
    storage.set('swapmid:identity-version', 'identity-old-marker')
    originalFetch.mockResolvedValue(new Response(null, {
      headers: { [SESSION_IDENTITY_VERSION_HEADER]: 'identity-server' },
    }))

    mount('identity-document')
    await vi.waitFor(() => expect(reload).toHaveBeenCalledOnce())

    expect(originalFetch).toHaveBeenCalledWith(
      '/api/auth/session-identity-version',
      { cache: 'no-store' },
    )
    expect(storage.get('swapmid:identity-version')).toBe('identity-server')
  })

  it('权威端点未知错误或缺少版本头时保持 fail-closed，不制造自动刷新循环', async () => {
    storage.set('swapmid:identity-version', 'identity-old-marker')
    originalFetch
      .mockResolvedValueOnce(new Response(null, { status: 500 }))
      .mockResolvedValueOnce(new Response(null, { status: 200 }))

    mount('identity-document')
    await vi.waitFor(() => expect(originalFetch).toHaveBeenCalledOnce())
    expect(reload).not.toHaveBeenCalled()
    effectState.cleanup?.()

    effectState.cleanup = undefined
    mount('identity-document')
    await vi.waitFor(() => expect(originalFetch).toHaveBeenCalledTimes(2))
    expect(reload).not.toHaveBeenCalled()
    expect(storage.get('swapmid:identity-version')).toBe('identity-old-marker')
  })

  it.each([
    ['非契约 201', () => new Response(null, {
      status: 201,
      headers: { [SESSION_IDENTITY_VERSION_HEADER]: 'identity-server' },
    })],
    ['带矛盾状态头的 401', () => new Response(null, {
      status: 401,
      headers: { [SESSION_STATE_HEADER]: SESSION_STATE_IDENTITY_CHANGED },
    })],
  ])('权威端点返回%s时不写标记、不刷新也不导航', async (_name, response) => {
    const initialHref = testWindow.location.href
    storage.set('swapmid:identity-version', 'identity-old-marker')
    originalFetch.mockResolvedValue(response())

    mount('identity-document')
    await vi.waitFor(() => expect(originalFetch).toHaveBeenCalledOnce())

    expect(testWindow.location.href).toBe(initialHref)
    expect(storage.get('swapmid:identity-version')).toBe('identity-old-marker')
    expect(reload).not.toHaveBeenCalled()
  })

  it.each([
    'short',
    'identity with space',
    `identity-${'a'.repeat(121)}`,
  ])('权威端点返回畸形版本 %s 时不写标记也不 reload', async (invalidVersion) => {
    storage.set('swapmid:identity-version', 'identity-old-marker')
    originalFetch.mockResolvedValue(new Response(null, {
      headers: { [SESSION_IDENTITY_VERSION_HEADER]: invalidVersion },
    }))

    mount('identity-document')
    await vi.waitFor(() => expect(originalFetch).toHaveBeenCalledOnce())

    expect(storage.get('swapmid:identity-version')).toBe('identity-old-marker')
    expect(reload).not.toHaveBeenCalled()
  })

  it('忽略畸形 storage marker，避免坏脚本制造权威探针风暴', () => {
    mount('identity-a')

    dispatchWindow('storage', {
      key: 'swapmid:identity-version',
      newValue: 'bad value',
    })
    dispatchWindow('storage', {
      key: 'swapmid:identity-change-signal',
      newValue: 'short',
    })

    expect(originalFetch).not.toHaveBeenCalled()
    expect(reload).not.toHaveBeenCalled()
  })

  it('重新聚焦时即使 Router Cache 零请求，也低频核对权威身份', async () => {
    originalFetch.mockResolvedValue(new Response(null, {
      headers: { [SESSION_IDENTITY_VERSION_HEADER]: 'identity-b' },
    }))
    mount()

    dispatchWindow('focus')
    dispatchDocument('visibilitychange')
    await vi.waitFor(() => expect(reload).toHaveBeenCalledOnce())

    expect(originalFetch).toHaveBeenCalledOnce()
    expect(storage.get('swapmid:identity-version')).toBe('identity-b')
  })

  it('旧 marker 与新 document 不一致时，pageshow 不抢在挂起的权威探针前刷新', async () => {
    storage.set('swapmid:identity-version', 'identity-old')
    let resolveAuthority: ((response: Response) => void) | undefined
    originalFetch.mockReturnValue(new Promise<Response>((resolve) => {
      resolveAuthority = resolve
    }))
    mount('identity-new')

    dispatchWindow('pageshow')
    dispatchWindow('focus')
    expect(reload).not.toHaveBeenCalled()
    expect(originalFetch).toHaveBeenCalledOnce()

    resolveAuthority!(new Response(null, {
      headers: { [SESSION_IDENTITY_VERSION_HEADER]: 'identity-new' },
    }))
    await vi.waitFor(() => {
      expect(storage.get('swapmid:identity-version')).toBe('identity-new')
    })
    expect(reload).not.toHaveBeenCalled()
  })

  it('storage 事件到达后，较早的权威响应不得写回旧版本', async () => {
    storage.set('swapmid:identity-version', 'identity-old-marker')
    let resolveAuthority: ((response: Response) => void) | undefined
    originalFetch
      .mockReturnValueOnce(new Promise<Response>((resolve) => {
        resolveAuthority = resolve
      }))
      .mockResolvedValueOnce(new Response(null, {
        headers: { [SESSION_IDENTITY_VERSION_HEADER]: 'identity-new' },
      }))
    mount('identity-document')

    storage.set('swapmid:identity-version', 'identity-new')
    dispatchWindow('storage', {
      key: 'swapmid:identity-version',
      newValue: 'identity-new',
    })
    resolveAuthority!(new Response(null, {
      headers: { [SESSION_IDENTITY_VERSION_HEADER]: 'identity-document' },
    }))
    await vi.waitFor(() => expect(originalFetch).toHaveBeenCalledTimes(2))
    await vi.waitFor(() => expect(reload).toHaveBeenCalledOnce())

    expect(reload).toHaveBeenCalledOnce()
    expect(storage.get('swapmid:identity-version')).toBe('identity-new')
  })

  it('多个旧标签连续发信号时，新身份文档只复核权威版本而不重复刷新', async () => {
    storage.set('swapmid:identity-version', 'identity-new')
    originalFetch.mockResolvedValue(new Response(null, {
      headers: { [SESSION_IDENTITY_VERSION_HEADER]: 'identity-new' },
    }))
    mount('identity-new')

    dispatchWindow('storage', {
      key: 'swapmid:identity-change-signal',
      newValue: 'signal-a',
    })
    dispatchWindow('storage', {
      key: 'swapmid:identity-change-signal',
      newValue: 'signal-b',
    })

    await vi.waitFor(() => expect(originalFetch).toHaveBeenCalledTimes(2))
    expect(reload).not.toHaveBeenCalled()
    expect(storage.get('swapmid:identity-version')).toBe('identity-new')
  })
})
