import assert from 'node:assert/strict'
import { spawn, spawnSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import { Redis } from 'ioredis'
import { startOAuthServer } from './session-refresh-oauth-fixture.mjs'

const root = dirname(dirname(fileURLToPath(import.meta.url)))
const workerPath = join(root, 'scripts', 'session-refresh-redis-worker.ts')
const sid = '11111111-1111-4111-8111-111111111111'
const sessionKey = `swapmid:sess:${sid}`
const leaseKey = `${sessionKey}:refresh:v1`
const fenceKey = `${sessionKey}:refresh:fence:v1`
// Docker Official Image manifest；升级时必须用 `docker buildx imagetools inspect` 同时核验 tag+digest。
const image =
  'redis:8.8.0-alpine@sha256:9d317178eceac8454a2284a9e6df2466b93c745529947f0cd42a0fa9609d7005'
const container = process.env.SESSION_REFRESH_CONTAINER_NAME
  ?? `swapmid-session-refresh-it-${randomUUID()}`
assert.match(
  container,
  /^swapmid-session-refresh-it-[0-9a-f-]+$/,
  '临时 Redis 容器名必须位于测试专属命名空间',
)
const activeWorkers = new Set()
let containerStarted = false
let cleanupStarted = false

function settleGate(name) {
  return `${sessionKey}:integration-gate:${name}`
}

const baseSession = {
  userId: 'user-admin-id',
  userName: 'admin',
  name: '管理员',
  grantedPolicies: { Foo: true },
  identityVersion: 'identity-v1',
  accessToken: 'access-old',
  refreshToken: 'refresh-old',
  accessTokenExpiresAt: 0,
}

function updatedSession(label) {
  return {
    ...baseSession,
    accessToken: `access-${label}`,
    refreshToken: `refresh-${label}`,
    accessTokenExpiresAt: Date.now() + 3_600_000,
  }
}

function docker(args, allowFailure = false, timeout) {
  const result = spawnSync('docker', args, {
    cwd: root,
    encoding: 'utf8',
    windowsHide: true,
    timeout,
  })
  if (!allowFailure && result.status !== 0) {
    throw new Error(`docker ${args[0]} failed: ${result.stderr || result.stdout}`)
  }
  return (result.stdout ?? '').trim()
}

async function waitForRedisReady() {
  const deadline = Date.now() + 20_000
  while (Date.now() < deadline) {
    if (
      docker(
        ['exec', container, 'redis-cli', '--raw', 'ping'],
        true,
        5_000,
      ) === 'PONG'
    ) {
      return
    }
    await new Promise(resolve => setTimeout(resolve, 100))
  }

  const state = docker(
    ['inspect', '--format', '{{json .State}}', container],
    true,
    5_000,
  )
  const logs = docker(['logs', '--tail', '100', container], true, 5_000)
  throw new Error(
    `Redis container did not become ready within 20s; state=${state || 'unavailable'}; logs=${logs || 'unavailable'}`,
  )
}

function cleanupResources() {
  if (cleanupStarted) return
  cleanupStarted = true
  for (const worker of activeWorkers) {
    try {
      worker.kill('SIGTERM')
      if (worker.exitCode === null) worker.kill('SIGKILL')
    } catch {
      // 进程可能恰好已经退出。
    }
  }
  activeWorkers.clear()
  if (containerStarted) {
    docker(['rm', '-f', container], true, 10_000)
    containerStarted = false
  }
}

for (const [signal, exitCode] of [
  ['SIGINT', 130],
  ['SIGTERM', 143],
  ['SIGHUP', 129],
]) {
  process.once(signal, () => {
    cleanupResources()
    process.exit(exitCode)
  })
}

function startWorker(redisUrl, extraEnvironment) {
  const child = spawn(
    process.execPath,
    [
      '--conditions=react-server',
      '--import',
      'tsx',
      workerPath,
    ],
    {
      cwd: root,
      windowsHide: true,
      env: {
        ...process.env,
        NODE_ENV: 'test',
        SESSION_STORE_MODE: 'redis',
        REDIS_URL: redisUrl,
        SESSION_ID: sid,
        SESSION_JSON: JSON.stringify(baseSession),
        ...extraEnvironment,
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    },
  )
  activeWorkers.add(child)

  const events = []
  const waiters = []
  let stdoutBuffer = ''
  let stderr = ''
  child.stdout.setEncoding('utf8')
  child.stderr.setEncoding('utf8')
  child.stdout.on('data', (chunk) => {
    stdoutBuffer += chunk
    const lines = stdoutBuffer.split(/\r?\n/)
    stdoutBuffer = lines.pop() ?? ''
    for (const line of lines) {
      if (!line.startsWith('{')) continue
      const event = JSON.parse(line)
      events.push(event)
      for (const waiter of [...waiters]) {
        if (waiter.event === event.event) {
          waiters.splice(waiters.indexOf(waiter), 1)
          waiter.resolve(event.value)
        }
      }
    }
  })
  child.stderr.on('data', chunk => {
    stderr += chunk
  })

  const done = new Promise((resolve, reject) => {
    child.on('error', reject)
    child.on('exit', (code) => {
      activeWorkers.delete(child)
      if (code !== 0) {
        reject(new Error(`worker exited ${code}: ${stderr}`))
        return
      }
      resolve(events)
    })
  })

  function waitFor(event) {
    const existing = events.find(item => item.event === event)
    if (existing) return Promise.resolve(existing.value)
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        reject(new Error(`worker did not emit ${event}; stderr=${stderr}`))
      }, 10_000)
      waiters.push({
        event,
        resolve(value) {
          clearTimeout(timer)
          resolve(value)
        },
      })
    })
  }

  return { done, waitFor }
}

