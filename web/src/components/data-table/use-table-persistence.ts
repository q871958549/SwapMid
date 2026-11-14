import { type ColumnDef, type ColumnSizingState, type VisibilityState } from '@tanstack/react-table'
export { INTERACTIVE_LIST_MAX_PAGE_SIZE, SERVER_MAX_PAGE_SIZE } from '@/lib/crud/list-pagination'

// 结构列 ID（非数据列）：左固定「选择」列、右固定「操作」列，排除出列筛选 / 拖拽 / 列顺序协调。
export const LEFT_STRUCTURAL_COL_IDS = new Set(['select'])
export const RIGHT_STRUCTURAL_COL_IDS = new Set(['actions'])
export const STRUCTURAL_COL_IDS = new Set([...LEFT_STRUCTURAL_COL_IDS, ...RIGHT_STRUCTURAL_COL_IDS])

type DataColumn<TData> = ColumnDef<TData, unknown> & { accessorKey?: string }

// 列身份键：优先 accessorKey，回退 column.id。列顺序 / 显隐 / 持久化全程以它为准。
export function getColumnKey<TData>(column: ColumnDef<TData, unknown>): string {
  const dataColumn = column as DataColumn<TData>
  return dataColumn.accessorKey ?? column.id ?? ''
}

function hashText(text: string): string {
  let hash = 0
  for (let i = 0; i < text.length; i += 1) {
    hash = ((hash << 5) - hash + text.charCodeAt(i)) | 0
  }
  return Math.abs(hash).toString(36)
}

