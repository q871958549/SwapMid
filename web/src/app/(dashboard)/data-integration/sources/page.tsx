import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, rethrowApiFetchControlFlow, type PagedResult } from '@/lib/api-client'
import { DataSourcesClient, type DataSource } from './client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'

// 首屏按 URL 的分页/搜索取数（方案A：URL 持久化列表视图状态，切走再切回/刷新可恢复）。
type DataSourcesSearch = { page?: string; size?: string; sort?: string; q?: string }

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function DataSourcesPage({ searchParams }: { searchParams: Promise<DataSourcesSearch> }) {
  const title = await resolvePageShellTitle('/data-integration/sources')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={6} />}>
      <DataSourcesPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function DataSourcesPageData({ searchParams }: { searchParams: Promise<DataSourcesSearch> }) {
  const sp = await searchParams
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1) // URL page 为 1-based，内部 0-based
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(pageIndex * pageSize) })
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  if (abpSorting) qs.set('Sorting', abpSorting)
  if (sp.q) qs.set('filter', sp.q)
  let data: PagedResult<DataSource> = { items: [], totalCount: 0 }
  try { data = await apiFetch<PagedResult<DataSource>>(`/data-sources?${qs.toString()}`) } catch (e) { rethrowApiFetchControlFlow(e); console.error('[data-sources]', e) }
  return (
    <DataSourcesClient
      data={data}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
      initialSearch={sp.q ?? ''}
    />
  )
}
