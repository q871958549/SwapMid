import { describe, expect, it } from 'vitest'
import {
  assertProductionWebLeaseSupervision,
  createRedisWebSingletonLease,
  RELEASE_WEB_SINGLETON_LEASE_SCRIPT,
  RENEW_WEB_SINGLETON_LEASE_SCRIPT,
  resolveWebSingletonLeaseConfiguration,
} from './web-singleton-lease-core.mjs'

class ExpiringFakeRedis {
  now = 0
  values = new Map<string, { owner: string; expiresAt: number }>()

  advance(ms: number) {
    this.now += ms
  }

  purge(key: string) {
    const current = this.values.get(key)
    if (current && current.expiresAt <= this.now) {
      this.values.delete(key)
    }
  }

  async set(
    key: string,
    owner: string,
    px: string,
    ttlMs: number,
    nx: string,
  ) {
    expect([px, nx]).toEqual(['PX', 'NX'])
    this.purge(key)
    if (this.values.has(key)) return null
    this.values.set(key, { owner, expiresAt: this.now + ttlMs })
    return 'OK'
  }

  async eval(
    script: string,
    keyCount: number,
    key: string,
    owner: string,
    ttlMs?: string,
  ) {
    expect(keyCount).toBe(1)
    this.purge(key)
    const current = this.values.get(key)
    if (!current || current.owner !== owner) return 0

    if (script === RENEW_WEB_SINGLETON_LEASE_SCRIPT) {
      current.expiresAt = this.now + Number(ttlMs)
      return 1
    }
    if (script === RELEASE_WEB_SINGLETON_LEASE_SCRIPT) {
      this.values.delete(key)
      return 1
    }
    throw new Error('unexpected script')
  }
}

describe('production web singleton Redis commands', () => {
  it('父进程崩溃不释放租约，由 TTL 到期后再允许接管', async () => {
    const redis = new ExpiringFakeRedis()
    const first = createRedisWebSingletonLease(redis, {
      key: 'swapmid:test:web:singleton',
      owner: 'owner-a',
      ttlMs: 15_000,
    })
    const successor = createRedisWebSingletonLease(redis, {
      key: 'swapmid:test:web:singleton',
      owner: 'owner-b',
      ttlMs: 15_000,
    })

    expect(await first.acquire()).toBe(true)
    expect(await successor.acquire()).toBe(false)
    redis.advance(14_999)
    expect(await successor.acquire()).toBe(false)
    redis.advance(1)
    expect(await successor.acquire()).toBe(true)
  })

  it('Lua 续租与删除都只允许当前 owner 操作', async () => {
    const redis = new ExpiringFakeRedis()
    const current = createRedisWebSingletonLease(redis, {
      key: 'swapmid:test:web:singleton',
      owner: 'owner-a',
      ttlMs: 15_000,
    })
    const stale = createRedisWebSingletonLease(redis, {
      key: 'swapmid:test:web:singleton',
      owner: 'owner-b',
      ttlMs: 15_000,
    })

    expect(await current.acquire()).toBe(true)
    expect(await stale.renew()).toBe(false)
    expect(await stale.release()).toBe(false)
    expect(await current.renew()).toBe(true)
    expect(await current.release()).toBe(true)
  })
})

describe('production web singleton configuration', () => {
  it('production instrumentation 只接受父租约进程注入的监督标记', () => {
    expect(() => assertProductionWebLeaseSupervision({
      NODE_ENV: 'production',
    })).toThrow('not supervised')
    expect(() => assertProductionWebLeaseSupervision({
      NODE_ENV: 'production',
      SWAPMID_WEB_LEASE_SUPERVISED: 'true',
    })).toThrow('not supervised')
    expect(() => assertProductionWebLeaseSupervision({
      NODE_ENV: 'production',
      SWAPMID_WEB_LEASE_SUPERVISED: '1',
    })).not.toThrow()
    expect(() => assertProductionWebLeaseSupervision({
      NODE_ENV: 'development',
    })).not.toThrow()
  })

  it('派生接管隔离、心跳与失租停机的安全窗口', () => {
    expect(resolveWebSingletonLeaseConfiguration({
      WEB_SINGLETON_LEASE_KEY: 'swapmid:production:web:singleton:v1',
      WEB_SINGLETON_LEASE_TTL_MS: '30000',
    })).toEqual({
      key: 'swapmid:production:web:singleton:v1',
      ttlMs: 30_000,
      renewIntervalMs: 10_000,
      shutdownGraceMs: 10_000,
      failureGraceMs: 1_500,
      forceKillWaitMs: 1_000,
      childWatchdogTimeoutMs: 5_000,
      heartbeatFreshnessMs: 19_500,
      heartbeatIntervalMs: 1_000,
      minimumTakeoverIsolationMs: 19_500,
      takeoverIsolationMs: 20_500,
      redisConnectTimeoutMs: 3_000,
      redisReadyTimeoutMs: 5_000,
      redisCommandTimeoutMs: 1_500,
      redisCloseTimeoutMs: 1_000,
      nextReadyAllowanceMs: 5_000,
      stopSchedulingMarginMs: 5_000,
      containerStopBudgetMs: 20_000,
      cleanStartBudgetMs: 33_500,
    })
  })

  it('接管隔离期只允许显式加长，禁止压低派生下限', () => {
    const extended = resolveWebSingletonLeaseConfiguration({
      WEB_SINGLETON_LEASE_KEY: 'swapmid:production:web:singleton:v1',
      WEB_SINGLETON_LEASE_TTL_MS: '30000',
      WEB_SINGLETON_TAKEOVER_ISOLATION_MS: '25000',
    })
    expect(extended.takeoverIsolationMs).toBe(25_000)
    expect(extended.cleanStartBudgetMs).toBe(38_000)

    expect(() => resolveWebSingletonLeaseConfiguration({
      WEB_SINGLETON_LEASE_KEY: 'swapmid:production:web:singleton:v1',
      WEB_SINGLETON_LEASE_TTL_MS: '30000',
      WEB_SINGLETON_TAKEOVER_ISOLATION_MS: '19500',
    })).toThrow('WEB_SINGLETON_TAKEOVER_ISOLATION_MS')
  })

  it.each([
    [{}, 'WEB_SINGLETON_LEASE_KEY'],
    [{
      WEB_SINGLETON_LEASE_KEY: 'swapmid:production web',
      WEB_SINGLETON_LEASE_TTL_MS: '30000',
    }, 'WEB_SINGLETON_LEASE_KEY'],
    [{
      WEB_SINGLETON_LEASE_KEY: 'swapmid:production:web',
      WEB_SINGLETON_LEASE_TTL_MS: '14999',
    }, 'WEB_SINGLETON_LEASE_TTL_MS'],
  ])('非法配置失败关闭', (environment, expected) => {
    expect(() => resolveWebSingletonLeaseConfiguration(environment)).toThrow(
      expected,
    )
  })
})
