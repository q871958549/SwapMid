import { type NextRequest, NextResponse } from 'next/server'
import { deleteSession } from '@/lib/session'
import {
  SESSION_IDENTITY_CHANGED_CODE,
  SESSION_IDENTITY_PRECONDITION_CODE,
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_UNAVAILABLE_CODE,
  sessionIdentityChangedHeaders,
  sessionUnavailableHeaders,
} from '@/lib/session-availability'

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
    const result = await deleteSession(expectedIdentityVersion)
    if (result.status === 'identity-changed') {
      return NextResponse.json(
        { error: 'Conflict', code: SESSION_IDENTITY_CHANGED_CODE },
        {
          status: 409,
          headers: sessionIdentityChangedHeaders(result.identityVersion),
        },
      )
    }
    if (result.status === 'unavailable') {
      return NextResponse.json(
        { error: 'Service Unavailable', code: SESSION_UNAVAILABLE_CODE },
        {
          status: 503,
          headers: sessionUnavailableHeaders(result.retryAfterSeconds),
        },
      )
    }

    return NextResponse.json(
      { ok: true },
      { headers: { 'Cache-Control': 'private, no-store' } },
    )
  } catch {
    return NextResponse.json(
      { error: 'Conflict', code: 'SESSION_CONCURRENT_MUTATION' },
      {
        status: 409,
        headers: { 'Cache-Control': 'private, no-store' },
      },
    )
  }
}
