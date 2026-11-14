'use client'

import { useEffect, useRef } from 'react'

export type SerializedPollingTaskResult = 'continue' | 'stop' | void

export interface SerializedPollingRetryOptions {
  initialMs: number
  maxMs: number
  multiplier?: number
  jitterRatio?: number
}

export interface SerializedPollingOptions {
  enabled: boolean
  key?: unknown
  runImmediately?: boolean
  initialDelayMs?: number
  intervalMs: number
  attemptTimeoutMs?: number
  retry?: SerializedPollingRetryOptions
  pauseWhenHidden?: boolean
  pauseWhenOffline?: boolean
  task: (context: { signal: AbortSignal; attempt: number }) => Promise<SerializedPollingTaskResult>
  onError?: (error: unknown, consecutiveFailures: number) => void
}

export interface SerializedPollingController {
  stop: () => void
}

const MAX_TIMER_MS = 2_147_483_647
const DEFAULT_RETRY_MULTIPLIER = 2
const DEFAULT_JITTER_RATIO = 0.2

class SerializedPollingTimeoutError extends Error {
  constructor(timeoutMs: number) {
    super(`Polling attempt timed out after ${timeoutMs}ms`)
    this.name = 'SerializedPollingTimeoutError'
  }
}

interface NormalizedOptions {
  enabled: boolean
  initialDelayMs: number
  intervalMs: number
  attemptTimeoutMs?: number
  retryInitialMs: number
  retryMaxMs: number
  retryMultiplier: number
  retryJitterRatio: number
  pauseWhenHidden: boolean
  pauseWhenOffline: boolean
  task: SerializedPollingOptions['task']
  onError?: SerializedPollingOptions['onError']
}

function assertDelay(name: string, value: number, allowZero: boolean): number {
  const minimum = allowZero ? 0 : 1
  if (!Number.isSafeInteger(value) || value < minimum || value > MAX_TIMER_MS) {
    throw new RangeError(`${name} must be a safe integer within ${minimum}..${MAX_TIMER_MS}`)
  }
  return value
}

function normalizeOptions(options: SerializedPollingOptions): NormalizedOptions {
  const intervalMs = assertDelay('intervalMs', options.intervalMs, false)
  const runImmediately = options.runImmediately ?? true
  const initialDelayMs = runImmediately
    ? 0
    : assertDelay('initialDelayMs', options.initialDelayMs ?? intervalMs, true)
  const attemptTimeoutMs = options.attemptTimeoutMs === undefined
    ? undefined
    : assertDelay('attemptTimeoutMs', options.attemptTimeoutMs, false)

  const defaultRetryMaxMs = Math.min(MAX_TIMER_MS, intervalMs * 8)
  const retryInitialMs = assertDelay(
    'retry.initialMs',
    options.retry?.initialMs ?? intervalMs,
    false,
  )
  const retryMaxMs = assertDelay(
    'retry.maxMs',
    options.retry?.maxMs ?? defaultRetryMaxMs,
    false,
  )
  if (retryMaxMs < retryInitialMs) {
    throw new RangeError('retry.maxMs must be greater than or equal to retry.initialMs')
  }

  const retryMultiplier = options.retry?.multiplier ?? DEFAULT_RETRY_MULTIPLIER
  if (!Number.isFinite(retryMultiplier) || retryMultiplier < 1) {
    throw new RangeError('retry.multiplier must be a finite number greater than or equal to 1')
  }

  const retryJitterRatio = options.retry?.jitterRatio ?? DEFAULT_JITTER_RATIO
  if (!Number.isFinite(retryJitterRatio) || retryJitterRatio < 0 || retryJitterRatio > 1) {
    throw new RangeError('retry.jitterRatio must be within 0..1')
  }

  return {
    enabled: options.enabled,
    initialDelayMs,
    intervalMs,
    attemptTimeoutMs,
    retryInitialMs,
    retryMaxMs,
    retryMultiplier,
    retryJitterRatio,
    pauseWhenHidden: options.pauseWhenHidden ?? true,
    pauseWhenOffline: options.pauseWhenOffline ?? true,
    task: options.task,
    onError: options.onError,
  }
}

function retryDelay(options: NormalizedOptions, consecutiveFailures: number): number {
  const base = Math.min(
    options.retryMaxMs,
    options.retryInitialMs * (options.retryMultiplier ** Math.max(0, consecutiveFailures - 1)),
  )
  const jitter = 1 + ((Math.random() * 2) - 1) * options.retryJitterRatio
  return Math.min(options.retryMaxMs, Math.max(0, Math.round(base * jitter)))
}

/**
 * 启动一个实例内单飞的轮询循环。每次任务 settle 后才安排下一次 setTimeout；stop 会
 * 清理 timer、事件监听并中止当前 attempt。React Hook 与单元测试共用这一状态机。
 */
