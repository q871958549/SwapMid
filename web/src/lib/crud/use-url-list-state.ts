'use client'

import { useCallback, useEffect, useRef, useState } from 'react'
import { patchListUrl } from './list-url'

// ============ 页面级筛选重置注册表（微型外部 store） ============
// 每个 useUrlListState 实例挂载时注册一个 { reset, isActive } 条目，供「重置列表视图」
// （useCrudResource.resetView → DataTable 工具栏重置按钮）一并清掉页面级筛选——
// 零页面接线：页面不必把 filters 传给重置按钮。同页多列表场景会全部清空（语义可接受）。
// 同时对外暴露 subscribeListFilters/hasActiveRegisteredFilters，供 useCrudResource 用
// useSyncExternalStore 订阅「当前是否存在非默认页面级筛选」，驱动工具栏 viewDirty 判定。
interface FilterResetEntry {
  /** 清空自身所有键（'' = 未筛选，URL 键随之删除）。 */
  reset: () => void
  /** 自身是否存在非空筛选值。 */
  isActive: () => boolean
  /** 读取自身当前非空筛选键值快照（供 DataTable 命名视图预设保存）。 */
  snapshot: () => Record<string, string>
  /** 应用命名视图预设保存的键值（预设未包含的本实例键清空为 ''），同步 URL。 */
  apply: (values: Record<string, string>) => void
}

const filterResets = new Set<FilterResetEntry>()
const listeners = new Set<() => void>()

/** 任一 set / reset / 注册 / 注销时调用，通知所有 useSyncExternalStore 订阅者重新读取快照。 */
function notifyListFilters() {
  for (const listener of listeners) listener()
}

/** 订阅「注册表活跃态变化」（useSyncExternalStore 的 subscribe 参数）。返回取消订阅函数。 */
export function subscribeListFilters(listener: () => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

/** 当前是否存在任一已挂载 useUrlListState 实例带非空筛选值（useSyncExternalStore 的 getSnapshot 参数）。 */
export function hasActiveRegisteredFilters(): boolean {
  for (const entry of filterResets) {
    if (entry.isActive()) return true
  }
  return false
}

/** 清空当前已挂载的所有 useUrlListState 筛选（值置 ''，URL 键删除）。 */
export function resetRegisteredListFilters() {
  for (const entry of filterResets) entry.reset()
}

/** 收集当前页面已挂载的所有 useUrlListState 非空筛选键值（供命名视图预设保存）。 */
export function snapshotRegisteredListFilters(): Record<string, string> {
  const out: Record<string, string> = {}
  for (const entry of filterResets) {
    Object.assign(out, entry.snapshot())
  }
  return out
}

/** 将命名视图预设保存的筛选键值恢复到当前页面已挂载的 useUrlListState（未包含键重置为 ''）。 */
export function applyRegisteredListFilters(values: Record<string, string>) {
  for (const entry of filterResets) entry.apply(values)
}

/**
 * 页面级筛选状态的「URL 持久化」封装：内部 useState 驱动 UI，`set` 同时把变更增量写进 URL query
 * 并同步页签 path（经 patchListUrl），从而「切走再切回」能恢复。
 *
 * 用法（配合 page.tsx 从 searchParams 解析初值下传，保证 SSR/hydration 一致）：
 *   const [filters, setFilters] = useUrlListState({ status: initialStatus, account: initialAccount })
 *   <Select value={filters.status} onValueChange={v => { setFilters({ status: v }); list.setPage(0) }} />
 *
 * 约定：所有值都是字符串（枚举/id 皆序列化为字符串）；空串表示「未筛选」，会从 URL 删除对应键。
 * 初值**必须**来自 props（page.tsx 的 searchParams），不要在此读 window.location，否则触发 hydration mismatch。
 */
export function useUrlListState<T extends Record<string, string>>(
  initial: T,
): [T, (patch: Partial<T>) => void] {
  const [state, setState] = useState<T>(initial)

  // ⚠️ 时序坑（蓝图 §7.3 第 1 条）：stateRef 若只在 effect 里更新，「set 之后、effect 之前」
  // 的那次渲染会读到旧值，且之后无再渲染来纠正（isActive 是被 useSyncExternalStore 按需拉取的
  // 快照函数，不是 state，不会自动触发订阅方重渲染）。解法：set()/reset() 内部同步更新 ref 并
  // notify，不依赖 effect 时序。
  const stateRef = useRef(state)

  const set = useCallback((patch: Partial<T>) => {
    const next = { ...stateRef.current, ...patch }
    stateRef.current = next
    setState(next)
    patchListUrl(patch)
    notifyListFilters()
  }, [])

  // 注册到重置注册表：{ reset, isActive } 均从 stateRef 同步读取最新值，键集合从最新 state 读。
  useEffect(() => {
    const reset = () => {
      const cleared = Object.fromEntries(Object.keys(stateRef.current).map(k => [k, ''])) as Partial<T>
      const next = { ...stateRef.current, ...cleared }
      stateRef.current = next
      setState(next)
      patchListUrl(cleared)
      notifyListFilters()
    }
    const isActive = () => Object.values(stateRef.current).some((v) => v !== '')
    const snapshot = () => {
      const current: Record<string, string> = {}
      for (const [k, v] of Object.entries(stateRef.current)) {
        if (v !== '') current[k] = v
      }
      return current
    }
    const apply = (values: Record<string, string>) => {
      const patch = Object.fromEntries(
        Object.keys(stateRef.current).map((k) => [k, values[k] ?? '']),
      ) as Partial<T>
      const next = { ...stateRef.current, ...patch }
      stateRef.current = next
      setState(next)
      patchListUrl(patch)
      notifyListFilters()
    }
    const entry: FilterResetEntry = { reset, isActive, snapshot, apply }
    filterResets.add(entry)
    notifyListFilters()
    return () => { filterResets.delete(entry); notifyListFilters() }
  }, [])

  return [state, set]
}
