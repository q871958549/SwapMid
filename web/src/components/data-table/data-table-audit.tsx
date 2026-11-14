'use client'

import { type ColumnDef } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import { UserRound } from 'lucide-react'
import { Skeleton } from '@/components/ui/skeleton'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import type { AuditUserLookupDto as AuditUserLookupRecord } from '@/lib/api/types'
import { DataTableColumnHeader, registerColumnTitle } from './data-table-column-header'
import { getColumnKey, RIGHT_STRUCTURAL_COL_IDS } from './use-table-persistence'

// 审计列（创建时间/创建人/修改时间/修改人）：数据含对应字段时自动追加到「操作」列之前。
// 创建人/修改人显示姓名；lookup 只给 Id/显示名/登录名，不扩成联系方式或组织目录。
//
// 列标题（创建时间/创建人/修改时间/修改人）由调用方（data-table.tsx，已持有 useTranslations('Common')
// 求出的 t）经 withAuditColumns 的 t 参数传入——本文件的 buildAudit* 是普通函数、非 React 组件，
// 不能直接调 hook（同 column-factories.tsx 的 ActionsCell 拆分理由）。AuditUserCell 是真正经 JSX
// 渲染的组件，弹层内的字段标签/兜底文案在其内部自行 useTranslations。
//
// 创建时间/修改时间列的展示格式随平台语言切换（批次 3f）：locale 同样由调用方
// （data-table.tsx，已持有 useLocale()）经 withAuditColumns 的 locale 参数传入，
// 缺省 'zh-CN' 兜底防遗漏调用点炸掉。
type CommonT = (key: string) => string

type DataRecord = Record<string, unknown>
type AuditUserKind = 'creator' | 'lastModifier'

export interface AuditUserInfo {
  id?: string
  userName?: string
  name?: string
  employeeNo?: string
  email?: string
  phoneNumber?: string
  enterpriseName?: string | null
  departmentName?: string | null
  positionName?: string | null
}

interface ListResponse<T> {
  items?: T[]
}

interface AuditUserCacheEntry {
  expiresAt: number
  value: AuditUserInfo
}

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const AUDIT_USER_BATCH_SIZE = 2000
const AUDIT_USER_CACHE_TTL_MS = 5 * 60 * 1000
const AUDIT_USER_CACHE_MAX_ENTRIES = 2000
const AUDIT_USER_MAX_CONCURRENCY = 2

let activeAuditIdentityVersion: string | null = null
let activeAuditLookupCount = 0
const auditUserCache = new Map<string, AuditUserCacheEntry>()
const auditUserFetches = new Map<string, Promise<AuditUserInfo | undefined>>()
const auditUserLookupQueue: Array<() => void> = []

function formatDateTime(value: unknown, locale: string = 'zh-CN'): string {
  if (!value) return '-'
  const date = new Date(String(value))
  if (Number.isNaN(date.getTime())) return String(value)
  return date.toLocaleString(locale, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function normalizeId(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}

function getNestedRecord(row: DataRecord, key: string): DataRecord | undefined {
  const value = row[key]
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as DataRecord
    : undefined
}

function firstText(...values: unknown[]): string | undefined {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) return value.trim()
  }
  return undefined
}

function activateAuditIdentity(identityVersion: string) {
  if (activeAuditIdentityVersion === identityVersion) return
  activeAuditIdentityVersion = identityVersion
  auditUserCache.clear()
  auditUserFetches.clear()
}

function scheduleAuditLookup<T>(operation: () => Promise<T>): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const start = () => {
      activeAuditLookupCount += 1
      void operation().then(resolve, reject).finally(() => {
        activeAuditLookupCount -= 1
        auditUserLookupQueue.shift()?.()
      })
    }

    if (activeAuditLookupCount < AUDIT_USER_MAX_CONCURRENCY) start()
    else auditUserLookupQueue.push(start)
  })
}

function cacheAuditUser(id: string, value: AuditUserInfo, now: number) {
  auditUserCache.delete(id)
  auditUserCache.set(id, { value, expiresAt: now + AUDIT_USER_CACHE_TTL_MS })
  while (auditUserCache.size > AUDIT_USER_CACHE_MAX_ENTRIES) {
    const oldest = auditUserCache.keys().next().value as string | undefined
    if (!oldest) break
    auditUserCache.delete(oldest)
  }
}

