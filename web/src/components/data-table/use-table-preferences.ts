import { type ColumnDef, type ColumnSizingState, type PaginationState, type VisibilityState } from '@tanstack/react-table'
import { type Dispatch, type SetStateAction, useEffect, useRef, useState } from 'react'
import {
  TABLE_CLOUD_SYNC_EVENT,
  type TableCloudSyncDetail,
} from './table-preference-cloud'
import {
  reconcileColumnOrder,
  readStoredOrder,
  readStoredVisibility,
  readStoredPageSize,
  readStoredPinned,
  readStoredSizing,
  saveVisibility,
  savePageSize,
  savePinned,
  saveSizing,
} from './use-table-persistence'

/**
 * 已保存的列显隐优先。默认要藏的列如果存档里没提过，仍然藏起来——
 * 这样后加的 defaultHidden 能作用到老用户，而用户明确开过或关过的列保持原样。
 */
export function mergeStoredVisibility(
  stored: VisibilityState | null,
  defaults: VisibilityState,
): VisibilityState {
  if (!stored) return defaults
  const merged: VisibilityState = { ...stored }
  for (const [key, visible] of Object.entries(defaults)) {
    if (visible === false && stored[key] === undefined) merged[key] = false
  }
  return merged
}

interface TablePreferencesArgs<TData> {
  /** 持久化键（显式 tableKey 或按路径 + 列签名自动生成的兜底 key）。 */
  effectiveTableKey: string
  /** SSR 安全的默认列顺序（结构列固定左右 + 数据列自然序）。 */
  defaultColumnOrder: string[]
  /** SSR 安全的默认列显隐（审计列默认隐藏）。 */
  defaultVisibility: VisibilityState
  /** 系统默认每页条数，作为 localStorage 无记录时的 fallback。 */
  pageSize: number
  /** 当前表格列（reconcileColumnOrder 协调存储顺序时需要）。 */
  tableColumns: ColumnDef<TData, unknown>[]
  /** 客户端分页模式下挂载即将生效 pageSize 落盘；服务端分页由受控分支按需落盘，不在首帧写默认值。 */
  persistInitialPageSize?: boolean
}

export interface TablePreferences {
  columnOrder: string[]
  setColumnOrder: Dispatch<SetStateAction<string[]>>
  columnVisibility: VisibilityState
  setColumnVisibility: Dispatch<SetStateAction<VisibilityState>>
  pagination: PaginationState
  setPagination: Dispatch<SetStateAction<PaginationState>>
  /** 用户拖出来的列宽（列 id → px）；没拖过的列不在里面，按列定义自动排版。 */
  columnSizing: ColumnSizingState
  setColumnSizing: Dispatch<SetStateAction<ColumnSizingState>>
  /** 用户固定到左侧的列（按固定先后）；页面在列定义里钉的不在里面。 */
  pinned: string[]
  setPinned: Dispatch<SetStateAction<string[]>>
  /** 是否已从 localStorage 恢复偏好（未恢复前 DataTable 渲染骨架屏，避免 hydration mismatch）。 */
  hydrated: boolean
}

interface RestoredBaseline {
  key: string
  visibilityJson: string
  pageSize: number
  sizingJson: string
  pinnedJson: string
}

/**
 * DataTable 的用户偏好状态机：列顺序 / 列显隐 / 列宽 / 每页条数的持久化编排。
 *
 * 抽离动机：这三项的「SSR 安全初值 + 挂载后从 localStorage 恢复 + 变更即存」时序是
 * DataTable 里最易错的一坨（曾因 hydration effect 反复重跑把 pagination 重置，导致
 * 改每页条数不生效 / 编辑后跳回第 1 页）。收进独立 hook + 回归网锁住行为。
 *
 * ⚠️ 初值必须 SSR 安全（不读 localStorage），否则服务端与客户端首帧渲染的列顺序 / 显隐 /
 * 分页不一致，触发 hydration mismatch；持久化偏好在挂载后由下方 effect 统一应用。
 * columnOrder 的「变更即存」不在这里——它由调用方在 setColumnOrder 处附带 saveOrder
 * （拖拽 / onColumnOrderChange 两处），本 hook 只负责 visibility / sizing / pageSize 的自动保存。
 */
