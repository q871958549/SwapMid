const DEFAULT_BUDGET_MS = 180_000
const MAX_BUDGET_MS = 180_000
const PROBE_TIMEOUT_MS = 5_000
const RETRY_INTERVAL_MS = 2_000

function budgetMs(raw) {
  const parsed = Number(raw)
  if (!Number.isFinite(parsed) || parsed <= 0) return DEFAULT_BUDGET_MS
  return Math.min(Math.floor(parsed), MAX_BUDGET_MS)
}

function sleep(ms, signal) {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(signal.reason ?? new Error('aborted'))
      return
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    const onAbort = () => {
      clearTimeout(timer)
      reject(signal.reason ?? new Error('aborted'))
    }
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

async function probe(fetchImpl, url, signal) {
  const timeout = AbortSignal.timeout(PROBE_TIMEOUT_MS)
  const combined = signal ? AbortSignal.any([signal, timeout]) : timeout
  const response = await fetchImpl(url, { signal: combined })
  await response.arrayBuffer().catch(() => undefined)
  return response.status
}

/**
 * 生产 web 在监听前等待 API。未配置地址时立刻返回，本地和测试不受影响。
 * `/health/ready` 为 404 时改探 `/health-status`，避免新 web 对着旧 API 永久不监听。
 * 预算耗尽仍返回，由代理把剩余失败显示成「系统更新中」，而不是让容器一直不监听。
 */
export async function waitForApiReady({
  env,
  fetchImpl = globalThis.fetch,
  sleepImpl = sleep,
  now = Date.now,
  log = console.log,
  signal,
} = {}) {
  const url = typeof env?.SWAPMID_API_READY_URL === 'string' ? env.SWAPMID_API_READY_URL.trim() : ''
  if (!url) return { skipped: true, ready: true, aborted: false }

  const fallbackUrl = typeof env?.SWAPMID_API_READY_FALLBACK_URL === 'string'
    ? env.SWAPMID_API_READY_FALLBACK_URL.trim()
    : ''
  const budget = budgetMs(env?.SWAPMID_API_READY_BUDGET_MS)
  const deadline = now() + budget
  log(`[web-startup] waiting for API ready at ${url} (budget ${budget}ms)`)
  let useFallback = false

  while (now() < deadline) {
    if (signal?.aborted) return { skipped: false, ready: false, aborted: true }
    const target = useFallback && fallbackUrl ? fallbackUrl : url
    let status = 0
    try {
      status = await probe(fetchImpl, target, signal)
    } catch {
      if (signal?.aborted) return { skipped: false, ready: false, aborted: true }
      status = 0
    }

    if (status === 200) {
      log(`[web-startup] API ready via ${target}`)
      return { skipped: false, ready: true, aborted: false }
    }
    if (!useFallback && status === 404 && fallbackUrl) {
      useFallback = true
      log('[web-startup] /health/ready is absent; falling back to /health-status')
      continue
    }

    const remaining = deadline - now()
    if (remaining <= 0) break
    try {
      await sleepImpl(Math.min(RETRY_INTERVAL_MS, remaining), signal)
    } catch {
      if (signal?.aborted) return { skipped: false, ready: false, aborted: true }
    }
  }

  log(`[web-startup] API ready probe exhausted after ${budget}ms; starting web anyway`)
  return { skipped: false, ready: false, aborted: false }
}
