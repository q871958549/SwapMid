import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { getInFlightStartedAt, isInFlight, resetInFlightForTests, runInFlight } from './inflight-action'
import { useInFlight } from './use-inflight-action'

afterEach(() => {
  resetInFlightForTests()
})

describe('runInFlight', () => {
  it('同一 key 复用进行中的 Promise，不重入任务', async () => {
    let starts = 0
    const deferred = Promise.withResolvers<string>()
    const first = runInFlight('k', () => {
      starts += 1
      return deferred.promise
    })
    const second = runInFlight('k', () => {
      starts += 1
      return Promise.resolve('other')
    })

    expect(isInFlight('k')).toBe(true)
    expect(starts).toBe(1)
    expect(second).toBe(first)

    deferred.resolve('ok')
    await expect(first).resolves.toBe('ok')
    await expect(second).resolves.toBe('ok')
    expect(isInFlight('k')).toBe(false)
  })

  it('结束后允许再次执行', async () => {
    await runInFlight('k', async () => 1)
    const again = await runInFlight('k', async () => 2)
    expect(again).toBe(2)
  })

  it('开始时间跨卸载保留，结束后清除', async () => {
    const deferred = Promise.withResolvers<void>()
    const pending = runInFlight('k', () => deferred.promise)
    const started = getInFlightStartedAt('k')
    expect(started).toBeTypeOf('number')

    const first = renderHook(() => useInFlight('k'))
    first.unmount()
    expect(getInFlightStartedAt('k')).toBe(started)

    deferred.resolve()
    await pending
    expect(getInFlightStartedAt('k')).toBeUndefined()
  })

  it('卸载重挂后仍能看到进行中的动作', async () => {
    const deferred = Promise.withResolvers<void>()
    const pending = runInFlight('k', () => deferred.promise)
    const first = renderHook(() => useInFlight('k'))
    expect(first.result.current).toBe(true)
    first.unmount()

    const second = renderHook(() => useInFlight('k'))
    expect(second.result.current).toBe(true)

    deferred.resolve()
    await pending
    await waitFor(() => expect(second.result.current).toBe(false))
    second.unmount()
  })
})
