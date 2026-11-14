import { randomUUID } from 'node:crypto'
import { EventEmitter } from 'node:events'
import assert from 'node:assert/strict'
import test from 'node:test'
import { Redis } from 'ioredis'
import {
  createRedisWebSingletonLease,
  WebSingletonLeaseAlreadyHeldError,
} from '../src/lib/web-singleton-lease-core.mjs'
import {
  WebSingletonLeaseSupervisor,
} from '../src/lib/web-singleton-lease-supervisor.mjs'

class IpcFakeChild extends EventEmitter {
  connected = true

  send(_message, callback) {
    callback?.(null)
    return this.connected
  }

  kill() {
    queueMicrotask(() => {
      this.connected = false
      this.emit('close', 0, null)
    })
    return true
  }
}

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms))
}

function createSupervisor({ client, key, owner, onSpawn }) {
  return new WebSingletonLeaseSupervisor({
    lease: createRedisWebSingletonLease(client, {
      key,
      owner,
      ttlMs: 2_000,
    }),
    spawnChild: () => {
      onSpawn()
      return new IpcFakeChild()
    },
    renewIntervalMs: 50,
    takeoverIsolationMs: 300,
    heartbeatIntervalMs: 25,
    heartbeatFreshnessMs: 1_000,
    shutdownGraceMs: 100,
    failureGraceMs: 25,
    forceKillWaitMs: 25,
  })
}

test('真实 Redis 下两个独立 supervisor 互斥并隔离失主接管', {
  timeout: 10_000,
}, async () => {
  const redisUrl = process.env.REDIS_URL
  const keyPrefix = process.env.WEB_SINGLETON_LEASE_KEY
  assert.ok(redisUrl, 'CI lease integration requires REDIS_URL')
  assert.ok(keyPrefix, 'CI lease integration requires isolated lease key')

  const key = `${keyPrefix}:integration:${randomUUID()}`
  const clients = [
    new Redis(redisUrl, { lazyConnect: true, maxRetriesPerRequest: 0 }),
    new Redis(redisUrl, { lazyConnect: true, maxRetriesPerRequest: 0 }),
  ]
  for (const client of clients) client.on('error', () => {})

  let first
  let successor
  let firstSpawnCount = 0
  let successorSpawnCount = 0
  try {
    await Promise.all(clients.map(client => client.connect()))
    first = createSupervisor({
      client: clients[0],
      key,
      owner: randomUUID(),
      onSpawn: () => { firstSpawnCount += 1 },
    })
    await first.start()
    assert.equal(firstSpawnCount, 1)

    const denied = createSupervisor({
      client: clients[1],
      key,
      owner: randomUUID(),
      onSpawn: () => { successorSpawnCount += 1 },
    })
    await assert.rejects(
      denied.start(),
      WebSingletonLeaseAlreadyHeldError,
    )
    assert.equal(successorSpawnCount, 0)

    // 模拟管理员误删 key / Redis 故障切换丢失旧 key：新 owner 能 acquire，
    // 但必须先隔离；旧 supervisor 在下一轮 renew 发现失主并停掉 child。
    await clients[1].del(key)
    successor = createSupervisor({
      client: clients[1],
      key,
      owner: randomUUID(),
      onSpawn: () => { successorSpawnCount += 1 },
    })
    const successorStart = successor.start()
    await delay(100)
    assert.equal(successorSpawnCount, 0)

    const firstResult = await first.completion
    assert.equal(firstResult.exitCode, 1)
    await successorStart
    assert.equal(successorSpawnCount, 1)
  } finally {
    if (successor && successor.state !== 'stopped') {
      await successor.requestShutdown()
    } else if (first && first.state !== 'stopped') {
      await first.requestShutdown()
    }
    await clients[0]?.del(key).catch(() => {})
    await Promise.all(clients.map(async (client) => {
      await client.quit().catch(() => {})
      client.disconnect(false)
    }))
  }
})
