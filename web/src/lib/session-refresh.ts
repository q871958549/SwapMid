import 'server-only'

import type { FullSession } from './session-types'
import {
  acquireSessionRefreshLease,
  settleSessionRefreshInvalidGrant,
  settleSessionRefreshSuccess,
  settleSessionRefreshUnavailable,
  type RefreshLease,
  type RefreshSettlementResult,
} from './session-refresh-protocol'
import {
  SESSION_STORE_COMMAND_TIMEOUT_MS,
  sessionRevision,
  type SessionRevision,
} from './session-store'
import {
  SERVER_BACKEND_DEADLINE_MS,
  SERVER_BACKEND_RESPONSE_LIMIT_BYTES,
  serverBackendFetch,
} from './server-backend-fetch'
import { resolveSessionIssuedAt } from './session-lifetime'

import { getWebOAuthClientId } from './site-instance'
const REFRESH_WAITER_BUDGET_MS = 10_000
const REFRESH_WAITER_MIN_POLL_MS = 50
const REFRESH_WAITER_MAX_POLL_MS = 250
export const REFRESH_SETTLEMENT_RESERVE_MS = 1_750

export type SessionRefreshResult =
  | { status: 'updated'; session: FullSession }
  | { status: 'changed' }
  | { status: 'missing' }
  | { status: 'unavailable' }

type RefreshSettlementObservation = RefreshSettlementResult | 'deadline'

function waitDuration(maximum: number): number {
  const jitter = REFRESH_WAITER_MIN_POLL_MS + Math.floor(
    Math.random() * (REFRESH_WAITER_MAX_POLL_MS - REFRESH_WAITER_MIN_POLL_MS + 1),
  )
  return Math.max(1, Math.min(jitter, maximum))
}

function sleep(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms))
}

export type SessionRefreshDeadlineWait<T> =
  | { status: 'completed'; value: T }
  | { status: 'timed-out' }

/**
 * Forced join 等待普通 L1 owner 时也受同一个绝对 deadline 约束。
 * timeout 胜出后仍保留 pending 上的 rejection handler，原 owner 可安全完成 fenced settle。
 */
export async function waitForSessionRefreshDeadline<T>(
  pending: Promise<T>,
  waitDeadline: number,
): Promise<SessionRefreshDeadlineWait<T>> {
  const remaining = waitDeadline - Date.now()
  if (remaining <= 0) return { status: 'timed-out' }

  let timeout: ReturnType<typeof setTimeout> | undefined
  try {
    return await Promise.race([
      pending.then(value => ({ status: 'completed' as const, value })),
      new Promise<{ status: 'timed-out' }>((resolve) => {
        timeout = setTimeout(() => resolve({ status: 'timed-out' }), remaining)
      }),
    ])
  } finally {
    if (timeout !== undefined) clearTimeout(timeout)
  }
}

function mapSuccessSettlement(
  settlement: RefreshSettlementObservation,
  updated: FullSession,
): SessionRefreshResult {
  if (settlement === 'updated') return { status: 'updated', session: updated }
  if (settlement === 'missing') return { status: 'missing' }
  if (settlement === 'conflict') return { status: 'changed' }
  return { status: 'unavailable' }
}

function logOAuthSuccessSettlementDegradation(
  settlement: RefreshSettlementObservation,
): void {
  if (
    settlement !== 'lost'
    && settlement !== 'unavailable'
    && settlement !== 'deadline'
  ) return
  console.error(
    '[session-refresh-event]',
    JSON.stringify({
      event: 'oauth_success_settlement_degraded',
      settlement,
      consequence: 'reauthentication_may_be_required',
    }),
  )
}

function mapInvalidSettlement(
  settlement: RefreshSettlementObservation,
): SessionRefreshResult {
  if (settlement === 'updated' || settlement === 'missing') {
    return { status: 'missing' }
  }
  if (settlement === 'conflict') return { status: 'changed' }
  return { status: 'unavailable' }
}