export function useTablePreferences<TData>({
  effectiveTableKey,
  defaultColumnOrder,
  defaultVisibility,
  pageSize,
  tableColumns,
  persistInitialPageSize = true,
}: TablePreferencesArgs<TData>): TablePreferences {
  const [columnVisibility, setColumnVisibility] = useState<VisibilityState>(defaultVisibility)
  const [pagination, setPagination] = useState<PaginationState>(() => ({ pageIndex: 0, pageSize }))
  const [columnOrder, setColumnOrder] = useState<string[]>(defaultColumnOrder)
  const [columnSizing, setColumnSizing] = useState<ColumnSizingState>({})
  const [pinned, setPinned] = useState<string[]>([])

  // 挂载前（SSR + 客户端首帧）渲染骨架屏，挂载后读完 localStorage 偏好再渲染真实表格，
  // 既避免 hydration mismatch，又不会让用户看到默认布局一闪而过。
  const [hydrated, setHydrated] = useState(false)
  // 仅在挂载 / 切换 tableKey 时恢复一次。防止可编辑表格（cell 随外部 state 变 → tableColumns
  // 频繁重建）令 hydration effect 反复重跑、把 pagination 重置（症状：改每页条数不生效、编辑后跳回第 1 页）。
  const hydratedKeyRef = useRef<string | null>(null)
  // 记录恢复基线：仅当调用方主动变更了某项状态（异于恢复基线）时才写回 localStorage，
  // 防止首帧挂载时把默认 15 条覆写进已存有 50/500 条的 localStorage。
  const baselineRef = useRef<RestoredBaseline | null>(null)
  const latestArgsRef = useRef({ defaultColumnOrder, defaultVisibility, pageSize, tableColumns })
  useEffect(() => {
    latestArgsRef.current = { defaultColumnOrder, defaultVisibility, pageSize, tableColumns }
  })

  useEffect(() => {
    if (hydratedKeyRef.current === effectiveTableKey) return
    hydratedKeyRef.current = effectiveTableKey
    const nextPinned = readStoredPinned(effectiveTableKey) ?? []
    const storedOrder = readStoredOrder(effectiveTableKey)
    const nextVis = mergeStoredVisibility(readStoredVisibility(effectiveTableKey), defaultVisibility)
    const nextPageSize = readStoredPageSize(effectiveTableKey, pageSize)
    const nextSizing = readStoredSizing(effectiveTableKey) ?? {}

    baselineRef.current = {
      key: effectiveTableKey,
      visibilityJson: JSON.stringify(nextVis),
      pageSize: nextPageSize,
      sizingJson: JSON.stringify(nextSizing),
      pinnedJson: JSON.stringify(nextPinned),
    }

    setPinned(nextPinned)
    setColumnOrder(reconcileColumnOrder(storedOrder ?? defaultColumnOrder, tableColumns, nextPinned))
    setColumnVisibility(nextVis)
    setPagination({ pageIndex: 0, pageSize: nextPageSize })
    if (persistInitialPageSize) {
      savePageSize(effectiveTableKey, nextPageSize)
    }
    setColumnSizing(nextSizing)
    setHydrated(true)
  }, [effectiveTableKey, defaultColumnOrder, defaultVisibility, pageSize, persistInitialPageSize, tableColumns])

  // 云端快照拉取完成后热更新本表状态，同时刷新恢复基线避免回声写入。
  useEffect(() => {
    function handleCloudSync(event: Event) {
      const detail = (event as CustomEvent<TableCloudSyncDetail>).detail
      if (!detail?.tables?.includes(effectiveTableKey)) return
      const {
        defaultColumnOrder: defOrder,
        defaultVisibility: defVis,
        pageSize: defSize,
        tableColumns: cols,
      } = latestArgsRef.current
      const nextPinned = readStoredPinned(effectiveTableKey) ?? []
      const storedOrder = readStoredOrder(effectiveTableKey)
      const nextVis = mergeStoredVisibility(readStoredVisibility(effectiveTableKey), defVis)
      const nextPageSize = readStoredPageSize(effectiveTableKey, defSize)
      const nextSizing = readStoredSizing(effectiveTableKey) ?? {}

      baselineRef.current = {
        key: effectiveTableKey,
        visibilityJson: JSON.stringify(nextVis),
        pageSize: nextPageSize,
        sizingJson: JSON.stringify(nextSizing),
        pinnedJson: JSON.stringify(nextPinned),
      }

      setPinned(nextPinned)
      setColumnOrder(reconcileColumnOrder(storedOrder ?? defOrder, cols, nextPinned))
      setColumnVisibility(nextVis)
      setPagination((prev) => (prev.pageSize === nextPageSize ? prev : { pageIndex: 0, pageSize: nextPageSize }))
      setColumnSizing(nextSizing)
    }

    window.addEventListener(TABLE_CLOUD_SYNC_EVENT, handleCloudSync)
    return () => window.removeEventListener(TABLE_CLOUD_SYNC_EVENT, handleCloudSync)
  }, [effectiveTableKey])

  useEffect(() => {
    if (!hydrated || baselineRef.current?.key !== effectiveTableKey) return
    const nextJson = JSON.stringify(columnVisibility)
    if (baselineRef.current.visibilityJson === nextJson) return
    baselineRef.current.visibilityJson = nextJson
    saveVisibility(effectiveTableKey, columnVisibility)
  }, [hydrated, effectiveTableKey, columnVisibility])

  useEffect(() => {
    if (!hydrated || baselineRef.current?.key !== effectiveTableKey) return
    if (baselineRef.current.pageSize === pagination.pageSize) return
    baselineRef.current.pageSize = pagination.pageSize
    savePageSize(effectiveTableKey, pagination.pageSize)
  }, [hydrated, effectiveTableKey, pagination.pageSize])

  useEffect(() => {
    if (!hydrated || baselineRef.current?.key !== effectiveTableKey) return
    const nextJson = JSON.stringify(columnSizing)
    if (baselineRef.current.sizingJson === nextJson) return
    baselineRef.current.sizingJson = nextJson
    saveSizing(effectiveTableKey, columnSizing)
  }, [hydrated, effectiveTableKey, columnSizing])

  useEffect(() => {
    if (!hydrated || baselineRef.current?.key !== effectiveTableKey) return
    const nextJson = JSON.stringify(pinned)
    if (baselineRef.current.pinnedJson === nextJson) return
    baselineRef.current.pinnedJson = nextJson
    savePinned(effectiveTableKey, pinned)
  }, [hydrated, effectiveTableKey, pinned])

  return {
    columnOrder,
    setColumnOrder,
    columnVisibility,
    setColumnVisibility,
    pagination,
    setPagination,
    columnSizing,
    setColumnSizing,
    pinned,
    setPinned,
    hydrated,
  }
}
