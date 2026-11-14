import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'
import { ScheduledJobExecutionsClient } from './client'
import { appendExecutionFilters } from './execution-query'
import type { JobExecution } from '../execution-shared'
import type { ScheduledJob } from '../columns'

// 与「定时任务」管控页对齐：跨全部任务的执行历史，实时数据不缓存。
export const dynamic = 'force-dynamic'

type ExecutionsSearch = {
  jobKey?: string
  status?: string
  triggerKind?: string
  isDryRun?: string
  startedFrom?: string
  windowStartedAt?: string
  windowEndedAt?: string
  startedTo?: string
  page?: string
  size?: string; sort?: string
}

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function ScheduledJobExecutionsPage({ searchParams }: { searchParams: Promise<ExecutionsSearch> }) {
  const title = await resolvePageShellTitle('/system/scheduled-jobs/executions')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={6} />}>
      <ScheduledJobExecutionsPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function ScheduledJobExecutionsPageData({ searchParams }: { searchParams: Promise<ExecutionsSearch> }) {
  const sp = await searchParams
  const pageSize = Math.max(1, Number(sp.size) || 20)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1) // URL page 为 1-based，内部 0-based
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(pageIndex * pageSize) })
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  qs.set('Sorting', abpSorting || 'StartedAt desc')
  appendExecutionFilters(qs, sp)

  const [executions, jobs] = await Promise.all([
    apiFetch<PagedResult<JobExecution>>(`/scheduling/executions?${qs.toString()}`).catch((e) => {
      console.error('[scheduled-jobs/executions]', e)
      return { items: [], totalCount: 0 } as PagedResult<JobExecution>
    }),
    apiFetch<ScheduledJob[]>('/scheduled-jobs').catch((e) => {
      console.error('[scheduled-jobs]', e)
      return [] as ScheduledJob[]
    }),
  ])

  return (
    <ScheduledJobExecutionsClient
      initial={executions}
      jobs={jobs}
      initialJobKey={sp.jobKey ?? ''}
      initialStatus={sp.status ?? ''}
      initialTriggerKind={sp.triggerKind ?? ''}
      initialIsDryRun={sp.isDryRun ?? ''}
      initialStartedFrom={sp.startedFrom ?? ''}
      initialWindowStartedAt={sp.windowStartedAt ?? ''}
      initialWindowEndedAt={sp.windowEndedAt ?? ''}
      initialStartedTo={sp.startedTo ?? ''}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
    />
  )
}