function mapUnavailableSettlement(
  settlement: RefreshSettlementObservation,
): SessionRefreshResult {
  if (settlement === 'missing') return { status: 'missing' }
  if (settlement === 'conflict') return { status: 'changed' }
  return { status: 'unavailable' }
}

async function observeSettlementBeforeDeadline(
  settlement: Promise<RefreshSettlementResult>,
  waitDeadline: number,
): Promise<RefreshSettlementObservation> {
  const observed = await waitForSessionRefreshDeadline(settlement, waitDeadline)
  // Promise.race 已在 settlement 上保留完成/拒绝处理器；超时只结束当前调用，
  // fenced Lua 仍会在后台完成，绝不能因响应 deadline 放弃已取得的 owner lease。
  return observed.status === 'completed' ? observed.value : 'deadline'
}

async function settleUnavailable(
  sid: string,
  expected: SessionRevision,
  lease: RefreshLease,
  waitDeadline: number,
): Promise<SessionRefreshResult> {
  return mapUnavailableSettlement(
    await observeSettlementBeforeDeadline(
      settleSessionRefreshUnavailable(sid, expected, lease),
      waitDeadline,
    ),
  )
}

async function refreshAsOwner(
  sid: string,
  session: FullSession,
  expected: SessionRevision,
  lease: RefreshLease,
  oauthDeadlineMs: number,
  waitDeadline: number,
): Promise<SessionRefreshResult> {
  let response: Response
  try {
    response = await serverBackendFetch('/connect/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'refresh_token',
        client_id: getWebOAuthClientId(),
        refresh_token: session.refreshToken!,
        scope: 'SwapMid offline_access',
      }),
      deadlineMs: oauthDeadlineMs,
      maxResponseBytes: SERVER_BACKEND_RESPONSE_LIMIT_BYTES.sessionRefresh,
    })
  } catch {
    console.error('[session] refresh request unavailable')
    return settleUnavailable(sid, expected, lease, waitDeadline)
  }

  if (!response.ok) {
    let oauthError: string | undefined
    try {
      const body = JSON.parse(await response.text()) as { error?: unknown }
      if (typeof body.error === 'string') oauthError = body.error
    } catch {
      // 非结构化错误不能证明 refresh token 已失效。
    }

    if (oauthError === 'invalid_grant') {
      return mapInvalidSettlement(
        await observeSettlementBeforeDeadline(
          settleSessionRefreshInvalidGrant(sid, expected, lease),
          waitDeadline,
        ),
      )
    }

    console.error(
      '[session] refresh endpoint unavailable',
      response.status,
      oauthError === undefined ? 'unstructured-error' : 'structured-error',
    )
    return settleUnavailable(sid, expected, lease, waitDeadline)
  }

  let tokenJson: {
    access_token?: unknown
    refresh_token?: unknown
    expires_in?: unknown
  }
  try {
    tokenJson = await response.json()
  } catch {
    console.error('[session] refresh response was not valid JSON')
    return settleUnavailable(sid, expected, lease, waitDeadline)
  }

  if (typeof tokenJson.access_token !== 'string' || !tokenJson.access_token) {
    console.error('[session] refresh response missing access_token')
    return settleUnavailable(sid, expected, lease, waitDeadline)
  }

  const expiresIn = typeof tokenJson.expires_in === 'number'
    && Number.isFinite(tokenJson.expires_in)
    && tokenJson.expires_in > 0
    ? tokenJson.expires_in
    : 3600
  const updated: FullSession = {
    ...session,
    accessToken: tokenJson.access_token,
    refreshToken: typeof tokenJson.refresh_token === 'string' && tokenJson.refresh_token
      ? tokenJson.refresh_token
      : session.refreshToken,
    accessTokenExpiresAt: Date.now() + expiresIn * 1000,
    issuedAt: resolveSessionIssuedAt(session),
  }

  const settlement = await observeSettlementBeforeDeadline(
    settleSessionRefreshSuccess(sid, expected, lease, updated),
    waitDeadline,
  )
  logOAuthSuccessSettlementDegradation(settlement)
  return mapSuccessSettlement(settlement, updated)
}

