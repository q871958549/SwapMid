import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { NextRequest } from 'next/server'

vi.mock('@/lib/session', () => ({
  getValidSessionResult: vi.fn(),
  invalidateSessionIfUnchanged: vi.fn(),
}))

import {
  getValidSessionResult,
  invalidateSessionIfUnchanged,
} from '@/lib/session'
import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
} from '@/lib/session-availability'
import { resetStartupConnectCache } from '../proxy-startup-retry'
import { GET, HEAD, POST } from './route'

const getValidSessionResultMock = vi.mocked(getValidSessionResult)
const invalidateSessionIfUnchangedMock = vi.mocked(invalidateSessionIfUnchanged)
const SESSION = {
  identityVersion: 'identity-current',
  accessToken: 'AT-1',
  userName: 'admin',
  name: '管理员',
  grantedPolicies: {},
  accessTokenExpiresAt: Date.now() + 3_600_000,
}
const LEGACY_SESSION = {
  accessToken: 'AT-legacy',
  userName: 'legacy-admin',
  name: '旧会话管理员',
  grantedPolicies: {},
  accessTokenExpiresAt: Date.now() + 3_600_000,
}

let fetchMock: ReturnType<typeof vi.fn>
let refuseHealth = false

beforeEach(() => {
  vi.clearAllMocks()
  resetStartupConnectCache()
  refuseHealth = false
  fetchMock = vi.fn()
  vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string'
      ? input
      : input instanceof URL
        ? input.href
        : input.url
    if (url.endsWith('/health/ready') || url.endsWith('/health/live')) {
      if (refuseHealth) return Promise.reject(connectRefused())
      return Promise.resolve(new Response(null, { status: 200 }))
    }
    const userFetch = fetchMock as unknown as (
      input: RequestInfo | URL,
      init?: RequestInit,
    ) => Promise<Response>
    return userFetch(input, init)
  })
  getValidSessionResultMock.mockResolvedValue({ status: 'found', session: SESSION })
  invalidateSessionIfUnchangedMock.mockResolvedValue({ status: 'invalidated' })
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllEnvs()
})

function streamFromChunks(chunks: Uint8Array[]): ReadableStream<Uint8Array> {
  return new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(chunk)
      controller.close()
    },
  })
}

function makeReq({
  pathname = '/api/proxy/employees',
  method = 'GET',
  search = '',
  headers = {},
  includeIdentityHeader = true,
  cookies = {},
  body = null,
  signal = new AbortController().signal,
}: {
  pathname?: string
  method?: string
  search?: string
  headers?: Record<string, string>
  includeIdentityHeader?: boolean
  cookies?: Record<string, string>
  body?: ReadableStream<Uint8Array> | null
  signal?: AbortSignal
} = {}): NextRequest {
  const requestHeaders = new Headers(
    includeIdentityHeader
      ? { [SESSION_IDENTITY_VERSION_HEADER]: SESSION.identityVersion }
      : undefined,
  )
  for (const [name, value] of Object.entries(headers)) {
    requestHeaders.set(name, value)
  }
  return {
    method,
    body,
    signal,
    nextUrl: { search, pathname },
    headers: requestHeaders,
    cookies: {
      get: (name: string) => name in cookies ? { name, value: cookies[name] } : undefined,
    },
    arrayBuffer: () => {
      throw new Error('proxy route must not buffer request bodies')
    },
  } as unknown as NextRequest
}

function ctx(path: string[]) {
  return { params: Promise.resolve({ path }) }
}

function emptyUpstream(status = 200, headers: HeadersInit = { 'content-type': 'application/json' }) {
  return new Response(null, { status, headers })
}

function connectRefused(): TypeError {
  const cause = Object.assign(new Error('connect ECONNREFUSED'), { code: 'ECONNREFUSED' })
  return Object.assign(new TypeError('fetch failed'), { cause })
}

function readyAfter(delayMs: number) {
  const started = Date.now()
  vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.endsWith('/health/ready') || url.endsWith('/health/live')) {
      return Promise.resolve(new Response(null, { status: Date.now() - started >= delayMs ? 200 : 503 }))
    }
    const forward = fetchMock as unknown as (
      input: RequestInfo | URL, init?: RequestInit,
    ) => Promise<Response>
    return forward(input, init)
  })
}

