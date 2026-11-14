import 'server-only'

import type { FullSession } from './session-types'
import {
  deleteStoredSession,
  deleteStoredSessionIfUnchanged,
  getMemorySessionRemainingTtl,
  getSessionStoreRedis,
  getStoredSession,
  isValidSessionId,
  matchesRevision,
  refreshFenceKey,
  refreshLeaseKey,
  replaceStoredSessionIfUnchanged,
  sessionDataKey,
  sessionRevisionDigest,
  type SessionRevision,
} from './session-store'
import { resolveSessionIssuedAt, slidingSessionTtlMs } from './session-lifetime'

export const REFRESH_LEASE_MS = 15_000
export const REFRESH_MIN_SESSION_TTL_MS = 12_000
export const REFRESH_COOLDOWN_MS = 2_000

export interface RefreshLease {
  ownerId: string
  fence: number
  source: string
}

export type RefreshLeaseAcquisition =
  | { status: 'owner'; lease: RefreshLease }
  | { status: 'waiter'; retryAfterMs: number }
  | { status: 'cooldown'; retryAfterMs: number }
  | { status: 'changed' }
  | { status: 'missing' }
  | { status: 'budget-exhausted' }
  | { status: 'unavailable' }

export type RefreshSettlementResult =
  | 'updated'
  | 'missing'
  | 'conflict'
  | 'lost'
  | 'unavailable'

const ACQUIRE_REFRESH_LEASE_SCRIPT = `
local raw = redis.call('GET', KEYS[1])
if not raw then return {0} end

local sessionTtl = redis.call('PTTL', KEYS[1])
if sessionTtl <= tonumber(ARGV[9]) then
  redis.call('DEL', KEYS[1], KEYS[2], KEYS[3])
  return {0}
end

local ok, current = pcall(cjson.decode, raw)
if not ok or type(current) ~= 'table' then return {3} end

local currentVersion = current['identityVersion']
if currentVersion == nil then currentVersion = '' end
if currentVersion ~= ARGV[1]
  or current['userName'] ~= ARGV[2]
  or current['accessToken'] ~= ARGV[3]
  or (current['refreshToken'] or '') ~= ARGV[4]
  or current['accessTokenExpiresAt'] ~= tonumber(ARGV[5]) then
  return {3}
end

local leaseRaw = redis.call('GET', KEYS[2])
if leaseRaw then
  local leaseOk, lease = pcall(cjson.decode, leaseRaw)
  if leaseOk and type(lease) == 'table' and lease['source'] == ARGV[6] then
    local leaseTtl = redis.call('PTTL', KEYS[2])
    if leaseTtl > 0 then
      if lease['state'] == 'cooldown' then return {4, leaseTtl} end
      if lease['state'] == 'active' then
        return {2, tonumber(lease['fence']), leaseTtl}
      end
    end
  end
  redis.call('DEL', KEYS[2])
end

if ARGV[10] ~= '1' then return {5} end

local fence = redis.call('INCR', KEYS[3])
local leaseTtl = math.min(tonumber(ARGV[8]), sessionTtl)
redis.call(
  'SET',
  KEYS[2],
  cjson.encode({
    version = 1,
    state = 'active',
    owner = ARGV[7],
    fence = fence,
    source = ARGV[6]
  }),
  'PX',
  leaseTtl
)
redis.call('PEXPIRE', KEYS[3], sessionTtl)
return {1, fence, leaseTtl}
`

const SETTLE_REFRESH_LEASE_SCRIPT = `
local leaseRaw = redis.call('GET', KEYS[2])
if not leaseRaw then return -2 end

local leaseOk, lease = pcall(cjson.decode, leaseRaw)
if not leaseOk or type(lease) ~= 'table'
  or lease['state'] ~= 'active'
  or lease['owner'] ~= ARGV[1]
  or tostring(lease['fence']) ~= ARGV[2]
  or lease['source'] ~= ARGV[3] then
  return -2
end

local raw = redis.call('GET', KEYS[1])
if not raw then
  redis.call('DEL', KEYS[2], KEYS[3])
  return 0
end

local ok, current = pcall(cjson.decode, raw)
if not ok or type(current) ~= 'table' then
  redis.call('DEL', KEYS[2])
  return -1
end

local currentVersion = current['identityVersion']
if currentVersion == nil then currentVersion = '' end
if currentVersion ~= ARGV[4]
  or current['userName'] ~= ARGV[5]
  or current['accessToken'] ~= ARGV[6]
  or (current['refreshToken'] or '') ~= ARGV[7]
  or current['accessTokenExpiresAt'] ~= tonumber(ARGV[8]) then
  redis.call('DEL', KEYS[2])
  return -1
end

if ARGV[9] == 'success' then
  local slideTtl = tonumber(ARGV[12])
  if slideTtl == nil or slideTtl <= 0 then
    redis.call('DEL', KEYS[1], KEYS[2], KEYS[3])
    return 0
  end
  local updated = redis.call('SET', KEYS[1], ARGV[10], 'PX', slideTtl, 'XX')
  redis.call('DEL', KEYS[2])
  if not updated then return 0 end
  return 1
end

if ARGV[9] == 'invalid' then
  redis.call('DEL', KEYS[1], KEYS[2], KEYS[3])
  return 1
end

local sessionTtl = redis.call('PTTL', KEYS[1])
if sessionTtl <= 0 then
  redis.call('DEL', KEYS[2], KEYS[3])
  return 0
end
local cooldownTtl = math.min(tonumber(ARGV[11]), sessionTtl)
redis.call(
  'SET',
  KEYS[2],
  cjson.encode({
    version = 1,
    state = 'cooldown',
    fence = tonumber(ARGV[2]),
    source = ARGV[3]
  }),
  'PX',
  cooldownTtl
)
return 1
`

