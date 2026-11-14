// Turbopack 16.2 会缓存失败的 chunk Promise。对本次 import 新建的 script 在
// 原 onerror 拒绝缓存前重试；耗尽后拒绝本次操作，保留运行时 Promise 待下一次操作恢复。
type Loader = () => Promise<unknown>
type Scope = { loader: Loader; urls: Set<string>; failed: Map<string, HTMLScriptElement>; reject: (error: Error) => void; finished: boolean }
const scopes = new Set<Scope>()
const failures = new WeakMap<Loader, Map<string, HTMLScriptElement>>()
const originalScripts = new WeakMap<HTMLScriptElement, HTMLScriptElement>()
const attempts = new Map<string, number>()

function chunkUrl(script: HTMLScriptElement): string | null {
  try {
    const url = new URL(script.src, window.location.href)
    return url.origin === window.location.origin
      && /^\/_next\/static\/chunks\/[\w.-]+\.js$/.test(url.pathname) ? url.href : null
  } catch {
    return null
  }
}

function replaceScript(script: HTMLScriptElement) {
  // cloneNode 会继承已执行 script 的 already-started 标记，须新建节点。
  const retry = document.createElement('script')
  for (const attribute of script.attributes) retry.setAttribute(attribute.name, attribute.value)
  retry.nonce = script.nonce
  const original = originalScripts.get(script) ?? script
  originalScripts.set(retry, original)
  retry.addEventListener('load', () => original.dispatchEvent(new Event('load')))
  // 保留原始 src（包括相对形式/查询串），让 chunk 注册兑现原 URL 的 Promise。
  if (script.parentNode) script.parentNode.replaceChild(retry, script)
  else document.head.appendChild(retry)
}

function retryChunk(event: Event) {
  const script = event.target
  if (!(script instanceof HTMLScriptElement)) return
  const url = chunkUrl(script)
  if (!url) return
  const affected = [...scopes].filter(scope => scope.urls.has(url))
  if (affected.length === 0) return
  event.stopImmediatePropagation()
  if ((attempts.get(url) ?? 0) === 0) {
    attempts.set(url, 1)
    replaceScript(script)
    return
  }

  const error = new Error(`Failed to load chunk ${url}`)
  error.name = 'ChunkLoadError'
  // 不调用原 onerror：它会永久污染运行时缓存。由操作边界返回最终失败，
  // 下一次相同 loader 调用重新插入 script，网络恢复后可继续兑现原 Promise。
  for (const scope of affected) {
    scope.failed.set(url, script)
    scope.reject(error)
  }
}

/** loader 必须是稳定函数；每次操作最多重试一次，不刷新、不持久化页面数据。 */
export async function loadWithChunkRetry<T>(load: () => Promise<T>): Promise<T> {
  if (typeof window === 'undefined') return load()
  let failed = failures.get(load)
  if (!failed) { failed = new Map(); failures.set(load, failed) }
  let reject!: (error: Error) => void
  const failure = new Promise<never>((_resolve, no) => { reject = no })
  const previousScopes = [...scopes].filter(scope => scope.loader === load)
  const urls = new Set(previousScopes.flatMap(scope => [...scope.urls]))
  const scope: Scope = { loader: load, urls, failed, reject, finished: false }
  if (scopes.size === 0) window.addEventListener('error', retryChunk, true)
  scopes.add(scope)
  // 新操作接管上次尚未兑现的资源；每个 loader 最多保留一个已结束的保护作用域。
  for (const previous of previousScopes) if (previous.finished) scopes.delete(previous)
  const release = () => {
    scopes.delete(scope)
    if (scopes.size === 0) {
      window.removeEventListener('error', retryChunk, true)
      attempts.clear()
    }
  }
  let loading: Promise<T> | undefined
  try {
    const previous = new Set(document.querySelectorAll('script[src]'))
    loading = load()
    // 仅接管本次 import 同步创建的资源，其他导航/预取的 script 错误照常传播。
    for (const script of document.querySelectorAll<HTMLScriptElement>('script[src]')) {
      const url = chunkUrl(script)
      if (!previous.has(script) && url) scope.urls.add(url)
    }
    for (const [url, script] of failed) {
      failed.delete(url)
      attempts.delete(url)
      scope.urls.add(url)
      replaceScript(script)
    }
    // 一个块先失败时，其他块仍可能稍后失败。保护到原 import 结算，避免其缓存再被污染。
    void loading.then(release, release)
    return await Promise.race([loading, failure])
  } finally {
    scope.finished = true
    if (scopes.has(scope)) {
      for (const previous of scopes) {
        if (previous !== scope && previous.loader === load && previous.finished) {
          for (const url of previous.urls) scope.urls.add(url)
          scopes.delete(previous)
        }
      }
    }
    if (!loading) release()
  }
}