export function startSerializedPolling(rawOptions: SerializedPollingOptions): SerializedPollingController {
  const options = normalizeOptions(rawOptions)
  let stopped = !options.enabled
  let paused = false
  let generation = 0
  let timer: ReturnType<typeof setTimeout> | undefined
  let activeAttempt: { generation: number; controller: AbortController } | undefined
  let consecutiveFailures = 0
  let listenersAttached = false

  const shouldPause = () => (
    (options.pauseWhenHidden && typeof document !== 'undefined' && document.hidden)
    || (options.pauseWhenOffline && typeof navigator !== 'undefined' && navigator.onLine === false)
  )

  const clearScheduledTimer = () => {
    if (timer === undefined) return
    clearTimeout(timer)
    timer = undefined
  }

  const invalidateCurrentWork = () => {
    generation += 1
    clearScheduledTimer()
    activeAttempt?.controller.abort()
    activeAttempt = undefined
  }

  const removeListeners = () => {
    if (!listenersAttached) return
    if (options.pauseWhenHidden && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', handleEnvironmentChange)
    }
    if (options.pauseWhenOffline && typeof window !== 'undefined') {
      window.removeEventListener('online', handleEnvironmentChange)
      window.removeEventListener('offline', handleEnvironmentChange)
    }
    listenersAttached = false
  }

  const stop = () => {
    if (stopped) return
    stopped = true
    invalidateCurrentWork()
    removeListeners()
  }

  const schedule = (delayMs: number) => {
    if (stopped || paused) return
    clearScheduledTimer()
    const scheduledGeneration = generation
    timer = setTimeout(() => {
      timer = undefined
      void runAttempt(scheduledGeneration)
    }, delayMs)
  }

  const finishWithError = (error: unknown) => {
    consecutiveFailures += 1
    try {
      options.onError?.(error, consecutiveFailures)
    } catch {
      // 观测回调不能破坏轮询状态机。
    }
    schedule(retryDelay(options, consecutiveFailures))
  }

  async function runAttempt(scheduledGeneration: number): Promise<void> {
    if (stopped || paused || scheduledGeneration !== generation || activeAttempt) return

    const controller = new AbortController()
    const attemptGeneration = generation
    const attempt = consecutiveFailures + 1
    activeAttempt = { generation: attemptGeneration, controller }

    let deadlineTimer: ReturnType<typeof setTimeout> | undefined
    const taskPromise = Promise.resolve().then(() => options.task({ signal: controller.signal, attempt }))
    let attemptPromise: Promise<SerializedPollingTaskResult> = taskPromise

    if (options.attemptTimeoutMs !== undefined) {
      const timeoutMs = options.attemptTimeoutMs
      const deadline = new Promise<never>((_, reject) => {
        deadlineTimer = setTimeout(() => {
          reject(new SerializedPollingTimeoutError(timeoutMs))
          controller.abort()
        }, timeoutMs)
      })
      attemptPromise = Promise.race([taskPromise, deadline])
    }

    try {
      const result = await attemptPromise
      if (deadlineTimer !== undefined) clearTimeout(deadlineTimer)
      if (stopped || paused || attemptGeneration !== generation) return
      activeAttempt = undefined

      if (result === 'stop') {
        stop()
        return
      }

      consecutiveFailures = 0
      schedule(options.intervalMs)
    } catch (error) {
      if (deadlineTimer !== undefined) clearTimeout(deadlineTimer)
      if (stopped || paused || attemptGeneration !== generation) return
      activeAttempt = undefined
      finishWithError(error)
    }
  }

  function handleEnvironmentChange(): void {
    if (stopped) return
    const nextPaused = shouldPause()
    if (nextPaused === paused) return
    paused = nextPaused
    invalidateCurrentWork()
    if (!paused) schedule(0)
  }

  if (!stopped) {
    paused = shouldPause()
    if (options.pauseWhenHidden && typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', handleEnvironmentChange)
      listenersAttached = true
    }
    if (options.pauseWhenOffline && typeof window !== 'undefined') {
      window.addEventListener('online', handleEnvironmentChange)
      window.addEventListener('offline', handleEnvironmentChange)
      listenersAttached = true
    }
    if (!paused) schedule(options.initialDelayMs)
  }

  return { stop }
}

/**
 * React 生命周期适配层。task/onError 存入 ref，因此仅更新回调不会重启轮询；enabled、key 或
 * 调度参数变化会清理旧 generation，并建立一个全新的循环。
 */
export function useSerializedPolling({
  enabled,
  key,
  runImmediately,
  initialDelayMs,
  intervalMs,
  attemptTimeoutMs,
  retry: retryOptions,
  pauseWhenHidden,
  pauseWhenOffline,
  task,
  onError,
}: SerializedPollingOptions): void {
  const taskRef = useRef(task)
  const onErrorRef = useRef(onError)
  const retryInitialMs = retryOptions?.initialMs
  const retryMaxMs = retryOptions?.maxMs
  const retryMultiplier = retryOptions?.multiplier
  const retryJitterRatio = retryOptions?.jitterRatio

  useEffect(() => {
    taskRef.current = task
    onErrorRef.current = onError
  }, [task, onError])

  useEffect(() => {
    const controller = startSerializedPolling({
      enabled,
      key,
      runImmediately,
      initialDelayMs,
      intervalMs,
      attemptTimeoutMs,
      retry: retryInitialMs === undefined || retryMaxMs === undefined
        ? undefined
        : {
            initialMs: retryInitialMs,
            maxMs: retryMaxMs,
            multiplier: retryMultiplier,
            jitterRatio: retryJitterRatio,
          },
      pauseWhenHidden,
      pauseWhenOffline,
      task: (context) => taskRef.current(context),
      onError: (error, consecutiveFailures) => onErrorRef.current?.(error, consecutiveFailures),
    })
    return controller.stop
  }, [
    enabled,
    key,
    runImmediately,
    initialDelayMs,
    intervalMs,
    attemptTimeoutMs,
    retryInitialMs,
    retryMaxMs,
    retryMultiplier,
    retryJitterRatio,
    pauseWhenHidden,
    pauseWhenOffline,
  ])
}
