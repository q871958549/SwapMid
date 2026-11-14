import { getPasswordPolicyAction } from '@/app/actions/auth'
import { readRuntimeReleaseTag } from '@/lib/runtime-release'
import { LoginClient } from './client'

export const dynamic = 'force-dynamic'

export default async function LoginPage() {
  const passwordPolicy = await getPasswordPolicyAction()
  const releaseTag = readRuntimeReleaseTag()
  return <LoginClient initialPasswordPolicy={passwordPolicy} releaseTag={releaseTag} />
}
