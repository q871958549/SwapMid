import { NextResponse, type NextRequest } from 'next/server'
import { getSessionCookieName } from '@/lib/site-instance'
import { getStoredSession, isValidSessionId } from '@/lib/session-store'
import { getSessionIdentityVersion } from '@/lib/session-identity'
import {
  SESSION_IDENTITY_CHANGED_CODE,
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_UNAVAILABLE_CODE,
  sessionIdentityChangedHeaders,
  sessionUnavailableHeaders,
} from '@/lib/session-availability'

const PUBLIC_PATHS = ['/login', '/build-meta']
const STATIC_EXTENSION = /\.(?:svg|png|jpg|jpeg|gif|webp|ico|txt|xml|woff2?)$/i

function isRscPartialRequest(request: NextRequest): boolean {
  return (
    request.headers.get('rsc') === '1'
    || request.headers.has('next-router-state-tree')
    || request.headers.has('next-action')
  )
}

function withPathnameHeader(request: NextRequest): NextResponse {
  const requestHeaders = new Headers(request.headers)
  requestHeaders.set('x-swapmid-pathname', request.nextUrl.pathname)
  return NextResponse.next({ request: { headers: requestHeaders } })
}

export async function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl

  if (STATIC_EXTENSION.test(pathname) || pathname.startsWith('/api/auth/')) {
    return NextResponse.next()
  }

  const isPublic = PUBLIC_PATHS.some(
    p => pathname === p || pathname.startsWith(p + '/'),
  )
  const rawSessionCookie = request.cookies.get(getSessionCookieName())?.value
  const sid = rawSessionCookie && isValidSessionId(rawSessionCookie)
    ? rawSessionCookie
    : undefined

  if (!sid && !isPublic) {
    const loginUrl = new URL('/login', request.url)
    const returnTo = pathname + search
    if (returnTo && returnTo !== '/' && returnTo !== '/dashboard') {
      loginUrl.searchParams.set('redirect', returnTo)
    }
    return NextResponse.redirect(loginUrl)
  }

  if (sid && pathname === '/login') {
    const stored = await getStoredSession(sid)
    if (stored.status === 'found') {
      return NextResponse.redirect(new URL('/dashboard', request.url))
    }
    if (stored.status === 'unavailable') {
      return NextResponse.json(
        { error: 'Service Unavailable', code: SESSION_UNAVAILABLE_CODE },
        {
          status: 503,
          headers: sessionUnavailableHeaders(stored.retryAfterSeconds),
        },
      )
    }
    return withPathnameHeader(request)
  }

  const expectedIdentityVersion = request.headers.get(
    SESSION_IDENTITY_VERSION_HEADER,
  )
  if (expectedIdentityVersion && !isPublic && sid && isRscPartialRequest(request)) {
    const stored = await getStoredSession(sid)
    if (stored.status === 'unavailable') {
      return NextResponse.json(
        { error: 'Service Unavailable', code: SESSION_UNAVAILABLE_CODE },
        {
          status: 503,
          headers: sessionUnavailableHeaders(stored.retryAfterSeconds),
        },
      )
    }
    if (stored.status === 'found') {
      const actualVersion = getSessionIdentityVersion(stored.session)
      if (actualVersion !== expectedIdentityVersion) {
        return NextResponse.json(
          { error: 'Conflict', code: SESSION_IDENTITY_CHANGED_CODE },
          {
            status: 409,
            headers: sessionIdentityChangedHeaders(actualVersion),
          },
        )
      }
    }
  }

  return withPathnameHeader(request)
}

export const config = {
  matcher: [
    '/((?!api/proxy|_next/static|_next/image|favicon.ico).*)',
  ],
}
