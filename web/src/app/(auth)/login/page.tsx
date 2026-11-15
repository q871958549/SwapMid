import { getExternalAuthProvidersAction, getPasswordPolicyAction } from '@/app/actions/auth'
import { readRuntimeReleaseTag } from '@/lib/runtime-release'
import { LoginClient } from './client'

export const dynamic = 'force-dynamic'

export default async function LoginPage() {
  const [passwordPolicy, externalAuth] = await Promise.all([
    getPasswordPolicyAction(),
    getExternalAuthProvidersAction(),
  ])
  const releaseTag = readRuntimeReleaseTag()
  return (
    <LoginClient
      initialPasswordPolicy={passwordPolicy}
      externalAuth={externalAuth}
      releaseTag={releaseTag}
    />
  )
}
