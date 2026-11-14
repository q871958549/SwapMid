import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import { DataDictionariesClient, type DataDictionaryDto } from './client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'

type DataDictionariesSearch = { page?: string; size?: string; sort?: string; q?: string }

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function DataDictionariesPage({ searchParams }: { searchParams: Promise<DataDictionariesSearch> }) {
  const title = await resolvePageShellTitle('/system/data-dictionaries')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={5} />}>
      <DataDictionariesPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function DataDictionariesPageData({ searchParams }: { searchParams: Promise<DataDictionariesSearch> }) {
  const sp = await searchParams
  // 首屏按 URL 的分页/搜索取数（方案A：URL 持久化列表视图状态，切走再切回/刷新可恢复）。
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1)
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(pageIndex * pageSize) })
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  if (abpSorting) qs.set('Sorting', abpSorting)
  if (sp.q) qs.set('filter', sp.q)

  const data = await apiFetch<PagedResult<DataDictionaryDto>>(
    `/data-dictionaries?${qs.toString()}`
  ).catch(() => ({ items: [], totalCount: 0 }))

  return (
    <DataDictionariesClient
      data={data}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
      initialSearch={sp.q ?? ''}
    />
  )
}
