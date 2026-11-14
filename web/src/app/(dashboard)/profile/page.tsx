import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { PageShellSkeleton } from '@/components/data-table/page-shell-skeleton'
import { Suspense } from 'react'
import { getSession } from '@/lib/session'
import { apiFetch, rethrowApiFetchControlFlow } from '@/lib/api-client'
import { parsePasswordPolicy } from '@/lib/password-policy'
import { ProfileClient } from './client'

// 个人中心（全员向、不挂 permission）：头像 / 界面偏好（主题·字号）/ 改密码。
// 平台界面固定简体，不再提供语言切换。对外产品查询仍用自己的语言开关。
// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function ProfilePage() {
  const title = await resolvePageShellTitle('/profile')
  return (
    <Suspense fallback={<PageShellSkeleton title={title} />}>
      <ProfilePageData />
    </Suspense>
  )
}

async function loadPasswordPolicy() {
  try {
    return parsePasswordPolicy(await apiFetch<unknown>('/account-management/password-policy'))
  } catch (e) {
    rethrowApiFetchControlFlow(e)
    return null
  }
}

async function ProfilePageData() {
  const session = await getSession()
  const passwordPolicy = await loadPasswordPolicy()
  return (
    <ProfileClient
      name={session?.name ?? ''}
      userName={session?.userName ?? ''}
      passwordPolicy={passwordPolicy}
    />
  )
}