function resultOf(events, name = 'result') {
  const event = events.find(item => item.event === name)
  assert.ok(event, `missing worker event ${name}`)
  return event.value
}

async function seed(redis, ttlMs = 60_000) {
  await redis.flushdb()
  await redis.set(sessionKey, JSON.stringify(baseSession), 'PX', ttlMs)
}

async function run() {
  docker([
    'run',
    '-d',
    '--rm',
    '-p',
    '127.0.0.1::6379',
    '--name',
    container,
    image,
  ])
  containerStarted = true
  // docker run -d 只证明容器进程已创建；高负载 Runner 上端口代理可能早于 Redis 接受命令。
  // 必须先在容器内确认 PONG，才允许经宿主映射端口创建 ioredis 客户端。
  await waitForRedisReady()
  if (process.argv.includes('--cleanup-signal-probe')) {
    process.stdout.write(`${JSON.stringify({
      event: 'cleanup-probe-ready',
      container,
    })}\n`)
    if (process.env.SESSION_REFRESH_SELF_SIGNAL === '1') {
      setTimeout(() => process.emit('SIGTERM'), 100)
    }
    // 未 settle 的顶层 await 本身不持有 Node 事件循环；Linux 会在父进程发 SIGTERM 前
    // 以 code 13 主动退出。显式 timer 让探针存活到 signal handler 完成真实清理。
    const keepAlive = setInterval(() => undefined, 1_000)
    try {
      await new Promise(() => {})
    } finally {
      clearInterval(keepAlive)
    }
  }
  const mapping = docker(['port', container, '6379/tcp'])
  const portMatch = mapping.match(/:(\d+)\s*$/)
  assert.ok(portMatch, `cannot parse redis port: ${mapping}`)
  const redisUrl = `redis://127.0.0.1:${portMatch[1]}/0`
  const redis = new Redis(redisUrl, {
    connectTimeout: 3_000,
    maxRetriesPerRequest: 2,
  })
  const oauth = await startOAuthServer()

  try {
    await redis.ping()

    // 两个真实 Node 进程同时刷新：败者必须等待，OAuth 只消费一次。
    await seed(redis)
    oauth.state.calls = 0
    oauth.state.delayMs = 500
    oauth.state.response = 'success'
    oauth.state.expectedRefreshToken = 'refresh-old'
    oauth.state.rejectAfterFirstCall = true
    oauth.state.successLabel = 'oauth-winner'
    const environment = {
      WORKER_MODE: 'refresh',
      INTERNAL_API_URL: oauth.origin,
    }
    const [first, second] = await Promise.all([
      startWorker(redisUrl, environment).done,
      startWorker(redisUrl, environment).done,
    ])
    const statuses = [
      resultOf(first).status,
      resultOf(second).status,
    ].sort()
    assert.deepEqual(statuses, ['changed', 'updated'])
    assert.equal(oauth.state.calls, 1)
    const winner = JSON.parse(await redis.get(sessionKey))
    assert.equal(winner.accessToken, 'access-oauth-winner')

    // 人工令 lease 失效模拟 owner 卡顿；新 fence 胜者写回后，迟到 owner 必须 lost。
    await seed(redis)
    const staleGate = settleGate('stale')
    const stale = startWorker(redisUrl, {
      WORKER_MODE: 'lease-settle',
      OWNER_ID: 'owner-stale',
      SETTLE_GATE_KEY: staleGate,
      UPDATED_SESSION_JSON: JSON.stringify(updatedSession('stale')),
    })
    assert.equal((await stale.waitFor('acquired')).status, 'owner')
    await redis.del(leaseKey)
    const current = startWorker(redisUrl, {
      WORKER_MODE: 'lease-settle',
      OWNER_ID: 'owner-current',
      UPDATED_SESSION_JSON: JSON.stringify(updatedSession('current')),
    })
    assert.equal(resultOf(await current.done, 'settled'), 'updated')
    await redis.set(staleGate, 'release', 'PX', 10_000)
    assert.equal(resultOf(await stale.done, 'settled'), 'lost')
    assert.equal(JSON.parse(await redis.get(sessionKey)).accessToken, 'access-current')

    // 控制 key 若被外部误操作去掉 TTL，不得让该会话永久等待；新 owner 应接管。
    await seed(redis)
    const persistentGate = settleGate('persistent')
    const persistentLeaseOwner = startWorker(redisUrl, {
      WORKER_MODE: 'lease-settle',
      OWNER_ID: 'owner-persistent-lease',
      SETTLE_GATE_KEY: persistentGate,
      UPDATED_SESSION_JSON: JSON.stringify(updatedSession('persistent-late')),
    })
    assert.equal((await persistentLeaseOwner.waitFor('acquired')).status, 'owner')
    assert.equal(await redis.persist(leaseKey), 1)
    const recoveredOwner = startWorker(redisUrl, {
      WORKER_MODE: 'lease-settle',
      OWNER_ID: 'owner-persistent-recovery',
      UPDATED_SESSION_JSON: JSON.stringify(updatedSession('persistent-recovered')),
    })
    assert.equal((await recoveredOwner.waitFor('acquired')).status, 'owner')
    assert.equal(resultOf(await recoveredOwner.done, 'settled'), 'updated')
    await redis.set(persistentGate, 'release', 'PX', 10_000)
    assert.equal(resultOf(await persistentLeaseOwner.done, 'settled'), 'lost')
    assert.equal(
      JSON.parse(await redis.get(sessionKey)).accessToken,
      'access-persistent-recovered',
    )

    // 迟到 401 只能看见 refreshing，不能抢先删除仍有 owner 的 S0。
    await seed(redis)
    const refreshingGate = settleGate('late-401')
    const refreshingOwner = startWorker(redisUrl, {
      WORKER_MODE: 'lease-settle',
      OWNER_ID: 'owner-before-401',
      SETTLE_GATE_KEY: refreshingGate,
      UPDATED_SESSION_JSON: JSON.stringify(updatedSession('after-401')),
    })
    await refreshingOwner.waitFor('acquired')
    const invalidation = await startWorker(redisUrl, {
      WORKER_MODE: 'invalidate',
    }).done
    assert.equal(resultOf(invalidation), 'refreshing')
    assert.notEqual(await redis.get(sessionKey), null)
    await redis.set(refreshingGate, 'release', 'PX', 10_000)
    assert.equal(resultOf(await refreshingOwner.done, 'settled'), 'updated')
    assert.equal(JSON.parse(await redis.get(sessionKey)).accessToken, 'access-after-401')

    // 身份切换是权威 mutation：原子清 lease，迟到刷新不得覆盖新身份。
    await seed(redis)
    const switchingGate = settleGate('identity-switch')
    const switchingOwner = startWorker(redisUrl, {
      WORKER_MODE: 'lease-settle',
      OWNER_ID: 'owner-before-switch',
      SETTLE_GATE_KEY: switchingGate,
      UPDATED_SESSION_JSON: JSON.stringify(updatedSession('late-switch')),
    })
    await switchingOwner.waitFor('acquired')
    const switched = {
      ...baseSession,
      identityVersion: 'identity-target-v2',
      userName: 'target',
      accessToken: 'access-target',
      refreshToken: undefined,
    }
    const replacement = await startWorker(redisUrl, {
      WORKER_MODE: 'replace',
      UPDATED_SESSION_JSON: JSON.stringify(switched),
    }).done
    assert.equal(resultOf(replacement), 'updated')
    await redis.set(switchingGate, 'release', 'PX', 10_000)
    assert.equal(resultOf(await switchingOwner.done, 'settled'), 'lost')
    assert.equal(JSON.parse(await redis.get(sessionKey)).userName, 'target')

    // R0 已被 OAuth 消费但 owner 尚未 settle：身份切换必须等待 R1，再以 R1 重建快照。
    await seed(redis)
    oauth.state.calls = 0
    oauth.state.delayMs = 0
    oauth.state.response = 'success'
    oauth.state.expectedRefreshToken = 'refresh-old'
    oauth.state.rejectAfterFirstCall = false
    oauth.state.successLabel = 'r1'
    const oauthSettleGate = settleGate('oauth-consumed-before-identity')
    const consumedOwner = startWorker(redisUrl, {
      WORKER_MODE: 'oauth-gated-settle',
      OWNER_ID: 'owner-oauth-r0',
      SETTLE_GATE_KEY: oauthSettleGate,
      INTERNAL_API_URL: oauth.origin,
    })
    assert.equal((await consumedOwner.waitFor('acquired')).status, 'owner')
    assert.equal(await consumedOwner.waitFor('oauth-consumed'), true)
    assert.equal(oauth.state.calls, 1)

    const identityTarget = {
      ...baseSession,
      userId: 'user-target-id',
      userName: 'target',
      name: '目标用户',
      identityVersion: 'identity-target-v2',
      accessToken: 'access-target',
      refreshToken: undefined,
      accessTokenExpiresAt: Date.now() + 3_600_000,
    }
    const identityStart = startWorker(redisUrl, {
      WORKER_MODE: 'identity-start',
      EXPECTED_IDENTITY_VERSION: baseSession.identityVersion,
      UPDATED_SESSION_JSON: JSON.stringify(identityTarget),
    })
    assert.equal(await identityStart.waitFor('identity-preflight'), 'refreshing')
    await redis.set(oauthSettleGate, 'release', 'PX', 10_000)
    assert.equal(resultOf(await consumedOwner.done, 'settled'), 'updated')
    const identitySession = resultOf(await identityStart.done)
    assert.equal(identitySession.userName, 'target')
    assert.equal(identitySession.impersonator.accessToken, 'access-r1')
    assert.equal(identitySession.impersonator.refreshToken, 'refresh-r1')

    const identityStop = await startWorker(redisUrl, {
      WORKER_MODE: 'identity-stop',
      EXPECTED_IDENTITY_VERSION: identitySession.identityVersion,
      SESSION_JSON: JSON.stringify(identitySession),
    }).done
    const restoredR1 = resultOf(identityStop)
    assert.equal(restoredR1.userName, 'admin')
    assert.equal(restoredR1.accessToken, 'access-r1')
    assert.equal(restoredR1.refreshToken, 'refresh-r1')

    oauth.state.calls = 0
    oauth.state.expectedRefreshToken = 'refresh-r1'
    oauth.state.rejectAfterFirstCall = true
    oauth.state.successLabel = 'r2'
    const refreshedAfterSwitchBack = await startWorker(redisUrl, {
      WORKER_MODE: 'refresh',
      SESSION_JSON: JSON.stringify(restoredR1),
      INTERNAL_API_URL: oauth.origin,
    }).done
    assert.equal(resultOf(refreshedAfterSwitchBack).status, 'updated')
    assert.equal(oauth.state.calls, 1)
    assert.equal(JSON.parse(await redis.get(sessionKey)).refreshToken, 'refresh-r2')

    // 显式退出同样清 lease，owner 不能把已退出会话复活。
    await seed(redis)
    const logoutGate = settleGate('logout')
    const logoutOwner = startWorker(redisUrl, {
      WORKER_MODE: 'lease-settle',
      OWNER_ID: 'owner-before-logout',
      SETTLE_GATE_KEY: logoutGate,
      UPDATED_SESSION_JSON: JSON.stringify(updatedSession('late-logout')),
    })
    await logoutOwner.waitFor('acquired')
    const deletion = await startWorker(redisUrl, {
      WORKER_MODE: 'delete',
    }).done
    assert.equal(resultOf(deletion), 'updated')
    await redis.set(logoutGate, 'release', 'PX', 10_000)
    assert.equal(resultOf(await logoutOwner.done, 'settled'), 'lost')
    assert.equal(await redis.get(sessionKey), null)

    // 成功刷新按空闲窗口滑动续期，不得 KEEPTTL 把 8 小时裁成剩余寿命。
    await seed(redis, 60_000)
    const ttlBefore = await redis.pttl(sessionKey)
    const ttlWorker = startWorker(redisUrl, {
      WORKER_MODE: 'lease-settle',
      OWNER_ID: 'owner-ttl',
      UPDATED_SESSION_JSON: JSON.stringify(updatedSession('ttl')),
    })
    assert.equal(resultOf(await ttlWorker.done, 'settled'), 'updated')
    const ttlAfter = await redis.pttl(sessionKey)
    assert.ok(ttlAfter > ttlBefore)
    assert.ok(ttlAfter > 7 * 60 * 60 * 1000)

    // session 剩余寿命不足覆盖刷新事务时直接失效，不消费 refresh token。
    await seed(redis, 5_000)
    oauth.state.calls = 0
    oauth.state.delayMs = 0
    const expiring = await startWorker(redisUrl, {
      WORKER_MODE: 'refresh',
      INTERNAL_API_URL: oauth.origin,
    }).done
    assert.equal(resultOf(expiring).status, 'missing')
    assert.equal(oauth.state.calls, 0)
    assert.equal(await redis.exists(sessionKey, leaseKey, fenceKey), 0)

    // 当前 owner 的结构化 invalid_grant 才能删除 source。
    await seed(redis)
    oauth.state.calls = 0
    oauth.state.response = 'invalid'
    const invalid = await startWorker(redisUrl, {
      WORKER_MODE: 'refresh',
      INTERNAL_API_URL: oauth.origin,
    }).done
    assert.equal(resultOf(invalid).status, 'missing')
    assert.equal(oauth.state.calls, 1)
    assert.equal(await redis.get(sessionKey), null)

    process.stdout.write('session refresh real Redis dual-process integration: PASS\n')
  } finally {
    await oauth.close()
    redis.disconnect()
  }
}

try {
  await run()
} finally {
  cleanupResources()
}
