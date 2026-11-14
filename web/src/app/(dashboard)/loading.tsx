import { Skeleton } from '@/components/ui/skeleton'

/**
 * 全站唯一的顶层 loading 骨架（改这里全站生效）。
 *
 * 🔴 根 div 的 `swapmid-skeleton-delay` 不是装饰，是**防闪烁**（2026-08-07）：`staleTimes.dynamic: 180`
 * 让绝大多数页签切换命中暖缓存、几十毫秒内就完成，骨架屏「闪一下就没」反而制造了割裂感。
 * 该类让骨架延迟 120ms 才开始淡入——快导航从头到尾看不见骨架，慢导航才温和显现。
 * 代价仅是慢路由多 120ms 空白，是 Suspense 场景的通行手法。
 */
export default function DashboardLoading() {
  return (
    <div className="swapmid-skeleton-delay">
      <div className="flex items-center justify-between mb-6">
        <div className="space-y-1.5">
          <Skeleton className="h-6 w-36" />
          <Skeleton className="h-4 w-24" />
        </div>
        <Skeleton className="h-8 w-24" />
      </div>
      <div className="rounded-xl border bg-card overflow-hidden">
        <div className="border-b bg-muted/40 px-4 py-3 flex gap-8">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-4 w-20" />
          ))}
        </div>
        {Array.from({ length: 8 }).map((_, i) => (
          <div key={i} className="flex items-center gap-8 px-4 py-3.5 border-b last:border-0">
            <Skeleton className="h-4 w-32" />
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-4 w-40" />
            <Skeleton className="h-4 w-20" />
            <Skeleton className="h-5 w-12 rounded-full" />
          </div>
        ))}
      </div>
    </div>
  )
}
