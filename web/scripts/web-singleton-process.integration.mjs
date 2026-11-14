import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { access, readFile } from 'node:fs/promises'
import {
  createConnection,
  createServer,
} from 'node:net'
import { performance } from 'node:perf_hooks'
import { setTimeout as delay } from 'node:timers/promises'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { Redis } from 'ioredis'
import {
  resolveWebSingletonLeaseConfiguration,
} from '../src/lib/web-singleton-lease-core.mjs'

const WEB_ROOT = fileURLToPath(new URL('..', import.meta.url))
const TEST_TTL_MS = 15_000
const POLL_INTERVAL_MS = 25
const PROCESS_TEST_TIMEOUT_MS = 150_000

function withTimeout(promise, timeoutMs, message) {
  let timer
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error(message)), timeoutMs)
    timer.unref?.()
  })
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer))
}

async function waitFor(description, timeoutMs, predicate) {
  const deadline = performance.now() + timeoutMs
  while (performance.now() < deadline) {
    if (await predicate()) return performance.now()
    await delay(POLL_INTERVAL_MS)
  }
  throw new Error(`Timed out waiting for ${description}`)
}

async function allocatePort() {
  const server = createServer()
  await new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, '127.0.0.1', resolve)
  })
  const address = server.address()
  assert.ok(address && typeof address === 'object')
  const port = address.port
  await new Promise((resolve, reject) => {
    server.close(error => error ? reject(error) : resolve())
  })
  return port
}

function observeConnection(port) {
  return new Promise((resolve) => {
    const socket = createConnection({
      host: '127.0.0.1',
      port,
    })
    let settled = false
    const finish = (connected) => {
      if (settled) return
      settled = true
      socket.destroy()
      resolve({
        connected,
        observedAt: performance.now(),
      })
    }
    socket.setTimeout(250, () => finish(false))
    socket.once('connect', () => finish(true))
    socket.once('error', () => finish(false))
  })
}

async function canConnect(port) {
  return (await observeConnection(port)).connected
}

async function waitForFirstConnect(port, timeoutMs) {
  const deadline = performance.now() + timeoutMs
  while (performance.now() < deadline) {
    const observation = await observeConnection(port)
    if (observation.connected) return observation.observedAt
    await delay(POLL_INTERVAL_MS)
  }
  throw new Error('Timed out waiting for successor first TCP listener')
}

async function isBuildMetaReady(port) {
  try {
    const response = await fetch(`http://127.0.0.1:${port}/build-meta`, {
      signal: AbortSignal.timeout(500),
    })
    await response.text()
    return response.status === 200
  } catch {
    return false
  }
}

async function snapshotLease(redis, key) {
  const replies = await redis.multi()
    .get(key)
    .pttl(key)
    .time()
    .exec()
  const owner = replies?.[0]?.[1]
  if (!owner) return undefined
  const remainingTtlMs = Number(replies?.[1]?.[1])
  const redisTime = replies?.[2]?.[1]
  assert.ok(Array.isArray(redisTime))
  return {
    owner: String(owner),
    remainingTtlMs,
    redisTimeMs: (Number(redisTime[0]) * 1_000)
      + (Number(redisTime[1]) / 1_000),
  }
}

function observeLeaseAcquire(monitor, key, timeoutMs) {
  let onMonitor
  const observation = new Promise((resolve) => {
    onMonitor = (time, args) => {
      if (
        String(args?.[0]).toLowerCase() !== 'set'
        || args?.[1] !== key
        || !args.some(argument => String(argument).toLowerCase() === 'nx')
      ) {
        return
      }
      resolve({
        owner: String(args[2]),
        redisTimeMs: Number(time) * 1_000,
      })
    }
    monitor.on('monitor', onMonitor)
  })
  return withTimeout(
    observation,
    timeoutMs,
    'Timed out observing successor lease acquisition',
  ).finally(() => monitor.removeListener('monitor', onMonitor))
}

async function readRedisTimeMs(redis) {
  const [seconds, microseconds] = await redis.time()
  return (Number(seconds) * 1_000) + (Number(microseconds) / 1_000)
}

async function calibrateRedisClock(redis) {
  let best
  for (let attempt = 0; attempt < 3; attempt += 1) {
    const startedAt = performance.now()
    const redisTimeMs = await readRedisTimeMs(redis)
    const finishedAt = performance.now()
    const roundTripMs = finishedAt - startedAt
    if (!best || roundTripMs < best.roundTripMs) {
      best = {
        roundTripMs,
        offsetMs: redisTimeMs - ((startedAt + finishedAt) / 2),
      }
    }
  }
  assert.ok(best)
  return best.offsetMs
}

