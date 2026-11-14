import { type NextRequest, NextResponse } from 'next/server'
import { isSafeInternalPath } from '@/lib/safe-internal-path'
import { getSessionResult } from '@/lib/session'
import {
  SESSION_IDENTITY_VERSION_QUERY,
  SESSION_STATE_UNAVAILABLE,
} from '@/lib/session-availability'

export const dynamic = 'force-dynamic'

function safeReturnLocation(redirectTo: string | null): string {
  return isSafeInternalPath(redirectTo) ? redirectTo : '/dashboard'
}

function loginLocation(redirectTo: string | null): string {
  return isSafeInternalPath(redirectTo)
    ? `/login?redirect=${encodeURIComponent(redirectTo)}`
    : '/login'
}

export async function GET(req: NextRequest) {
  const redirectTo = req.nextUrl.searchParams.get('redirect')
  const expectedVersions = req.nextUrl.searchParams.getAll(
    SESSION_IDENTITY_VERSION_QUERY,
  )
  if (expectedVersions.length > 1) {
    return NextResponse.json(
      { error: 'Invalid identity precondition' },
      {
        status: 400,
        headers: { 'Cache-Control': 'private, no-store' },
      },
    )
  }

  const sessionResult = await getSessionResult()
  if (sessionResult.status === 'unavailable') {
    const search = isSafeInternalPath(redirectTo)
      ? `?redirect=${encodeURIComponent(redirectTo)}`
      : ''
    return new NextResponse(null, {
      status: 307,
      headers: {
        Location: `/api/auth/session-unavailable${search}`,
        'Cache-Control': 'private, no-store',
        'X-SwapMid-Session-State': SESSION_STATE_UNAVAILABLE,
      },
    })
  }

  if (sessionResult.status === 'found') {
    return new NextResponse(null, {
      status: 307,
      headers: {
        Location: safeReturnLocation(redirectTo),
        'Cache-Control': 'private, no-store',
      },
    })
  }

  return new NextResponse(null, {
    status: 307,
    headers: {
      Location: loginLocation(redirectTo),
      'Cache-Control': 'private, no-store',
    },
  })
}
