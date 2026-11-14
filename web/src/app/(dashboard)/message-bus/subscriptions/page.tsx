import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import { SubscriptionsClient, type MessageSubscription, type TopicOption } from './client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'

// 方案A：URL 持久化列表视图状态（分页/搜索），切走再切回 / 刷新可恢复。
type SubscriptionsSearch = { page?: string; size?: string; sort?: string; q?: string }

async function fetchAll(subsQuery: string) {
  const [subs, topics] = await Promise.all([
    apiFetch<PagedResult<MessageSubscription>>(`/message-subscriptions?${subsQuery}`).catch(() => ({ items: [], totalCount: 0 })),
    apiFetch<PagedResult<TopicOption>>('/message-topics?maxResultCount=1000').catch(() => ({ items: [], totalCount: 0 })),
  ])
  return { subs, topics: topics.items }
}

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function SubscriptionsPage({ searchParams }: { searchParams: Promise<SubscriptionsSearch> }) {
  const title = await resolvePageShellTitle('/message-bus/subscriptions')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={6} />}>
      <SubscriptionsPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function SubscriptionsPageData({ searchParams }: { searchParams: Promise<SubscriptionsSearch> }) {
  const sp = await searchParams
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1) // URL page 为 1-based，内部 0-based
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(pageIndex * pageSize) })
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  if (abpSorting) qs.set('Sorting', abpSorting)
  if (sp.q) qs.set('filter', sp.q)
  const { subs, topics } = await fetchAll(qs.toString())
  return <SubscriptionsClient data={subs} topics={topics} initialPage={pageIndex} initialPageSize={pageSize} initialSorting={sorting} initialSearch={sp.q ?? ''} />
}
