export { resolveBackendApiBase } from '@/lib/backend-origin'

/**
 * Catch-all 参数通常已由 Next 解码。代理重新逐段编码，并拒绝任何可改变路径层级的输入。
 */
export function encodeBackendPathSegments(segments: readonly string[]): string | undefined {
  if (segments.length === 0) return undefined

  const encoded: string[] = []
  for (const segment of segments) {
    if (
      !segment
      || segment.length > 1024
      || segment === '.'
      || segment === '..'
      || segment.includes('/')
      || segment.includes('\\')
      || /[\u0000-\u001f\u007f]/.test(segment)
    ) {
      return undefined
    }
    encoded.push(encodeURIComponent(segment))
  }
  return encoded.join('/')
}

export type ContentLengthValidation = 'ok' | 'invalid' | 'too-large'

export function validateContentLength(value: string | null, maxBytes: number): ContentLengthValidation {
  if (value == null) return 'ok'
  if (!/^(0|[1-9]\d*)$/.test(value)) return 'invalid'

  const parsed = Number(value)
  if (!Number.isSafeInteger(parsed)) return 'invalid'
  return parsed > maxBytes ? 'too-large' : 'ok'
}

export class ForwardingPayloadTooLargeError extends Error {
  constructor() {
    super('forwarded request body exceeds configured limit')
    this.name = 'ForwardingPayloadTooLargeError'
  }
}

export class ForwardingRequestIdleTimeoutError extends Error {
  constructor() {
    super('forwarded request body became idle')
    this.name = 'ForwardingRequestIdleTimeoutError'
  }
}

export class ForwardingResponseIdleTimeoutError extends Error {
  constructor() {
    super('forwarded response body became idle')
    this.name = 'ForwardingResponseIdleTimeoutError'
  }
}

export interface RequestBodyLimitOptions {
  idleTimeoutMs?: number
  signal?: AbortSignal
  gracefulStopSignal?: AbortSignal
  onComplete?: () => void
  onFinalize?: () => void
}

/**
 * 计数包装请求流：Content-Length 缺失或伪造偏小时仍按真实字节限长。
 * 可选 idle deadline 每收到一块便续期，避免慢速上传永久占用连接。
 * 上游已返回最终响应时使用 gracefulStopSignal 正常结束 wrapper，同时取消客户端源；
 * 客户端断连、deadline、idle 与超限仍走 error/abort，二者不可混用。
 */
export function limitRequestBody(
  source: ReadableStream<Uint8Array>,
  maxBytes: number,
  abortController: AbortController,
  options: RequestBodyLimitOptions = {},
): ReadableStream<Uint8Array> {
  const reader = source.getReader()
  let total = 0
  let idleTimer: ReturnType<typeof setTimeout> | undefined
  let removeAbortListener: (() => void) | undefined
  let removeGracefulStopListener: (() => void) | undefined
  let finished = false

  function clearIdleTimer() {
    if (idleTimer) clearTimeout(idleTimer)
    idleTimer = undefined
  }

  function cleanup(): boolean {
    if (finished) return false
    finished = true
    clearIdleTimer()
    removeAbortListener?.()
    removeAbortListener = undefined
    removeGracefulStopListener?.()
    removeGracefulStopListener = undefined
    options.onFinalize?.()
    return true
  }

  function complete() {
    if (cleanup()) options.onComplete?.()
  }

  function armIdleTimer(controller: ReadableStreamDefaultController<Uint8Array>) {
    clearIdleTimer()
    if (!options.idleTimeoutMs) return
    idleTimer = setTimeout(() => {
      const error = new ForwardingRequestIdleTimeoutError()
      if (!cleanup()) return
      abortController.abort(error)
      void reader.cancel(error).catch(() => undefined)
      controller.error(error)
    }, options.idleTimeoutMs)
  }

  return new ReadableStream<Uint8Array>({
    start(controller) {
      const onAbort = () => {
        const reason = options.signal?.reason
          ?? new DOMException('forwarded request aborted', 'AbortError')
        if (!cleanup()) return
        void reader.cancel(reason).catch(() => undefined)
        controller.error(reason)
      }
      if (options.signal?.aborted) {
        onAbort()
        return
      }
      options.signal?.addEventListener('abort', onAbort, { once: true })
      removeAbortListener = options.signal
        ? () => options.signal?.removeEventListener('abort', onAbort)
        : undefined

      const onGracefulStop = () => {
        const reason = options.gracefulStopSignal?.reason
          ?? new DOMException('forwarded request no longer needed', 'AbortError')
        if (!cleanup()) return
        // 上游已给出最终响应时，把 fetch 请求体正常收束为 EOF；controller.error()
        // 会让 Node/Undici 连已经取得的响应正文一起 AbortError。
        void reader.cancel(reason).catch(() => undefined)
        controller.close()
      }
      if (options.gracefulStopSignal?.aborted) {
        onGracefulStop()
        return
      }
      options.gracefulStopSignal?.addEventListener('abort', onGracefulStop, { once: true })
      removeGracefulStopListener = options.gracefulStopSignal
        ? () => options.gracefulStopSignal?.removeEventListener('abort', onGracefulStop)
        : undefined
      armIdleTimer(controller)
    },
    async pull(controller) {
      try {
        const { done, value } = await reader.read()
        if (finished) return
        if (done) {
          controller.close()
          complete()
          return
        }

        total += value.byteLength
        if (total > maxBytes) {
          const error = new ForwardingPayloadTooLargeError()
          if (!cleanup()) return
          abortController.abort(error)
          void reader.cancel(error).catch(() => undefined)
          controller.error(error)
          return
        }

        controller.enqueue(value)
        armIdleTimer(controller)
      } catch (error) {
        if (cleanup()) controller.error(error)
      }
    },
    cancel(reason) {
      if (!cleanup()) return
      return reader.cancel(reason)
    },
  })
}

