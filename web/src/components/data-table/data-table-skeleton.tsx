import { type ColumnDef, type VisibilityState } from '@tanstack/react-table'
import { Skeleton } from '@/components/ui/skeleton'
import { getColumnKey } from './use-table-persistence'

// 挂载前（SSR + 客户端首帧、读 localStorage 偏好之前）的加载骨架屏：工具栏 + 表格 + 分页三段占位。
// 列数按「默认可见列数」估算（跳过审计等默认隐藏列，至少 3 列），行数按数据量估算（3~8 行）。
export function DataTableSkeleton<TData>({
  tableColumns,
  defaultVisibility,
  dataLength,
}: {
  tableColumns: ColumnDef<TData, unknown>[]
  defaultVisibility: VisibilityState
  dataLength: number
}) {
  const skeletonColCount = Math.max(
    3,
    tableColumns.filter((col) => defaultVisibility[getColumnKey(col)] !== false).length,
  )
  const skeletonRowCount = Math.min(8, Math.max(3, dataLength || 6))
  return (
    <div className="flex h-full min-h-0 flex-1 flex-col gap-3">
      {/* Toolbar skeleton */}
      <div className="flex shrink-0 items-center gap-2">
        <Skeleton className="h-8 w-full max-w-sm" />
        <div className="ml-auto flex items-center gap-2">
          <Skeleton className="h-8 w-20" />
          <Skeleton className="h-8 w-20" />
          <Skeleton className="h-8 w-16" />
          <Skeleton className="h-8 w-14" />
        </div>
      </div>

      {/* Table skeleton */}
      <div className="min-h-0 flex-1 overflow-hidden rounded-xl border bg-card">
        <div className="flex items-center gap-4 border-b bg-muted/40 px-4 py-3">
          {Array.from({ length: skeletonColCount }).map((_, i) => (
            <Skeleton key={i} className="h-3.5 flex-1" />
          ))}
        </div>
        {Array.from({ length: skeletonRowCount }).map((_, r) => (
          <div key={r} className="flex items-center gap-4 border-b px-4 py-3.5 last:border-b-0">
            {Array.from({ length: skeletonColCount }).map((_, c) => (
              <Skeleton key={c} className="h-4 flex-1" />
            ))}
          </div>
        ))}
      </div>

      {/* Pagination skeleton */}
      <div className="flex shrink-0 items-center justify-between">
        <Skeleton className="h-4 w-24" />
        <Skeleton className="h-7 w-64" />
      </div>
    </div>
  )
}
