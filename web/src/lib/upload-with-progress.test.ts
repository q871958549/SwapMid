import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getApiErrorMessage } from '@/lib/api-error'
import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
  SESSION_STATE_IDENTITY_CHANGED,
  SESSION_STATE_UNAVAILABLE,
} from '@/lib/session-availability'
import { SessionTakeoverError, uploadWithProgress } from './upload-with-progress'

const signalSessionIdentityChange = vi.hoisted(() => vi.fn())
vi.mock('@/lib/client-session-recovery', () => ({ signalSessionIdentityChange }))

/**
 * 最小 XHR 替身。jsdom 自带的 XMLHttpRequest 会真发请求且无法驱动 upload 事件，
 * 而本模块的全部价值恰恰在 upload 进度与响应头分支上，故整体替换。
 */
class FakeXhr {
  static instances: FakeXhr[] = []
  static get last(): FakeXhr {
    const xhr = FakeXhr.instances.at(-1)
    if (!xhr) throw new Error('没有 XHR 实例——uploadWithProgress 应当同步 open+send')
    return xhr
  }

  status = 0
  responseText = ''
  upload: {
    onprogress: ((e: { loaded: number; total: number; lengthComputable: boolean }) => void) | null
    onload: (() => void) | null
  } = { onprogress: null, onload: null }

  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  ontimeout: (() => void) | null = null

  method = ''
  url = ''
  sentBody: unknown = null
  private readonly requestHeaders = new Map<string, string>()
  private responseHeaders: Record<string, string> = {}

  constructor() {
    FakeXhr.instances.push(this)
  }

  open(method: string, url: string) {
    this.method = method
    this.url = url
  }

  setRequestHeader(key: string, value: string) {
    this.requestHeaders.set(key, value)
  }

  getResponseHeader(key: string): string | null {
    return this.responseHeaders[key] ?? null
  }

  send(body: unknown) {
    this.sentBody = body
  }

  // —— 以下是测试驱动用的，不属于 XHR 接口 ——
  header(key: string): string | undefined {
    return this.requestHeaders.get(key)
  }

  progress(loaded: number, total: number, lengthComputable = true) {
    this.upload.onprogress?.({ loaded, total, lengthComputable })
  }

  uploadDone() {
    this.upload.onload?.()
  }

  respond(status: number, body: string, headers: Record<string, string> = {}) {
    this.status = status
    this.responseText = body
    this.responseHeaders = headers
    this.onload?.()
  }
}

let reload: ReturnType<typeof vi.fn>

