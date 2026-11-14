import { randomUUID } from 'node:crypto'
import { existsSync } from 'node:fs'
import { readFile } from 'node:fs/promises'
import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { Redis } from 'ioredis'
import {
  resolveSessionStoreConfiguration,
} from '../src/lib/session-store-config-core.mjs'
import {
  createRedisWebSingletonLease,
  resolveWebSingletonLeaseConfiguration,
  WEB_LEASE_SUPERVISED_ENV,
  WEB_LEASE_WATCHDOG_ENV,
  WebSingletonLeaseAlreadyHeldError,
} from '../src/lib/web-singleton-lease-core.mjs'
import {
  WebSingletonLeaseSupervisor,
} from '../src/lib/web-singleton-lease-supervisor.mjs'
import { waitForApiReady } from './wait-for-api-ready.mjs'
import { retainNextStaticGenerations } from './retain-next-static.mjs'

const appRootUrl = new URL('..', import.meta.url)
const appRoot = fileURLToPath(appRootUrl)
// Docker 把 .next/standalone 铺到 /app 后，server.js 与 scripts/ 同级。
// 本地和 CI 没有这层布局，下面会退回 next start。
const standaloneServerPath = fileURLToPath(new URL('server.js', appRootUrl))
const nextCliPath = fileURLToPath(
  new URL('node_modules/next/dist/bin/next', appRootUrl),
)

function spawnProductionServer(env) {
  // 两条路径都由本包装器监督。容器 CMD 不得改成直接 node server.js 或 next start。
  if (existsSync(standaloneServerPath)) {
    return spawn(process.execPath, [standaloneServerPath], {
      cwd: appRoot,
      // Docker 把 HOSTNAME 设成容器 ID。standalone server 会拿它当监听地址，
      // 健康检查 wget localhost 会连不上。子进程显式绑所有接口。
      env: { ...env, HOSTNAME: '0.0.0.0' },
      stdio: ['inherit', 'inherit', 'inherit', 'ipc'],
    })
  }

  return spawn(
    process.execPath,
    // 本地正式模式验收可用原生 --hostname/--port 绑定回环；仍由同一租约和 watchdog 监督。
    [nextCliPath, 'start', ...process.argv.slice(2)],
    {
      cwd: appRoot,
      env,
      stdio: ['inherit', 'inherit', 'inherit', 'ipc'],
    },
  )
}

function logStartupFailure(error) {
  if (error instanceof WebSingletonLeaseAlreadyHeldError) {
    console.error(
      '[web-singleton] another production web instance is active; startup refused',
    )
    return
  }

  console.error(
    '[web-singleton] production startup failed before the server was allowed to listen',
  )
}

async function closeRedis(redis, timeoutMs) {
  let timeoutHandle
  const timeout = new Promise((resolve) => {
    timeoutHandle = setTimeout(resolve, timeoutMs)
  })

  try {
    await Promise.race([
      redis.quit().catch(() => undefined),
      timeout,
    ])
  } finally {
    clearTimeout(timeoutHandle)
    redis.disconnect(false)
  }
}

async function connectRedis(redis, timeoutMs) {
  let timeoutHandle
  const timeout = new Promise((_, reject) => {
    timeoutHandle = setTimeout(() => {
      reject(new Error('Redis did not become ready within the startup budget'))
    }, timeoutMs)
  })

  try {
    await Promise.race([redis.connect(), timeout])
  } finally {
    clearTimeout(timeoutHandle)
  }
}

let redis
let supervisor
let redisClosing = false
const signalHandlers = new Map()