function spawnWrapper({ redisUrl, key, port }) {
  const environment = {
    ...process.env,
    NODE_ENV: 'production',
    PORT: String(port),
    HOSTNAME: '127.0.0.1',
    SESSION_STORE_MODE: 'redis',
    REDIS_URL: redisUrl,
    WEB_SINGLETON_LEASE_KEY: key,
    WEB_SINGLETON_LEASE_TTL_MS: String(TEST_TTL_MS),
  }
  delete environment.SWAPMID_WEB_LEASE_SUPERVISED
  delete environment.SWAPMID_WEB_LEASE_WATCHDOG_MS
  delete environment.WEB_SINGLETON_TAKEOVER_ISOLATION_MS

  const child = spawn(
    process.execPath,
    ['scripts/start-production.mjs'],
    {
      cwd: WEB_ROOT,
      env: environment,
      stdio: ['ignore', 'pipe', 'pipe'],
    },
  )
  const record = {
    child,
    output: '',
    result: undefined,
    completion: undefined,
  }
  for (const stream of [child.stdout, child.stderr]) {
    stream.setEncoding('utf8')
    stream.on('data', (chunk) => {
      record.output = `${record.output}${chunk}`.slice(-8_000)
    })
  }
  record.completion = new Promise((resolve) => {
    let settled = false
    const finish = (result) => {
      if (settled) return
      settled = true
      record.result = result
      resolve(result)
    }
    child.once('error', error => finish({ error }))
    child.once('close', (code, signal) => finish({ code, signal }))
  })
  return record
}

async function waitForWrapperExit(record, timeoutMs, description) {
  try {
    return await withTimeout(record.completion, timeoutMs, description)
  } catch (error) {
    throw new Error(`${error.message}\n${record.output || '[no child output]'}`)
  }
}

async function waitForWrapperReady(record, port, timeoutMs, description) {
  try {
    return await waitFor(description, timeoutMs, () => isBuildMetaReady(port))
  } catch (error) {
    throw new Error(`${error.message}\n${record.output || '[no child output]'}`)
  }
}

function signalChild(child, signal) {
  try {
    return child.kill(signal)
  } catch (error) {
    if (error?.code === 'ESRCH') return false
    throw error
  }
}

async function readProcessIdentity(pid) {
  try {
    const stat = await readFile(`/proc/${pid}/stat`, 'utf8')
    const commandEnd = stat.lastIndexOf(')')
    assert.ok(commandEnd > 0)
    const fieldsAfterCommand = stat.slice(commandEnd + 2).trim().split(/\s+/)
    return {
      pid,
      startTime: fieldsAfterCommand[19],
    }
  } catch (error) {
    if (error?.code === 'ENOENT' || error?.code === 'ESRCH') return undefined
    throw error
  }
}

async function isSameProcess(identity) {
  const current = await readProcessIdentity(identity.pid)
  return current?.startTime === identity.startTime
}

async function findNextChild(supervisorPid) {
  let found
  await waitFor('Next child in /proc', 3_000, async () => {
    let children
    try {
      children = await readFile(
        `/proc/${supervisorPid}/task/${supervisorPid}/children`,
        'utf8',
      )
    } catch (error) {
      if (error?.code === 'ENOENT') return false
      throw error
    }

    for (const rawPid of children.trim().split(/\s+/).filter(Boolean)) {
      const pid = Number(rawPid)
      const [command, processName] = await Promise.all([
        readFile(`/proc/${pid}/cmdline`, 'utf8').catch(() => ''),
        readFile(`/proc/${pid}/comm`, 'utf8').catch(() => ''),
      ])
      // Next 启动后会把 Linux process title 改成 next-server，覆盖原始
      // argv，因此稳定态的 /proc/<pid>/cmdline 不再包含 next/dist/bin/next。
      if (
        !command.includes('next/dist/bin/next')
        && !processName.trim().startsWith('next-server')
      ) {
        continue
      }
      found = await readProcessIdentity(pid)
      if (found) return true
    }
    return false
  })
  assert.ok(found)
  return found
}

async function stopWrapper(record, timeoutMs) {
  if (!record || record.result) return
  signalChild(record.child, 'SIGTERM')
  try {
    await waitForWrapperExit(record, timeoutMs, 'wrapper cleanup SIGTERM')
  } catch {
    signalChild(record.child, 'SIGKILL')
    await waitForWrapperExit(record, 2_000, 'wrapper cleanup SIGKILL')
      .catch(() => undefined)
  }
}

