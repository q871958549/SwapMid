import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, rethrowApiFetchControlFlow, type PagedResult } from '@/lib/api-client'
import { AuditLogsClient, type AuditLog } from './client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'

// 审计日志实时数据，不缓存。
export const dynamic = 'force-dynamic'

type AuditLogsSearch = {
  startTime?: string
  endTime?: string
  url?: string
  userName?: string
  method?: string
  onlyException?: string
  page?: string
  size?: string; sort?: string
}

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function AuditLogsPage({ searchParams }: { searchParams: Promise<AuditLogsSearch> }) {
  const title = await resolvePageShellTitle('/system/audit-logs')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={7} />}>
      <AuditLogsPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function AuditLogsPageData({ searchParams }: { searchParams: Promise<AuditLogsSearch> }) {
  const sp = await searchParams
  // 首屏按 URL 的分页/筛选取数（方案A：URL 持久化列表视图状态，切走再切回/刷新可恢复）。
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1)
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(pageIndex * pageSize) })
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  if (abpSorting) qs.set('Sorting', abpSorting)
  // 首屏筛选口径须与 client 的 buildParams 完全一致（尤其 EndTime 拼 23:59:59），否则切回后首屏与点「查询」不一致。
  if (sp.startTime) qs.set('StartTime', sp.startTime)
  if (sp.endTime) qs.set('EndTime', `${sp.endTime} 23:59:59`)
  if (sp.url) qs.set('Url', sp.url)
  if (sp.userName) qs.set('UserName', sp.userName)
  if (sp.method) qs.set('HttpMethod', sp.method)
  if (sp.onlyException === 'true') qs.set('HasException', 'true')

  let data: PagedResult<AuditLog> = { items: [], totalCount: 0 }
  try {
    data = await apiFetch<PagedResult<AuditLog>>(`/audit-logs?${qs.toString()}`)
  } catch (error) {
    rethrowApiFetchControlFlow(error)
  }

  return (
    <AuditLogsClient
      initial={data}
      initialStartTime={sp.startTime ?? ''}
      initialEndTime={sp.endTime ?? ''}
      initialUrl={sp.url ?? ''}
      initialUserName={sp.userName ?? ''}
      initialMethod={sp.method ?? ''}
      initialOnlyException={sp.onlyException === 'true'}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
    />
  )
}
