'use client'

import { useEffect, useState } from 'react'
import type {
  ColumnDef,
  ColumnFiltersState,
  ColumnSizingState,
  SortingState,
  VisibilityState,
} from '@tanstack/react-table'
import {
  applyRegisteredListFilters,
  snapshotRegisteredListFilters,
} from '@/lib/crud/use-url-list-state'
import {
  clampColumnSizing,
  getColumnKey,
  MAX_SAVED_PRESET_NAME_LENGTH,
  MAX_SAVED_PRESETS_PER_TABLE,
  readStoredPresets,
  reconcileColumnOrder,
  saveOrder,
  savePinned,
  savePresets,
  saveSizing,
  saveVisibility,
  type TableSavedPresetPayload,
} from './use-table-persistence'
import {
  scheduleCloudTableViewSync,
  TABLE_CLOUD_SYNC_EVENT,
  type TableCloudSyncDetail,
} from './table-preference-cloud'

export interface TableSavedViewsController {
  presets: TableSavedPresetPayload[]
  maxPresets: number
  activePresetId: string | null
  saveCurrentAsPreset: (name: string) => TableSavedPresetPayload | null
  applyPreset: (presetId: string) => void
  deletePreset: (presetId: string) => void
}

interface UseTableSavedViewsOptions<TData> {
  effectiveTableKey: string
  hydrated: boolean
  storagePrincipalId: string | null
  tableColumns: ColumnDef<TData, unknown>[]
  defaultColumnOrder: string[]
  defaultVisibility: VisibilityState
  columnOrder: string[]
  setColumnOrder: (order: string[]) => void
  columnVisibility: VisibilityState
  setColumnVisibility: (vis: VisibilityState) => void
  columnSizing: ColumnSizingState
  setColumnSizing: (sizing: ColumnSizingState) => void
  pinned: string[]
  setPinned: (pinned: string[]) => void
  enableSearch: boolean
  globalFilter: string
  setGlobalFilter: (value: string) => void
  manualPagination: boolean
  onSearchChange?: (value: string) => void
  onPaginationChange?: (next: { pageIndex: number; pageSize: number }) => void
  effectivePageSize: number
  manualSorting: boolean
  effectiveSorting: SortingState
  onSortingChange?: (sorting: SortingState) => void
  setSorting: (sorting: SortingState) => void
  columnFilters: ColumnFiltersState
  setColumnFilters: (filters: ColumnFiltersState) => void
  setShowFilters: (show: boolean) => void
}

function parseSavedColumnFilters(raw: TableSavedPresetPayload['columnFilters']): ColumnFiltersState {
  if (!raw?.length) return []
  const out: ColumnFiltersState = []
  for (const item of raw) {
    try {
      out.push({ id: item.id, value: JSON.parse(item.value) })
    } catch {
      out.push({ id: item.id, value: item.value })
    }
  }
  return out
}

