import { createHash, createHmac, randomBytes, timingSafeEqual } from 'node:crypto'

export const OIDC_FLOW_COOKIE_NAME = 'swapmid_oidc_flow'
export const OIDC_FLOW_MAX_AGE_SECONDS = 300

const PROCESS_EPHEMERAL_SECRET = randomBytes(32).toString('base64url')

export interface OidcFlowState {
  state: string
  codeVerifier: string
  redirect: string
}

function firstHeaderValue(value: string | null): string | null {
  if (!value) return null
  const first = value.split(',')[0]?.trim()
  return first ? first : null
}

function isValidHostHeader(host: string): boolean {
  return host.length > 0
    && host.length <= 253
    && !/[\s/@\\?#]/.test(host)
}

export function isSecureCookieEnabled(): boolean {
  return process.env.COOKIE_SECURE !== undefined
    ? process.env.COOKIE_SECURE !== 'false'
    : process.env.NODE_ENV === 'production'
}

export function resolveOidcPublicOrigin(headers: Headers, fallbackOrigin: string): string {
  const fallbackUrl = new URL(fallbackOrigin)
  const forwardedProto = firstHeaderValue(headers.get('x-forwarded-proto'))?.toLowerCase()
  const protocol = forwardedProto === 'http' || forwardedProto === 'https'
    ? `${forwardedProto}:`
    : fallbackUrl.protocol

  const forwardedHost = firstHeaderValue(headers.get('x-forwarded-host'))
  if (forwardedHost && isValidHostHeader(forwardedHost)) {
    return `${protocol}//${forwardedHost}`
  }

  const host = firstHeaderValue(headers.get('host'))
  if (host && isValidHostHeader(host)) {
    return `${protocol}//${host}`
  }

  return `${protocol}//${fallbackUrl.host}`
}

function getSigningSecret(): string {
  return process.env.NEXT_SERVER_ACTIONS_ENCRYPTION_KEY?.trim() || PROCESS_EPHEMERAL_SECRET
}

export function createPkcePair(): { codeVerifier: string; codeChallenge: string } {
  const codeVerifier = randomBytes(32).toString('base64url')
  const codeChallenge = createHash('sha256').update(codeVerifier).digest('base64url')
  return { codeVerifier, codeChallenge }
}

export function createOidcStateNonce(): string {
  return randomBytes(24).toString('base64url')
}

export function sealOidcFlowCookie(payload: OidcFlowState, secret = getSigningSecret()): string {
  const encoded = Buffer.from(JSON.stringify(payload), 'utf8').toString('base64url')
  const sig = createHmac('sha256', secret).update(encoded).digest('base64url')
  return `${encoded}.${sig}`
}

export function unsealOidcFlowCookie(
  rawCookie: string | undefined,
  expectedState: string | null,
  secret = getSigningSecret(),
): OidcFlowState | null {
  if (!rawCookie || !expectedState) return null
  const dotIdx = rawCookie.indexOf('.')
  if (dotIdx <= 0 || dotIdx >= rawCookie.length - 1) return null

  const encoded = rawCookie.slice(0, dotIdx)
  const actualSig = rawCookie.slice(dotIdx + 1)
  const expectedSig = createHmac('sha256', secret).update(encoded).digest('base64url')

  const actualSigBuf = Buffer.from(actualSig, 'utf8')
  const expectedSigBuf = Buffer.from(expectedSig, 'utf8')
  if (actualSigBuf.length !== expectedSigBuf.length || !timingSafeEqual(actualSigBuf, expectedSigBuf)) {
    return null
  }

  try {
    const parsed = JSON.parse(Buffer.from(encoded, 'base64url').toString('utf8')) as Partial<OidcFlowState>
    if (
      typeof parsed.state !== 'string'
      || typeof parsed.codeVerifier !== 'string'
      || typeof parsed.redirect !== 'string'
    ) {
      return null
    }
    const stateBuf = Buffer.from(parsed.state, 'utf8')
    const expectedStateBuf = Buffer.from(expectedState, 'utf8')
    if (stateBuf.length !== expectedStateBuf.length || !timingSafeEqual(stateBuf, expectedStateBuf)) {
      return null
    }
    return {
      state: parsed.state,
      codeVerifier: parsed.codeVerifier,
      redirect: parsed.redirect,
    }
  } catch {
    return null
  }
}
