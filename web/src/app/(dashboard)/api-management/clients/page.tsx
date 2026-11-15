import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import {
  ThirdPartyClientsClient,
  type ApiGroupOption,
  type ThirdPartyClient,
} from './client'

// 第三方应用/凭据与网关访问策略管理（OpenIddict ClientCredentials 客户端）。
// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function ThirdPartyClientsPage() {
  const title = await resolvePageShellTitle('/api-management/clients')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={5} />}>
      <ThirdPartyClientsPageData />
    </Suspense>
  )
}

async function ThirdPartyClientsPageData() {
  const [initial, groups] = await Promise.all([
    apiFetch<ThirdPartyClient[]>('/third-party-clients').catch(() => []),
    apiFetch<PagedResult<ApiGroupOption>>('/api-groups?maxResultCount=1000').catch(() => ({
      items: [],
      totalCount: 0,
    })),
  ])
  return <ThirdPartyClientsClient initial={initial} groups={groups.items} />
}
