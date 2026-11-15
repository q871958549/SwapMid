import { type NextRequest, NextResponse } from 'next/server'
import { fetchAbpUserConfig } from '@/lib/abp-config'
import {
  OIDC_FLOW_COOKIE_NAME,
  unsealOidcFlowCookie,
} from '@/lib/oidc-flow-cookie'
import { isSafeInternalPath } from '@/lib/safe-internal-path'
import {
  SERVER_BACKEND_DEADLINE_MS,
  SERVER_BACKEND_RESPONSE_LIMIT_BYTES,
  serverBackendFetch,
} from '@/lib/server-backend-fetch'
import { createSession } from '@/lib/session'
import { getWebOAuthClientId } from '@/lib/site-instance'

function redirectToLoginWithError(req: NextRequest, reason: string): NextResponse {
  const url = new URL('/login', req.url)
  url.searchParams.set('sso_error', reason)
  const res = NextResponse.redirect(url)
  res.cookies.delete({ name: OIDC_FLOW_COOKIE_NAME, path: '/api/auth/oidc' })
  return res
}

export async function GET(req: NextRequest) {
  const code = req.nextUrl.searchParams.get('code')
  const state = req.nextUrl.searchParams.get('state')
  const rawCookie = req.cookies.get(OIDC_FLOW_COOKIE_NAME)?.value
  const flow = unsealOidcFlowCookie(rawCookie, state)

  if (!code || !flow) {
    return redirectToLoginWithError(req, 'invalid_state')
  }

  try {
    const callbackUri = new URL('/api/auth/oidc/callback', req.url).toString()
    const tokenRes = await serverBackendFetch('/connect/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'external_oidc',
        client_id: getWebOAuthClientId(),
        scope: 'SwapMid offline_access',
        code,
        redirect_uri: callbackUri,
        code_verifier: flow.codeVerifier,
      }),
      deadlineMs: SERVER_BACKEND_DEADLINE_MS.authentication,
      maxResponseBytes: SERVER_BACKEND_RESPONSE_LIMIT_BYTES.oauthToken,
    })

    if (!tokenRes.ok) {
      return redirectToLoginWithError(req, 'exchange_failed')
    }

    const tokenJson = (await tokenRes.json()) as {
      access_token: string
      refresh_token?: string
      expires_in?: number
    }
    const userConfig = await fetchAbpUserConfig(tokenJson.access_token, 'oidc-user')
    await createSession({
      userId: userConfig.userId,
      accessToken: tokenJson.access_token,
      refreshToken: tokenJson.refresh_token,
      accessTokenExpiresAt: Date.now() + (tokenJson.expires_in ?? 3600) * 1000,
      userName: userConfig.userName,
      name: userConfig.name,
      grantedPolicies: userConfig.grantedPolicies,
      theme: userConfig.theme,
      fontSize: userConfig.fontSize,
      language: userConfig.language,
    })

    const destination = isSafeInternalPath(flow.redirect) ? flow.redirect : '/dashboard'
    const res = NextResponse.redirect(new URL(destination, req.url))
    res.cookies.delete({ name: OIDC_FLOW_COOKIE_NAME, path: '/api/auth/oidc' })
    return res
  } catch (err) {
    console.error('[oidc/callback] failed to complete BFF session establishment', err)
    return redirectToLoginWithError(req, 'session_failed')
  }
}