function delayedResponse(ms: number, init?: RequestInit): Promise<Response> {
  return new Promise((resolve, reject) => {
    const signal = init?.signal
    const stop = () => { clearTimeout(timer); reject(signal?.reason) }
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', stop)
      resolve(emptyUpstream())
    }, ms)
    signal?.addEventListener('abort', stop, { once: true })
  })
}

describe('proxy route · 就绪等待与转发计时边界', () => {
  it('正文写请求等待超过上传idle后仍只发送一次，保留原始字节', async () => {
    vi.useFakeTimers()
    readyAfter(35_000)
    let forwarded = ''
    fetchMock.mockImplementation(async (_url: RequestInfo | URL, init?: RequestInit) => {
      forwarded = await new Response(init?.body).text()
      return emptyUpstream()
    })
    const body = streamFromChunks([new TextEncoder().encode('{"fixture":true}')])
    const pending = POST(makeReq({ method: 'POST', body }), ctx(['employees']))
    await vi.advanceTimersByTimeAsync(50_000)
    expect((await pending).status).toBe(200)
    expect(forwarded).toBe('{"fixture":true}')
    expect(fetchMock).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('无正文写请求的响应头预算从实际发送开始，不包含就绪等待', async () => {
    vi.useFakeTimers()
    readyAfter(35_000)
    fetchMock.mockImplementation((_url: RequestInfo | URL, init?: RequestInit) => delayedResponse(110_000, init))
    const pending = POST(makeReq({ method: 'POST' }), ctx(['employees']))
    await vi.advanceTimersByTimeAsync(160_000)
    expect((await pending).status).toBe(200)
    expect(fetchMock).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('GET连接退避不消耗恢复后实际请求的响应头预算', async () => {
    vi.useFakeTimers()
    const started = Date.now()
    let accepted = 0
    fetchMock.mockImplementation((_url: RequestInfo | URL, init?: RequestInit) => {
      if (Date.now() - started < 35_000) return Promise.reject(connectRefused())
      accepted += 1
      return delayedResponse(110_000, init)
    })
    const pending = GET(makeReq(), ctx(['employees']))
    await vi.advanceTimersByTimeAsync(160_000)
    expect((await pending).status).toBe(200)
    expect(accepted).toBe(1)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('就绪预算耗尽返回更新中，正文未转发且未消费流被取消', async () => {
    vi.useFakeTimers()
    refuseHealth = true
    const cancel = vi.fn()
    const body = new ReadableStream<Uint8Array>({
      start(controller) { controller.enqueue(new Uint8Array([1])) },
      cancel,
    })
    const pending = POST(makeReq({ method: 'POST', body }), ctx(['employees']))
    await vi.advanceTimersByTimeAsync(50_000)
    const response = await pending
    expect(response.status).toBe(503)
    expect((await response.json()).code).toBe('PLATFORM_UPDATING')
    expect(fetchMock).not.toHaveBeenCalled()
    expect(cancel).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('客户端在就绪等待中断开仍返回499并清理未转发流和总deadline', async () => {
    vi.useFakeTimers()
    refuseHealth = true
    const cancel = vi.fn()
    const body = new ReadableStream<Uint8Array>({
      start(controller) { controller.enqueue(new Uint8Array([1])) },
      cancel,
    })
    const controller = new AbortController()
    const pending = POST(makeReq({ method: 'POST', body, signal: controller.signal }), ctx(['employees']))
    await vi.advanceTimersByTimeAsync(5_000)
    controller.abort(new DOMException('fixture disconnected', 'AbortError'))
    const response = await pending
    expect(response.status).toBe(499)
    expect(fetchMock).not.toHaveBeenCalled()
    expect(cancel).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)
  })
})

describe('proxy route · 鉴权与路径边界', () => {
  it('无 session 返回 401，且不打后端', async () => {
    getValidSessionResultMock.mockResolvedValue({ status: 'missing' })

    const res = await GET(makeReq(), ctx(['employees']))

    expect(res.status).toBe(401)
    expect(await res.json()).toEqual({ error: 'Unauthorized' })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('会话存储不可用返回可识别 503，保留会话且不打后端', async () => {
    getValidSessionResultMock.mockResolvedValue({
      status: 'unavailable',
      retryAfterSeconds: 7,
    })

    const res = await GET(makeReq(), ctx(['employees']))

    expect(res.status).toBe(503)
    expect(await res.json()).toEqual({
      error: 'Service Unavailable',
      code: 'SESSION_UNAVAILABLE',
    })
    expect(res.headers.get('retry-after')).toBe('7')
    expect(res.headers.get('x-swapmid-session-state')).toBe('unavailable')
    expect(res.headers.get('cache-control')).toBe('private, no-store')
    expect(res.headers.get('set-cookie')).toBeNull()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('会话在校验期间已切换时返回 tagged 409，不以新身份继续原请求', async () => {
    getValidSessionResultMock.mockResolvedValue({
      status: 'identity-changed',
      reason: 'concurrent-session-change',
      session: { ...SESSION, identityVersion: 'identity-new' },
    })

    const res = await POST(
      makeReq({ method: 'POST' }),
      ctx(['employees']),
    )

    expect(res.status).toBe(409)
    expect(res.headers.get(SESSION_STATE_HEADER)).toBe('identity-changed')
    expect(res.headers.get(SESSION_IDENTITY_VERSION_HEADER)).toBe('identity-new')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('legacy 会话允许无版本 GET 过渡，并使用服务端令牌回源', async () => {
    getValidSessionResultMock.mockResolvedValue({
      status: 'found',
      session: LEGACY_SESSION,
    })
    fetchMock.mockResolvedValue(emptyUpstream())

    const res = await GET(
      makeReq({ includeIdentityHeader: false }),
      ctx(['employees']),
    )

    expect(res.status).toBe(200)
    const headers = new Headers((fetchMock.mock.calls[0][1] as RequestInit).headers)
    expect(headers.get('authorization')).toBe('Bearer AT-legacy')
  })

  it('legacy 会话在兼容窗内允许无版本 POST，并使用服务端令牌回源', async () => {
    getValidSessionResultMock.mockResolvedValue({
      status: 'found',
      session: LEGACY_SESSION,
    })
    fetchMock.mockResolvedValue(emptyUpstream())

    const res = await POST(
      makeReq({ method: 'POST', includeIdentityHeader: false }),
      ctx(['employees']),
    )

    expect(res.status).toBe(200)
    const headers = new Headers((fetchMock.mock.calls[0][1] as RequestInit).headers)
    expect(headers.get('authorization')).toBe('Bearer AT-legacy')
  })

  it('显式身份世代会话的无版本 GET fail-closed，且不打后端', async () => {
    const res = await GET(
      makeReq({ includeIdentityHeader: false }),
      ctx(['employees']),
    )

    expect(res.status).toBe(401)
    expect(await res.json()).toEqual({
      error: 'Precondition Required',
      code: 'SESSION_IDENTITY_PRECONDITION_REQUIRED',
    })
    expect(res.headers.get(SESSION_STATE_HEADER)).toBe('identity-changed')
    expect(res.headers.get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-current')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('陈旧身份世代返回权威 tagged 409', async () => {
    const res = await POST(
      makeReq({
        method: 'POST',
        headers: { [SESSION_IDENTITY_VERSION_HEADER]: 'identity-old' },
      }),
      ctx(['employees']),
    )

    expect(res.status).toBe(409)
    expect(res.headers.get(SESSION_STATE_HEADER)).toBe('identity-changed')
    expect(res.headers.get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-current')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('原生 GET 可用查询参数绑定身份，回源时只剥离内部参数并逐字保留其余查询', async () => {
    fetchMock.mockResolvedValue(emptyUpstream())

    const res = await GET(
      makeReq({
        includeIdentityHeader: false,
        search: '?filter=a%20b&_swapmidIdentityVersion=identity-current&tag=x&tag=y',
      }),
      ctx(['employees']),
    )

    expect(res.status).toBe(200)
    expect(fetchMock.mock.calls[0][0]).toBe(
      'https://localhost:44362/api/v1/employees?filter=a%20b&tag=x&tag=y',
    )
  })

  it('重复或非法编码的身份查询参数在回源前拒绝', async () => {
    const duplicate = await GET(
      makeReq({
        includeIdentityHeader: false,
        search: '?_swapmidIdentityVersion=identity-current&%5FswapmidIdentityVersion=identity-current',
      }),
      ctx(['employees']),
    )
    const malformed = await GET(
      makeReq({
        includeIdentityHeader: false,
        search: '?_swapmidIdentityVersion=%ZZ',
      }),
      ctx(['employees']),
    )

    expect(duplicate.status).toBe(400)
    expect(malformed.status).toBe(400)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it.each([
    { path: [] },
    { path: ['..', 'uploads', 'secret.pdf'] },
    { path: ['employees', 'a/b'] },
    { path: ['employees', 'a\\b'] },
  ])('路径控制输入在读取 session 前返回 404：$path', async ({ path }) => {
    const res = await GET(makeReq(), ctx(path))

    expect(res.status).toBe(404)
    expect(getValidSessionResultMock).not.toHaveBeenCalled()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('自定义/API/Swagger 路由逐段编码，并原样保留合法查询串', async () => {
    fetchMock.mockResolvedValue(emptyUpstream())

    await GET(
      makeReq({ search: '?maxResultCount=20&filter=a%20b' }),
      ctx(['employees', '研发 部门']),
    )
    await GET(makeReq(), ctx(['api', 'identity', 'users']))
    await GET(makeReq(), ctx(['swagger', 'v1', 'swagger.json']))

    expect(fetchMock.mock.calls[0][0]).toBe(
      'https://localhost:44362/api/v1/employees/%E7%A0%94%E5%8F%91%20%E9%83%A8%E9%97%A8'
      + '?maxResultCount=20&filter=a%20b',
    )
    expect(fetchMock.mock.calls[1][0]).toBe('https://localhost:44362/api/identity/users')
    expect(fetchMock.mock.calls[2][0]).toBe('https://localhost:44362/swagger/v1/swagger.json')
  })

  it('超长查询串在回源前返回 404', async () => {
    const res = await GET(makeReq({ search: `?${'a'.repeat(8192)}` }), ctx(['employees']))

    expect(res.status).toBe(404)
    expect(fetchMock).not.toHaveBeenCalled()
  })
})

describe('proxy route · 请求流、限额与请求头', () => {
  it('二进制请求逐块原样转发，不调用 arrayBuffer，并启用 half duplex', async () => {
    const raw = new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0x80, 0xff, 0x00])
    fetchMock.mockImplementation(async (_url, init: RequestInit & { duplex?: string }) => {
      const forwarded = new Uint8Array(await new Response(init.body).arrayBuffer())
      expect(Array.from(forwarded)).toEqual(Array.from(raw))
      expect(init.duplex).toBe('half')
      expect(init.cache).toBe('no-store')
      expect(init.redirect).toBe('manual')
      return emptyUpstream()
    })

    const res = await POST(
      makeReq({
        method: 'POST',
        headers: { 'content-type': 'application/octet-stream' },
        body: streamFromChunks([raw.subarray(0, 3), raw.subarray(3)]),
      }),
      ctx(['binary-test']),
    )

    expect(res.status).toBe(200)
  })

  it('上游可在第二块产生前读取首块，上传链路没有隐式整包缓冲', async () => {
    let releaseSecond: (() => void) | undefined
    let reportFirst: ((chunk: Uint8Array) => void) | undefined
    const firstSeen = new Promise<Uint8Array>(resolve => { reportFirst = resolve })
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array([0x50, 0x4b]))
        releaseSecond = () => {
          controller.enqueue(new Uint8Array([0x80, 0xff]))
          controller.close()
        }
      },
    })
    fetchMock.mockImplementation(async (_url, init: RequestInit) => {
      const reader = (init.body as ReadableStream<Uint8Array>).getReader()
      const first = await reader.read()
      reportFirst!(first.value!)
      const second = await reader.read()
      expect(Array.from(second.value ?? [])).toEqual([0x80, 0xff])
      expect((await reader.read()).done).toBe(true)
      return emptyUpstream()
    })

    const pending = POST(
      makeReq({ method: 'POST', body }),
      ctx(['binary-test']),
    )
    expect(Array.from(await firstSeen)).toEqual([0x50, 0x4b])
    releaseSecond!()

    expect((await pending).status).toBe(200)
  })

  it('声明长度超限/非法时直接拒绝，伪造偏小时仍按真实字节返回 413', async () => {
    const declaredTooLarge = await POST(
      makeReq({
        method: 'POST',
        headers: { 'content-length': String(1024 * 1024 + 1) },
        body: streamFromChunks([new Uint8Array([1])]),
      }),
      ctx(['ordinary-json']),
    )
    const invalidLength = await POST(
      makeReq({
        method: 'POST',
        headers: { 'content-length': '-1' },
        body: streamFromChunks([new Uint8Array([1])]),
      }),
      ctx(['ordinary-json']),
    )

    expect(declaredTooLarge.status).toBe(413)
    expect(invalidLength.status).toBe(400)
    expect(fetchMock).not.toHaveBeenCalled()

    fetchMock.mockImplementation(async (_url, init: RequestInit) => {
      await new Response(init.body).arrayBuffer()
      return emptyUpstream()
    })
    const actualTooLarge = await POST(
      makeReq({
        method: 'POST',
        headers: { 'content-length': '1' },
        body: streamFromChunks([new Uint8Array(1024 * 1024), new Uint8Array([1])]),
      }),
      ctx(['ordinary-json']),
    )
    expect(actualTooLarge.status).toBe(413)
  })

  it('精确上传端点的非法 multipart boundary 返回 400，压缩请求体返回 415', async () => {
    const invalidBoundary = await POST(
      makeReq({
        method: 'POST',
        headers: { 'content-type': 'multipart/form-data' },
        body: streamFromChunks([new Uint8Array([1])]),
      }),
      ctx(['avatars']),
    )
    const compressed = await POST(
      makeReq({
        method: 'POST',
        headers: { 'content-encoding': 'gzip' },
        body: streamFromChunks([new Uint8Array([1])]),
      }),
      ctx(['ordinary-json']),
    )

    expect(invalidBoundary.status).toBe(400)
    expect(compressed.status).toBe(415)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('头像 multipart 上传允许 2MB 请求体并正常回源', async () => {
    fetchMock.mockImplementation(async (_url, init: RequestInit) => {
      await new Response(init.body).arrayBuffer()
      return emptyUpstream()
    })
    const allowed = await POST(
      makeReq({
        method: 'POST',
        headers: {
          'content-type': 'multipart/form-data; boundary=----SwapMidBoundary123',
          'content-length': String(2 * 1024 * 1024),
        },
        body: streamFromChunks([new Uint8Array([1])]),
      }),
      ctx(['avatars']),
    )

    expect(allowed.status).toBe(200)
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('仅透传服务端 Bearer、语言、Accept 与 GET Range 条件头', async () => {
    fetchMock.mockResolvedValue(emptyUpstream())
    await GET(
      makeReq({
        headers: {
          accept: 'application/octet-stream',
          authorization: 'Bearer browser-token',
          cookie: 'secret=1',
          range: 'bytes=0-3',
          'if-range': '"etag"',
          'x-secret': 'never-forward',
        },
        cookies: { swapmid_language: 'zh-Hant' },
      }),
      ctx(['attachments', 'download']),
    )

    const headers = new Headers((fetchMock.mock.calls[0][1] as RequestInit).headers)
    expect(headers.get('authorization')).toBe('Bearer AT-1')
    expect(headers.get('accept-language')).toBe('zh-Hans')
    expect(headers.get('accept')).toBe('application/octet-stream')
    expect(headers.get('accept-encoding')).toBe('identity')
    expect(headers.get('range')).toBe('bytes=0-3')
    expect(headers.get('if-range')).toBe('"etag"')
    expect(headers.get('cookie')).toBeNull()
    expect(headers.get('x-secret')).toBeNull()
  })
})

describe('proxy route · 分阶段超时与断连传播', () => {
  it('请求体持续空闲时返回 408', async () => {
    vi.useFakeTimers()
    fetchMock.mockImplementation((_url, init: RequestInit) => new Promise((_resolve, reject) => {
      init.signal?.addEventListener('abort', () => reject(init.signal?.reason), { once: true })
    }))
    const neverProducesBody = new ReadableStream<Uint8Array>({ start() {} })

    const pending = POST(
      makeReq({ method: 'POST', body: neverProducesBody }),
      ctx(['ordinary-json']),
    )
    await vi.advanceTimersByTimeAsync(30_000)

    expect((await pending).status).toBe(408)
  })

  it('无请求体时等待响应头超过窗口返回 504', async () => {
    vi.useFakeTimers()
    fetchMock.mockImplementation((_url, init: RequestInit) => new Promise((_resolve, reject) => {
      init.signal?.addEventListener('abort', () => reject(init.signal?.reason), { once: true })
    }))

    const pending = GET(makeReq(), ctx(['employees']))
    await vi.advanceTimersByTimeAsync(120_000)

    expect((await pending).status).toBe(504)
  })

  it('目标回执机器核验要求独立权限，无权限不回源', async () => {
    const segments = [
      'pipeline-executions',
      '11111111-1111-4111-8111-111111111111',
      'verify-target-receipt',
    ]
    const denied = await POST(makeReq({ method: 'POST' }), ctx(segments))

    expect(denied.status).toBe(403)
    expect(fetchMock).not.toHaveBeenCalled()

    getValidSessionResultMock.mockResolvedValue({
      status: 'found',
      session: {
        ...SESSION,
        grantedPolicies: {
          'SwapMid.DataIntegration.PipelineExecutions.VerifyTargetReceipt': true,
        },
      },
    })
    fetchMock.mockResolvedValue(emptyUpstream())
    const allowed = await POST(makeReq({ method: 'POST' }), ctx(segments))

    expect(allowed.status).toBe(200)
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('数据管道执行端点要求 Execute 权限且享受 6 分钟响应头预算', async () => {
    const segments = [
      'data-pipelines',
      '11111111-1111-4111-8111-111111111111',
      'execute',
    ]
    expect((await POST(makeReq({ method: 'POST' }), ctx(segments))).status).toBe(403)

    vi.useFakeTimers()
    getValidSessionResultMock.mockResolvedValue({
      status: 'found',
      session: {
        ...SESSION,
        grantedPolicies: {
          'SwapMid.DataIntegration.DataPipelines.Execute': true,
        },
      },
    })
    fetchMock.mockImplementation((_url, init: RequestInit) => new Promise((_resolve, reject) => {
      init.signal?.addEventListener('abort', () => reject(init.signal?.reason), { once: true })
    }))

    let settled = false
    const pending = POST(makeReq({ method: 'POST' }), ctx(segments))
    void pending.finally(() => { settled = true })

    await vi.advanceTimersByTimeAsync(120_000)
    expect(settled).toBe(false)

    await vi.advanceTimersByTimeAsync(4 * 60_000)
    const response = await pending
    expect(response.status).toBe(504)
    expect(response.headers.get('cache-control')).toBe('private, no-store')
  })

  it('客户端取消映射 499；无关网络错误即使随后 abort 也保持 502', async () => {
    const client = new AbortController()
    fetchMock.mockImplementation((_url, init: RequestInit) => new Promise((_resolve, reject) => {
      if (init.signal?.aborted) {
        reject(init.signal.reason)
        return
      }
      init.signal?.addEventListener('abort', () => reject(init.signal?.reason), { once: true })
    }))
    const pending = GET(makeReq({ signal: client.signal }), ctx(['employees']))
    client.abort(new DOMException('client disconnected', 'AbortError'))
    expect((await pending).status).toBe(499)

    const lateAbort = new AbortController()
    fetchMock.mockImplementation(async () => {
      lateAbort.abort(new DOMException('late disconnect', 'AbortError'))
      throw new TypeError('connect failed first')
    })
    const networkFailure = await GET(
      makeReq({ signal: lateAbort.signal }),
      ctx(['employees']),
    )
    expect(networkFailure.status).toBe(502)
  })

  it('上游最终响应消费完成后立即取消仍未结束的请求源', async () => {
    let sourceCancelled = false
    const endlessBody = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array([1]))
      },
      cancel() {
        sourceCancelled = true
      },
    })
    fetchMock.mockImplementation(async (_url, init: RequestInit) => {
      const reader = (init.body as ReadableStream<Uint8Array>).getReader()
      expect(Array.from((await reader.read()).value ?? [])).toEqual([1])
      void (async () => {
        try {
          await reader.read()
        } catch {
          // 上游最终响应结束后，请求源应被主动取消。
        }
      })()
      return new Response('forbidden', {
        status: 403,
        headers: { 'content-type': 'text/plain' },
      })
    })

    const res = await POST(
      makeReq({ method: 'POST', body: endlessBody }),
      ctx(['ordinary-json']),
    )
    expect(sourceCancelled).toBe(false)
    expect(await res.text()).toBe('forbidden')
    await vi.waitFor(() => expect(sourceCancelled).toBe(true))
  })
})

describe('proxy route · 响应流、状态与响应头', () => {
  it('上游 401 的 exact-revision CAS 命中时返回 401', async () => {
    fetchMock.mockResolvedValue(emptyUpstream(401))

    const res = await GET(makeReq(), ctx(['employees']))

    expect(res.status).toBe(401)
    expect(await res.json()).toEqual({ error: 'Unauthorized' })
    expect(invalidateSessionIfUnchangedMock).toHaveBeenCalledWith(SESSION)
  })

  it('上游 401 遇到同身份新 token 时返回 tagged 409，不淘汰胜者会话', async () => {
    const refreshed = { ...SESSION, accessToken: 'AT-2' }
    fetchMock.mockResolvedValue(emptyUpstream(401))
    invalidateSessionIfUnchangedMock.mockResolvedValue({
      status: 'session-updated',
      session: refreshed,
    })

    const res = await GET(makeReq(), ctx(['employees']))

    expect(res.status).toBe(409)
    expect(await res.json()).toEqual({
      error: 'Conflict',
      code: 'SESSION_IDENTITY_CHANGED',
    })
    expect(res.headers.get(SESSION_STATE_HEADER)).toBe('identity-changed')
    expect(res.headers.get(SESSION_IDENTITY_VERSION_HEADER))
      .toBe('identity-current')
    expect(invalidateSessionIfUnchangedMock).toHaveBeenCalledWith(SESSION)
  })

  it('首个响应块可在第二块产生前读取，不缓冲完整下载', async () => {
    let releaseSecond: (() => void) | undefined
    const upstreamBody = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array([0x50, 0x4b]))
        releaseSecond = () => {
          controller.enqueue(new Uint8Array([0x80, 0xff]))
          controller.close()
        }
      },
    })
    fetchMock.mockResolvedValue(new Response(upstreamBody, {
      headers: {
        'content-type': 'application/octet-stream',
        'content-length': '4',
        'cache-control': 'public, max-age=3600',
        location: 'https://internal.example/secret',
        'set-cookie': 'secret=1',
      },
    }))

    const res = await GET(makeReq(), ctx(['export']))
    const reader = res.body!.getReader()
    expect(Array.from((await reader.read()).value ?? [])).toEqual([0x50, 0x4b])
    expect(res.headers.get('content-type')).toBe('application/octet-stream')
    expect(res.headers.get('cache-control')).toBe('private, no-store')
    expect(res.headers.get('content-length')).toBeNull()
    expect(res.headers.get('location')).toBeNull()
    expect(res.headers.get('set-cookie')).toBeNull()

    releaseSecond!()
    expect(Array.from((await reader.read()).value ?? [])).toEqual([0x80, 0xff])
    expect((await reader.read()).done).toBe(true)
  })

  it('状态码透传；HEAD、204、205、304 都不返回 body', async () => {
    fetchMock.mockResolvedValueOnce(new Response('forbidden', { status: 403 }))
    const forbidden = await GET(makeReq(), ctx(['employees']))
    expect(forbidden.status).toBe(403)
    expect(await forbidden.text()).toBe('forbidden')

    fetchMock.mockResolvedValueOnce(emptyUpstream(204))
    const noContent = await GET(makeReq(), ctx(['employees']))
    expect(noContent.status).toBe(204)
    expect(noContent.body).toBeNull()

    fetchMock.mockResolvedValueOnce(emptyUpstream(205))
    const resetContent = await GET(makeReq(), ctx(['employees']))
    expect(resetContent.status).toBe(205)
    expect(resetContent.body).toBeNull()

    fetchMock.mockResolvedValueOnce(emptyUpstream(304, { etag: '"abc"' }))
    const notModified = await GET(makeReq(), ctx(['employees']))
    expect(notModified.status).toBe(304)
    expect(notModified.body).toBeNull()
    expect(notModified.headers.get('etag')).toBe('"abc"')

    fetchMock.mockResolvedValueOnce(new Response('must-not-forward'))
    const head = await HEAD(makeReq({ method: 'HEAD' }), ctx(['employees']))
    expect(head.status).toBe(200)
    expect(head.body).toBeNull()
  })

  it('API 连接被拒时 GET 退避后返回 503，POST 不重试', async () => {
    vi.useFakeTimers()
    fetchMock.mockRejectedValue(connectRefused())
    const pending = GET(makeReq(), ctx(['employees']))
    await vi.advanceTimersByTimeAsync(50_000)
    const retried = await pending
    expect(retried.status).toBe(503)
    expect(retried.headers.get('retry-after')).toBe('15')
    expect(await retried.json()).toEqual({
      error: 'Service Unavailable',
      code: 'PLATFORM_UPDATING',
    })
    expect(fetchMock.mock.calls.length).toBeGreaterThan(1)

    fetchMock.mockReset()
    fetchMock.mockRejectedValue(connectRefused())
    resetStartupConnectCache()
    refuseHealth = true
    const pendingWrite = POST(makeReq({ method: 'POST' }), ctx(['employees']))
    await vi.advanceTimersByTimeAsync(50_000)
    const write = await pendingWrite
    expect(write.status).toBe(503)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('健康路径都 404 时写请求立即发出', async () => {
    fetchMock.mockResolvedValue(emptyUpstream())
    vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string'
        ? input
        : input instanceof URL
          ? input.href
          : input.url
      if (url.endsWith('/health/ready') || url.endsWith('/health/live')) {
        return Promise.resolve(new Response(null, { status: 404 }))
      }
      const userFetch = fetchMock as unknown as (
        input: RequestInfo | URL,
        init?: RequestInit,
      ) => Promise<Response>
      return userFetch(input, init)
    })

    const write = await POST(makeReq({ method: 'POST' }), ctx(['employees']))
    expect(write.status).toBe(200)
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('GET 在连接恢复后返回上游结果，非启动类网络错误仍是 502', async () => {
    vi.useFakeTimers()
    fetchMock
      .mockRejectedValueOnce(connectRefused())
      .mockResolvedValueOnce(emptyUpstream())
    const pending = GET(makeReq(), ctx(['employees']))
    await vi.advanceTimersByTimeAsync(1_000)
    const recovered = await pending
    expect(recovered.status).toBe(200)
    expect(fetchMock).toHaveBeenCalledTimes(2)

    vi.useRealTimers()
    fetchMock.mockReset()
    fetchMock.mockRejectedValueOnce(new TypeError('connect failed'))
    expect((await GET(makeReq(), ctx(['employees']))).status).toBe(502)
  })

  it('上游连接失败返回 502；缺 Content-Type 的有体响应保持 JSON 兼容兜底', async () => {
    fetchMock.mockRejectedValueOnce(new TypeError('connect failed'))
    expect((await GET(makeReq(), ctx(['employees']))).status).toBe(502)

    fetchMock.mockResolvedValueOnce(new Response('{}'))
    const response = await GET(makeReq(), ctx(['employees']))
    expect(response.headers.get('content-type')).toBe('text/plain;charset=UTF-8')
    await response.text()

    const rawBody = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new TextEncoder().encode('{}'))
        controller.close()
      },
    })
    fetchMock.mockResolvedValueOnce(new Response(rawBody))
    const rawResponse = await GET(makeReq(), ctx(['employees']))
    expect(rawResponse.headers.get('content-type')).toBe('application/json')
    await rawResponse.text()
  })
})