async function fetchAuditUserBatch(ids: string[], identityVersion: string): Promise<Record<string, AuditUserInfo>> {
  return scheduleAuditLookup(async () => {
    // 身份世代已变化时，排队中的旧请求直接作废，不能携旧身份结果写入新世代。
    if (activeAuditIdentityVersion !== identityVersion) return {}

    const response = await fetch('/api/proxy/audit-users/lookup', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ids }),
    })
    if (!response.ok) throw new Error(`Audit user lookup failed with status ${response.status}`)

    const payload = await response.json() as ListResponse<AuditUserLookupRecord>
    if (!Array.isArray(payload.items)) throw new TypeError('Audit user lookup response must contain an items array')

    const requestedIds = new Set(ids)
    const result: Record<string, AuditUserInfo> = {}
    for (const item of payload.items) {
      const id = normalizeId(item.id)?.toLowerCase()
      const name = firstText(item.displayName)
      if (!id || !GUID_PATTERN.test(id) || !name || !requestedIds.has(id)) continue
      const userName = firstText(item.userName)
      result[id] = userName ? { id, name, userName } : { id, name }
    }

    if (activeAuditIdentityVersion === identityVersion) {
      const now = Date.now()
      Object.entries(result).forEach(([id, value]) => cacheAuditUser(id, value, now))
    }
    return result
  })
}

/**
 * 仅按当前可见行的审计用户 ID 批量解析显示名。缓存严格绑定身份世代；传输失败不做负缓存，
 * 下一次翻页/重开仍会重试。接口只返回 id/displayName/userName，避免把联系方式和组织信息扩成全员目录。
 */
export async function loadAuditUsers(
  userIds: string[],
  identityVersion: string,
): Promise<Record<string, AuditUserInfo>> {
  activateAuditIdentity(identityVersion)
  const ids = [...new Set(userIds
    .map((id) => normalizeId(id)?.toLowerCase())
    .filter((id): id is string => Boolean(id && GUID_PATTERN.test(id))))]
    .sort()
  if (ids.length === 0) return {}

  const now = Date.now()
  const result: Record<string, AuditUserInfo> = {}
  const missingIds: string[] = []
  for (const id of ids) {
    const cached = auditUserCache.get(id)
    if (cached && cached.expiresAt > now) {
      // Map 插入序即 LRU 顺序；命中后移到末尾，避免热点名字被冷页挤出。
      auditUserCache.delete(id)
      auditUserCache.set(id, cached)
      result[id] = cached.value
    } else {
      auditUserCache.delete(id)
      if (!auditUserFetches.has(id)) missingIds.push(id)
    }
  }

  for (let offset = 0; offset < missingIds.length; offset += AUDIT_USER_BATCH_SIZE) {
    const chunk = missingIds.slice(offset, offset + AUDIT_USER_BATCH_SIZE)
    const batch = fetchAuditUserBatch(chunk, identityVersion)
      .catch(() => ({} as Record<string, AuditUserInfo>))
    for (const id of chunk) {
      const pending = batch.then((users) => users[id])
      auditUserFetches.set(id, pending)
      void pending.then(() => {
        if (auditUserFetches.get(id) === pending) auditUserFetches.delete(id)
      })
    }
  }

  const pendingResults = ids.map((id) => {
    const pending = auditUserFetches.get(id)
    return pending ? pending.then((value) => [id, value] as const) : null
  }).filter((pending): pending is Promise<readonly [string, AuditUserInfo | undefined]> => pending !== null)
  const fetched = await Promise.all(pendingResults)
  if (activeAuditIdentityVersion !== identityVersion) return {}

  for (const [id, value] of fetched) {
    if (value) result[id] = value
  }
  return result
}

export function collectAuditUserIds<TData>(data: TData[]): string[] {
  const ids = new Set<string>()
  data.forEach((item) => {
    const row = item as DataRecord
    ;(['creator', 'lastModifier'] as AuditUserKind[]).forEach((kind) => {
      const nested = getNestedRecord(row, kind)
      const id = normalizeId(row[`${kind}Id`] ?? nested?.id)
      if (id) ids.add(id)
    })
  })
  return [...ids].sort()
}

