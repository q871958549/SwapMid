'use client'

import type {
  SaveTableDensityInput,
  SaveTableViewPreferenceInput,
  TablePreferenceSnapshotDto,
} from '@/lib/api/types'
import {
  applyCloudTablePreferences,
  readTableViewPayload,
  type TableDensity,
  type TablePreferenceSnapshotPayload,
} from './use-table-persistence'

export const TABLE_CLOUD_SYNC_EVENT = 'swapmid-table-cloud-sync'
export const TABLE_DENSITY_CHANGE_EVENT = 'swapmid-table-density-change'
export const TABLE_CLOUD_DEBOUNCE_MS = 500

export interface TableCloudSyncDetail {
  tables: string[]
}

const ENDPOINT = '/api/proxy/api/v1/table-preference'

const loadedPrincipals = new Set<string>()
const inflightByPrincipal = new Map<string, Promise<void>>()
const tableSyncTimers = new Map<string, ReturnType<typeof setTimeout>>()
const densitySyncTimers = new Map<string, ReturnType<typeof setTimeout>>()
const pendingKeysByPrincipal = new Map<string, Set<string>>()

function isValidCloudSnapshot(data: unknown): data is TablePreferenceSnapshotDto & TablePreferenceSnapshotPayload {
  if (!data || typeof data !== 'object') return false
  const record = data as Record<string, unknown>
  return typeof record.density === 'string'
    && record.tables !== null
    && typeof record.tables === 'object'
    && !Array.isArray(record.tables)
}

function markPendingTable(principalId: string, tableKey: string) {
  let set = pendingKeysByPrincipal.get(principalId)
  if (!set) {
    set = new Set<string>()
    pendingKeysByPrincipal.set(principalId, set)
  }
  set.add(tableKey)
}

function unmarkPendingTable(principalId: string, tableKey: string) {
  const set = pendingKeysByPrincipal.get(principalId)
  if (!set) return
  set.delete(tableKey)
  if (set.size === 0) pendingKeysByPrincipal.delete(principalId)
}

/**
 * 按当前登录账号单飞拉取一次云端列表视图偏好快照，落入本机存储并向当前页广播变更事件。
 * 未登录或网络异常时静默降级至本机缓存。
 */
export function ensureCloudTablePreferencesLoaded(principalId: string | null | undefined): Promise<void> {
  if (typeof window === 'undefined' || !principalId) {
    return Promise.resolve()
  }
  if (loadedPrincipals.has(principalId)) {
    return Promise.resolve()
  }
  const existing = inflightByPrincipal.get(principalId)
  if (existing) return existing

  const task = fetch(ENDPOINT, { method: 'GET' })
    .then(async (res) => {
      if (!res.ok) return
      const data: unknown = await res.json().catch(() => null)
      if (!isValidCloudSnapshot(data)) return
      loadedPrincipals.add(principalId)
      const { changedTables, densityChanged } = applyCloudTablePreferences(
        principalId,
        data,
        pendingKeysByPrincipal.get(principalId),
      )
      if (densityChanged) {
        window.dispatchEvent(new Event(TABLE_DENSITY_CHANGE_EVENT))
      }
      if (changedTables.length > 0) {
        window.dispatchEvent(
          new CustomEvent<TableCloudSyncDetail>(TABLE_CLOUD_SYNC_EVENT, {
            detail: { tables: changedTables },
          }),
        )
      }
    })
    .catch(() => {})
    .finally(() => {
      inflightByPrincipal.delete(principalId)
    })

  inflightByPrincipal.set(principalId, task)
  return task
}

/**
 * 单张列表视图偏好（列序/显隐/列宽/固定列/每页条数/自定义档位）变更后 500ms 防抖合并写回云端。
 */
export function scheduleCloudTableViewSync(
  principalId: string | null | undefined,
  tableKey: string | undefined,
): void {
  if (typeof window === 'undefined' || !principalId || !tableKey) return
  const timerKey = `${principalId}::${tableKey}`
  const prevTimer = tableSyncTimers.get(timerKey)
  if (prevTimer) clearTimeout(prevTimer)
  markPendingTable(principalId, tableKey)

  const timer = setTimeout(() => {
    tableSyncTimers.delete(timerKey)
    unmarkPendingTable(principalId, tableKey)
    const view = readTableViewPayload(tableKey)
    const payload: SaveTableViewPreferenceInput = { tableKey, view }
    fetch(`${ENDPOINT}/table`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    }).catch(() => {})
  }, TABLE_CLOUD_DEBOUNCE_MS)

  tableSyncTimers.set(timerKey, timer)
}

/**
 * 全局列表行密度变更后 500ms 防抖写回云端。
 */
export function scheduleCloudTableDensitySync(
  principalId: string | null | undefined,
  density: TableDensity,
): void {
  if (typeof window === 'undefined' || !principalId) return
  const prevTimer = densitySyncTimers.get(principalId)
  if (prevTimer) clearTimeout(prevTimer)

  const timer = setTimeout(() => {
    densitySyncTimers.delete(principalId)
    const payload: SaveTableDensityInput = { density }
    fetch(`${ENDPOINT}/density`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    }).catch(() => {})
  }, TABLE_CLOUD_DEBOUNCE_MS)

  densitySyncTimers.set(principalId, timer)
}

/** 仅供单元测试重置内存单飞状态与防抖计时器。 */
export function resetTablePreferenceCloudStateForTests(): void {
  loadedPrincipals.clear()
  inflightByPrincipal.clear()
  for (const timer of tableSyncTimers.values()) clearTimeout(timer)
  tableSyncTimers.clear()
  for (const timer of densitySyncTimers.values()) clearTimeout(timer)
  densitySyncTimers.clear()
  pendingKeysByPrincipal.clear()
}
