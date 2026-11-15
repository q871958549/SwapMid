import { createHash } from 'node:crypto'
import { describe, expect, it } from 'vitest'
import {
  createOidcStateNonce,
  createPkcePair,
  resolveOidcPublicOrigin,
  sealOidcFlowCookie,
  unsealOidcFlowCookie,
} from '@/lib/oidc-flow-cookie'

describe('oidc-flow-cookie PKCE & HMAC state protection', () => {
  it('generates valid S256 PKCE verifier and challenge pair', () => {
    const { codeVerifier, codeChallenge } = createPkcePair()
    expect(codeVerifier.length).toBeGreaterThanOrEqual(43)
    const recomputed = createHash('sha256').update(codeVerifier).digest('base64url')
    expect(codeChallenge).toBe(recomputed)
  })

  it('seals and unseals flow cookie when HMAC signature and state match', () => {
    const state = createOidcStateNonce()
    const sealed = sealOidcFlowCookie(
      { state, codeVerifier: 'pkce-verifier-123', redirect: '/data-integration/pipelines' },
      'unit-test-secret',
    )

    const opened = unsealOidcFlowCookie(sealed, state, 'unit-test-secret')
    expect(opened).toEqual({
      state,
      codeVerifier: 'pkce-verifier-123',
      redirect: '/data-integration/pipelines',
    })
  })

  it('rejects tampered cookie or mismatched OIDC state parameter', () => {
    const state = createOidcStateNonce()
    const sealed = sealOidcFlowCookie(
      { state, codeVerifier: 'pkce-verifier-123', redirect: '/dashboard' },
      'unit-test-secret',
    )

    expect(unsealOidcFlowCookie(sealed, 'wrong-state', 'unit-test-secret')).toBeNull()
    expect(unsealOidcFlowCookie(`${sealed}tamper`, state, 'unit-test-secret')).toBeNull()
    expect(unsealOidcFlowCookie(sealed, state, 'wrong-secret')).toBeNull()
  })

  it('resolves public OIDC origin safely from forwarded proxy headers or Host header', () => {
    expect(
      resolveOidcPublicOrigin(
        new Headers({
          'x-forwarded-proto': 'https',
          'x-forwarded-host': 'swapmid.example.com, internal-hop:3000',
          host: '0.0.0.0:3000',
        }),
        'http://0.0.0.0:3000',
      ),
    ).toBe('https://swapmid.example.com')

    expect(
      resolveOidcPublicOrigin(
        new Headers({ host: 'localhost:33000' }),
        'http://0.0.0.0:3000',
      ),
    ).toBe('http://localhost:33000')

    expect(
      resolveOidcPublicOrigin(
        new Headers({
          'x-forwarded-proto': 'javascript',
          'x-forwarded-host': 'evil.com/path@victim',
        }),
        'http://localhost:33000',
      ),
    ).toBe('http://localhost:33000')
  })
})
