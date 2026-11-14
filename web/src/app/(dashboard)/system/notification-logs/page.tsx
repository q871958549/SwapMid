import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import { NotificationLogsClient, type NotificationLog } from './client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'

type NotificationLogsSearch = { page?: string; size?: string; sort?: string }

export default async function NotificationLogsPage({ searchParams }: { searchParams: Promise<NotificationLogsSearch> }) {
  const title = await resolvePageShellTitle('/system/notification-logs')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={6} />}>
      <NotificationLogsPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function NotificationLogsPageData({ searchParams }: { searchParams: Promise<NotificationLogsSearch> }) {
  const sp = await searchParams
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1)
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(pageIndex * pageSize) })
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  if (abpSorting) qs.set('Sorting', abpSorting)

  const data = await apiFetch<PagedResult<NotificationLog>>(
    `/notification/logs?${qs.toString()}`,
  ).catch((e) => {
    console.error('[notification-logs]', e)
    return { items: [], totalCount: 0 } as PagedResult<NotificationLog>
  })
  return (
    <NotificationLogsClient
      data={data}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
    />
  )
}