function resolveAuditUser(row: DataRecord, kind: AuditUserKind, users: Record<string, AuditUserInfo>): AuditUserInfo {
  const nested = getNestedRecord(row, kind)
  const id = normalizeId(row[`${kind}Id`] ?? nested?.id)
  // 后端 Guid 无大小写语义；缓存统一使用 lower-case，读取侧也必须规范化，避免混合大小写 DTO 查到却显示未知。
  const cached = id ? users[id.toLowerCase()] : undefined
  return {
    id,
    userName: firstText(nested?.userName, row[`${kind}UserName`], cached?.userName),
    name: firstText(row[`${kind}Name`], nested?.name, cached?.name, nested?.userName, cached?.userName),
    employeeNo: firstText(nested?.employeeNo, row[`${kind}EmployeeNo`], cached?.employeeNo),
    email: firstText(nested?.email, row[`${kind}Email`], cached?.email),
    phoneNumber: firstText(nested?.phoneNumber, row[`${kind}PhoneNumber`], cached?.phoneNumber),
    enterpriseName: firstText(nested?.enterpriseName, cached?.enterpriseName),
    departmentName: firstText(nested?.departmentName, cached?.departmentName),
    positionName: firstText(nested?.positionName, cached?.positionName),
  }
}

function hasDataField<TData>(data: TData[], key: string): boolean {
  return data.some((row) => {
    const value = (row as DataRecord)[key]
    return value !== undefined && value !== null && value !== ''
  })
}

function AuditUserCell({
  row,
  kind,
  users,
  loading,
}: {
  row: DataRecord
  kind: AuditUserKind
  users: Record<string, AuditUserInfo>
  loading: boolean
}) {
  const t = useTranslations('Common')
  const user = resolveAuditUser(row, kind, users)
  if (loading && user.id && !user.name) {
    return <Skeleton className="h-5 w-20" />
  }

  const label = user.name ?? (user.id ? t('unknownUser') : undefined)
  if (!label) return <span className="text-sm text-muted-foreground">-</span>

  const details = [
    [t('username'), user.userName],
    [t('employeeNo'), user.employeeNo],
    [t('email'), user.email],
    [t('phone'), user.phoneNumber],
    [t('enterprise'), user.enterpriseName],
    [t('department'), user.departmentName],
    [t('position'), user.positionName],
  ].filter(([, value]) => value)

  return (
    <Popover>
      <PopoverTrigger asChild>
        <button
          type="button"
          className="inline-flex max-w-[6.5rem] items-center gap-1 text-left text-sm font-medium text-foreground hover:text-primary"
        >
          <UserRound className="size-3.5 shrink-0 text-muted-foreground" />
          <span className="truncate">{label}</span>
        </button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-64 p-3">
        <div className="space-y-2">
          <div>
            <div className="font-medium leading-none">{label}</div>
            <div className="mt-1 text-xs text-muted-foreground">{kind === 'creator' ? t('creator') : t('lastModifier')}</div>
          </div>
          {details.length > 0 ? (
            <dl className="grid grid-cols-[4rem_1fr] gap-x-2 gap-y-1.5 text-xs">
              {details.map(([name, value]) => (
                <div key={name} className="contents">
                  <dt className="text-muted-foreground">{name}</dt>
                  <dd className="truncate text-foreground">{value}</dd>
                </div>
              ))}
            </dl>
          ) : (
            <p className="text-xs text-muted-foreground">{t('noAccountExtraInfo')}</p>
          )}
        </div>
      </PopoverContent>
    </Popover>
  )
}

function buildAuditDateColumn<TData>(
  key: string,
  title: string,
  locale: string,
  enableSorting: boolean,
): ColumnDef<TData, unknown> {
  registerColumnTitle(key, title)
  return {
    accessorKey: key,
    enableSorting,
    header: ({ column }) => <DataTableColumnHeader column={column} title={title} />,
    cell: ({ row }) => {
      const value = (row.original as DataRecord)[key]
      return (
        <span className="block max-w-[8rem] truncate text-sm text-muted-foreground">
          {formatDateTime(value, locale)}
        </span>
      )
    },
    size: 128,
    meta: { title, audit: true, filterVariant: 'dateRange' },
  }
}

