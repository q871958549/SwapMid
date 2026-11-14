import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, rethrowApiFetchControlFlow, type PagedResult } from '@/lib/api-client'
import { EnterprisesClient, type Enterprise } from './client'

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function EnterprisesPage() {
  const title = await resolvePageShellTitle('/enterprise-management/enterprises')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={6} />}>
      <EnterprisesPageData />
    </Suspense>
  )
}

async function EnterprisesPageData() {
  let data: PagedResult<Enterprise> = { items: [], totalCount: 0 }
  try { data = await apiFetch<PagedResult<Enterprise>>('/enterprises?maxResultCount=1000') } catch (e) { rethrowApiFetchControlFlow(e); console.error('[enterprises]', e) }
  return <EnterprisesClient data={data} />
}
