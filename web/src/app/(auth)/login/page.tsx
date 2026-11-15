import { getExternalAuthProvidersAction, getPasswordPolicyAction } from '@/app/actions/auth'
import { readRuntimeReleaseTag } from '@/lib/runtime-release'
import { isSafeInternalPath } from '@/lib/safe-internal-path'
import { LoginClient } from './client'

export const dynamic = 'force-dynamic'

const VALID_SSO_ERRORS = new Set(['invalid_state', 'exchange_failed', 'session_failed'])

function firstQueryValue(value: string | string[] | undefined): string | null {
  if (typeof value === 'string') return value
  if (Array.isArray(value) && typeof value[0] === 'string') return value[0]
  return null
}

export default async function LoginPage({
  searchParams,
}: {
  searchParams?: Promise<{ sso_error?: string | string[]; redirect?: string | string[] }>
}) {
  const emptyParams: { sso_error?: string | string[]; redirect?: string | string[] } = {}
  const [passwordPolicy, externalAuth, resolvedParams] = await Promise.all([
    getPasswordPolicyAction(),
    getExternalAuthProvidersAction(),
    searchParams ?? Promise.resolve(emptyParams),
  ])
  const releaseTag = readRuntimeReleaseTag()
  const rawSsoError = firstQueryValue(resolvedParams.sso_error)
  const initialSsoError = rawSsoError && VALID_SSO_ERRORS.has(rawSsoError) ? rawSsoError : null
  const rawRedirect = firstQueryValue(resolvedParams.redirect)
  const initialRedirect = isSafeInternalPath(rawRedirect) ? rawRedirect : null
  return (
    <LoginClient
      initialPasswordPolicy={passwordPolicy}
      externalAuth={externalAuth}
      releaseTag={releaseTag}
      initialSsoError={initialSsoError}
      initialRedirect={initialRedirect}
    />
  )
}
