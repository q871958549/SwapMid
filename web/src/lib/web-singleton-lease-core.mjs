export const WEB_LEASE_HEARTBEAT_TYPE = 'swapmid-web-lease-heartbeat'
export const WEB_LEASE_HEARTBEAT_PROTOCOL = 1
export const WEB_LEASE_SUPERVISED_ENV = 'SWAPMID_WEB_LEASE_SUPERVISED'
export const WEB_LEASE_WATCHDOG_ENV = 'SWAPMID_WEB_LEASE_WATCHDOG_MS'

export const RENEW_WEB_SINGLETON_LEASE_SCRIPT = `
if redis.call('GET', KEYS[1]) == ARGV[1] then
  return redis.call('PEXPIRE', KEYS[1], ARGV[2])
end
return 0
`

export const RELEASE_WEB_SINGLETON_LEASE_SCRIPT = `
if redis.call('GET', KEYS[1]) == ARGV[1] then
  return redis.call('DEL', KEYS[1])
end
return 0
`

const MIN_LEASE_TTL_MS = 15_000
const MAX_LEASE_TTL_MS = 300_000
const COMMAND_UNCERTAINTY_MS = 2_000
const TAKEOVER_SAFETY_BUFFER_MS = 1_000
const REDIS_CONNECT_TIMEOUT_MS = 3_000
const REDIS_READY_TIMEOUT_MS = 5_000
const REDIS_COMMAND_TIMEOUT_MS = 1_500
const REDIS_CLOSE_TIMEOUT_MS = 1_000
const NEXT_READY_ALLOWANCE_MS = 5_000
const STOP_SCHEDULING_MARGIN_MS = 5_000

export class WebSingletonLeaseAlreadyHeldError extends Error {
  constructor() {
    super('The production web singleton lease is already held')
    this.name = 'WebSingletonLeaseAlreadyHeldError'
  }
}

export class WebSingletonLeaseBackendError extends Error {
  constructor() {
    super('The production web singleton lease backend is unavailable')
    this.name = 'WebSingletonLeaseBackendError'
  }
}

/**
 * @param {Record<string, string | undefined>} [environment]
 */
export function assertProductionWebLeaseSupervision(
  environment = process.env,
) {
  if (
    environment.NODE_ENV === 'production'
    && environment[WEB_LEASE_SUPERVISED_ENV] !== '1'
  ) {
    throw new Error(
      'Production web startup is not supervised by the singleton lease guard',
    )
  }
}

/**
 * @returns {never}
 */
function invalidLeaseConfiguration(message) {
  throw new Error(`Web singleton lease configuration is invalid: ${message}`)
}

function parseInteger(environment, name, minimum, maximum) {
  const raw = environment[name]
  if (!raw || !/^[1-9]\d*$/.test(raw)) {
    return invalidLeaseConfiguration(`${name} must be an integer`)
  }

  const parsed = Number(raw)
  if (!Number.isSafeInteger(parsed) || parsed < minimum || parsed > maximum) {
    return invalidLeaseConfiguration(
      `${name} must be between ${minimum} and ${maximum}`,
    )
  }

  return parsed
}

/**
 * @param {Record<string, string | undefined>} [environment]
 */