export function useTableSavedViews<TData>({
  effectiveTableKey,
  hydrated,
  storagePrincipalId,
  tableColumns,
  defaultColumnOrder,
  defaultVisibility,
  columnOrder,
  setColumnOrder,
  columnVisibility,
  setColumnVisibility,
  columnSizing,
  setColumnSizing,
  pinned,
  setPinned,
  enableSearch,
  globalFilter,
  setGlobalFilter,
  manualPagination,
  onSearchChange,
  onPaginationChange,
  effectivePageSize,
  manualSorting,
  effectiveSorting,
  onSortingChange,
  setSorting,
  columnFilters,
  setColumnFilters,
  setShowFilters,
}: UseTableSavedViewsOptions<TData>): TableSavedViewsController {
  const [state, setState] = useState<{
    tableKey: string
    presets: TableSavedPresetPayload[]
    activePresetId: string | null
  }>({ tableKey: '', presets: [], activePresetId: null })

  useEffect(() => {
    if (!hydrated) return
    const loaded = readStoredPresets(effectiveTableKey)
    const timer = window.setTimeout(() => {
      setState({ tableKey: effectiveTableKey, presets: loaded, activePresetId: null })
    }, 0)
    return () => window.clearTimeout(timer)
  }, [hydrated, effectiveTableKey])

  useEffect(() => {
    function handleCloudSync(event: Event) {
      const detail = (event as CustomEvent<TableCloudSyncDetail>).detail
      if (!detail?.tables?.includes(effectiveTableKey)) return
      const loaded = readStoredPresets(effectiveTableKey)
      setState((prev) => ({
        tableKey: effectiveTableKey,
        presets: loaded,
        activePresetId: loaded.some((p) => p.id === prev.activePresetId) ? prev.activePresetId : null,
      }))
    }
    window.addEventListener(TABLE_CLOUD_SYNC_EVENT, handleCloudSync)
    return () => window.removeEventListener(TABLE_CLOUD_SYNC_EVENT, handleCloudSync)
  }, [effectiveTableKey])

  const presets = state.tableKey === effectiveTableKey ? state.presets : []
  const activePresetId = state.tableKey === effectiveTableKey ? state.activePresetId : null

  function saveCurrentAsPreset(rawName: string): TableSavedPresetPayload | null {
    const name = rawName.trim().slice(0, MAX_SAVED_PRESET_NAME_LENGTH)
    if (!name) return null

    const currentList = readStoredPresets(effectiveTableKey)
    const existingIndex = currentList.findIndex((p) => p.name === name)
    if (existingIndex === -1 && currentList.length >= MAX_SAVED_PRESETS_PER_TABLE) {
      return null
    }

    const urlSnapshot = snapshotRegisteredListFilters()
    const id = existingIndex >= 0
      ? currentList[existingIndex].id
      : `v_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 5)}`

    const effectiveOrder = reconcileColumnOrder(columnOrder, tableColumns, pinned)
    const baselineOrder = reconcileColumnOrder(defaultColumnOrder, tableColumns, pinned)
    const isDefaultOrder =
      effectiveOrder.length === baselineOrder.length &&
      effectiveOrder.every((colId, idx) => colId === baselineOrder[idx])

    const visibilityDiff: VisibilityState = {}
    const visibilityKeys = new Set([
      ...Object.keys(defaultVisibility),
      ...Object.keys(columnVisibility),
    ])
    for (const key of visibilityKeys) {
      const currentVisible = columnVisibility[key] ?? true
      const baselineVisible = defaultVisibility[key] ?? true
      if (currentVisible !== baselineVisible) {
        visibilityDiff[key] = currentVisible
      }
    }

    const entry: TableSavedPresetPayload = {
      id,
      name,
      order: isDefaultOrder ? null : effectiveOrder,
      visibility: Object.keys(visibilityDiff).length > 0 ? visibilityDiff : null,
      sizing: Object.keys(columnSizing).length > 0 ? clampColumnSizing(columnSizing) : null,
      pinned: pinned.length > 0 ? [...pinned] : null,
      search: enableSearch && globalFilter.trim() ? globalFilter.trim().slice(0, 64) : null,
      sortItems: effectiveSorting.length > 0
        ? effectiveSorting.slice(0, 1).map((s) => ({ id: s.id, desc: Boolean(s.desc) }))
        : null,
      columnFilters: !manualPagination && columnFilters.length > 0
        ? columnFilters.map((f) => ({ id: f.id, value: JSON.stringify(f.value) }))
        : null,
      urlFilters: Object.keys(urlSnapshot).length > 0 ? urlSnapshot : null,
    }

    const next = existingIndex >= 0
      ? currentList.map((item, idx) => (idx === existingIndex ? entry : item))
      : [...currentList, entry]

    savePresets(effectiveTableKey, next)
    setState({ tableKey: effectiveTableKey, presets: next, activePresetId: entry.id })
    scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
    return entry
  }

  function applyPreset(presetId: string) {
    const target = presets.find((p) => p.id === presetId)
    if (!target) return

    const nextPinned = target.pinned ? [...target.pinned] : []
    const nextOrder = reconcileColumnOrder(target.order ?? defaultColumnOrder, tableColumns, nextPinned)
    const nextVisibility: VisibilityState = { ...defaultVisibility, ...(target.visibility ?? {}) }
    const nextSizing: ColumnSizingState = target.sizing ? clampColumnSizing(target.sizing) : {}

    setPinned(nextPinned)
    savePinned(effectiveTableKey, nextPinned)
    setColumnOrder(nextOrder)
    saveOrder(effectiveTableKey, nextOrder)
    setColumnVisibility(nextVisibility)
    saveVisibility(effectiveTableKey, nextVisibility)
    setColumnSizing(nextSizing)
    saveSizing(effectiveTableKey, nextSizing)

    const nextSearch = enableSearch ? (target.search ?? '') : ''
    setGlobalFilter(nextSearch)
    if (manualPagination) {
      onSearchChange?.(nextSearch)
    }

    const sortableIds = new Set(
      tableColumns
        .filter((col) => col.enableSorting !== false)
        .map(getColumnKey)
        .filter(Boolean),
    )
    const nextSorting: SortingState = (target.sortItems ?? [])
      .filter((s) => sortableIds.has(s.id))
      .slice(0, 1)
      .map((s) => ({ id: s.id, desc: Boolean(s.desc) }))

    if (manualSorting) {
      onSortingChange?.(nextSorting)
    } else {
      setSorting(nextSorting)
    }

    if (!manualPagination) {
      const parsedFilters = parseSavedColumnFilters(target.columnFilters)
      setColumnFilters(parsedFilters)
      setShowFilters(parsedFilters.length > 0)
    }

    applyRegisteredListFilters(target.urlFilters ?? {})
    if (manualPagination && onPaginationChange) {
      onPaginationChange({ pageIndex: 0, pageSize: effectivePageSize })
    }

    setState((prev) => ({ ...prev, activePresetId: target.id }))
    scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
  }

  function deletePreset(presetId: string) {
    const next = presets.filter((p) => p.id !== presetId)
    savePresets(effectiveTableKey, next)
    setState({
      tableKey: effectiveTableKey,
      presets: next,
      activePresetId: activePresetId === presetId ? null : activePresetId,
    })
    scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
  }

  return {
    presets,
    maxPresets: MAX_SAVED_PRESETS_PER_TABLE,
    activePresetId,
    saveCurrentAsPreset,
    applyPreset,
    deletePreset,
  }
}
