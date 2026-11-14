import type { SortingState } from '@tanstack/react-table'

/**
 * 列表排序状态的 URL 序列化（方案 A：URL 持久化列表视图状态的排序维度）。
 * URL 键 `sort`，值形如 `name.desc` / 多列 `name.desc,code.asc`（点号分隔避免空格转义）。
 * 本模块 server-safe（纯函数、无 'use client'）：page.tsx 解析初值 + 拼首屏取数、
 * use-crud-resource 序列化写回 URL，两端共用同一套编解码。
 */

/** URL `sort` 值 → 表格 SortingState；非法段（无字段名/未知方向）忽略。 */
export function parseSortParam(sort: string | undefined): SortingState {
  if (!sort) return []
  const result: SortingState = []
  for (const seg of sort.split(',')) {
    const dot = seg.lastIndexOf('.')
    const id = (dot >= 0 ? seg.slice(0, dot) : seg).trim()
    const dir = dot >= 0 ? seg.slice(dot + 1) : 'asc'
    if (!id || (dir !== 'asc' && dir !== 'desc')) continue
    result.push({ id, desc: dir === 'desc' })
  }
  return result
}

/** SortingState → URL `sort` 值；空数组返回 ''（patchListUrl 会据此从 URL 删除该键）。 */
export function serializeSortParam(sorting: SortingState): string {
  return sorting.map((s) => `${s.id}.${s.desc ? 'desc' : 'asc'}`).join(',')
}

/** SortingState → ABP `Sorting` 串（"field desc,field2 asc"）；列 id 即后端字段名（白名单大小写不敏感匹配）。 */
export function toAbpSorting(sorting: SortingState): string {
  return sorting.map((s) => `${s.id} ${s.desc ? 'desc' : 'asc'}`).join(',')
}
