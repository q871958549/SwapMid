import { Skeleton } from '@/components/ui/skeleton'
import { PageHeader } from './page-header'

interface PageShellSkeletonProps {
  title: string
  description?: string
}

/**
 * 非列表页的即时外壳（大盘 / 详情 / 检索 / 设置）。
 * 标题立即可见，内容区用块状骨架占位；列表页请用 ListPageSkeleton。
 */
export function PageShellSkeleton({ title, description }: PageShellSkeletonProps) {
  return (
    <>
      <PageHeader title={title} description={description} />
      <div className="space-y-3">
        <div className="grid gap-3 sm:grid-cols-3">
          <Skeleton className="h-24 rounded-xl" />
          <Skeleton className="h-24 rounded-xl" />
          <Skeleton className="h-24 rounded-xl" />
        </div>
        <Skeleton className="h-64 w-full rounded-xl" />
      </div>
    </>
  )
}
