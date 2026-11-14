import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { resolveBackendApiBase } from './backend-origin'
import {
  ServerBackendFetchError,
  serverBackendFetch,
} from './server-backend-fetch'

const DEFAULT_POLICY = {
  deadlineMs: 5_000,
  maxResponseBytes: 1024,
} as const

beforeEach(() => {
  vi.stubEnv('INTERNAL_API_URL', 'https://backend.example')
  vi.stubEnv('NEXT_PUBLIC_API_URL', 'https://public.example')
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllEnvs()
})

describe('resolveBackendApiBase', () => {
  it('INTERNAL_API_URL 优先，并规范化为纯 origin', () => {
    expect(resolveBackendApiBase({
      INTERNAL_API_URL: 'http://api:80/',
      NEXT_PUBLIC_API_URL: 'https://public.example',
    })).toBe('http://api')
    expect(resolveBackendApiBase({
      NEXT_PUBLIC_API_URL: 'https://public.example:443/',
    })).toBe('https://public.example')
    expect(resolveBackendApiBase({})).toBe('https://localhost:44362')
  })

  it.each([
    'ftp://api',
    'https://user:pass@api',
    'https://api/internal',
    'https://api?target=other',
    'https://api#fragment',
    ' https://api',
    'https://api ',
    '',
  ])('拒绝非严格 HTTP(S) origin：%s', value => {
    expect(() => resolveBackendApiBase({ INTERNAL_API_URL: value })).toThrow()
  })
})