export function resolveWebSingletonLeaseConfiguration(
  environment = process.env,
) {
  const key = environment.WEB_SINGLETON_LEASE_KEY
  if (
    !key
    || key !== key.trim()
    || key.length > 256
    || /[\u0000-\u0020\u007f]/.test(key)
  ) {
    return invalidLeaseConfiguration(
      'WEB_SINGLETON_LEASE_KEY must be a non-empty key without whitespace',
    )
  }

  const ttlMs = parseInteger(
    environment,
    'WEB_SINGLETON_LEASE_TTL_MS',
    MIN_LEASE_TTL_MS,
    MAX_LEASE_TTL_MS,
  )
  const renewIntervalMs = Math.floor(ttlMs / 3)
  const shutdownGraceMs = Math.min(10_000, Math.floor(ttlMs / 3))
  const failureGraceMs = Math.min(1_500, Math.floor(ttlMs / 10))
  const forceKillWaitMs = 1_000
  const childWatchdogTimeoutMs = Math.min(5_000, Math.floor(ttlMs / 6))
  const renewalDetectionMs = renewIntervalMs + COMMAND_UNCERTAINTY_MS
  const leaseSafetyMarginMs = childWatchdogTimeoutMs
    + failureGraceMs
    + forceKillWaitMs
    + COMMAND_UNCERTAINTY_MS
    + TAKEOVER_SAFETY_BUFFER_MS
  const heartbeatFreshnessMs = ttlMs - leaseSafetyMarginMs
  const heartbeatIntervalMs = Math.min(
    1_000,
    Math.floor(childWatchdogTimeoutMs / 3),
  )
  const minimumTakeoverIsolationMs = renewalDetectionMs
    + failureGraceMs
    + forceKillWaitMs
    + childWatchdogTimeoutMs
  const derivedTakeoverIsolationMs = minimumTakeoverIsolationMs
    + TAKEOVER_SAFETY_BUFFER_MS
  const configuredIsolation = environment.WEB_SINGLETON_TAKEOVER_ISOLATION_MS
  const takeoverIsolationMs = configuredIsolation
    ? parseInteger(
      environment,
      'WEB_SINGLETON_TAKEOVER_ISOLATION_MS',
      derivedTakeoverIsolationMs,
      MAX_LEASE_TTL_MS * 2,
    )
    : derivedTakeoverIsolationMs
  const containerStopBudgetMs = shutdownGraceMs
    + forceKillWaitMs
    + (REDIS_COMMAND_TIMEOUT_MS * 2)
    + REDIS_CLOSE_TIMEOUT_MS
    + STOP_SCHEDULING_MARGIN_MS
  const cleanStartBudgetMs = REDIS_READY_TIMEOUT_MS
    + REDIS_COMMAND_TIMEOUT_MS
    + takeoverIsolationMs
    + REDIS_COMMAND_TIMEOUT_MS
    + NEXT_READY_ALLOWANCE_MS

  if (heartbeatFreshnessMs <= heartbeatIntervalMs) {
    return invalidLeaseConfiguration(
      'WEB_SINGLETON_LEASE_TTL_MS leaves no safe heartbeat window',
    )
  }

  return {
    key,
    ttlMs,
    renewIntervalMs,
    shutdownGraceMs,
    failureGraceMs,
    forceKillWaitMs,
    childWatchdogTimeoutMs,
    heartbeatFreshnessMs,
    heartbeatIntervalMs,
    minimumTakeoverIsolationMs,
    takeoverIsolationMs,
    redisConnectTimeoutMs: REDIS_CONNECT_TIMEOUT_MS,
    redisReadyTimeoutMs: REDIS_READY_TIMEOUT_MS,
    redisCommandTimeoutMs: REDIS_COMMAND_TIMEOUT_MS,
    redisCloseTimeoutMs: REDIS_CLOSE_TIMEOUT_MS,
    nextReadyAllowanceMs: NEXT_READY_ALLOWANCE_MS,
    stopSchedulingMarginMs: STOP_SCHEDULING_MARGIN_MS,
    containerStopBudgetMs,
    cleanStartBudgetMs,
  }
}

/**
 * @param {{
 *   set: (
 *     key: string,
 *     owner: string,
 *     px: string,
 *     ttlMs: number,
 *     nx: string,
 *   ) => Promise<unknown>,
 *   eval: (
 *     script: string,
 *     keyCount: number,
 *     key: string,
 *     owner: string,
 *     ttlMs?: string,
 *   ) => Promise<unknown>,
 * }} redis
 * @param {{ key: string, owner: string, ttlMs: number }} options
 */
export function createRedisWebSingletonLease(
  redis,
  { key, owner, ttlMs },
) {
  return {
    async acquire() {
      const result = await redis.set(key, owner, 'PX', ttlMs, 'NX')
      return result === 'OK'
    },

    async renew() {
      const result = await redis.eval(
        RENEW_WEB_SINGLETON_LEASE_SCRIPT,
        1,
        key,
        owner,
        String(ttlMs),
      )
      return Number(result) === 1
    },

    async release() {
      const result = await redis.eval(
        RELEASE_WEB_SINGLETON_LEASE_SCRIPT,
        1,
        key,
        owner,
      )
      return Number(result) === 1
    },
  }
}
