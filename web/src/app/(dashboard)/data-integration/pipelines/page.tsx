import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import { hasPermission, Permissions } from '@/lib/permissions'
import { getSession } from '@/lib/session'
import { DataPipelinesClient, type DataPipeline, type DataSourceOption } from './client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'

// 首屏按 URL 的分页/搜索取数（方案A：URL 持久化列表视图状态，切走再切回/刷新可恢复）。
type DataPipelinesSearch = { page?: string; size?: string; sort?: string; q?: string }

async function fetchAll(pageIndex: number, pageSize: number, q?: string, abpSorting?: string) {
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(pageIndex * pageSize) })
  if (abpSorting) qs.set('Sorting', abpSorting)
  if (q) qs.set('filter', q)
  const [pipelines, sources] = await Promise.all([
    apiFetch<PagedResult<DataPipeline>>(`/data-pipelines?${qs.toString()}`).catch(() => ({ items: [], totalCount: 0 })),
    apiFetch<PagedResult<DataSourceOption>>('/data-sources?maxResultCount=1000').catch(() => ({ items: [], totalCount: 0 })),
  ])
  return { pipelines, sources: sources.items }
}

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function DataPipelinesPage({ searchParams }: { searchParams: Promise<DataPipelinesSearch> }) {
  const title = await resolvePageShellTitle('/data-integration/pipelines')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={7} />}>
      <DataPipelinesPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function DataPipelinesPageData({ searchParams }: { searchParams: Promise<DataPipelinesSearch> }) {
  const [sp, session] = await Promise.all([searchParams, getSession()])
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1) // URL page 为 1-based，内部 0-based
  const sorting = parseSortParam(sp.sort)
  const { pipelines, sources } = await fetchAll(pageIndex, pageSize, sp.q, toAbpSorting(sorting))
  const grantedPolicies = session?.grantedPolicies ?? {}
  return (
    <DataPipelinesClient
      data={pipelines}
      sources={sources}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
      initialSearch={sp.q ?? ''}
      canExecute={hasPermission(
        grantedPolicies,
        Permissions.DataIntegration.DataPipelinesExecute,
      )}
      canUpdate={hasPermission(
        grantedPolicies,
        Permissions.DataIntegration.DataPipelinesUpdate,
      )}
    />
  )
}