describe('serverBackendFetch', () => {
  it('单次请求后按实际字节缓冲，保留 JSON/text 读取语义', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ ok: true }),
      {
        status: 200,
        headers: {
          'content-type': 'application/json',
          'content-length': '11',
        },
      },
    ))
    vi.stubGlobal('fetch', fetchMock)

    const response = await serverBackendFetch('/api/items?filter=safe', DEFAULT_POLICY)

    await expect(response.clone().text()).resolves.toBe('{"ok":true}')
    await expect(response.json()).resolves.toEqual({ ok: true })
    expect(response.headers.get('content-length')).toBeNull()
    expect(fetchMock).toHaveBeenCalledOnce()
    expect(fetchMock.mock.calls[0][0])
      .toBe('https://backend.example/api/items?filter=safe')
    expect(fetchMock.mock.calls[0][1]).toMatchObject({
      redirect: 'manual',
      signal: expect.any(AbortSignal),
    })
  })

  it('Content-Length 伪造偏小时仍按实际读取字节拒绝', async () => {
    const source = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array([1, 2, 3, 4]))
        controller.enqueue(new Uint8Array([5, 6, 7, 8]))
        controller.close()
      },
    })
    const fetchMock = vi.fn().mockResolvedValue(new Response(source, {
      headers: { 'content-length': '1' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(serverBackendFetch('/api/items', {
      deadlineMs: 5_000,
      maxResponseBytes: 6,
    })).rejects.toMatchObject({
      name: 'ServerBackendFetchError',
      kind: 'response-too-large',
    })
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('声明长度超限时提前拒绝，但仍不发起第二次请求', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('small', {
      headers: { 'content-length': '2048' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(serverBackendFetch('/api/items', DEFAULT_POLICY))
      .rejects.toMatchObject({ kind: 'response-too-large' })
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('声明长度超限时不等待底层永不完成的 cancel', async () => {
    const cancel = vi.fn(() => new Promise<void>(() => {}))
    const source = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array([1]))
      },
      cancel,
    })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(source, {
      headers: { 'content-length': '2048' },
    })))

    await expect(serverBackendFetch('/api/items', DEFAULT_POLICY))
      .rejects.toMatchObject({ kind: 'response-too-large' })
    expect(cancel).toHaveBeenCalledOnce()
  })

  it('实际字节超限时不等待底层永不完成的 cancel', async () => {
    const cancel = vi.fn(() => new Promise<void>(() => {}))
    const source = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array([1, 2, 3, 4]))
        controller.enqueue(new Uint8Array([5, 6, 7, 8]))
      },
      cancel,
    })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(source)))

    await expect(serverBackendFetch('/api/items', {
      deadlineMs: 5_000,
      maxResponseBytes: 6,
    })).rejects.toMatchObject({ kind: 'response-too-large' })
    expect(cancel).toHaveBeenCalledOnce()
  })

  it('deadline 覆盖 fetch 黑洞并分类为 timeout', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.fn((_url: string, init: RequestInit) =>
      new Promise((_resolve, reject) => {
        init.signal?.addEventListener(
          'abort',
          () => reject(init.signal?.reason),
          { once: true },
        )
      }))
    vi.stubGlobal('fetch', fetchMock)

    const request = serverBackendFetch('/api/slow', {
      deadlineMs: 100,
      maxResponseBytes: 1024,
    })
    const rejection = request.catch(error => error)
    await vi.advanceTimersByTimeAsync(100)

    await expect(rejection).resolves.toMatchObject({
      name: 'ServerBackendFetchError',
      kind: 'timeout',
    })
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('deadline 同样覆盖已经收到响应首部后的正文黑洞', async () => {
    vi.useFakeTimers()
    const cancel = vi.fn(() => new Promise<void>(() => {}))
    const source = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array([1]))
      },
      cancel,
    })
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(source)),
    )

    const request = serverBackendFetch('/api/slow-body', {
      deadlineMs: 100,
      maxResponseBytes: 1024,
    })
    const rejection = request.catch(error => error)
    await vi.advanceTimersByTimeAsync(100)

    await expect(rejection).resolves.toMatchObject({ kind: 'timeout' })
    expect(cancel).toHaveBeenCalledOnce()
  })

  it('调用方取消优先分类为 aborted，不误报 timeout', async () => {
    const caller = new AbortController()
    const fetchMock = vi.fn((_url: string, init: RequestInit) =>
      new Promise((_resolve, reject) => {
        init.signal?.addEventListener(
          'abort',
          () => reject(init.signal?.reason),
          { once: true },
        )
        caller.abort(new Error('client disconnected with secret-token'))
      }))
    vi.stubGlobal('fetch', fetchMock)

    const request = serverBackendFetch('/api/slow', {
      ...DEFAULT_POLICY,
      signal: caller.signal,
    })

    await expect(request).rejects.toMatchObject({
      name: 'ServerBackendFetchError',
      kind: 'aborted',
      message: 'Server backend request was cancelled',
    })
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('调用方即使使用同类错误作为 abort reason 也只能分类为 aborted', async () => {
    const caller = new AbortController()
    const fetchMock = vi.fn((_url: string, init: RequestInit) =>
      new Promise((_resolve, reject) => {
        init.signal?.addEventListener(
          'abort',
          () => reject(init.signal?.reason),
          { once: true },
        )
        caller.abort(new ServerBackendFetchError(
          'response-too-large',
          'caller supplied misleading error',
        ))
      }))
    vi.stubGlobal('fetch', fetchMock)

    const request = serverBackendFetch('/api/slow', {
      ...DEFAULT_POLICY,
      signal: caller.signal,
    })

    await expect(request).rejects.toMatchObject({
      name: 'ServerBackendFetchError',
      kind: 'aborted',
      message: 'Server backend request was cancelled',
    })
  })

  it('POST 网络失败不自动重试，且分类错误不泄漏 query/token', async () => {
    const fetchMock = vi.fn().mockRejectedValue(
      new Error('request to ?token=secret-token failed'),
    )
    vi.stubGlobal('fetch', fetchMock)

    let caught: unknown
    try {
      await serverBackendFetch('/connect/token?trace=sensitive-query', {
        ...DEFAULT_POLICY,
        method: 'POST',
        body: new URLSearchParams({ refresh_token: 'secret-token' }),
      })
    } catch (error) {
      caught = error
    }

    expect(caught).toBeInstanceOf(ServerBackendFetchError)
    expect(caught).toMatchObject({ kind: 'unavailable' })
    expect((caught as Error).message).not.toContain('secret-token')
    expect((caught as Error).message).not.toContain('sensitive-query')
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it.each([
    ['跨 origin 路径', '//evil.example/path'],
    ['反斜杠路径', '/api\\evil'],
    ['fragment', '/api/items#secret'],
    ['字面量点段', '/api/v1/../connect/token'],
    ['编码点段', '/api/v1/%2e%2e/connect/token'],
    ['双重编码点段', '/api/v1/%252e%252e/connect/token'],
  ])('%s 在 fetch 前被拒绝', async (_label, path) => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    await expect(serverBackendFetch(path, DEFAULT_POLICY))
      .rejects.toMatchObject({ kind: 'invalid-request' })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('禁止自动跟随重定向', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    await expect(serverBackendFetch('/api/items', {
      ...DEFAULT_POLICY,
      redirect: 'follow',
    })).rejects.toMatchObject({ kind: 'invalid-request' })
    expect(fetchMock).not.toHaveBeenCalled()
  })
})
