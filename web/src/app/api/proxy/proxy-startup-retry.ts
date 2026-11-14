/** 发布窗口内静默等待的上限。页面上的加载状态继续转，不先弹提示。 */
export const PROXY_STARTUP_RETRY_BUDGET_MS = 45_000
const PROBE_FRESH_MS = 5_000

const RETRY_DELAYS_MS = [500, 1_000, 2_000, 4_000, 8_000] as const

const STARTUP_CONNECT_CODES = new Set([
  'ECONNREFUSED',
  'ENOTFOUND',
  'EAI_AGAIN',
  'EAI_FAIL',
  'ENETUNREACH',
  'EHOSTUNREACH',
])

export class StartupUnavailableError extends Error {
  constructor() {
    super('API startup connect failure')
    this.name = 'StartupUnavailableError'
  }
}

let probedReadyAt = 0

export function resetStartupConnectCache() {
  probedReadyAt = 0
}

function probeIsFresh(now: number): boolean {
  return probedReadyAt > 0 && now - probedReadyAt < PROBE_FRESH_MS
}

export function startupRetryDelayMs(attemptIndex: number): number {
  return RETRY_DELAYS_MS[Math.min(Math.max(attemptIndex, 0), RETRY_DELAYS_MS.length - 1)]
}

function collectErrorCodes(error: unknown, depth = 0): string[] {
  if (!error || typeof error !== 'object' || depth > 6) return []
  const record = error as { code?: unknown; cause?: unknown; errors?: unknown }
  const codes: string[] = []
  if (typeof record.code === 'string') codes.push(record.code)
  if (Array.isArray(record.errors)) {
    for (const item of record.errors) codes.push(...collectErrorCodes(item, depth + 1))
  }
  if ('cause' in record) codes.push(...collectErrorCodes(record.cause, depth + 1))
  return codes
}

/** 只认连接被拒和 DNS 失败。连接重置说明请求可能已经送出，不能再发一次。 */
export function isStartupConnectFailure(error: unknown): boolean {
  return collectErrorCodes(error).some(code => STARTUP_CONNECT_CODES.has(code))
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) {
      reject(signal.reason)
      return
    }
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    const onAbort = () => {
      clearTimeout(timer)
      reject(signal.reason)
    }
    signal.addEventListener('abort', onAbort, { once: true })
  })
}

async function waitUntilOpen(
  probe: (signal: AbortSignal) => Promise<boolean>,
  signal: AbortSignal,
  budgetMs: number,
  now: () => number,
  sleep: (ms: number, signal: AbortSignal) => Promise<void>,
): Promise<boolean> {
  const started = now()
  let attempt = 0
  while (true) {
    if (signal.aborted) throw signal.reason
    let open = false
    try {
      open = await probe(signal)
    } catch (error) {
      if (signal.aborted || !isStartupConnectFailure(error)) throw error
    }
    if (open) return true
    const delayMs = startupRetryDelayMs(attempt)
    if (now() - started + delayMs > budgetMs) return false
    attempt += 1
    await sleep(delayMs, signal)
  }
}

/**
 * 幂等请求在连接被拒或 DNS 失败时退避重试。
 * 写请求先等探针，再把正文发送恰好一次。连接没建立不算重放；
 * 一旦这次发送本身因连接失败结束，不再发第二次。
 */
export async function fetchWithStartupRetry(
  url: string,
  init: RequestInit,
  options: {
    idempotent: boolean
    budgetMs?: number
    now?: () => number
    sleep?: (ms: number, signal: AbortSignal) => Promise<void>
    fetchImpl?: typeof fetch
    probe?: (signal: AbortSignal) => Promise<boolean>
  },
): Promise<Response> {
  const budgetMs = options.budgetMs ?? PROXY_STARTUP_RETRY_BUDGET_MS
  const now = options.now ?? Date.now
  const fetchImpl = options.fetchImpl ?? fetch
  const sleep = options.sleep ?? delay
  const signal = init.signal ?? new AbortController().signal

  if (!options.idempotent) {
    if (options.probe && !probeIsFresh(now())) {
      const open = await waitUntilOpen(options.probe, signal, budgetMs, now, sleep)
      if (!open) throw new StartupUnavailableError()
      probedReadyAt = now()
    }
    try {
      return await fetchImpl(url, init)
    } catch (error) {
      if (signal.aborted || !isStartupConnectFailure(error)) throw error
      probedReadyAt = 0
      throw new StartupUnavailableError()
    }
  }

  const started = now()
  let attempt = 0
  while (true) {
    try {
      return await fetchImpl(url, init)
    } catch (error) {
      if (signal.aborted || !isStartupConnectFailure(error)) throw error
      const delayMs = startupRetryDelayMs(attempt)
      if (now() - started + delayMs > budgetMs) throw new StartupUnavailableError()
      attempt += 1
    }
    await sleep(startupRetryDelayMs(attempt - 1), signal)
  }
}