export interface ForwardedResponseStreamOptions {
  idleTimeoutMs: number
  signal: AbortSignal
  upstreamAbortController: AbortController
  onFinalize?: () => void
}

type ForwardedResponseStreamState =
  | 'open'
  | 'reading'
  | 'completed'
  | 'failed-or-cancelled'

/**
 * 逐块回传上游响应，并把下游取消、交换 deadline 和响应空闲传播到上游。
 *
 * cleanup 必须先于 AbortController.abort：abort 事件同步派发，先落终态才能保证
 * idle / client abort / reader rejection 的竞态只 finalize 一次。
 */
export function streamForwardedResponseBody(
  source: ReadableStream<Uint8Array>,
  options: ForwardedResponseStreamOptions,
): ReadableStream<Uint8Array> {
  const reader = source.getReader()
  let idleTimer: ReturnType<typeof setTimeout> | undefined
  let removeAbortListener: (() => void) | undefined
  let state: ForwardedResponseStreamState = 'open'

  function clearIdleTimer() {
    if (idleTimer) clearTimeout(idleTimer)
    idleTimer = undefined
  }

  function isSettled(): boolean {
    return state === 'completed' || state === 'failed-or-cancelled'
  }

  function settle(nextState: Extract<
    ForwardedResponseStreamState,
    'completed' | 'failed-or-cancelled'
  >): boolean {
    if (state === 'completed' || state === 'failed-or-cancelled') return false
    state = nextState
    clearIdleTimer()
    removeAbortListener?.()
    removeAbortListener = undefined
    options.onFinalize?.()
    return true
  }

  function fail(
    controller: ReadableStreamDefaultController<Uint8Array>,
    reason: unknown,
    abortUpstream: boolean,
  ) {
    if (!settle('failed-or-cancelled')) return
    if (abortUpstream && !options.upstreamAbortController.signal.aborted) {
      options.upstreamAbortController.abort(reason)
    }
    void reader.cancel(reason).catch(() => undefined)
    controller.error(reason)
  }

  function armIdleTimer(controller: ReadableStreamDefaultController<Uint8Array>) {
    clearIdleTimer()
    if (options.idleTimeoutMs <= 0) return
    idleTimer = setTimeout(() => {
      fail(controller, new ForwardingResponseIdleTimeoutError(), true)
    }, options.idleTimeoutMs)
  }

  return new ReadableStream<Uint8Array>({
    start(controller) {
      const onAbort = () => {
        fail(
          controller,
          options.signal.reason
            ?? new DOMException('forwarded response aborted', 'AbortError'),
          false,
        )
      }
      if (options.signal.aborted) {
        onAbort()
        return
      }
      options.signal.addEventListener('abort', onAbort, { once: true })
      removeAbortListener = () => options.signal.removeEventListener('abort', onAbort)
      armIdleTimer(controller)
    },
    async pull(controller) {
      if (isSettled()) return
      state = 'reading'
      try {
        const { done, value } = await reader.read()
        if (isSettled()) return
        if (done) {
          if (settle('completed')) controller.close()
          return
        }
        controller.enqueue(value)
        state = 'open'
        armIdleTimer(controller)
      } catch (error) {
        fail(controller, error, false)
      }
    },
    cancel(reason) {
      if (!settle('failed-or-cancelled')) return
      if (!options.upstreamAbortController.signal.aborted) {
        options.upstreamAbortController.abort(reason)
      }
      return reader.cancel(reason)
    },
  })
}

export function selectForwardedResponseHeaders(
  source: Headers,
  allowedNames: readonly string[],
): Headers {
  const result = new Headers()
  for (const name of allowedNames) {
    const value = source.get(name)
    if (value) result.set(name, value)
  }
  return result
}
