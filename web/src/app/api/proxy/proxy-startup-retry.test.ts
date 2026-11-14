import { describe, expect, it } from 'vitest'
import {
  StartupUnavailableError,
  fetchWithStartupRetry,
  isStartupConnectFailure,
  resetStartupConnectCache,
  startupRetryDelayMs,
} from './proxy-startup-retry'

function refused(): Error {
  const cause = Object.assign(new Error('connect ECONNREFUSED'), { code: 'ECONNREFUSED' })
  return Object.assign(new TypeError('fetch failed'), { cause })
}

describe('proxy startup retry', () => {
  it('识别连接被拒和 DNS，不把连接重置当启动失败', () => {
    expect(isStartupConnectFailure(refused())).toBe(true)
    expect(isStartupConnectFailure(Object.assign(new Error('dns'), { code: 'ENOTFOUND' }))).toBe(true)
    expect(isStartupConnectFailure(Object.assign(new Error('reset'), { code: 'ECONNRESET' }))).toBe(false)
    expect(isStartupConnectFailure(new TypeError('connect failed'))).toBe(false)
    expect(startupRetryDelayMs(0)).toBe(500)
    expect(startupRetryDelayMs(99)).toBe(8_000)
  })

  it('幂等请求在预算内退避，耗尽后抛出启动不可用', async () => {
    let now = 0
    let calls = 0
    await expect(fetchWithStartupRetry('http://api/employees', { method: 'GET' }, {
      idempotent: true,
      budgetMs: 45_000,
      now: () => now,
      sleep: async (ms) => { now += ms },
      fetchImpl: async () => {
        calls += 1
        throw refused()
      },
    })).rejects.toBeInstanceOf(StartupUnavailableError)
    expect(calls).toBeGreaterThan(1)
    expect(now).toBeLessThanOrEqual(45_000)
    expect(now).toBeGreaterThanOrEqual(30_000)
  })

  it('写请求先等探针再发送一次，探针失败时不发出正文', async () => {
    resetStartupConnectCache()
    let now = 0
    let probes = 0
    let posts = 0
    const response = await fetchWithStartupRetry('http://api/employees', { method: 'POST' }, {
      idempotent: false,
      now: () => now,
      sleep: async (ms) => { now += ms },
      probe: async () => {
        probes += 1
        return probes >= 2
      },
      fetchImpl: async () => {
        posts += 1
        return new Response('created', { status: 201 })
      },
    })
    expect(response.status).toBe(201)
    expect(posts).toBe(1)
    expect(probes).toBe(2)

    resetStartupConnectCache()
    now = 0
    posts = 0
    await expect(fetchWithStartupRetry('http://api/employees', { method: 'POST' }, {
      idempotent: false,
      budgetMs: 45_000,
      now: () => now,
      sleep: async (ms) => { now += ms },
      probe: async () => false,
      fetchImpl: async () => {
        posts += 1
        return new Response(null, { status: 201 })
      },
    })).rejects.toBeInstanceOf(StartupUnavailableError)
    expect(posts).toBe(0)
  })

  it('写请求一旦发出就不再重放', async () => {
    resetStartupConnectCache()
    let posts = 0
    await expect(fetchWithStartupRetry('http://api/employees', { method: 'POST' }, {
      idempotent: false,
      now: () => 0,
      probe: async () => true,
      fetchImpl: async () => {
        posts += 1
        throw refused()
      },
    })).rejects.toBeInstanceOf(StartupUnavailableError)
    expect(posts).toBe(1)
  })

  it('非幂等请求不退避', async () => {
    let calls = 0
    let slept = 0
    await expect(fetchWithStartupRetry('http://api/employees', { method: 'POST' }, {
      idempotent: false,
      now: () => 0,
      sleep: async (ms) => { slept += ms },
      fetchImpl: async () => {
        calls += 1
        throw refused()
      },
    })).rejects.toBeInstanceOf(StartupUnavailableError)
    expect(calls).toBe(1)
    expect(slept).toBe(0)
  })

  it('连接恢复后返回上游响应', async () => {
    let calls = 0
    const response = await fetchWithStartupRetry('http://api/employees', { method: 'GET' }, {
      idempotent: true,
      now: () => 0,
      sleep: async () => undefined,
      fetchImpl: async () => {
        calls += 1
        if (calls === 1) throw refused()
        return new Response('ok', { status: 200 })
      },
    })
    expect(response.status).toBe(200)
    expect(calls).toBe(2)
  })
})
