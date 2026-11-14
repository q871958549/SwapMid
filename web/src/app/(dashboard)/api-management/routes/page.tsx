import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import { ApiRoutesClient, type ApiRoute, type ApiGroup } from './client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'

// 首屏按 URL 的分页/搜索取数（方案A：URL 持久化列表视图状态，切走再切回/刷新可恢复）。
type ApiRoutesSearch = { page?: string; size?: string; sort?: string; q?: string }

async function fetchAll(pageIndex: number, pageSize: number, q?: string, abpSorting?: string) {
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(pageIndex * pageSize) })
  if (abpSorting) qs.set('Sorting', abpSorting)
  if (q) qs.set('filter', q)
  const [routes, groups] = await Promise.all([
    apiFetch<PagedResult<ApiRoute>>(`/api-routes?${qs.toString()}`).catch(() => ({ items: [], totalCount: 0 })),
    apiFetch<PagedResult<ApiGroup>>('/api-groups?maxResultCount=1000').catch(() => ({ items: [], totalCount: 0 })),
  ])
  return { routes, groups: groups.items }
}

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function ApiRoutesPage({ searchParams }: { searchParams: Promise<ApiRoutesSearch> }) {
  const title = await resolvePageShellTitle('/api-management/routes')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={6} />}>
      <ApiRoutesPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function ApiRoutesPageData({ searchParams }: { searchParams: Promise<ApiRoutesSearch> }) {
  const sp = await searchParams
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1) // URL page 为 1-based，内部 0-based
  const sorting = parseSortParam(sp.sort)
  const { routes, groups } = await fetchAll(pageIndex, pageSize, sp.q, toAbpSorting(sorting))
  return (
    <ApiRoutesClient
      initialData={routes}
      groups={groups}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
      initialSearch={sp.q ?? ''}
    />
  )
}
