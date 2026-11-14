import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch } from '@/lib/api-client'
import { ScheduledJobsClient, type ScheduledJob } from './client'
import type { ScheduledJobStats } from './stats-cards'

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function ScheduledJobsPage() {
  const title = await resolvePageShellTitle('/system/scheduled-jobs')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={6} />}>
      <ScheduledJobsPageData />
    </Suspense>
  )
}

async function ScheduledJobsPageData() {
  const [jobs, stats] = await Promise.all([
    apiFetch<ScheduledJob[]>('/scheduled-jobs').catch(e => {
      console.error('[scheduled-jobs]', e)
      return [] as ScheduledJob[]
    }),
    apiFetch<ScheduledJobStats>('/scheduled-jobs/stats').catch(e => {
      console.error('[scheduled-jobs/stats]', e)
      return null
    }),
  ])
  return <ScheduledJobsClient initial={jobs} initialStats={stats} />
}