test('Linux 下 supervisor SIGKILL 后 watchdog、TTL 与隔离接管闭环', {
  skip: process.platform !== 'linux',
  timeout: PROCESS_TEST_TIMEOUT_MS,
}, async () => {
  const redisUrl = process.env.REDIS_URL
  const keyPrefix = process.env.WEB_SINGLETON_LEASE_KEY
  assert.ok(redisUrl, 'process integration requires REDIS_URL')
  assert.ok(keyPrefix, 'process integration requires isolated lease key')
  await access(new URL('../.next/BUILD_ID', import.meta.url))

  const key = `${keyPrefix}:process:${randomUUID()}`
  const configuration = resolveWebSingletonLeaseConfiguration({
    WEB_SINGLETON_LEASE_KEY: key,
    WEB_SINGLETON_LEASE_TTL_MS: String(TEST_TTL_MS),
  })
  const acquirePathTimeoutMs = configuration.redisReadyTimeoutMs
    + configuration.redisCommandTimeoutMs
    + 2_000
  const redis = new Redis(redisUrl, {
    lazyConnect: true,
    enableOfflineQueue: false,
    retryStrategy: () => null,
    connectTimeout: 3_000,
    commandTimeout: 1_500,
    maxRetriesPerRequest: 0,
  })
  redis.on('error', () => {})

  const wrappers = []
  const nextProcesses = []
  let monitor
  let port
  try {
    await withTimeout(
      redis.connect(),
      5_000,
      'Timed out connecting integration Redis',
    )
    await redis.del(key)
    port = await allocatePort()

    const first = spawnWrapper({ redisUrl, key, port })
    wrappers.push(first)
    await waitForWrapperReady(
      first,
      port,
      configuration.cleanStartBudgetMs + 5_000,
      'first wrapper HTTP 200',
    )
    const firstNext = await findNextChild(first.child.pid)
    nextProcesses.push(firstNext)
    const firstOwner = await redis.get(key)
    assert.ok(firstOwner)

    process.kill(first.child.pid, 'SIGKILL')
    const firstExit = await waitForWrapperExit(
      first,
      configuration.childWatchdogTimeoutMs + 3_000,
      'SIGKILLed supervisor exit',
    )
    assert.equal(firstExit.code, null)
    assert.equal(firstExit.signal, 'SIGKILL')

    await waitFor(
      'orphan Next watchdog exit and closed port',
      configuration.childWatchdogTimeoutMs + 3_000,
      async () => !await isSameProcess(firstNext) && !await canConnect(port),
    )
    assert.equal(await redis.get(key), firstOwner)

    const denied = spawnWrapper({ redisUrl, key, port })
    wrappers.push(denied)
    const deniedExit = await waitForWrapperExit(
      denied,
      acquirePathTimeoutMs,
      'second wrapper refusal',
    )
    assert.equal(deniedExit.signal, null)
    assert.notEqual(deniedExit.code, 0)
    assert.equal(await canConnect(port), false)
    assert.equal(await redis.get(key), firstOwner)

    await waitFor(
      'orphan lease TTL expiry',
      TEST_TTL_MS + 3_000,
      async () => await redis.exists(key) === 0,
    )

    monitor = await redis.monitor()
    monitor.on('error', () => {})
    const redisClockOffsetMs = await calibrateRedisClock(redis)
    const acquisitionObservation = observeLeaseAcquire(
      monitor,
      key,
      acquirePathTimeoutMs,
    )
    const successor = spawnWrapper({ redisUrl, key, port })
    wrappers.push(successor)
    let successorSnapshot
    const [, acquired] = await Promise.all([
      waitFor('successor lease acquisition', acquirePathTimeoutMs, async () => {
        successorSnapshot = await snapshotLease(redis, key)
        return Boolean(successorSnapshot)
      }),
      acquisitionObservation,
    ])
    assert.equal(acquired.owner, successorSnapshot.owner)
    assert.notEqual(acquired.owner, firstOwner)
    assert.ok(
      successorSnapshot.remainingTtlMs > 0
      && successorSnapshot.remainingTtlMs <= TEST_TTL_MS,
    )
    assert.ok(successorSnapshot.redisTimeMs >= acquired.redisTimeMs)

    const firstListenAt = await waitForFirstConnect(
      port,
      configuration.cleanStartBudgetMs,
    )
    const acquiredAt = acquired.redisTimeMs - redisClockOffsetMs
    assert.ok(
      firstListenAt - acquiredAt
        >= configuration.takeoverIsolationMs,
      'successor listened before the complete takeover isolation window',
    )
    await waitForWrapperReady(
      successor,
      port,
      5_000,
      'successor HTTP 200',
    )

    const successorNext = await findNextChild(successor.child.pid)
    nextProcesses.push(successorNext)
    assert.equal(successor.child.kill('SIGTERM'), true)
    const successorExit = await waitForWrapperExit(
      successor,
      20_000,
      'successor graceful SIGTERM',
    )
    assert.deepEqual(successorExit, { code: 0, signal: null })
    await waitFor(
      'successor port close and compare-owner release',
      2_000,
      async () => !await canConnect(port) && await redis.exists(key) === 0,
    )
  } finally {
    for (const wrapper of wrappers.reverse()) {
      await stopWrapper(
        wrapper,
        configuration.containerStopBudgetMs + 2_000,
      )
    }
    for (const identity of nextProcesses) {
      if (!await isSameProcess(identity)) continue
      try {
        process.kill(identity.pid, 'SIGKILL')
      } catch (error) {
        if (error?.code !== 'ESRCH') throw error
      }
    }
    monitor?.disconnect(false)
    if (redis.status === 'ready') {
      await redis.del(key).catch(() => undefined)
      await redis.quit().catch(() => undefined)
    }
    redis.disconnect(false)
  }
})
