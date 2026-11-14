import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, rethrowApiFetchControlFlow, type PagedResult } from '@/lib/api-client'
import { TopicsClient, type MessageTopic } from './client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'

// 方案A：URL 持久化列表视图状态（分页/搜索），切走再切回 / 刷新可恢复。
type TopicsSearch = { page?: string; size?: string; sort?: string; q?: string }

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function TopicsPage({ searchParams }: { searchParams: Promise<TopicsSearch> }) {
  const title = await resolvePageShellTitle('/message-bus/topics')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={5} />}>
      <TopicsPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function TopicsPageData({ searchParams }: { searchParams: Promise<TopicsSearch> }) {
  const sp = await searchParams
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1) // URL page 为 1-based，内部 0-based
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(pageIndex * pageSize) })
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  if (abpSorting) qs.set('Sorting', abpSorting)
  if (sp.q) qs.set('filter', sp.q)
  let data: PagedResult<MessageTopic> = { items: [], totalCount: 0 }
  try { data = await apiFetch<PagedResult<MessageTopic>>(`/message-topics?${qs.toString()}`) } catch (e) { rethrowApiFetchControlFlow(e); console.error('[topics]', e) }
  return <TopicsClient data={data} initialPage={pageIndex} initialPageSize={pageSize} initialSorting={sorting} initialSearch={sp.q ?? ''} />
}
