import { type NextRequest, NextResponse } from 'next/server'
import type { ExternalAuthProvidersDto } from '@/lib/api/types'
import {
  createOidcStateNonce,
  createPkcePair,
  isSecureCookieEnabled,
  OIDC_FLOW_COOKIE_NAME,
  OIDC_FLOW_MAX_AGE_SECONDS,
  resolveOidcPublicOrigin,
  sealOidcFlowCookie,
} from '@/lib/oidc-flow-cookie'
import { isSafeInternalPath } from '@/lib/safe-internal-path'
import {
  SERVER_BACKEND_DEADLINE_MS,
  SERVER_BACKEND_RESPONSE_LIMIT_BYTES,
  serverBackendFetch,
} from '@/lib/server-backend-fetch'

export async function GET(req: NextRequest) {
  const publicOrigin = resolveOidcPublicOrigin(req.headers, req.nextUrl.origin)
  const loginFallback = new URL('/login', publicOrigin)

  try {
    const providerRes = await serverBackendFetch('/api/v1/auth-providers', {
      cache: 'no-store',
      deadlineMs: SERVER_BACKEND_DEADLINE_MS.publicPage,
      maxResponseBytes: SERVER_BACKEND_RESPONSE_LIMIT_BYTES.oauthToken,
    })
    if (!providerRes.ok) {
      return NextResponse.redirect(loginFallback)
    }

    const provider = (await providerRes.json()) as ExternalAuthProvidersDto
    if (
      !provider.externalOidcEnabled ||
      !provider.authority ||
      !provider.clientId ||
      !provider.authorizationEndpoint
    ) {
      return NextResponse.redirect(loginFallback)
    }

    const rawRedirect = req.nextUrl.searchParams.get('redirect')
    const redirect = isSafeInternalPath(rawRedirect) ? rawRedirect : '/dashboard'
    const state = createOidcStateNonce()
    const { codeVerifier, codeChallenge } = createPkcePair()
    const callbackUri = new URL('/api/auth/oidc/callback', publicOrigin).toString()

    const idpUrl = new URL(provider.authorizationEndpoint)
    idpUrl.searchParams.set('response_type', 'code')
    idpUrl.searchParams.set('client_id', provider.clientId)
    idpUrl.searchParams.set('redirect_uri', callbackUri)
    idpUrl.searchParams.set('scope', 'openid profile email')
    idpUrl.searchParams.set('state', state)
    idpUrl.searchParams.set('code_challenge', codeChallenge)
    idpUrl.searchParams.set('code_challenge_method', 'S256')

    const response = NextResponse.redirect(idpUrl)
    response.cookies.set(OIDC_FLOW_COOKIE_NAME, sealOidcFlowCookie({ state, codeVerifier, redirect }), {
      httpOnly: true,
      sameSite: 'lax',
      secure: isSecureCookieEnabled(),
      path: '/api/auth/oidc',
      maxAge: OIDC_FLOW_MAX_AGE_SECONDS,
    })
    return response
  } catch (err) {
    console.error('[oidc/start] failed to initiate external OIDC flow', err)
    return NextResponse.redirect(loginFallback)
  }
}