beforeEach(() => {
  FakeXhr.instances = []
  signalSessionIdentityChange.mockClear()
  vi.stubGlobal('XMLHttpRequest', FakeXhr)
  reload = vi.fn()
  Object.defineProperty(window, 'location', {
    configurable: true,
    writable: true,
    value: { href: '', pathname: '/quality-document/stamp', search: '?page=2', reload },
  })
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function upload(onProgress?: Parameters<typeof uploadWithProgress>[0]['onProgress']) {
  return uploadWithProgress<{ id: string }>({
    url: '/api/proxy/stamp-jobs/abc/items',
    body: new FormData(),
    identityVersion: 'identity-v7',
    onProgress,
  })
}

describe('uploadWithProgress', () => {
  it('POST 到目标 URL 并带上会话身份头（XHR 不经 SessionGuard 的 fetch 补丁，漏了必 401）', () => {
    void upload().catch(() => {})
    expect(FakeXhr.last.method).toBe('POST')
    expect(FakeXhr.last.url).toBe('/api/proxy/stamp-jobs/abc/items')
    expect(FakeXhr.last.header(SESSION_IDENTITY_VERSION_HEADER)).toBe('identity-v7')
  })

  it('2xx 解析 JSON 响应体', async () => {
    const promise = upload()
    FakeXhr.last.respond(200, '{"id":"item-1"}')
    await expect(promise).resolves.toEqual({ id: 'item-1' })
  })

  it('上传阶段回报百分比，且封顶 99（100% 只能由服务端确认置位）', async () => {
    const onProgress = vi.fn()
    const promise = upload(onProgress)
    FakeXhr.last.progress(50, 200)
    expect(onProgress).toHaveBeenLastCalledWith({
      phase: 'uploading', loaded: 50, total: 200, percent: 25,
    })

    FakeXhr.last.progress(200, 200)
    expect(onProgress).toHaveBeenLastCalledWith({
      phase: 'uploading', loaded: 200, total: 200, percent: 99,
    })

    FakeXhr.last.respond(200, '{"id":"x"}')
    await promise
  })

  it('总长未知时 percent 为 null，不拿 total=0 去算出 Infinity', async () => {
    const onProgress = vi.fn()
    const promise = upload(onProgress)
    FakeXhr.last.progress(1024, 0, false)
    expect(onProgress).toHaveBeenLastCalledWith({
      phase: 'uploading', loaded: 1024, total: 0, percent: null,
    })
    FakeXhr.last.respond(200, '{"id":"x"}')
    await promise
  })

  it('字节发完切 processing 阶段——否则进度条停在 99% 不动，看着就像卡死', async () => {
    const onProgress = vi.fn()
    const promise = upload(onProgress)
    FakeXhr.last.progress(200, 200)
    FakeXhr.last.uploadDone()
    expect(onProgress).toHaveBeenLastCalledWith({
      phase: 'processing', loaded: 0, total: 0, percent: null,
    })
    FakeXhr.last.respond(200, '{"id":"x"}')
    await promise
  })

  it('409 + identity-changed：广播身份变化并刷新页面，抛 SessionTakeoverError', async () => {
    const promise = upload()
    FakeXhr.last.respond(409, '{}', { [SESSION_STATE_HEADER]: SESSION_STATE_IDENTITY_CHANGED })
    await expect(promise).rejects.toBeInstanceOf(SessionTakeoverError)
    expect(signalSessionIdentityChange).toHaveBeenCalledTimes(1)
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('503 + unavailable：跳会话故障页并带回跳地址', async () => {
    const promise = upload()
    FakeXhr.last.respond(503, '{}', { [SESSION_STATE_HEADER]: SESSION_STATE_UNAVAILABLE })
    await expect(promise).rejects.toBeInstanceOf(SessionTakeoverError)
    expect(window.location.href).toBe(
      `/api/auth/session-unavailable?redirect=${encodeURIComponent('/quality-document/stamp?page=2')}`,
    )
  })

  it('401：跳登录过期页并带上当前身份世代', async () => {
    const promise = upload()
    FakeXhr.last.respond(401, '')
    await expect(promise).rejects.toBeInstanceOf(SessionTakeoverError)
    expect(window.location.href).toContain('/api/auth/session-expired')
    expect(window.location.href).toContain('identity-v7')
  })

  it('JSON 错误体原样抛出，getApiErrorMessage 能解出后端中文原因', async () => {
    const promise = upload()
    FakeXhr.last.respond(400, '{"error":{"message":"文件已损坏"}}')
    await expect(promise).rejects.toThrow()
    await promise.catch((e: unknown) => {
      expect(getApiErrorMessage(e, '兜底')).toBe('文件已损坏')
    })
  })

  it('非 JSON 错误体降级为状态码——网关的 HTML 错误页整篇塞进 UI 只会刷屏', async () => {
    const promise = upload()
    FakeXhr.last.respond(502, '<html><body>Bad Gateway</body></html>')
    await expect(promise).rejects.toThrow('HTTP 502')
  })

  it('网络错误抛出可识别的标记', async () => {
    const promise = upload()
    FakeXhr.last.onerror?.()
    await expect(promise).rejects.toThrow('NETWORK_ERROR')
  })

  it('会话分支不吞 500——非围栏状态码走普通错误路径，不触发任何导航', async () => {
    const promise = upload()
    FakeXhr.last.respond(409, '{"error":{"message":"编码重复"}}') // 409 但无会话状态头
    await expect(promise).rejects.not.toBeInstanceOf(SessionTakeoverError)
    expect(signalSessionIdentityChange).not.toHaveBeenCalled()
    expect(reload).not.toHaveBeenCalled()
  })
})
