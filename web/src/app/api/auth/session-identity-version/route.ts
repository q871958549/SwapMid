import { NextResponse } from 'next/server'
import { getValidSessionResult } from '@/lib/session'
import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_UNAVAILABLE_CODE,
  sessionUnavailableHeaders,
} from '@/lib/session-availability'
import { getSessionIdentityVersion } from '@/lib/session-identity'

export async function GET() {
  const sessionResult = await getValidSessionResult()
  if (sessionResult.status === 'missing') {
    return NextResponse.json(
      { error: 'Unauthorized' },
      {
        status: 401,
        headers: { 'Cache-Control': 'private, no-store' },
      },
    )
  }
  if (sessionResult.status === 'unavailable') {
    return NextResponse.json(
      { error: 'Service Unavailable', code: SESSION_UNAVAILABLE_CODE },
      {
        status: 503,
        headers: sessionUnavailableHeaders(sessionResult.retryAfterSeconds),
      },
    )
  }

  // identity-changed 表示本次读取刚完成了安全的自动切回；其 session 已是当前权威身份。
  const identityVersion = getSessionIdentityVersion(sessionResult.session)
  return NextResponse.json(
    { identityVersion },
    {
      headers: {
        'Cache-Control': 'private, no-store',
        [SESSION_IDENTITY_VERSION_HEADER]: identityVersion,
      },
    },
  )
}
