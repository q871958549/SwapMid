import { createHash } from 'node:crypto'
import { Redis } from 'ioredis'
import type { FullSession } from './session-types'
import { SESSION_RETRY_AFTER_SECONDS } from './session-availability'
import { resolveSessionStoreConfiguration } from './session-store-config'
import {
  CONDITIONAL_SESSION_INVALIDATION_SCRIPT,
  CONDITIONAL_SESSION_MUTATION_SCRIPT,
  IDENTITY_SAFE_SESSION_MUTATION_SCRIPT,
} from './session-store-scripts'
import { SESSION_IDLE_TTL_MS } from './session-lifetime'

// 会话存储：生产必须显式 SESSION_STORE_MODE=redis 并配置 REDIS_URL；
// 进程内 Map 只允许本地开发/测试使用，不得成为生产漏配时的静默降级。
//
// 生产 compose 为 web 注入 REDIS_URL=redis://:密码@redis:6379/1（db1，与 ABP 缓存隔离）。

export type SessionLookupResult =
  | { status: 'found'; session: FullSession }
  | { status: 'missing' }
  | { status: 'unavailable'; retryAfterSeconds: number }

export type ValidSessionResult =
  | SessionLookupResult
  | {
      status: 'identity-changed'
      reason: 'impersonation-expired' | 'concurrent-session-change'
      session: FullSession
    }

export type ConditionalSessionMutationResult = 'updated' | 'missing' | 'conflict'
export type ConditionalSessionInvalidationResult =
  | ConditionalSessionMutationResult
  | 'refreshing'
export type IdentitySessionMutationResult =
  | ConditionalSessionMutationResult
  | 'refreshing'

export interface SessionRevision {
  /** 旧会话可能尚无 identityVersion；空串是受控的兼容哨兵。 */
  identityVersion: string
  userName: string
  accessToken: string
  /** 精确 refresh source 必须覆盖 refresh token；缺失时使用空串哨兵。 */
  refreshToken: string
  accessTokenExpiresAt: number
}

// Global singletons survive HMR in dev
declare global {
  var __swapmidSessionStore: Map<string, { data: FullSession; expiresAt: number }> | undefined
  // Dedupes concurrent token refreshes for the same session (refresh tokens are one-time-use)
  var __swapmidRefreshLocks: Map<string, Promise<ValidSessionResult>> | undefined
  var __swapmidRedis: Redis | undefined
}

const TTL_MS = SESSION_IDLE_TTL_MS
export const SESSION_STORE_COMMAND_TIMEOUT_MS = 1_500
const KEY_PREFIX = 'swapmid:sess:'
const SESSION_ID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

export function isValidSessionId(id: string): boolean {
  return SESSION_ID_PATTERN.test(id)
}

export function sessionDataKey(id: string): string {
  return KEY_PREFIX + id
}

export function refreshLeaseKey(id: string): string {
  return `${KEY_PREFIX}${id}:refresh:v1`
}

export function refreshFenceKey(id: string): string {
  return `${KEY_PREFIX}${id}:refresh:fence:v1`
}

export function sessionRevision(session: FullSession): SessionRevision {
  return {
    identityVersion: session.identityVersion ?? '',
    userName: session.userName,
    accessToken: session.accessToken,
    refreshToken: session.refreshToken ?? '',
    accessTokenExpiresAt: session.accessTokenExpiresAt,
  }
}

/**
 * 控制 key 只保存不可逆摘要，不复制 access/refresh token。Lua 仍对会话 JSON 做逐字段精确比较。
 */