function buildAuditUserColumn<TData>(
  kind: AuditUserKind,
  users: Record<string, AuditUserInfo>,
  loading: boolean,
  title: string,
  enableSorting: boolean,
): ColumnDef<TData, unknown> {
  registerColumnTitle(kind, title)
  return {
    id: kind,
    accessorFn: (row) => resolveAuditUser(row as DataRecord, kind, users).name ?? '',
    header: ({ column }) => <DataTableColumnHeader column={column} title={title} />,
    cell: ({ row }) => <AuditUserCell row={row.original as DataRecord} kind={kind} users={users} loading={loading} />,
    size: 104,
    // 创建人/修改人的值是**前端派生**的：后端只给 creatorId（GUID），姓名靠客户端加载的用户字典解析。
    // 服务端分页时排序要下推后端，而后端排序白名单里不可能有 'creator' 这个字段 → 点了会静默回退默认列
    // （列头箭头变了、数据没变，即「界面撒谎」）。故服务端分页一律禁排序；客户端分页仍按姓名前端排，能力不减。
    enableSorting,
    meta: { title, audit: true },
  }
}

function hasAuditUserField<TData>(data: TData[], kind: AuditUserKind): boolean {
  return data.some((item) => {
    const row = item as DataRecord
    const nested = getNestedRecord(row, kind)
    return Boolean(
      nested ||
      row[`${kind}Id`] ||
      row[`${kind}Name`] ||
      row[`${kind}UserName`]
    )
  })
}

export function withAuditColumns<TData>(
  columns: ColumnDef<TData, unknown>[],
  data: TData[],
  showAuditColumns: boolean,
  auditUsers: Record<string, AuditUserInfo>,
  auditUsersLoading: boolean,
  t: CommonT,
  locale: string = 'zh-CN',
  /** 服务端排序模式：影响审计「创建人/修改人」列是否可排序（见 buildAuditUserColumn 注释）。 */
  manualSorting: boolean = false,
  /** 服务端分页模式：纯分页未接服务端排序（manualPagination && !manualSorting）时审计日期列禁排，防仅排当前页的假排序。 */
  manualPagination: boolean = false,
): ColumnDef<TData, unknown>[] {
  if (!showAuditColumns) return columns

  const allColumnsNonSortable = columns.length > 0 && columns.every((col) => col.enableSorting === false)
  const canSortAuditDate = !(manualPagination && !manualSorting) && !allColumnsNonSortable
  const canSortAuditUser = !manualSorting && canSortAuditDate

  const existing = new Set(columns.map(getColumnKey).filter(Boolean))
  const auditColumns: ColumnDef<TData, unknown>[] = []

  const addAuditDate = (key: string, title: string) => {
    if (existing.has(key) || !hasDataField(data, key)) return
    existing.add(key)
    auditColumns.push(buildAuditDateColumn<TData>(key, title, locale, canSortAuditDate))
  }

  const addAuditUser = (kind: AuditUserKind, title: string) => {
    const rawKeys = [kind, `${kind}Id`, `${kind}Name`, `${kind}UserName`]
    if (rawKeys.some((key) => existing.has(key)) || !hasAuditUserField(data, kind)) return
    existing.add(kind)
    auditColumns.push(buildAuditUserColumn<TData>(kind, auditUsers, auditUsersLoading, title, canSortAuditUser))
  }

  addAuditDate('creationTime', t('creationTime'))
  addAuditUser('creator', t('creator'))
  addAuditDate('lastModificationTime', t('lastModificationTime'))
  addAuditUser('lastModifier', t('lastModifier'))

  if (auditColumns.length === 0) return columns

  const rightColumns = columns.filter((column) => RIGHT_STRUCTURAL_COL_IDS.has(getColumnKey(column)))
  const nonRightColumns = columns.filter((column) => !RIGHT_STRUCTURAL_COL_IDS.has(getColumnKey(column)))
  return [...nonRightColumns, ...auditColumns, ...rightColumns]
}
