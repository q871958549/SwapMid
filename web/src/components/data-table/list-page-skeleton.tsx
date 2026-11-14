import { Plus } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { PageHeader } from './page-header'

interface ListPageSkeletonProps {
  title: string
  description?: string
  createLabel?: string
  columns?: number
  rows?: number
}

/**
 * 列表页的"即时外壳"：用作 page.tsx 的 Suspense fallback。
 * 标题/描述/新建按钮（禁用占位）立即渲染，让路由切换瞬间就显示出"已经切到这个页面"，
 * 只有数据表区域是骨架；待 Server Component 数据流式返回后整体替换为可交互页面。
 */
export function ListPageSkeleton({ title, description, createLabel, columns = 6, rows = 8 }: ListPageSkeletonProps) {
  return (
    <>
      <PageHeader
        title={title}
        description={description}
        action={createLabel ? (
          <Button size="sm" disabled>
            <Plus className="size-4" />
            {createLabel}
          </Button>
        ) : undefined}
      />
      <div className="space-y-3">
        {/* 工具栏骨架 */}
        <div className="flex items-center gap-2">
          <Skeleton className="h-8 w-full max-w-sm" />
          <div className="ml-auto flex items-center gap-2">
            <Skeleton className="h-8 w-20" />
            <Skeleton className="h-8 w-20" />
            <Skeleton className="h-8 w-16" />
            <Skeleton className="h-8 w-14" />
          </div>
        </div>
        {/* 表格骨架 */}
        <div className="overflow-hidden rounded-xl border bg-card">
          <div className="flex items-center gap-4 border-b bg-muted/40 px-4 py-3">
            {Array.from({ length: columns }).map((_, i) => (
              <Skeleton key={i} className="h-3.5 flex-1" />
            ))}
          </div>
          {Array.from({ length: rows }).map((_, r) => (
            <div key={r} className="flex items-center gap-4 border-b px-4 py-3.5 last:border-b-0">
              {Array.from({ length: columns }).map((_, c) => (
                <Skeleton key={c} className="h-4 flex-1" />
              ))}
            </div>
          ))}
        </div>
      </div>
    </>
  )
}
