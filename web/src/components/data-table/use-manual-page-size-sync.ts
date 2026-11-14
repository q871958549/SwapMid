'use client'

import { useEffect, useRef } from 'react'
import { INTERACTIVE_LIST_MAX_PAGE_SIZE, readStoredPageSize } from './use-table-persistence'
import { TABLE_CLOUD_SYNC_EVENT, type TableCloudSyncDetail } from './table-preference-cloud'

interface UseManualPageSizeSyncOptions {
  effectiveTableKey: string
  manualPagination: boolean
  onPaginationChange?: (next: { pageIndex: number; pageSize: number }) => void
  maxPageSize?: number
  manualPageSize?: number
  pageSize: number
}

function hasExplicitUrlPageSize(): boolean {
  try {
    if (typeof window === 'undefined') return false
    return new URLSearchParams(window.location.search).has('size')
  } catch {
    return false
  }
}

/**
 * 服务端分页模式下的每页条数跨会话与跨设备恢复：
 * 显式 URL `?size=` 优先，否则在挂载或云端同步到达时应用已存 `pageSize`。
 */
export function useManualPageSizeSync({
  effectiveTableKey,
  manualPagination,
  onPaginationChange,
  maxPageSize,
  manualPageSize,
  pageSize,
}: UseManualPageSizeSyncOptions): void {
  const manualSizeRestoredKeyRef = useRef<string | null>(null)
  const manualPageSizeSyncRef = useRef({
    manualPagination,
    onPaginationChange,
    maxPageSize,
    manualPageSize,
    pageSize,
  })

  useEffect(() => {
    manualPageSizeSyncRef.current = {
      manualPagination,
      onPaginationChange,
      maxPageSize,
      manualPageSize,
      pageSize,
    }
  })

  useEffect(() => {
    if (!manualPagination || !onPaginationChange) return
    if (manualSizeRestoredKeyRef.current === effectiveTableKey) return
    manualSizeRestoredKeyRef.current = effectiveTableKey
    if (hasExplicitUrlPageSize()) return
    const stored = readStoredPageSize(effectiveTableKey, 0)
    if (stored <= 0) return
    const effectiveMax = maxPageSize ?? INTERACTIVE_LIST_MAX_PAGE_SIZE
    const clamped = Math.min(Math.max(1, Math.floor(stored)), effectiveMax)
    const currentSize = manualPageSize ?? pageSize
    if (clamped !== currentSize) {
      onPaginationChange({ pageIndex: 0, pageSize: clamped })
    }
  }, [manualPagination, onPaginationChange, effectiveTableKey, maxPageSize, manualPageSize, pageSize])

  useEffect(() => {
    function handleCloudSync(event: Event) {
      const detail = (event as CustomEvent<TableCloudSyncDetail>).detail
      if (!detail?.tables?.includes(effectiveTableKey)) return
      const latest = manualPageSizeSyncRef.current
      if (!latest.manualPagination || !latest.onPaginationChange) return
      if (hasExplicitUrlPageSize()) return
      const stored = readStoredPageSize(effectiveTableKey, 0)
      const effectiveMax = latest.maxPageSize ?? INTERACTIVE_LIST_MAX_PAGE_SIZE
      const targetSize = stored > 0
        ? Math.min(Math.max(1, Math.floor(stored)), effectiveMax)
        : latest.pageSize
      const currentSize = latest.manualPageSize ?? latest.pageSize
      if (targetSize !== currentSize) {
        latest.onPaginationChange({ pageIndex: 0, pageSize: targetSize })
      }
    }
    window.addEventListener(TABLE_CLOUD_SYNC_EVENT, handleCloudSync)
    return () => window.removeEventListener(TABLE_CLOUD_SYNC_EVENT, handleCloudSync)
  }, [effectiveTableKey])
}
