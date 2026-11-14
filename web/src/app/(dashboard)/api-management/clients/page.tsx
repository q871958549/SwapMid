import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch } from '@/lib/api-client'
import { ThirdPartyClientsClient, type ThirdPartyClient } from './client'

// 第三方应用/凭据管理（对外 API 的 OpenIddict ClientCredentials 客户端，见 ADR-0015）。
// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function ThirdPartyClientsPage() {
  const title = await resolvePageShellTitle('/api-management/clients')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={6} />}>
      <ThirdPartyClientsPageData />
    </Suspense>
  )
}

async function ThirdPartyClientsPageData() {
  const initial = await apiFetch<ThirdPartyClient[]>('/third-party-clients').catch(() => [])
  return <ThirdPartyClientsClient initial={initial} />
}
