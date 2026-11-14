import 'server-only'

import { headers } from 'next/headers'
import { resolveBackendApiBase } from './backend-origin'
import { withTrustedClientIp } from './trusted-client-ip'

const KIB = 1024
const MIB = 1024 * KIB
const MAX_CONFIGURABLE_RESPONSE_BYTES = 64 * MIB
const MAX_BACKEND_PATH_AND_QUERY_LENGTH = 16 * KIB

/**
 * 每类服务端回源显式选择资源预算，禁止调用点自行给出无边界的“大数兜底”。
 * 限额针对解码后的实际响应字节；Content-Length 只用于提前拒绝，不能替代逐块计数。
 */
export const SERVER_BACKEND_RESPONSE_LIMIT_BYTES = {
  oauthToken: 64 * KIB,
  sessionRefresh: 64 * KIB,
  applicationConfiguration: 2 * MIB,
  authenticatedApiJson: 16 * MIB,
  externalPage: 2 * MIB,
  productQuery: 1 * MIB,
} as const

/** deadline 覆盖连接、响应首部和完整有界响应体读取。 */
export const SERVER_BACKEND_DEADLINE_MS = {
  sessionRefresh: 8_000,
  authentication: 15_000,
  applicationConfiguration: 15_000,
  authenticatedApi: 60_000,
  publicPage: 10_000,
} as const

export type ServerBackendFetchErrorKind =
  | 'configuration'
  | 'invalid-request'
  | 'aborted'
  | 'timeout'
  | 'unavailable'
  | 'invalid-response'
  | 'response-too-large'

/**
 * 错误消息刻意不包含完整 URL、query、请求体或原始 fetch cause，避免日志泄漏
 * OAuth token、凭证密钥等敏感值；调用方只按 kind/status 做稳定分类。
 */
export class ServerBackendFetchError extends Error {
  constructor(
    readonly kind: ServerBackendFetchErrorKind,
    message: string,
  ) {
    super(message)
    this.name = 'ServerBackendFetchError'
  }
}

export interface ServerBackendFetchInit extends Omit<RequestInit, 'signal'> {
  signal?: AbortSignal
  deadlineMs: number
  maxResponseBytes: number
}

async function incomingRequestHeaders(): Promise<{ headers: Pick<Headers, 'get'>, hasRequest: boolean, pathname: string }> {
  try {
    const current = await headers()
    return {
      headers: current,
      hasRequest: true,
      pathname: current.get('x-swapmid-pathname') ?? '/',
    }
  } catch {
    // 构建期或无请求作用域时没有客户端。不写来源头，API 判为未知。
    return { headers: { get: () => null }, hasRequest: false, pathname: '/' }
  }
}

function validatePositiveInteger(value: number, name: string, maximum: number): void {
  if (!Number.isSafeInteger(value) || value <= 0 || value > maximum) {
    throw new ServerBackendFetchError(
      'invalid-request',
      `Server backend ${name} is invalid`,
    )
  }
}

