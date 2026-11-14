import { describe, expect, it } from 'vitest'
import { waitForApiReady as waitForApiReadyImport } from '../../scripts/wait-for-api-ready.mjs'

const waitForApiReady = waitForApiReadyImport as unknown as (options?: {
  env?: Record<string, string | undefined>
  fetchImpl?: (url: string, init?: RequestInit) => Promise<{
    status: number
    arrayBuffer: () => Promise<ArrayBuffer>
  }>
  sleepImpl?: (ms: number, signal?: AbortSignal) => Promise<void>
  now?: () => number
  log?: (...data: unknown[]) => void
  signal?: AbortSignal
}) => Promise<{ skipped: boolean; ready: boolean; aborted: boolean }>

function jsonResponse(status: number) {
  return {
    status,
    arrayBuffer: async () => new ArrayBuffer(0),
  }
}

describe('waitForApiReady', () => {
  it('未配置地址时跳过', async () => {
    const result = await waitForApiReady({
      env: {},
      log: () => undefined,
    })
    expect(result).toEqual({ skipped: true, ready: true, aborted: false })
  })

  it('200 后放行；404 改探 health-status', async () => {
    const seen: string[] = []
    const ready = await waitForApiReady({
      env: {
        SWAPMID_API_READY_URL: 'http://api:80/health/ready',
        SWAPMID_API_READY_FALLBACK_URL: 'http://api:80/health-status',
      },
      now: () => 0,
      sleepImpl: async () => undefined,
      log: () => undefined,
      fetchImpl: async (url: string) => {
        seen.push(url)
        return jsonResponse(200)
      },
    })
    expect(ready.ready).toBe(true)
    expect(seen).toEqual(['http://api:80/health/ready'])

    const statuses = [404, 200]
    const fallbackSeen: string[] = []
    const fallback = await waitForApiReady({
      env: {
        SWAPMID_API_READY_URL: 'http://api:80/health/ready',
        SWAPMID_API_READY_FALLBACK_URL: 'http://api:80/health-status',
      },
      now: () => 0,
      sleepImpl: async () => undefined,
      log: () => undefined,
      fetchImpl: async (url: string) => {
        fallbackSeen.push(url)
        return jsonResponse(statuses.shift() ?? 500)
      },
    })
    expect(fallback.ready).toBe(true)
    expect(fallbackSeen).toEqual([
      'http://api:80/health/ready',
      'http://api:80/health-status',
    ])
  })

  it('预算耗尽仍返回，并钳制过长预算', async () => {
    let clock = 0
    const result = await waitForApiReady({
      env: {
        SWAPMID_API_READY_URL: 'http://api:80/health/ready',
        SWAPMID_API_READY_BUDGET_MS: '999999999',
      },
      now: () => clock,
      sleepImpl: async (ms: number) => { clock += ms },
      log: () => undefined,
      fetchImpl: async () => {
        throw new Error('ECONNREFUSED')
      },
    })
    expect(result).toEqual({ skipped: false, ready: false, aborted: false })
    expect(clock).toBe(180_000)
  })

  it('取消等待时不启动后续流程', async () => {
    const signal = AbortSignal.abort()
    const result = await waitForApiReady({
      env: { SWAPMID_API_READY_URL: 'http://api:80/health/ready' },
      signal,
      now: () => 0,
      log: () => undefined,
      fetchImpl: async () => jsonResponse(200),
    })
    expect(result.aborted).toBe(true)
    expect(result.ready).toBe(false)
  })
})