function positiveNumber(value: unknown): number {
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 1
}

function parseAcquisition(
  raw: unknown,
  ownerId: string,
  source: string,
): RefreshLeaseAcquisition {
  if (!Array.isArray(raw)) return { status: 'unavailable' }
  const code = Number(raw[0])
  if (code === 1) {
    return {
      status: 'owner',
      lease: {
        ownerId,
        fence: positiveNumber(raw[1]),
        source,
      },
    }
  }
  if (code === 2) {
    return { status: 'waiter', retryAfterMs: positiveNumber(raw[2]) }
  }
  if (code === 4) {
    return { status: 'cooldown', retryAfterMs: positiveNumber(raw[1]) }
  }
  if (code === 5) return { status: 'budget-exhausted' }
  if (code === 3) return { status: 'changed' }
  return { status: 'missing' }
}

function mapSettlement(raw: unknown): RefreshSettlementResult {
  const code = Number(raw)
  if (code === 1) return 'updated'
  if (code === 0) return 'missing'
  if (code === -1) return 'conflict'
  return 'lost'
}

export async function acquireSessionRefreshLease(
  id: string,
  expected: SessionRevision,
  ownerId: string,
  allowOwnership = true,
): Promise<RefreshLeaseAcquisition> {
  if (!isValidSessionId(id)) return { status: 'missing' }
  const source = sessionRevisionDigest(expected)
  const redis = getSessionStoreRedis()
  if (!redis) {
    const lookup = await getStoredSession(id)
    if (lookup.status !== 'found') return lookup.status === 'unavailable'
      ? { status: 'unavailable' }
      : { status: 'missing' }
    if (!matchesRevision(lookup.session, expected)) return { status: 'changed' }
    const ttl = getMemorySessionRemainingTtl(id) ?? 0
    if (ttl <= REFRESH_MIN_SESSION_TTL_MS) {
      await deleteStoredSession(id)
      return { status: 'missing' }
    }
    if (!allowOwnership) return { status: 'budget-exhausted' }
    return {
      status: 'owner',
      lease: { ownerId, fence: 0, source },
    }
  }

  try {
    const result = await redis.eval(
      ACQUIRE_REFRESH_LEASE_SCRIPT,
      3,
      sessionDataKey(id),
      refreshLeaseKey(id),
      refreshFenceKey(id),
      expected.identityVersion,
      expected.userName,
      expected.accessToken,
      expected.refreshToken,
      expected.accessTokenExpiresAt,
      source,
      ownerId,
      REFRESH_LEASE_MS,
      REFRESH_MIN_SESSION_TTL_MS,
      allowOwnership ? '1' : '0',
    )
    return parseAcquisition(result, ownerId, source)
  } catch {
    console.error('[session-refresh] lease acquisition unavailable')
    return { status: 'unavailable' }
  }
}

async function settleRedisLease(
  id: string,
  expected: SessionRevision,
  lease: RefreshLease,
  action: 'success' | 'invalid' | 'unavailable',
  updated?: FullSession,
): Promise<RefreshSettlementResult> {
  const redis = getSessionStoreRedis()
  if (!redis) {
    if (action === 'success') {
      const slideTtlMs = slidingSessionTtlMs(resolveSessionIssuedAt(updated!))
      return replaceStoredSessionIfUnchanged(id, expected, updated!, slideTtlMs)
    }
    if (action === 'invalid') {
      return deleteStoredSessionIfUnchanged(id, expected)
    }
    return 'updated'
  }

  try {
    const slideTtlMs = updated
      ? slidingSessionTtlMs(resolveSessionIssuedAt(updated))
      : 0
    return mapSettlement(await redis.eval(
      SETTLE_REFRESH_LEASE_SCRIPT,
      3,
      sessionDataKey(id),
      refreshLeaseKey(id),
      refreshFenceKey(id),
      lease.ownerId,
      lease.fence,
      lease.source,
      expected.identityVersion,
      expected.userName,
      expected.accessToken,
      expected.refreshToken,
      expected.accessTokenExpiresAt,
      action,
      updated ? JSON.stringify(updated) : '',
      REFRESH_COOLDOWN_MS,
      slideTtlMs,
    ))
  } catch {
    console.error('[session-refresh] lease settlement unavailable')
    return 'unavailable'
  }
}

export function settleSessionRefreshSuccess(
  id: string,
  expected: SessionRevision,
  lease: RefreshLease,
  updated: FullSession,
): Promise<RefreshSettlementResult> {
  return settleRedisLease(id, expected, lease, 'success', updated)
}

export function settleSessionRefreshInvalidGrant(
  id: string,
  expected: SessionRevision,
  lease: RefreshLease,
): Promise<RefreshSettlementResult> {
  return settleRedisLease(id, expected, lease, 'invalid')
}

export function settleSessionRefreshUnavailable(
  id: string,
  expected: SessionRevision,
  lease: RefreshLease,
): Promise<RefreshSettlementResult> {
  return settleRedisLease(id, expected, lease, 'unavailable')
}