function buildBackendUrl(pathAndQuery: string): string {
  const queryIndex = pathAndQuery.indexOf('?')
  const rawPath = queryIndex >= 0
    ? pathAndQuery.slice(0, queryIndex)
    : pathAndQuery
  const hasInvalidPathSegment = rawPath.split('/').some((segment) => {
    if (!segment) return false

    // 在 URL 规范化前拒绝点段；否则 `%2e%2e` 会被 new URL 静默折叠，
    // 让调用方声明的 `/api/v1` 前缀逃逸。有限次重复解码同时防御双重编码。
    let decoded = segment
    for (let depth = 0; depth < 4; depth += 1) {
      if (decoded === '.' || decoded === '..') return true
      let next: string
      try {
        next = decodeURIComponent(decoded)
      } catch {
        return true
      }
      if (next === decoded) return false
      decoded = next
    }
    return decoded === '.' || decoded === '..'
  })

  if (
    !pathAndQuery.startsWith('/')
    || pathAndQuery.startsWith('//')
    || pathAndQuery.length > MAX_BACKEND_PATH_AND_QUERY_LENGTH
    || pathAndQuery.includes('\\')
    || pathAndQuery.includes('#')
    || /[\u0000-\u001f\u007f]/.test(pathAndQuery)
    || hasInvalidPathSegment
  ) {
    throw new ServerBackendFetchError(
      'invalid-request',
      'Server backend request path is invalid',
    )
  }

  let origin: string
  try {
    origin = resolveBackendApiBase()
  } catch {
    throw new ServerBackendFetchError(
      'configuration',
      'Server backend origin is invalid',
    )
  }

  const parsed = new URL(pathAndQuery, origin)
  if (
    parsed.origin !== origin
    || parsed.username
    || parsed.password
    || parsed.hash
  ) {
    throw new ServerBackendFetchError(
      'invalid-request',
      'Server backend request path is invalid',
    )
  }
  return parsed.href
}

function validateContentLength(response: Response, maxResponseBytes: number): void {
  // 生产 fetch 永远提供 Headers；旧单测的最小 Response-like 桩可能只实现 json/text。
  if (!response.headers || typeof response.headers.get !== 'function') return
  const contentLength = response.headers.get('content-length')
  if (contentLength == null) return
  if (!/^(0|[1-9]\d*)$/.test(contentLength)) {
    throw new ServerBackendFetchError(
      'invalid-response',
      'Server backend response length is invalid',
    )
  }

  const parsed = Number(contentLength)
  if (!Number.isSafeInteger(parsed)) {
    throw new ServerBackendFetchError(
      'invalid-response',
      'Server backend response length is invalid',
    )
  }
  if (parsed > maxResponseBytes) {
    throw new ServerBackendFetchError(
      'response-too-large',
      'Server backend response exceeded its configured limit',
    )
  }
}

async function cancelQuietly(
  reader: ReadableStreamDefaultReader<Uint8Array>,
  reason: unknown,
): Promise<void> {
  try {
    await reader.cancel(reason)
  } catch {
    // 原始失败已经由调用方分类；取消失败不能覆盖主错误。
  }
}

function cancelBodyQuietly(
  body: ReadableStream<Uint8Array>,
  reason: unknown,
): void {
  try {
    void body.cancel(reason).catch(() => undefined)
  } catch {
    // 非标准测试桩即使同步抛错，也不能覆盖长度校验的主错误。
  }
}

async function readBoundedResponseBody(
  response: Response,
  maxResponseBytes: number,
  signal: AbortSignal,
): Promise<Uint8Array | undefined> {
  try {
    validateContentLength(response, maxResponseBytes)
  } catch (error) {
    if (response.body) {
      // cancel 是尽力清理：底层流可以返回永不 settle 的 Promise，不能反向突破硬 deadline。
      cancelBodyQuietly(response.body, error)
    }
    throw error
  }
  if (response.body == null) return undefined

  const reader = response.body.getReader()
  const chunks: Uint8Array[] = []
  let total = 0
  const onAbort = () => {
    void cancelQuietly(reader, signal.reason)
  }

  if (signal.aborted) {
    void cancelQuietly(reader, signal.reason)
    throw signal.reason
  }
  signal.addEventListener('abort', onAbort, { once: true })

  try {
    while (true) {
      const { done, value } = await reader.read()
      if (signal.aborted) throw signal.reason
      if (done) break

      total += value.byteLength
      if (total > maxResponseBytes) {
        const error = new ServerBackendFetchError(
          'response-too-large',
          'Server backend response exceeded its configured limit',
        )
        void cancelQuietly(reader, error)
        throw error
      }
      chunks.push(value)
    }
  } finally {
    signal.removeEventListener('abort', onAbort)
  }

  const body = new Uint8Array(total)
  let offset = 0
  for (const chunk of chunks) {
    body.set(chunk, offset)
    offset += chunk.byteLength
  }
  return body
}