async function coordinateSessionRefresh(
  sid: string,
  session: FullSession,
  waitThroughCooldown: boolean,
  waitDeadline = Date.now() + REFRESH_WAITER_BUDGET_MS,
): Promise<SessionRefreshResult> {
  const expected = sessionRevision(session)
  const ownerId = crypto.randomUUID()

  while (true) {
    const remainingBeforeAcquire = waitDeadline - Date.now()
    if (remainingBeforeAcquire <= 0) return { status: 'unavailable' }
    const allowOwnership = remainingBeforeAcquire
      > SESSION_STORE_COMMAND_TIMEOUT_MS + REFRESH_SETTLEMENT_RESERVE_MS
    const acquisitionPromise = acquireSessionRefreshLease(
      sid,
      expected,
      ownerId,
      allowOwnership,
    )
    let acquisition
    if (allowOwnership) {
      acquisition = await acquisitionPromise
    } else {
      const observed = await waitForSessionRefreshDeadline(
        acquisitionPromise,
        waitDeadline,
      )
      if (observed.status === 'timed-out') return { status: 'unavailable' }
      acquisition = observed.value
    }
    if (acquisition.status === 'budget-exhausted') {
      return { status: 'unavailable' }
    }
    if (acquisition.status === 'owner') {
      const ownerRemaining = waitDeadline - Date.now()
      if (ownerRemaining <= REFRESH_SETTLEMENT_RESERVE_MS) {
        // 已持有 owner lease 就不能裸返回；即使调用预算刚耗尽，也要 fenced settle
        // unavailable，让等待者看到短 cooldown，而不是悬挂到 15 秒 lease 自然过期。
        return settleUnavailable(sid, expected, acquisition.lease, waitDeadline)
      }
      const oauthDeadlineMs = Math.max(
        1,
        Math.min(
          SERVER_BACKEND_DEADLINE_MS.sessionRefresh,
          ownerRemaining - REFRESH_SETTLEMENT_RESERVE_MS,
        ),
      )
      return refreshAsOwner(
        sid,
        session,
        expected,
        acquisition.lease,
        oauthDeadlineMs,
        waitDeadline,
      )
    }
    if (acquisition.status === 'changed') return { status: 'changed' }
    if (acquisition.status === 'missing') return { status: 'missing' }
    if (acquisition.status === 'unavailable') {
      return { status: 'unavailable' }
    }

    const remaining = waitDeadline - Date.now()
    if (remaining <= 0) return { status: 'unavailable' }
    if (acquisition.status === 'cooldown' && !waitThroughCooldown) {
      return { status: 'unavailable' }
    }
    await sleep(waitDuration(Math.min(remaining, acquisition.retryAfterMs)))
  }
}

/**
 * Redis owner/fence/source 是跨进程正确性边界；调用方的进程内 Promise Map 只做 L1 合并。
 */
export function refreshSession(
  sid: string,
  session: FullSession,
): Promise<SessionRefreshResult> {
  return coordinateSessionRefresh(sid, session, false)
}

/**
 * 身份切换与迟到 401 已确认同 source 存在远端 owner，必须绕过本地 expiresAt 快路径；
 * cooldown 结束后也应在同一有界预算内 takeover，而不是返回旧 token。
 */
export function forceJoinSessionRefresh(
  sid: string,
  session: FullSession,
  waitDeadline = createSessionRefreshWaitDeadline(),
): Promise<SessionRefreshResult> {
  return coordinateSessionRefresh(sid, session, true, waitDeadline)
}

/**
 * 本地普通刷新失败后升级为 forced join 时，必须沿用调用方最初的等待预算，
 * 不能每经过一层 L1 Promise 就重新获得完整 10 秒。
 */
export function createSessionRefreshWaitDeadline(): number {
  return Date.now() + REFRESH_WAITER_BUDGET_MS
}
