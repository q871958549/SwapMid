/**
 * 页面级「生成 / 同步」进行中状态。必须放模块单例，不能放组件 useState：
 * App Router 切页会卸载 client，useState 丢失，请求其实还在飞，回来按钮又变可点。
 */
const inflight = new Map<string, Promise<unknown>>()
const startedAt = new Map<string, number>()
const listeners = new Set<() => void>()

function emit() {
  for (const listener of listeners) listener()
}

export function isInFlight(key: string): boolean {
  return inflight.has(key)
}

/** 该 key 开始执行的时间戳；切页重挂后用来续算已等待秒数。 */
export function getInFlightStartedAt(key: string): number | undefined {
  return startedAt.get(key)
}

export function subscribeInFlight(onStoreChange: () => void): () => void {
  listeners.add(onStoreChange)
  return () => {
    listeners.delete(onStoreChange)
  }
}

export function runInFlight<T>(key: string, task: () => Promise<T>): Promise<T> {
  const existing = inflight.get(key)
  if (existing) {
    return existing as Promise<T>
  }

  let pending: Promise<T>
  try {
    pending = Promise.resolve(task())
  } catch (error) {
    pending = Promise.reject(error) as Promise<T>
  }

  const tracked = pending.finally(() => {
    if (inflight.get(key) === tracked) {
      inflight.delete(key)
      startedAt.delete(key)
    }
    emit()
  })

  inflight.set(key, tracked)
  startedAt.set(key, Date.now())
  emit()
  return tracked
}

export function resetInFlightForTests() {
  inflight.clear()
  startedAt.clear()
  emit()
}