export function sessionRevisionDigest(revision: SessionRevision): string {
  return createHash('sha256')
    .update(JSON.stringify([
      revision.identityVersion,
      revision.userName,
      revision.accessToken,
      revision.refreshToken,
      revision.accessTokenExpiresAt,
    ]))
    .digest('base64url')
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isPolicyMap(value: unknown): value is Record<string, boolean> {
  return isRecord(value) && Object.values(value).every(item => typeof item === 'boolean')
}

function isOptionalUserId(value: unknown): value is string | undefined {
  return value === undefined
    || (typeof value === 'string'
      && value.length > 0
      && value.length <= 256
      && value.trim() === value)
}

function isSessionSnapshot(value: unknown): value is NonNullable<FullSession['impersonator']> {
  if (!isRecord(value)) return false
  return isOptionalUserId(value.userId)
    && typeof value.userName === 'string'
    && typeof value.name === 'string'
    && isPolicyMap(value.grantedPolicies)
    && (value.identityVersion === undefined
      || (typeof value.identityVersion === 'string' && value.identityVersion.length <= 128))
    && typeof value.accessToken === 'string'
    && value.accessToken.length > 0
    && (value.refreshToken === undefined || typeof value.refreshToken === 'string')
    && typeof value.accessTokenExpiresAt === 'number'
    && Number.isFinite(value.accessTokenExpiresAt)
}

function isFullSession(value: unknown): value is FullSession {
  if (!isRecord(value)) return false
  return isOptionalUserId(value.userId)
    && typeof value.userName === 'string'
    && typeof value.name === 'string'
    && isPolicyMap(value.grantedPolicies)
    && (value.identityVersion === undefined
      || (typeof value.identityVersion === 'string' && value.identityVersion.length <= 128))
    && typeof value.accessToken === 'string'
    && value.accessToken.length > 0
    && (value.refreshToken === undefined || typeof value.refreshToken === 'string')
    && typeof value.accessTokenExpiresAt === 'number'
    && Number.isFinite(value.accessTokenExpiresAt)
    && (value.theme === undefined || typeof value.theme === 'string')
    && (value.fontSize === undefined
      || (typeof value.fontSize === 'number' && Number.isFinite(value.fontSize)))
    && (value.language === undefined || typeof value.language === 'string')
    && (value.impersonator === undefined || isSessionSnapshot(value.impersonator))
    && (value.issuedAt === undefined
      || (typeof value.issuedAt === 'number' && Number.isFinite(value.issuedAt)))
}

// ---------- Redis 后端（懒初始化；生产配置由租约启动包装器和 instrumentation 双重校验） ----------

export function getSessionStoreRedis(): Redis | null {
  const configuration = resolveSessionStoreConfiguration()
  if (configuration.mode === 'memory') return null
  const url = configuration.redisUrl!
  if (!global.__swapmidRedis) {
    global.__swapmidRedis = new Redis(url, {
      maxRetriesPerRequest: 2,
      // 命令失败由上层映射为 unavailable/503，绝不能伪装成“会话不存在”。
      connectTimeout: 3000,
      commandTimeout: SESSION_STORE_COMMAND_TIMEOUT_MS,
      lazyConnect: false,
    })
    global.__swapmidRedis.on('error', (e) => console.error('[session-store] redis error', e.message))
  }
  return global.__swapmidRedis
}

// ---------- 内存后端（开发兜底） ----------

const store: Map<string, { data: FullSession; expiresAt: number }> =
  global.__swapmidSessionStore ?? new Map()

global.__swapmidSessionStore = store

const refreshLocks: Map<string, Promise<ValidSessionResult>> =
  global.__swapmidRefreshLocks ?? new Map()

global.__swapmidRefreshLocks = refreshLocks

// ---------- 统一异步 API ----------

export async function storeSession(id: string, data: FullSession): Promise<void> {
  if (!isValidSessionId(id)) throw new TypeError('Invalid session id')
  const redis = getSessionStoreRedis()
  if (redis) {
    await redis.set(sessionDataKey(id), JSON.stringify(data), 'PX', TTL_MS)
    return
  }
  store.set(id, { data, expiresAt: Date.now() + TTL_MS })
}

export async function getStoredSession(id: string): Promise<SessionLookupResult> {
  if (!isValidSessionId(id)) return { status: 'missing' }
  const redis = getSessionStoreRedis()
  if (redis) {
    let raw: string | null
    try {
      raw = await redis.get(sessionDataKey(id))
    } catch {
      console.error('[session-store] redis get failed')
      return { status: 'unavailable', retryAfterSeconds: SESSION_RETRY_AFTER_SECONDS }
    }

    if (raw === null) return { status: 'missing' }

    try {
      const parsed: unknown = JSON.parse(raw)
      if (isFullSession(parsed)) return { status: 'found', session: parsed }
    } catch {
      // 统一落入下方损坏数据隔离。
    }

    // 单条损坏数据是该会话失效，不是 Redis 全局故障；不记录原文（内含令牌）。
    console.error('[session-store] corrupt session payload removed')
    void redis.del(
      sessionDataKey(id),
      refreshLeaseKey(id),
      refreshFenceKey(id),
    ).catch(() => {
      console.error('[session-store] corrupt session cleanup failed')
    })
    return { status: 'missing' }
  }
  const entry = store.get(id)
  if (!entry) return { status: 'missing' }
  if (Date.now() >= entry.expiresAt) {
    store.delete(id)
    return { status: 'missing' }
  }
  return { status: 'found', session: entry.data }
}

export function matchesRevision(data: FullSession, expected: SessionRevision): boolean {
  return (data.identityVersion ?? '') === expected.identityVersion
    && data.userName === expected.userName
    && data.accessToken === expected.accessToken
    && (data.refreshToken ?? '') === expected.refreshToken
    && data.accessTokenExpiresAt === expected.accessTokenExpiresAt
}

async function mutateStoredSessionIfUnchanged(
  id: string,
  expected: SessionRevision,
  data: FullSession | null,
  slideTtlMs?: number,
): Promise<ConditionalSessionMutationResult> {
  if (!isValidSessionId(id)) return 'missing'
  const observedRefreshLock = refreshLocks.get(id)
  const redis = getSessionStoreRedis()
  if (redis) {
    const result = await redis.eval(
      CONDITIONAL_SESSION_MUTATION_SCRIPT,
      3,
      sessionDataKey(id),
      refreshLeaseKey(id),
      refreshFenceKey(id),
      expected.identityVersion,
      expected.userName,
      expected.accessToken,
      expected.refreshToken,
      expected.accessTokenExpiresAt,
      data ? 'replace' : 'delete',
      data ? JSON.stringify(data) : '',
    )
    if (Number(result) === 1) {
      if (observedRefreshLock) clearRefreshLock(id, observedRefreshLock)
      return 'updated'
    }
    if (Number(result) === 0) return 'missing'
    return 'conflict'
  }

  const entry = store.get(id)
  if (!entry || Date.now() >= entry.expiresAt) {
    if (entry) store.delete(id)
    return 'missing'
  }
  if (!matchesRevision(entry.data, expected)) return 'conflict'

  if (observedRefreshLock) clearRefreshLock(id, observedRefreshLock)
  if (data) {
    entry.data = data
    if (slideTtlMs !== undefined) {
      if (slideTtlMs <= 0) {
        store.delete(id)
        return 'missing'
      }
      entry.expiresAt = Date.now() + slideTtlMs
    }
  }
  else store.delete(id)
  return 'updated'
}

/**
 * 身份切换与 token 轮换必须使用 compare-and-swap：
 * 外部请求发生期间若另一标签页已改变身份/令牌，旧请求不得覆盖新会话。
 */
export async function replaceStoredSessionIfUnchanged(
  id: string,
  expected: SessionRevision,
  data: FullSession,
  slideTtlMs?: number,
): Promise<ConditionalSessionMutationResult> {
  return mutateStoredSessionIfUnchanged(id, expected, data, slideTtlMs)
}

/**
 * 权威删除（当前仅显式退出）会同步取消刷新 lease；迟到 owner 必须因 fence/owner 失配而失败。
 * invalid_grant 不得调用本函数，只能走 session-refresh-protocol 的 owner settle。
 */
export async function deleteStoredSessionIfUnchanged(
  id: string,
  expected: SessionRevision,
): Promise<ConditionalSessionMutationResult> {
  return mutateStoredSessionIfUnchanged(id, expected, null)
}

/**
 * 后端迟到 401 不能删除仍有 owner 正在刷新的同一 source；调用方应等待 single-flight 结果。
 */
export async function invalidateStoredSessionIfUnchanged(
  id: string,
  expected: SessionRevision,
): Promise<ConditionalSessionInvalidationResult> {
  if (!isValidSessionId(id)) return 'missing'
  const observedRefreshLock = refreshLocks.get(id)
  const redis = getSessionStoreRedis()
  if (redis) {
    const result = Number(await redis.eval(
      CONDITIONAL_SESSION_INVALIDATION_SCRIPT,
      3,
      sessionDataKey(id),
      refreshLeaseKey(id),
      refreshFenceKey(id),
      expected.identityVersion,
      expected.userName,
      expected.accessToken,
      expected.refreshToken,
      expected.accessTokenExpiresAt,
      sessionRevisionDigest(expected),
    ))
    if (result === 2) return 'refreshing'
    if (result === 1) {
      if (observedRefreshLock) clearRefreshLock(id, observedRefreshLock)
      return 'updated'
    }
    if (result === 0) return 'missing'
    return 'conflict'
  }

  const entry = store.get(id)
  if (!entry || Date.now() >= entry.expiresAt) {
    if (entry) store.delete(id)
    return 'missing'
  }
  if (!matchesRevision(entry.data, expected)) return 'conflict'
  if (refreshLocks.has(id)) return 'refreshing'
  store.delete(id)
  return 'updated'
}

/**
 * 身份切换不能取消已经把 refresh token 交给 OAuth 的 owner；先返回 refreshing，
 * 调用方强制 join 胜者后，必须用胜者的新 revision 重建身份快照再有限重试。
 */
export async function replaceIdentitySessionIfUnchanged(
  id: string,
  expected: SessionRevision,
  data: FullSession,
): Promise<IdentitySessionMutationResult> {
  if (!isValidSessionId(id)) return 'missing'
  const observedRefreshLock = refreshLocks.get(id)
  const redis = getSessionStoreRedis()
  if (redis) {
    const result = Number(await redis.eval(
      IDENTITY_SAFE_SESSION_MUTATION_SCRIPT,
      3,
      sessionDataKey(id),
      refreshLeaseKey(id),
      refreshFenceKey(id),
      expected.identityVersion,
      expected.userName,
      expected.accessToken,
      expected.refreshToken,
      expected.accessTokenExpiresAt,
      sessionRevisionDigest(expected),
      JSON.stringify(data),
    ))
    if (result === 2) return 'refreshing'
    if (result === 1) {
      if (observedRefreshLock) clearRefreshLock(id, observedRefreshLock)
      return 'updated'
    }
    if (result === 0) return 'missing'
    return 'conflict'
  }

  const entry = store.get(id)
  if (!entry || Date.now() >= entry.expiresAt) {
    if (entry) store.delete(id)
    return 'missing'
  }
  if (!matchesRevision(entry.data, expected)) return 'conflict'
  if (refreshLocks.has(id)) return 'refreshing'
  entry.data = data
  return 'updated'
}

export async function deleteStoredSession(id: string): Promise<void> {
  if (!isValidSessionId(id)) return
  refreshLocks.delete(id)
  const redis = getSessionStoreRedis()
  if (redis) {
    await redis.del(
      sessionDataKey(id),
      refreshLeaseKey(id),
      refreshFenceKey(id),
    )
    return
  }
  store.delete(id)
}

/** 只供 memory 模式的 single-flight TTL 守卫；Redis 由 acquire Lua 原子读取 PTTL。 */
export function getMemorySessionRemainingTtl(id: string): number | null {
  if (getSessionStoreRedis()) return null
  const entry = store.get(id)
  if (!entry) return 0
  return Math.max(0, entry.expiresAt - Date.now())
}

// 进程内锁只是 L1 合并；Redis owner/fence/source lease 才承担跨进程正确性。
export function getRefreshLock(id: string) {
  return refreshLocks.get(id)
}

export function setRefreshLock(id: string, p: Promise<ValidSessionResult>) {
  refreshLocks.set(id, p)
}

export function clearRefreshLock(
  id: string,
  expected: Promise<ValidSessionResult>,
): boolean {
  if (refreshLocks.get(id) !== expected) return false
  return refreshLocks.delete(id)
}
