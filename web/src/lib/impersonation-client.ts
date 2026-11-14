'use client'

import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
  SESSION_STATE_UNAVAILABLE,
} from '@/lib/session-availability'

export const START_IMPERSONATION_PATH = '/api/auth/start-impersonation'
export const STOP_IMPERSONATION_PATH = '/api/auth/stop-impersonation'

export type StopImpersonationClientResult =
  | { status: 'switched'; identityVersion?: string }
  | { status: 'failed'; errorKey: 'session-expired' | 'stop-failed' | 'unavailable' }

export type StartImpersonationClientResult =
  | { status: 'started'; identityVersion?: string }
  | {
    status: 'failed'
    errorKey:
      | 'session-expired'
      | 'already-impersonating'
      | 'failed'
      | 'network'
      | 'unavailable'
  }

interface ImpersonationPayload {
  ok?: unknown
  identityChanged?: unknown
  identityVersion?: unknown
  error?: unknown
}

function asIdentityVersion(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined
}

function parsePayload(value: unknown): ImpersonationPayload {
  return typeof value === 'object' && value !== null
    ? value as ImpersonationPayload
    : {}
}

async function readPayload(response: Response): Promise<ImpersonationPayload | null> {
  try {
    return parsePayload(await response.json())
  } catch {
    return null
  }
}

function isUnavailable(response: Response): boolean {
  return response.status === 503
    && response.headers.get(SESSION_STATE_HEADER) === SESSION_STATE_UNAVAILABLE
}

/**
 * 账号页「以此账号登录」的唯一出口。走 `/api/auth/start-impersonation`，
 * 不走 Server Action，避免当前业务页 RSC 重渲把换身份结果打飞。
 */
export async function requestStartImpersonation(
  targetUserId: string,
  identityVersion: string,
): Promise<StartImpersonationClientResult> {
  const response = await fetch(START_IMPERSONATION_PATH, {
    method: 'POST',
    cache: 'no-store',
    headers: {
      'Content-Type': 'application/json',
      [SESSION_IDENTITY_VERSION_HEADER]: identityVersion,
    },
    body: JSON.stringify({ targetUserId }),
  })

  if (isUnavailable(response)) {
    return { status: 'failed', errorKey: 'unavailable' }
  }
  if (response.status === 401 || response.status === 428) {
    return { status: 'failed', errorKey: 'session-expired' }
  }

  const payload = await readPayload(response)
  if (!payload) {
    return { status: 'failed', errorKey: 'network' }
  }
  if (payload.ok === true || payload.identityChanged === true) {
    return {
      status: 'started',
      identityVersion: asIdentityVersion(payload.identityVersion),
    }
  }
  if (payload.error === 'session-expired') {
    return { status: 'failed', errorKey: 'session-expired' }
  }
  if (payload.error === 'already-impersonating') {
    return { status: 'failed', errorKey: 'already-impersonating' }
  }
  if (payload.error === 'unavailable') {
    return { status: 'failed', errorKey: 'unavailable' }
  }
  if (payload.error === 'network') {
    return { status: 'failed', errorKey: 'network' }
  }
  return { status: 'failed', errorKey: 'failed' }
}

/**
 * 横幅「切回我的账号」的唯一出口。走 `/api/auth/stop-impersonation`，
 * 不走 Server Action，避免当前业务页 RSC 重渲把切回结果打飞。
 */
export async function requestStopImpersonation(
  identityVersion: string,
): Promise<StopImpersonationClientResult> {
  const response = await fetch(STOP_IMPERSONATION_PATH, {
    method: 'POST',
    cache: 'no-store',
    headers: { [SESSION_IDENTITY_VERSION_HEADER]: identityVersion },
  })

  if (isUnavailable(response)) {
    return { status: 'failed', errorKey: 'unavailable' }
  }
  if (response.status === 401 || response.status === 428) {
    return { status: 'failed', errorKey: 'session-expired' }
  }

  const payload = await readPayload(response)
  if (!payload) {
    return { status: 'failed', errorKey: 'stop-failed' }
  }
  if (payload.ok === true || payload.identityChanged === true) {
    return {
      status: 'switched',
      identityVersion: asIdentityVersion(payload.identityVersion),
    }
  }
  if (payload.error === 'session-expired') {
    return { status: 'failed', errorKey: 'session-expired' }
  }
  if (payload.error === 'unavailable') {
    return { status: 'failed', errorKey: 'unavailable' }
  }
  return { status: 'failed', errorKey: 'stop-failed' }
}