try {
  const buildIdPath = fileURLToPath(new URL('.next/BUILD_ID', appRootUrl))
  let buildId = ''
  try {
    buildId = (await readFile(buildIdPath, 'utf8')).trim()
  } catch {
    buildId = ''
  }
  if (buildId) {
    await retainNextStaticGenerations({
      staticDir: fileURLToPath(new URL('.next/static', appRootUrl)),
      archiveDir: process.env.SWAPMID_NEXT_STATIC_ARCHIVE,
      buildId,
      keep: process.env.SWAPMID_NEXT_STATIC_KEEP,
    })
  }

  const readyAbort = new AbortController()
  const stopWaiting = () => readyAbort.abort()
  process.on('SIGINT', stopWaiting)
  process.on('SIGTERM', stopWaiting)
  const ready = await waitForApiReady({
    env: process.env,
    signal: readyAbort.signal,
  })
  process.off('SIGINT', stopWaiting)
  process.off('SIGTERM', stopWaiting)
  if (ready.aborted) process.exit(0)

  const sessionConfiguration = resolveSessionStoreConfiguration({
    ...process.env,
    NODE_ENV: 'production',
  })
  const leaseConfiguration = resolveWebSingletonLeaseConfiguration(process.env)

  redis = new Redis(sessionConfiguration.redisUrl, {
    lazyConnect: true,
    enableOfflineQueue: false,
    autoResendUnfulfilledCommands: false,
    maxRetriesPerRequest: 0,
    connectTimeout: leaseConfiguration.redisConnectTimeoutMs,
    commandTimeout: leaseConfiguration.redisCommandTimeoutMs,
    retryStrategy: () => null,
    reconnectOnError: () => false,
    connectionName: 'swapmid-web-singleton-lease',
  })

  const lease = createRedisWebSingletonLease(redis, {
    key: leaseConfiguration.key,
    owner: randomUUID(),
    ttlMs: leaseConfiguration.ttlMs,
  })
  const childEnvironment = {
    ...process.env,
    // 只传无秘密的监督协议开关与 watchdog 时限；owner 永不离开父进程闭包。
    [WEB_LEASE_SUPERVISED_ENV]: '1',
    [WEB_LEASE_WATCHDOG_ENV]: String(
      leaseConfiguration.childWatchdogTimeoutMs,
    ),
  }
  delete childEnvironment.WEB_SINGLETON_LEASE_KEY
  delete childEnvironment.WEB_SINGLETON_LEASE_TTL_MS
  delete childEnvironment.WEB_SINGLETON_TAKEOVER_ISOLATION_MS

  supervisor = new WebSingletonLeaseSupervisor({
    lease,
    spawnChild: () => spawnProductionServer(childEnvironment),
    renewIntervalMs: leaseConfiguration.renewIntervalMs,
    takeoverIsolationMs: leaseConfiguration.takeoverIsolationMs,
    heartbeatIntervalMs: leaseConfiguration.heartbeatIntervalMs,
    heartbeatFreshnessMs: leaseConfiguration.heartbeatFreshnessMs,
    shutdownGraceMs: leaseConfiguration.shutdownGraceMs,
    failureGraceMs: leaseConfiguration.failureGraceMs,
    forceKillWaitMs: leaseConfiguration.forceKillWaitMs,
    logError: message => console.error(message),
  })

  for (const signal of ['SIGINT', 'SIGTERM']) {
    const handler = () => {
      void supervisor.requestShutdown({ signal })
    }
    signalHandlers.set(signal, handler)
    process.on(signal, handler)
  }

  const failForRedisUncertainty = () => {
    if (redisClosing) return
    console.error(
      '[web-singleton] Redis connectivity became uncertain; stopping the web server',
    )
    void supervisor.requestShutdown({ fatal: true })
  }
  redis.on('error', failForRedisUncertainty)
  redis.on('close', failForRedisUncertainty)
  redis.on('end', failForRedisUncertainty)

  await connectRedis(redis, leaseConfiguration.redisReadyTimeoutMs)
  await supervisor.start()
  const result = await supervisor.completion

  for (const [signal, handler] of signalHandlers) {
    process.removeListener(signal, handler)
  }

  redisClosing = true
  if (result.hardExit) {
    redis.disconnect(false)
    process.exit(1)
  }

  await closeRedis(redis, leaseConfiguration.redisCloseTimeoutMs)
  process.exitCode = result.exitCode
} catch (error) {
  for (const [signal, handler] of signalHandlers) {
    process.removeListener(signal, handler)
  }
  redisClosing = true
  redis?.disconnect(false)
  logStartupFailure(error)
  process.exitCode = 1
}
