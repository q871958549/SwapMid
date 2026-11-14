import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  startSerializedPolling,
  useSerializedPolling,
  type SerializedPollingTaskResult,
} from './use-serialized-polling'

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

async function advance(ms: number): Promise<void> {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
  })
}

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('startSerializedPolling', () => {
  it('慢请求期间不重叠，settle 后才开始计算下一轮间隔', async () => {
    const first = deferred<SerializedPollingTaskResult>()
    const second = deferred<SerializedPollingTaskResult>()
    let inFlight = 0
    let maxInFlight = 0
    const task = vi.fn(() => {
      inFlight += 1
      maxInFlight = Math.max(maxInFlight, inFlight)
      const current = task.mock.calls.length === 1 ? first : second
      return current.promise.finally(() => { inFlight -= 1 })
    })
    const controller = startSerializedPolling({ enabled: true, intervalMs: 100, task })

    await advance(0)
    expect(task).toHaveBeenCalledTimes(1)
    await advance(1_000)
    expect(task).toHaveBeenCalledTimes(1)

    first.resolve('continue')
    await advance(0)
    await advance(99)
    expect(task).toHaveBeenCalledTimes(1)
    await advance(1)
    expect(task).toHaveBeenCalledTimes(2)
    expect(maxInFlight).toBe(1)

    controller.stop()
    second.resolve('stop')
    await advance(0)
  })

  it('task 返回 stop 后终止，不再安排下一轮', async () => {
    const task = vi.fn(async () => 'stop' as const)
    const controller = startSerializedPolling({ enabled: true, intervalMs: 25, task })

    await advance(0)
    await advance(1_000)

    expect(task).toHaveBeenCalledTimes(1)
    controller.stop()
  })

  it('连续失败指数退避，成功后把失败次数和退避重置', async () => {
    const task = vi.fn()
      .mockRejectedValueOnce(new Error('first'))
      .mockRejectedValueOnce(new Error('second'))
      .mockResolvedValueOnce('continue')
      .mockRejectedValueOnce(new Error('after-success'))
      .mockResolvedValue('stop')
    const onError = vi.fn()
    const controller = startSerializedPolling({
      enabled: true,
      intervalMs: 50,
      retry: { initialMs: 100, maxMs: 1_000, multiplier: 2, jitterRatio: 0 },
      task,
      onError,
    })

    await advance(0)
    expect(task).toHaveBeenCalledTimes(1)
    await advance(99)
    expect(task).toHaveBeenCalledTimes(1)
    await advance(1)
    expect(task).toHaveBeenCalledTimes(2)
    expect(task.mock.calls[0]?.[0].attempt).toBe(1)
    expect(task.mock.calls[1]?.[0].attempt).toBe(2)

    await advance(199)
    expect(task).toHaveBeenCalledTimes(2)
    await advance(1)
    expect(task).toHaveBeenCalledTimes(3)
    expect(task.mock.calls[2]?.[0].attempt).toBe(3)

    await advance(50)
    expect(task).toHaveBeenCalledTimes(4)
    expect(task.mock.calls[3]?.[0].attempt).toBe(1)
    await advance(99)
    expect(task).toHaveBeenCalledTimes(4)
    await advance(1)
    expect(task).toHaveBeenCalledTimes(5)
    expect(onError).toHaveBeenCalledTimes(3)
    expect(onError.mock.calls.map((call) => call[1])).toEqual([1, 2, 1])

    controller.stop()
  })

  it('attempt deadline 会 abort 挂起任务、报告超时并按退避继续', async () => {
    const signals: AbortSignal[] = []
    const task = vi.fn(({ signal }: { signal: AbortSignal }) => {
      signals.push(signal)
      return new Promise<SerializedPollingTaskResult>(() => {})
    })
    const onError = vi.fn()
    const controller = startSerializedPolling({
      enabled: true,
      intervalMs: 100,
      attemptTimeoutMs: 50,
      retry: { initialMs: 80, maxMs: 80, jitterRatio: 0 },
      task,
      onError,
    })

    await advance(0)
    await advance(49)
    expect(onError).not.toHaveBeenCalled()
    await advance(1)
    expect(signals[0]?.aborted).toBe(true)
    expect(onError).toHaveBeenCalledTimes(1)
    expect((onError.mock.calls[0]?.[0] as Error).name).toBe('SerializedPollingTimeoutError')

    await advance(79)
    expect(task).toHaveBeenCalledTimes(1)
    await advance(1)
    expect(task).toHaveBeenCalledTimes(2)
    controller.stop()
  })

  it('hidden/offline 时暂停并 abort，两个条件都恢复后只立即续跑一次', async () => {
    let hidden = true
    let online = true
    vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden)
    vi.spyOn(navigator, 'onLine', 'get').mockImplementation(() => online)
    const firstAttempt = deferred<SerializedPollingTaskResult>()
    const resumedAttempt = deferred<SerializedPollingTaskResult>()
    const signals: AbortSignal[] = []
    const task = vi.fn(({ signal }: { signal: AbortSignal }) => {
      signals.push(signal)
      return signals.length === 1 ? firstAttempt.promise : resumedAttempt.promise
    })
    const controller = startSerializedPolling({ enabled: true, intervalMs: 100, task })

    await advance(1_000)
    expect(task).not.toHaveBeenCalled()

    hidden = false
    document.dispatchEvent(new Event('visibilitychange'))
    await advance(0)
    expect(task).toHaveBeenCalledTimes(1)
    expect(signals[0]?.aborted).toBe(false)

    online = false
    window.dispatchEvent(new Event('offline'))
    expect(signals[0]?.aborted).toBe(true)
    hidden = true
    document.dispatchEvent(new Event('visibilitychange'))
    await advance(1_000)
    expect(task).toHaveBeenCalledTimes(1)

    hidden = false
    document.dispatchEvent(new Event('visibilitychange'))
    await advance(0)
    expect(task).toHaveBeenCalledTimes(1)

    online = true
    window.dispatchEvent(new Event('online'))
    window.dispatchEvent(new Event('online'))
    await advance(0)
    expect(task).toHaveBeenCalledTimes(2)

    firstAttempt.resolve('continue')
    await advance(1_000)
    expect(task).toHaveBeenCalledTimes(2)
    controller.stop()
    resumedAttempt.resolve('stop')
    await advance(0)
  })

  it('cleanup 会 abort 在途任务，晚到成功或失败都不能再调度', async () => {
    const pending = deferred<SerializedPollingTaskResult>()
    let signal: AbortSignal | undefined
    const task = vi.fn((context: { signal: AbortSignal }) => {
      signal = context.signal
      return pending.promise
    })
    const onError = vi.fn()
    const controller = startSerializedPolling({
      enabled: true,
      intervalMs: 20,
      retry: { initialMs: 20, maxMs: 20, jitterRatio: 0 },
      task,
      onError,
    })

    await advance(0)
    controller.stop()
    expect(signal?.aborted).toBe(true)

    pending.reject(new Error('late failure'))
    await advance(0)
    await advance(1_000)
    expect(task).toHaveBeenCalledTimes(1)
    expect(onError).not.toHaveBeenCalled()
  })
})

