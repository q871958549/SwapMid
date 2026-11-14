import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { ExecutionsPageData, type ExecutionsSearch } from './page-data'

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function ExecutionsPage({ searchParams }: { searchParams: Promise<ExecutionsSearch> }) {
  const title = await resolvePageShellTitle('/data-integration/executions')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={7} />}>
      <ExecutionsPageData searchParams={searchParams} />
    </Suspense>
  )
}