// 持久化键：显式 tableKey 优先，否则按当前路径 + 列签名哈希自动生成兜底 key。
export function deriveTableKey<TData>(explicitKey: string | undefined, pathname: string | null, columns: ColumnDef<TData, unknown>[]): string {
  if (explicitKey?.trim()) return explicitKey.trim()
  const pathKey = (pathname ?? 'table')
    .replace(/[^a-zA-Z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .toLowerCase() || 'table'
  const columnKey = columns.map(getColumnKey).filter(Boolean).join('|')
  return `${pathKey}:${hashText(columnKey)}`
}

function isPinnedLeft<TData>(column: ColumnDef<TData, unknown> | undefined): boolean {
  return column?.meta?.pin === 'left'
}

// 列顺序协调：结构列恒定左/右，左钉列紧跟选择列且不跟存储顺序走，普通列按存储顺序、新增列补尾。
// 左钉列 = 页面在列定义里钉的（meta.pin）在前，用户在列设置里固定的（userPinned，按固定先后）在后。
export function reconcileColumnOrder<TData>(order: string[], columns: ColumnDef<TData, unknown>[], userPinned: string[] = []): string[] {
  const byId = new Map(columns.map((column) => [getColumnKey(column), column]))
  const ids = columns.map(getColumnKey).filter(Boolean)
  const known = new Set(ids)
  const left = ids.filter((id) => LEFT_STRUCTURAL_COL_IDS.has(id))
  const pagePinned = ids.filter((id) => isPinnedLeft(byId.get(id)))
  const pinned = [
    ...pagePinned,
    ...userPinned.filter((id) => known.has(id) && !STRUCTURAL_COL_IDS.has(id) && !pagePinned.includes(id)),
  ]
  const right = ids.filter((id) => RIGHT_STRUCTURAL_COL_IDS.has(id))
  const locked = new Set([...left, ...pinned, ...right])
  const normal = ids.filter((id) => !locked.has(id))
  const storedNormal = order.filter((id) => known.has(id) && !locked.has(id))
  const missingNormal = normal.filter((id) => !storedNormal.includes(id))
  return [...left, ...pinned, ...storedNormal, ...missingNormal, ...right]
}

export interface LeftStickySpec {
  id: string
  /** 选择列，或 meta.pin = 'left'。 */
  sticky: boolean
  width: number
}

/** 从左往右连续的钉住列：偏移是前面钉住列宽度之和，最后一列才画右侧阴影。 */
export function leftStickyLayout(columns: LeftStickySpec[]): Map<string, { offset: number; edge: boolean }> {
  const sticky: { id: string; width: number }[] = []
  for (const column of columns) {
    if (!column.sticky) break
    sticky.push({ id: column.id, width: column.width })
  }
  const layout = new Map<string, { offset: number; edge: boolean }>()
  let offset = 0
  sticky.forEach((column, index) => {
    layout.set(column.id, { offset, edge: index === sticky.length - 1 })
    offset += column.width
  })
  return layout
}

function readStoredJson<T>(key: string): T | null {
  try {
    if (typeof window === 'undefined') return null
    const raw = localStorage.getItem(key)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

function saveStoredJson<T>(key: string, value: T) {
  try {
    if (typeof window === 'undefined') return
    localStorage.setItem(key, JSON.stringify(value))
  } catch {}
}

function removeStoredKey(key: string) {
  try {
    if (typeof window === 'undefined') return
    localStorage.removeItem(key)
  } catch {}
}

function tablePrefKey(tableKey: string, name: 'order' | 'visibility' | 'pageSize' | 'sizing' | 'pinned' | 'presets'): string {
  return `swapmid-table:${tableKey}:${name}`
}

export function readStoredOrder(tableKey: string): string[] | null {
  return readStoredJson<string[]>(tablePrefKey(tableKey, 'order'))
    ?? readStoredJson<string[]>(`swapmid-cols:${tableKey}`)
}

export function saveOrder(tableKey: string, order: string[]) {
  saveStoredJson(tablePrefKey(tableKey, 'order'), order)
}

export function readStoredVisibility(tableKey: string): VisibilityState | null {
  return readStoredJson<VisibilityState>(tablePrefKey(tableKey, 'visibility'))
}

export function saveVisibility(tableKey: string, visibility: VisibilityState) {
  saveStoredJson(tablePrefKey(tableKey, 'visibility'), visibility)
}

export function readStoredSizing(tableKey: string): ColumnSizingState | null {
  return readStoredJson<ColumnSizingState>(tablePrefKey(tableKey, 'sizing'))
}

export function saveSizing(tableKey: string, sizing: ColumnSizingState) {
  saveStoredJson(tablePrefKey(tableKey, 'sizing'), sizing)
}

/** 用户在列设置里固定到左侧的列（按固定先后）。 */
export function readStoredPinned(tableKey: string): string[] | null {
  return readStoredJson<string[]>(tablePrefKey(tableKey, 'pinned'))
}

export function savePinned(tableKey: string, pinned: string[]) {
  saveStoredJson(tablePrefKey(tableKey, 'pinned'), pinned)
}

export type TableDensity = 'standard' | 'compact'

// 行密度是个人习惯，所有列表共用一个设置（与每页条数选项的全局键同理），不按表存。
const DENSITY_KEY = 'swapmid-table:density'

export function readStoredDensity(): TableDensity {
  return readStoredJson<string>(DENSITY_KEY) === 'compact' ? 'compact' : 'standard'
}

export function saveDensity(density: TableDensity) {
  saveStoredJson(DENSITY_KEY, density)
}

/** 用户拖出来的列宽上下限（px）。列定义里的默认宽度不受此限。 */
export const COLUMN_RESIZE_MIN = 64
export const COLUMN_RESIZE_MAX = 800

export function clampColumnSizing(sizing: ColumnSizingState): ColumnSizingState {
  return Object.fromEntries(Object.entries(sizing).map(([id, size]) =>
    [id, Math.round(Math.min(COLUMN_RESIZE_MAX, Math.max(COLUMN_RESIZE_MIN, size)))]))
}

export function readStoredPageSize(tableKey: string, fallback: number): number {
  const stored = readStoredJson<number>(tablePrefKey(tableKey, 'pageSize'))
  return stored && stored > 0 ? stored : fallback
}

export function savePageSize(tableKey: string, value: number) {
  saveStoredJson(tablePrefKey(tableKey, 'pageSize'), value)
}

// 系统默认每页条数选项，所有列表共用。
export const BASE_PAGE_SIZE_OPTIONS = [10, 15, 20, 50, 100]
// 自定义每页条数上限（客户端分页：一次性加载全部、前端切片，不受后端单次返回上限约束）。
export const MAX_PAGE_SIZE = 10000
// 旧全局键：只读回退，不再写入。一页加 1000、所有表下拉都出现，是账号列表「切档转很久」的放大器。
const GLOBAL_CUSTOM_PAGE_SIZES_KEY = 'swapmid-table:page-size-options'

function scopedCustomPageSizesKey(tableKey: string) {
  return `swapmid-table:${tableKey}:page-size-options`
}

function sanitizePageSizes(values: unknown): number[] {
  if (!Array.isArray(values)) return []
  return Array.from(
    new Set(
      values.filter(
        (n): n is number => typeof n === 'number' && Number.isInteger(n) && n > 0 && n <= MAX_PAGE_SIZE,
      ),
    ),
  ).sort((a, b) => a - b)
}

function writeCustomPageSizes(next: number[], tableKey?: string) {
  saveStoredJson(tableKey ? scopedCustomPageSizesKey(tableKey) : GLOBAL_CUSTOM_PAGE_SIZES_KEY, next)
}

function clampPersistMax(maxPageSize: number | undefined): number {
  if (typeof maxPageSize !== 'number' || !Number.isInteger(maxPageSize) || maxPageSize <= 0) {
    return MAX_PAGE_SIZE
  }
  return Math.min(maxPageSize, MAX_PAGE_SIZE)
}

/** 按本页上限拆成可用 / 不可用。全局旧键里的 500/1000 在 200 上限页只进不可用组。 */
export function splitCustomPageSizes(values: number[], maxPageSize: number): {
  usable: number[]
  unavailable: number[]
} {
  const max = clampPersistMax(maxPageSize)
  const usable: number[] = []
  const unavailable: number[] = []
  for (const n of values) {
    if (n <= max) usable.push(n)
    else unavailable.push(n)
  }
  return { usable, unavailable }
}

// 读取用户自定义的每页条数（已去非法值、去重、升序）。
// 有 tableKey 时优先读本表键；尚未写过则回退全局旧键，老用户不丢已加档位。
export function readCustomPageSizes(tableKey?: string): number[] {
  if (tableKey) {
    const scoped = readStoredJson<unknown>(scopedCustomPageSizesKey(tableKey))
    if (scoped !== null) return sanitizePageSizes(scoped)
  }
  return sanitizePageSizes(readStoredJson<unknown>(GLOBAL_CUSTOM_PAGE_SIZES_KEY))
}

// 添加一个自定义每页条数；非法（非正整数 / 超本页上限）返回 null。
// 有 tableKey 时只写本表键，且只持久化 ≤ 本页上限的档位，避免把全局 500/1000 写进 200 上限页。
export function addCustomPageSize(
  value: number,
  tableKey?: string,
  maxPageSize = MAX_PAGE_SIZE,
): number[] | null {
  const max = clampPersistMax(maxPageSize)
  if (!Number.isInteger(value) || value <= 0 || value > max) return null
  const { usable } = splitCustomPageSizes(readCustomPageSizes(tableKey), max)
  const next = sanitizePageSizes([...usable, value])
  writeCustomPageSizes(next, tableKey)
  return next
}

// 移除一个自定义每页条数（系统默认项移不掉，传入也无副作用）。
// 写入同样只保留 ≤ 本页上限的档位，顺带把本表记录从全局超限档位上切开。
export function removeCustomPageSize(
  value: number,
  tableKey?: string,
  maxPageSize = MAX_PAGE_SIZE,
): number[] {
  const max = clampPersistMax(maxPageSize)
  const { usable } = splitCustomPageSizes(readCustomPageSizes(tableKey), max)
  const next = usable.filter((n) => n !== value)
  writeCustomPageSizes(next, tableKey)
  return next
}

/** 把本表自定义档位收敛为当前上限内的项（从本表移除全局遗留的超限档位，不改全局旧键）。 */
export function retainUsableCustomPageSizes(tableKey: string | undefined, maxPageSize: number): number[] {
  const { usable } = splitCustomPageSizes(readCustomPageSizes(tableKey), maxPageSize)
  writeCustomPageSizes(usable, tableKey)
  return usable
}

// ===== 云端列表视图偏好同步快照（本文件是 DataTable 唯一允许直碰 localStorage 的出口） =====

export interface TableSavedSortItemPayload {
  id: string
  desc: boolean
}

export interface TableSavedColumnFilterPayload {
  id: string
  value: string
}

export interface TableSavedPresetPayload {
  id: string
  name: string
  order?: string[] | null
  visibility?: VisibilityState | null
  sizing?: ColumnSizingState | null
  pinned?: string[] | null
  search?: string | null
  sortItems?: TableSavedSortItemPayload[] | null
  columnFilters?: TableSavedColumnFilterPayload[] | null
  urlFilters?: Record<string, string> | null
}

export const MAX_SAVED_PRESETS_PER_TABLE = 5
export const MAX_SAVED_PRESET_NAME_LENGTH = 16

export function sanitizeStoredPresets(raw: unknown): TableSavedPresetPayload[] {
  if (!Array.isArray(raw)) return []
  const result: TableSavedPresetPayload[] = []
  const seenIds = new Set<string>()
  for (const item of raw) {
    if (!item || typeof item !== 'object') continue
    const candidate = item as Partial<TableSavedPresetPayload>
    const id = typeof candidate.id === 'string' ? candidate.id.trim().slice(0, 32) : ''
    const name = typeof candidate.name === 'string' ? candidate.name.trim().slice(0, MAX_SAVED_PRESET_NAME_LENGTH) : ''
    if (!id || !name || seenIds.has(id)) continue
    seenIds.add(id)
    result.push({
      id,
      name,
      order: Array.isArray(candidate.order) ? candidate.order.filter((x): x is string => typeof x === 'string' && x.length > 0) : null,
      visibility: candidate.visibility && typeof candidate.visibility === 'object' && !Array.isArray(candidate.visibility)
        ? candidate.visibility
        : null,
      sizing: candidate.sizing && typeof candidate.sizing === 'object' && !Array.isArray(candidate.sizing)
        ? clampColumnSizing(candidate.sizing)
        : null,
      pinned: Array.isArray(candidate.pinned) ? candidate.pinned.filter((x): x is string => typeof x === 'string' && x.length > 0) : null,
      search: typeof candidate.search === 'string' && candidate.search.trim() ? candidate.search.trim().slice(0, 64) : null,
      sortItems: Array.isArray(candidate.sortItems)
        ? candidate.sortItems
          .filter((s): s is TableSavedSortItemPayload => !!s && typeof s.id === 'string' && s.id.length > 0)
          .map((s) => ({ id: s.id, desc: Boolean(s.desc) }))
        : null,
      columnFilters: Array.isArray(candidate.columnFilters)
        ? candidate.columnFilters
          .filter((f): f is TableSavedColumnFilterPayload => !!f && typeof f.id === 'string' && typeof f.value === 'string' && f.id.length > 0)
          .map((f) => ({ id: f.id, value: f.value }))
        : null,
      urlFilters: candidate.urlFilters && typeof candidate.urlFilters === 'object' && !Array.isArray(candidate.urlFilters)
        ? Object.fromEntries(
          Object.entries(candidate.urlFilters).filter(([k, v]) => typeof k === 'string' && typeof v === 'string' && k.trim() && v.trim()),
        )
        : null,
    })
    if (result.length >= MAX_SAVED_PRESETS_PER_TABLE) break
  }
  return result
}

export function readStoredPresets(tableKey: string): TableSavedPresetPayload[] {
  return sanitizeStoredPresets(readStoredJson<unknown>(tablePrefKey(tableKey, 'presets')))
}

export function savePresets(tableKey: string, presets: TableSavedPresetPayload[]) {
  const sanitized = sanitizeStoredPresets(presets)
  if (sanitized.length === 0) {
    removeStoredKey(tablePrefKey(tableKey, 'presets'))
    return
  }
  saveStoredJson(tablePrefKey(tableKey, 'presets'), sanitized)
}

export interface TableViewPreferencePayload {
  order?: string[] | null
  visibility?: VisibilityState | null
  sizing?: ColumnSizingState | null
  pinned?: string[] | null
  pageSize?: number | null
  customPageSizes?: number[] | null
  presets?: TableSavedPresetPayload[] | null
}

export interface TablePreferenceSnapshotPayload {
  density?: string | null
  tables?: Record<string, TableViewPreferencePayload> | null
}

const OWNER_KEY = 'swapmid-table:owner'
const TABLE_KEY_PREFIX = 'swapmid-table:'
const LEGACY_COLS_PREFIX = 'swapmid-cols:'
const KNOWN_TABLE_SUFFIXES = [':order', ':visibility', ':sizing', ':pinned', ':pageSize', ':page-size-options', ':presets'] as const

function extractTableKeyFromStorageKey(storageKey: string): string | null {
  if (storageKey.startsWith(LEGACY_COLS_PREFIX)) {
    return storageKey.slice(LEGACY_COLS_PREFIX.length) || null
  }
  if (!storageKey.startsWith(TABLE_KEY_PREFIX)) return null
  if (storageKey === DENSITY_KEY || storageKey === GLOBAL_CUSTOM_PAGE_SIZES_KEY || storageKey === OWNER_KEY) {
    return null
  }
  for (const suffix of KNOWN_TABLE_SUFFIXES) {
    if (storageKey.endsWith(suffix)) {
      const key = storageKey.slice(TABLE_KEY_PREFIX.length, storageKey.length - suffix.length)
      return key || null
    }
  }
  return null
}

function listStoredTableKeys(): { storageKey: string; tableKey: string | null }[] {
  const entries: { storageKey: string; tableKey: string | null }[] = []
  try {
    if (typeof window === 'undefined') return entries
    for (let i = 0; i < localStorage.length; i += 1) {
      const k = localStorage.key(i)
      if (!k) continue
      if (k.startsWith(TABLE_KEY_PREFIX) || k.startsWith(LEGACY_COLS_PREFIX)) {
        entries.push({ storageKey: k, tableKey: extractTableKeyFromStorageKey(k) })
      }
    }
  } catch {}
  return entries
}

function syncScopedKey<T>(key: string, nextValue: T | null | undefined): boolean {
  const prevRaw = (() => {
    try {
      return typeof window === 'undefined' ? null : localStorage.getItem(key)
    } catch {
      return null
    }
  })()
  if (nextValue === null || nextValue === undefined) {
    if (prevRaw === null) return false
    removeStoredKey(key)
    return true
  }
  const nextRaw = JSON.stringify(nextValue)
  if (prevRaw === nextRaw) return false
  try {
    if (typeof window !== 'undefined') localStorage.setItem(key, nextRaw)
  } catch {}
  return true
}

/** 组装单张表当前需同步至云端的紧凑载荷；若全部恢复默认则返回 null（通知后端清除该表记录）。 */
export function readTableViewPayload(tableKey: string): TableViewPreferencePayload | null {
  const rawOrder = readStoredJson<string[]>(tablePrefKey(tableKey, 'order'))
  const order = Array.isArray(rawOrder) && rawOrder.length > 0 ? rawOrder : null

  const rawVis = readStoredVisibility(tableKey)
  const visibility = rawVis && Object.keys(rawVis).length > 0 ? rawVis : null

  const rawSizing = readStoredSizing(tableKey)
  const sizing = rawSizing && Object.keys(rawSizing).length > 0 ? clampColumnSizing(rawSizing) : null

  const rawPinned = readStoredPinned(tableKey)
  const pinned = Array.isArray(rawPinned) && rawPinned.length > 0 ? rawPinned : null

  const rawPageSize = readStoredJson<number>(tablePrefKey(tableKey, 'pageSize'))
  const pageSize = typeof rawPageSize === 'number' && Number.isInteger(rawPageSize) && rawPageSize > 0
    ? rawPageSize
    : null

  const rawCustomSizes = readStoredJson<unknown>(scopedCustomPageSizesKey(tableKey))
  const customPageSizes = rawCustomSizes !== null ? sanitizePageSizes(rawCustomSizes) : null

  const storedPresets = readStoredPresets(tableKey)
  const presets = storedPresets.length > 0 ? storedPresets : null

  if (!order && !visibility && !sizing && !pinned && pageSize === null && customPageSizes === null && !presets) {
    return null
  }

  return { order, visibility, sizing, pinned, pageSize, customPageSizes, presets }
}

/** 清空单张表的列布局本机记录（恢复默认列时调用；不影响每页条数偏好与已保存命名视图预设）。 */
export function clearStoredColumnLayout(tableKey: string) {
  removeStoredKey(tablePrefKey(tableKey, 'order'))
  removeStoredKey(`${LEGACY_COLS_PREFIX}${tableKey}`)
  removeStoredKey(tablePrefKey(tableKey, 'visibility'))
  removeStoredKey(tablePrefKey(tableKey, 'sizing'))
  removeStoredKey(tablePrefKey(tableKey, 'pinned'))
}

/**
 * 将云端快照落入本机存储并对齐账号归属：
 * - 同浏览器切换不同账号（prevOwner !== principalId）：先清掉上一账号残留的全部 swapmid-table:* / swapmid-cols:* 键防串味；
 * - 老用户首次连云（prevOwner === null）：云端已有记录覆盖本机，云端尚无记录的表保留本机既有习惯；
 * - 同账号跨设备对齐（prevOwner === principalId）：以云端为准同步增删改（跳过本页正在防抖待发送的 pendingTableKeys）。
 */
export function applyCloudTablePreferences(
  principalId: string,
  snapshot: TablePreferenceSnapshotPayload,
  pendingTableKeys?: ReadonlySet<string>,
): { changedTables: string[]; densityChanged: boolean } {
  const changedSet = new Set<string>()
  let densityChanged = false
  if (typeof window === 'undefined' || !principalId) {
    return { changedTables: [], densityChanged: false }
  }

  const prevOwner = readStoredJson<string>(OWNER_KEY)
  const prevDensity = readStoredDensity()
  const isAccountSwitch = prevOwner !== null && prevOwner !== principalId
  const cloudTables = snapshot.tables ?? {}
  const cloudDensity: TableDensity = snapshot.density === 'compact' ? 'compact' : 'standard'

  if (isAccountSwitch) {
    for (const { storageKey, tableKey } of listStoredTableKeys()) {
      removeStoredKey(storageKey)
      if (tableKey) changedSet.add(tableKey)
    }
  }

  saveStoredJson(OWNER_KEY, principalId)

  const isBrandNewCloud = prevOwner === null && cloudDensity === 'standard' && Object.keys(cloudTables).length === 0
  if (!isBrandNewCloud && (prevDensity !== cloudDensity || isAccountSwitch)) {
    saveDensity(cloudDensity)
    densityChanged = prevDensity !== cloudDensity
  }

  for (const [tableKey, view] of Object.entries(cloudTables)) {
    if (!tableKey || pendingTableKeys?.has(tableKey)) continue
    let tableChanged = false
    tableChanged = syncScopedKey(
      tablePrefKey(tableKey, 'order'),
      view?.order && view.order.length > 0 ? view.order : null,
    ) || tableChanged
    tableChanged = syncScopedKey(
      tablePrefKey(tableKey, 'visibility'),
      view?.visibility && Object.keys(view.visibility).length > 0 ? view.visibility : null,
    ) || tableChanged
    tableChanged = syncScopedKey(
      tablePrefKey(tableKey, 'sizing'),
      view?.sizing && Object.keys(view.sizing).length > 0 ? clampColumnSizing(view.sizing) : null,
    ) || tableChanged
    tableChanged = syncScopedKey(
      tablePrefKey(tableKey, 'pinned'),
      view?.pinned && view.pinned.length > 0 ? view.pinned : null,
    ) || tableChanged
    tableChanged = syncScopedKey(
      tablePrefKey(tableKey, 'pageSize'),
      typeof view?.pageSize === 'number' && view.pageSize > 0 ? view.pageSize : null,
    ) || tableChanged
    tableChanged = syncScopedKey(
      scopedCustomPageSizesKey(tableKey),
      Array.isArray(view?.customPageSizes) ? sanitizePageSizes(view.customPageSizes) : null,
    ) || tableChanged
    const nextPresets = Array.isArray(view?.presets) ? sanitizeStoredPresets(view.presets) : []
    tableChanged = syncScopedKey(
      tablePrefKey(tableKey, 'presets'),
      nextPresets.length > 0 ? nextPresets : null,
    ) || tableChanged

    if (tableChanged) changedSet.add(tableKey)
  }

  // 同账号二次刷新：若另一台设备恢复默认删除了某张表的云端记录，本机同步清理对应键
  if (prevOwner === principalId) {
    for (const { storageKey, tableKey } of listStoredTableKeys()) {
      if (!tableKey || pendingTableKeys?.has(tableKey)) continue
      if (Object.prototype.hasOwnProperty.call(cloudTables, tableKey)) continue
      removeStoredKey(storageKey)
      changedSet.add(tableKey)
    }
  }

  return { changedTables: Array.from(changedSet), densityChanged }
}