describe('useSerializedPolling', () => {
  it('disable/key 变化会 abort 旧 generation，晚到结果不会重排', async () => {
    const oldAttempt = deferred<SerializedPollingTaskResult>()
    const disabledGenerationAttempt = deferred<SerializedPollingTaskResult>()
    const currentAttempt = deferred<SerializedPollingTaskResult>()
    const signals: AbortSignal[] = []
    const task = vi.fn(({ signal }: { signal: AbortSignal }) => {
      signals.push(signal)
      if (signals.length === 1) return oldAttempt.promise
      if (signals.length === 2) return disabledGenerationAttempt.promise
      return currentAttempt.promise
    })
    const { rerender, unmount } = renderHook(
      ({ enabled, pollKey }) => useSerializedPolling({
        enabled,
        key: pollKey,
        intervalMs: 100,
        task,
      }),
      { initialProps: { enabled: true, pollKey: 'old' } },
    )

    await advance(0)
    rerender({ enabled: false, pollKey: 'old' })
    expect(signals[0]?.aborted).toBe(true)
    oldAttempt.resolve('continue')
    await advance(1_000)
    expect(task).toHaveBeenCalledTimes(1)

    rerender({ enabled: true, pollKey: 'new' })
    await advance(0)
    expect(task).toHaveBeenCalledTimes(2)
    rerender({ enabled: true, pollKey: 'newer' })
    expect(signals[1]?.aborted).toBe(true)
    await advance(0)
    expect(task).toHaveBeenCalledTimes(3)

    disabledGenerationAttempt.resolve('continue')
    await advance(1_000)
    expect(task).toHaveBeenCalledTimes(3)
    unmount()
    expect(signals[2]?.aborted).toBe(true)
    currentAttempt.resolve('continue')
    await advance(1_000)
    expect(task).toHaveBeenCalledTimes(3)
  })

  it('仅更新 task/onError 回调不会重启循环，下一轮使用最新回调', async () => {
    const firstTask = vi.fn(async (): Promise<SerializedPollingTaskResult> => 'continue')
    const latestTask = vi.fn(async (): Promise<SerializedPollingTaskResult> => 'stop')
    const firstError = vi.fn()
    const latestError = vi.fn()
    const { rerender } = renderHook(
      ({ task, onError }) => useSerializedPolling({
        enabled: true,
        intervalMs: 100,
        task,
        onError,
      }),
      { initialProps: { task: firstTask, onError: firstError } },
    )

    await advance(0)
    expect(firstTask).toHaveBeenCalledTimes(1)
    rerender({ task: latestTask, onError: latestError })
    await advance(99)
    expect(latestTask).not.toHaveBeenCalled()
    await advance(1)
    expect(latestTask).toHaveBeenCalledTimes(1)
    expect(firstTask).toHaveBeenCalledTimes(1)
    expect(firstError).not.toHaveBeenCalled()
    expect(latestError).not.toHaveBeenCalled()
  })
})
