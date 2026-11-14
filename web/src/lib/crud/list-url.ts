import { useTabStore } from '@/stores/use-tab-store'

/**
 * 把一批列表状态（页面级筛选 / 分页 / 每页条数 / 搜索词）增量镜像到当前 URL 的 query，
 * 并同步当前激活页签的 path，使「切走再切回」能从 URL 恢复（方案 A：URL 作为列表视图状态的持久化载体）。
 *
 * 关键设计：
 * - 用 `window.history.replaceState` 做 **shallow** 更新——只改地址栏，不触发 Next 路由 / 不重跑 Server Component，
 *   故连续筛选/翻页不会闪烁或多余取数（列表数据仍由 useCrudResource 经 /api/proxy 客户端取）。
 * - 同步调用 tab store 的 `updateActivePath`，**不依赖** useSearchParams 对 replaceState 的响应（更鲁棒、跨 Next 版本稳定）。
 * - 值为 `''` / `null` / `undefined` 时从 URL 删除该键（保持地址栏干净、默认值不入 URL）。
 *
 * 注意：这里**只负责写 URL**；读初值一律在 page.tsx 从 `searchParams` 解析后经 props 下传（保证 SSR/hydration 一致）。
 */
export function patchListUrl(patch: Record<string, string | number | null | undefined>) {
  if (typeof window === 'undefined') return
  const qs = new URLSearchParams(window.location.search)
  for (const [key, value] of Object.entries(patch)) {
    if (value === undefined || value === null || value === '') qs.delete(key)
    else qs.set(key, String(value))
  }
  const query = qs.toString()
  const url = window.location.pathname + (query ? `?${query}` : '')
  window.history.replaceState(null, '', url)
  useTabStore.getState().updateActivePath(url)
}