function rebuildBufferedResponse(
  response: Response,
  body: Uint8Array | undefined,
): Response {
  // 测试桩可能是最小 Response-like 对象；真实 fetch 的无正文响应也无需重建。
  if (body === undefined) return response

  const headers = new Headers(response.headers)
  // fetch 已完成解码，原始线上长度/编码不再描述重建后的内存正文。
  headers.delete('content-encoding')
  headers.delete('content-length')
  headers.delete('transfer-encoding')
  return new Response(body.buffer as ArrayBuffer, {
    status: response.status,
    statusText: response.statusText,
    headers,
  })
}

/**
 * 服务端到后端的单次有界请求。无任何自动重试（尤其 POST），调用方若需要业务级重试，
 * 必须在具备幂等键/幂等语义的更高层显式实现。
 */
export async function serverBackendFetch(
  pathAndQuery: string,
  init: ServerBackendFetchInit,
): Promise<Response> {
  const {
    deadlineMs,
    maxResponseBytes,
    signal: callerSignal,
    redirect,
    ...requestInit
  } = init

  validatePositiveInteger(deadlineMs, 'deadline', 10 * 60_000)
  validatePositiveInteger(
    maxResponseBytes,
    'response limit',
    MAX_CONFIGURABLE_RESPONSE_BYTES,
  )
  if (redirect != null && redirect !== 'manual') {
    throw new ServerBackendFetchError(
      'invalid-request',
      'Server backend redirects must not be followed automatically',
    )
  }
  if (callerSignal?.aborted) {
    throw new ServerBackendFetchError(
      'aborted',
      'Server backend request was cancelled',
    )
  }

  const url = buildBackendUrl(pathAndQuery)
  const incoming = await incomingRequestHeaders()
  const outboundHeaders = withTrustedClientIp(
    requestInit.headers,
    incoming.headers,
  )
  if (callerSignal?.aborted) {
    throw new ServerBackendFetchError(
      'aborted',
      'Server backend request was cancelled',
    )
  }
  const exchange = new AbortController()
  let timedOut = false
  const onCallerAbort = () => {
    if (!exchange.signal.aborted) exchange.abort(callerSignal?.reason)
  }
  callerSignal?.addEventListener('abort', onCallerAbort, { once: true })
  const timeout = setTimeout(() => {
    timedOut = true
    if (!exchange.signal.aborted) {
      exchange.abort(new ServerBackendFetchError(
        'timeout',
        'Server backend request timed out',
      ))
    }
  }, deadlineMs)

  try {
    const response = await fetch(url, {
      ...requestInit,
      headers: outboundHeaders,
      redirect: 'manual',
      signal: exchange.signal,
    })
    const body = await readBoundedResponseBody(
      response,
      maxResponseBytes,
      exchange.signal,
    )
    return rebuildBufferedResponse(response, body)
  } catch (error) {
    // 调用方可给 AbortController 任意 reason（甚至恰好也是本类错误）；必须按本次
    // exchange 的实际 abort reason 判定来源，不能让 reason 的运行时类型伪装分类。
    const abortedByCaller = callerSignal?.aborted
      && exchange.signal.aborted
      && Object.is(exchange.signal.reason, callerSignal.reason)
    if (abortedByCaller) {
      throw new ServerBackendFetchError(
        'aborted',
        'Server backend request was cancelled',
      )
    }
    if (timedOut) {
      throw new ServerBackendFetchError(
        'timeout',
        'Server backend request timed out',
      )
    }
    if (error instanceof ServerBackendFetchError) throw error
    throw new ServerBackendFetchError(
      'unavailable',
      'Server backend request failed',
    )
  } finally {
    clearTimeout(timeout)
    callerSignal?.removeEventListener('abort', onCallerAbort)
  }
}
