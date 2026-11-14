import { type NextRequest, NextResponse } from 'next/server'
import {
  SessionIdentityChangedError,
  stopImpersonation,
} from '@/lib/session'
import { getSessionIdentityVersion } from '@/lib/session-identity'
import {
  SESSION_IDENTITY_PRECONDITION_CODE,
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_UNAVAILABLE_CODE,
  sessionMissingHeaders,
  sessionUnavailableHeaders,
} from '@/lib/session-availability'

/**
 * 切回管理员原身份。必须是独立 Route Handler，不能挂在当前业务页的 Server Action 上：
 * Action 飞行会重渲 layout/样品页，旧 `X-SwapMid-Identity-Version` 与已还原会话冲突，
 * 再被横幅 catch 显示成「切回失败」。本路径在 Caddy `/api/auth/*` 豁免内，也不经
 * `proxy.ts` 的 next-action 409 围栏。
 *
 * 身份已变（含自动切回已完成）返回 200 + identityChanged，避免 SessionGuard 把 409
 * 当成需要停留在当前页的硬刷新。
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

  try {
    const restored = await stopImpersonation(expectedIdentityVersion)
    if (!restored) {
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

    const identityVersion = getSessionIdentityVersion(restored)
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
      return NextResponse.json(
        {
          identityChanged: true,
          identityVersion: error.identityVersion,
        },
        {
          status: 200,
          headers: {
            'Cache-Control': 'private, no-store',
            [SESSION_IDENTITY_VERSION_HEADER]: error.identityVersion,
          },
        },
      )
    }
    if (error instanceof Error && error.message === 'SESSION_UNAVAILABLE') {
      return NextResponse.json(
        { error: 'unavailable', code: SESSION_UNAVAILABLE_CODE },
        {
          status: 503,
          headers: sessionUnavailableHeaders(),
        },
      )
    }
    console.error('[impersonation] stop caught error', error)
    return NextResponse.json(
      { error: 'stop-failed' },
      {
        status: 200,
        headers: { 'Cache-Control': 'private, no-store' },
      },
    )
  }
}
