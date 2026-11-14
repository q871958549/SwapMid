import { type NextRequest, NextResponse } from 'next/server'
import {
  getValidSessionResult,
  SessionIdentityChangedError,
  startImpersonation,
} from '@/lib/session'
import { fetchAbpUserConfig } from '@/lib/abp-config'
import { getSessionIdentityVersion } from '@/lib/session-identity'
import {
  SESSION_IDENTITY_PRECONDITION_CODE,
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_UNAVAILABLE_CODE,
  sessionMissingHeaders,
  sessionUnavailableHeaders,
} from '@/lib/session-availability'
import {
  SERVER_BACKEND_DEADLINE_MS,
  SERVER_BACKEND_RESPONSE_LIMIT_BYTES,
  serverBackendFetch,
} from '@/lib/server-backend-fetch'

const CLIENT_ID = 'SwapMid_App'
const MAX_TARGET_USER_ID_LENGTH = 64

/**
 * 账号页「以此账号登录」。必须是独立 Route Handler，不能挂在当前业务页的 Server Action 上：
 * Action 飞行会重渲 layout/列表，旧 `X-SwapMid-Identity-Version` 与已切换会话冲突。
 * 本路径在 Caddy `/api/auth/*` 豁免内，也不经 `proxy.ts` 的 next-action 409 围栏。
 *
 * 身份已变返回 200 + identityChanged，避免 SessionGuard 把 409 当成停在当前页的硬刷新。
 * 真正的权限边界在后端 Impersonation grant；这里只换 token、落同一 sid。
 */
export async function POST(req: NextRequest) {
  const expectedIdentityVersion = req.headers.get(
    SESSION_IDENTITY_VERSION_HEADER,
  )
  if (!expectedIdentityVersion) {
    return NextResponse.json(
      {
        error: 'Precondition Required',
        code: SESSION_IDENTITY_PRECONDITION_CODE,
      },
      {
        status: 428,
        headers: { 'Cache-Control': 'private, no-store' },
      },
    )
  }

  const targetUserId = await readTargetUserId(req)
  if (!targetUserId) {
    return jsonError('failed')
  }

  try {
    const sessionResult = await getValidSessionResult()
    if (sessionResult.status === 'missing') {
      return sessionExpired()
    }
    if (sessionResult.status === 'unavailable') {
      return storageUnavailable()
    }
    if (sessionResult.status === 'identity-changed') {
      return identityChanged(getSessionIdentityVersion(sessionResult.session))
    }

    const session = sessionResult.session
    if (session.impersonator) {
      return jsonError('already-impersonating')
    }

    const tokenRes = await serverBackendFetch('/connect/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'Impersonation',
        client_id: CLIENT_ID,
        access_token: session.accessToken,
        target_user_id: targetUserId,
        // ⚠️ 刻意不含 offline_access——即使这里手滑加了，grant 内也会强制剔除（安全红线②，双保险）。
        scope: 'SwapMid',
      }),
      deadlineMs: SERVER_BACKEND_DEADLINE_MS.authentication,
      maxResponseBytes: SERVER_BACKEND_RESPONSE_LIMIT_BYTES.oauthToken,
    })

    if (!tokenRes.ok) {
      const body = await tokenRes.text()
      let oauthError = 'unknown'
      try {
        const parsed = JSON.parse(body) as { error?: unknown }
        if (typeof parsed.error === 'string') oauthError = parsed.error
      } catch {
        // 非结构化身份服务错误不进入日志，避免误回显的内部信息或凭据落盘。
      }
      console.error('[impersonation] token error', tokenRes.status, oauthError)
      return jsonError('failed')
    }

    const tokenJson: { access_token: string; expires_in?: number } = await tokenRes.json()
    const userConfig = await fetchAbpUserConfig(tokenJson.access_token)
    const next = await startImpersonation({
      userId: userConfig.userId,
      userName: userConfig.userName,
      name: userConfig.name,
      grantedPolicies: userConfig.grantedPolicies,
      accessToken: tokenJson.access_token,
      accessTokenExpiresAt: Date.now() + (tokenJson.expires_in ?? 3600) * 1000,
    }, expectedIdentityVersion)

    const identityVersion = getSessionIdentityVersion(next)
    return NextResponse.json(
      { ok: true, identityVersion },
      {
        headers: {
          'Cache-Control': 'private, no-store',
          [SESSION_IDENTITY_VERSION_HEADER]: identityVersion,
        },
      },
    )
  } catch (error) {
    if (error instanceof SessionIdentityChangedError) {
      return identityChanged(error.identityVersion)
    }
    if (error instanceof Error && error.message === 'SESSION_MISSING') {
      return sessionExpired()
    }
    if (error instanceof Error && error.message === 'SESSION_UNAVAILABLE') {
      return storageUnavailable()
    }
    if (error instanceof Error && error.message === 'IMPERSONATION_ALREADY_ACTIVE') {
      return jsonError('already-impersonating')
    }
    console.error('[impersonation] start caught error', error)
    return jsonError('network')
  }
}

async function readTargetUserId(req: NextRequest): Promise<string | null> {
  let body: unknown
  try {
    body = await req.json()
  } catch {
    return null
  }
  if (typeof body !== 'object' || body === null) return null
  const raw = (body as { targetUserId?: unknown }).targetUserId
  if (typeof raw !== 'string') return null
  const targetUserId = raw.trim()
  if (targetUserId.length === 0 || targetUserId.length > MAX_TARGET_USER_ID_LENGTH) {
    return null
  }
  return targetUserId
}

function jsonError(error: 'failed' | 'network' | 'already-impersonating') {
  return NextResponse.json(
    { error },
    {
      status: 200,
      headers: { 'Cache-Control': 'private, no-store' },
    },
  )
}

function identityChanged(identityVersion: string) {
  return NextResponse.json(
    { identityChanged: true, identityVersion },
    {
      status: 200,
      headers: {
        'Cache-Control': 'private, no-store',
        [SESSION_IDENTITY_VERSION_HEADER]: identityVersion,
      },
    },
  )
}

function sessionExpired() {
  return NextResponse.json(
    { error: 'session-expired' },
    {
      status: 401,
      headers: {
        'Cache-Control': 'private, no-store',
        ...sessionMissingHeaders(),
      },
    },
  )
}

function storageUnavailable() {
  return NextResponse.json(
    { error: 'unavailable', code: SESSION_UNAVAILABLE_CODE },
    {
      status: 503,
      headers: sessionUnavailableHeaders(),
    },
  )
}
