'use client'

// P1-1 角色详情抽屉 + P1-2 变更历史 tab。四段：概要（行上数据直显，零请求）/ 成员（服务端分页，
// 复用 users 页服务端分页模式的简化版：GET account-users?roleId=...）/ 权限（按组 granted/total 计数条，
// 复用现有 GET account-roles/{roleName}/permissions）/ 变更历史（同一权限端点各自独立懒拉取一次
// 供 name→displayName 映射 + GET .../permission-changes 分页时间线）。
// 四个 tab 均为 Radix Tabs 默认行为下的「未激活即不挂载」（TabsContent 无 forceMount 时 present=false
// 令 children=false），故各 tab 组件用普通 useEffect 挂载即拉，天然懒加载、切走即卸载重置。

import { useEffect, useMemo, useState } from 'react'
import { dataTableLinkClass } from '@/components/data-table/data-table-link-cell'
import Link from 'next/link'
import { useTranslations } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { ChevronLeft, ChevronRight, Loader2 } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Combobox } from '@/components/ui/combobox'
import { IconButton } from '@/components/ui/icon-button'
import { Sheet, SheetContent, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'
import { useLazyRef } from '@/lib/use-lazy-ref'
import type { PagedResult } from '@/lib/api-client'
import type { AccountRoleDto, AccountRolePermissionDto, CompanyDepartmentDto, RolePermissionChangeDto } from '@/lib/api/types'
import { formatPermName } from '../permissions/permission-tree'
import { formatDateTime, parsePermissionChangeLine } from './role-history'

const MEMBERS_DEFAULT_PAGE_SIZE = 10
const HISTORY_PAGE_SIZE = 8

export function RoleDetailSheet({ role, onClose }: { role: AccountRoleDto | null; onClose: () => void }) {
  const t = useTranslations('Account')
  const [tab, setTab] = useState('overview')

  // role 变为新的非空值：回到概要 tab（渲染期按 prop 变化调整 state，同 AssignAccountsDialog 的 prevRole 范式）。
  const [prevRole, setPrevRole] = useState(role)
  if (role !== prevRole) {
    setPrevRole(role)
    if (role) setTab('overview')
  }

  return (
    <Sheet open={!!role} onOpenChange={(o) => { if (!o) onClose() }}>
      <SheetContent side="right" aria-describedby={undefined} className="sm:max-w-2xl">
        {role && (
          <>
            <SheetHeader>
              <SheetTitle>{t('role.detailTitle', { name: role.name })}</SheetTitle>
            </SheetHeader>
            <Tabs value={tab} onValueChange={setTab} className="flex min-h-0 flex-1 flex-col gap-0">
              <TabsList className="mx-6 mt-3 w-fit shrink-0">
                <TabsTrigger value="overview">{t('role.detailTabOverview')}</TabsTrigger>
                <TabsTrigger value="members">{t('role.detailTabMembers')}</TabsTrigger>
                <TabsTrigger value="permissions">{t('role.detailTabPermissions')}</TabsTrigger>
                <TabsTrigger value="history">{t('role.detailTabHistory')}</TabsTrigger>
              </TabsList>
              <TabsContent value="overview" className="mt-0 min-h-0 flex-1 overflow-y-auto px-6 py-4">
                <RoleOverviewTab role={role} onViewMembers={() => setTab('members')} />
              </TabsContent>
              <TabsContent value="members" className="mt-0 flex min-h-0 flex-1 flex-col px-6 py-4">
                <RoleMembersTab role={role} />
              </TabsContent>
              <TabsContent value="permissions" className="mt-0 min-h-0 flex-1 overflow-y-auto px-6 py-4">
                <RolePermissionsTab role={role} />
              </TabsContent>
              <TabsContent value="history" className="mt-0 min-h-0 flex-1 overflow-y-auto px-6 py-4">
                <RoleHistoryTab role={role} />
              </TabsContent>
            </Tabs>
          </>
        )}
      </SheetContent>
    </Sheet>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="text-sm">{children}</div>
    </div>
  )
}

/** 概要 tab：行上 AccountRoleDto 数据直显，零请求。 */
function RoleOverviewTab({ role, onViewMembers }: { role: AccountRoleDto; onViewMembers: () => void }) {
  const t = useTranslations('Account')
  const tc = useTranslations('Common')
  return (
    <div className="grid grid-cols-2 gap-x-6 gap-y-4">
      <Field label={t('role.nameColumnHeader')}>{role.name}</Field>
      <Field label={tc('creationTime')}>{formatDateTime(role.creationTime)}</Field>
      <div className="col-span-2">
        <Field label={t('role.descriptionColumnHeader')}>{role.description || '—'}</Field>
      </div>
      <Field label={t('common.defaultRoleLabel')}>
        <Badge variant={role.isDefault ? 'success' : 'secondary'}>{role.isDefault ? tc('yes') : tc('no')}</Badge>
      </Field>
      <Field label={t('common.systemRoleLabel')}>
        <Badge variant={role.isStatic ? 'outline' : 'secondary'}>{role.isStatic ? tc('yes') : tc('no')}</Badge>
      </Field>
      <Field label={t('role.memberCountColumnHeader')}>
        <button type="button" className={`${dataTableLinkClass} font-medium tabular-nums`} onClick={onViewMembers}>
          {role.memberCount}
        </button>
      </Field>
      <Field label={t('role.permissionCountColumnHeader')}>
        <Link
          href={`/account-management/permissions?roleName=${encodeURIComponent(role.name)}`}
          className="font-medium tabular-nums text-primary hover:underline"
          title={t('role.permissionCountLinkTitle')}
        >
          {role.permissionCount}
        </Link>
      </Field>
    </div>
  )
}

interface MemberRow {
  id: string
  userName: string
  name: string | null
  boundEmployeeNo?: string | null
  boundDepartmentName?: string | null
}

/** 成员 tab：服务端分页简表（姓名/工号/部门 + 部门筛 + 搜索），复用 users 页服务端分页模式的最小可用子集。
 * 不走 useCrudResource（该 hook 假定页面拥有 URL——用在抽屉里会污染 roles 页地址栏），改用局部 state
 * 直接驱动 DataTable 的 manualPagination 模式（AGENTS.md「服务端分页」小节点名的标准 props 组合）。 */
function RoleMembersTab({ role }: { role: AccountRoleDto }) {
  const t = useTranslations('Account')
  const tc = useTranslations('Common')
  const { items: departments, ensure: ensureDepartments } = useLazyRef<CompanyDepartmentDto>('departments?maxResultCount=2000')
  useEffect(() => { void ensureDepartments() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const [rows, setRows] = useState<MemberRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(MEMBERS_DEFAULT_PAGE_SIZE)
  const [search, setSearch] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [loading, setLoading] = useState(false)

  // 拆成具名函数、经 void 调用（同 permissions/client.tsx 的 loadPermissions 范式）；
  // 规避 react-hooks/set-state-in-effect 的关键在下方 useEffect 里的 setTimeout 宏任务边界
  // （该规则会追踪被调函数内部的 setState，但调用点一旦脱离 effect 本轮同步执行就视为安全）。
  async function loadMembers(guard: { cancelled: boolean }) {
    setLoading(true)
    const qs = new URLSearchParams({
      roleId: role.id,
      skipCount: String(page * pageSize),
      maxResultCount: String(pageSize),
    })
    if (search.trim()) qs.set('filter', search.trim())
    if (departmentId) { qs.set('departmentId', departmentId); qs.set('includeChildDepartments', 'true') }
    try {
      const res = await fetch(`/api/proxy/account-users?${qs.toString()}`)
      if (!res.ok) throw new Error(await res.text())
      const d = (await res.json()) as PagedResult<MemberRow>
      if (guard.cancelled) return
      setRows(d.items)
      setTotal(d.totalCount)
    } catch (e) {
      if (guard.cancelled) return
      console.error(e)
      toast({ variant: 'destructive', title: t('role.detailMembersLoadFailedTitle'), description: getApiErrorMessage(e, tc('tryAgainLater')) })
    } finally {
      if (!guard.cancelled) setLoading(false)
    }
  }

  useEffect(() => {
    const guard = { cancelled: false }
    // setTimeout 宏任务边界规避 react-hooks/set-state-in-effect（同 permissions/client.tsx 的
    // loadPermissions 调用范式，详见 compare-dialog.tsx 同型注释）。
    const timer = window.setTimeout(() => { void loadMembers(guard) }, 0)
    return () => { window.clearTimeout(timer); guard.cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [role.id, page, pageSize, search, departmentId])

  const columns = useMemo<ColumnDef<MemberRow, unknown>[]>(() => [
    {
      accessorKey: 'name',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('name')} />,
      cell: ({ row }) => (
        <div className="min-w-0">
          <p className="truncate text-sm font-medium">{row.original.name || row.original.userName}</p>
          <p className="truncate text-xs text-muted-foreground">@{row.original.userName}</p>
        </div>
      ),
    },
    {
      accessorKey: 'boundEmployeeNo',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('employeeNo')} />,
      size: 120,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.boundEmployeeNo ?? '—'}</span>,
    },
    {
      accessorKey: 'boundDepartmentName',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('department')} />,
      size: 160,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.boundDepartmentName ?? '—'}</span>,
    },
  ], [tc])

  return (
    <DataTable
      columns={columns}
      data={rows}
      tableKey="role-detail-members"
      searchPlaceholder={t('role.detailMembersSearchPlaceholder')}
      manualPagination
      rowCount={total}
      pageIndex={page}
      manualPageSize={pageSize}
      onPaginationChange={({ pageIndex, pageSize: nextSize }) => { setPage(pageIndex); setPageSize(nextSize) }}
      onSearchChange={(v) => { setSearch(v); setPage(0) }}
      loading={loading}
      enableExport={false}
      toolbarExtra={
        <div className="flex items-center gap-1.5">
          <span className="shrink-0 text-xs text-muted-foreground">{tc('department')}</span>
          <div className="w-40 min-w-0">
            <Combobox
              options={departments.map(d => ({ value: d.id, label: d.name, description: d.enterpriseName ?? d.code }))}
              value={departmentId}
              onChange={(v) => { setDepartmentId(v); setPage(0) }}
              placeholder={t('user.allDepartmentsPlaceholder')}
              searchPlaceholder={t('user.searchDeptPlaceholder')}
              emptyText={t('user.noDeptFound')}
              clearable
            />
          </div>
        </div>
      }
    />
  )
}

/** 权限 tab：按组 granted/total 计数条，点击跳权限页带 roleName（复用现有 GET .../permissions）。
 * 平铺展示（不建父子树）——本 tab 只需"每组配了多少"的摘要，不需要动作级层级。 */
function RolePermissionsTab({ role }: { role: AccountRoleDto }) {
  const t = useTranslations('Account')
  const [data, setData] = useState<AccountRolePermissionDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)

  // 具名函数 + setTimeout 边界规避 react-hooks/set-state-in-effect（见 RoleMembersTab 顶部注释）。
  async function loadPermissions(guard: { cancelled: boolean }) {
    setLoading(true)
    setFailed(false)
    try {
      const res = await fetch(`/api/proxy/account-roles/${encodeURIComponent(role.name)}/permissions`)
      if (!res.ok) throw new Error(await res.text())
      const d = (await res.json()) as AccountRolePermissionDto
      if (!guard.cancelled) setData(d)
    } catch (e) {
      if (guard.cancelled) return
      console.error(e)
      setFailed(true)
    } finally {
      if (!guard.cancelled) setLoading(false)
    }
  }

  useEffect(() => {
    const guard = { cancelled: false }
    const timer = window.setTimeout(() => { void loadPermissions(guard) }, 0)
    return () => { window.clearTimeout(timer); guard.cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [role.name])

  if (loading) {
    return (
      <div className="flex items-center justify-center gap-2 py-12 text-sm text-muted-foreground">
        <Loader2 className="size-4 animate-spin" />{t('permission.loadingPermissions')}
      </div>
    )
  }
  if (failed || !data) {
    return <p className="py-8 text-center text-sm text-destructive">{t('role.detailPermissionsLoadFailedTitle')}</p>
  }

  return (
    <div className="space-y-1.5">
      {data.groups.map(group => {
        const total = group.permissions.length
        const granted = group.permissions.filter(p => p.isGranted).length
        return (
          <Link
            key={group.name}
            href={`/account-management/permissions?roleName=${encodeURIComponent(role.name)}`}
            title={t('role.detailPermissionsGroupLinkTitle')}
            className="flex items-center gap-2 rounded-lg border px-3 py-2 transition-colors hover:bg-muted/40 focus-visible:outline-none focus-visible:border-ring focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)"
          >
            <div className="min-w-0 flex-1">
              <div className="truncate text-sm font-medium" title={group.displayName}>{group.displayName}</div>
              <div className="truncate font-mono text-xs text-muted-foreground" title={group.name}>{formatPermName(group.name)}</div>
            </div>
            <Badge variant={granted > 0 ? 'secondary' : 'outline'} className="shrink-0 text-xs tabular-nums">{granted}/{total}</Badge>
          </Link>
        )
      })}
    </div>
  )
}

/** 变更历史 tab（P1-2）：时间线（时间/操作人/+n −m 摘要，展开看明细权限名）。
 * name→displayName 映射与「权限」tab 各自独立懒拉取一次（简化版，两个 tab 均可能单独打开，
 * 未做跨 tab 状态提升——同一角色重复请求一次权限目录，成本可接受）。 */
function RoleHistoryTab({ role }: { role: AccountRoleDto }) {
  const t = useTranslations('Account')
  const tc = useTranslations('Common')
  const [rows, setRows] = useState<RolePermissionChangeDto[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(0)
  const [loading, setLoading] = useState(false)
  const [failed, setFailed] = useState(false)
  const [nameMap, setNameMap] = useState<Record<string, string>>({})
  const [expanded, setExpanded] = useState<Set<number>>(new Set())

  useEffect(() => {
    let cancelled = false
    fetch(`/api/proxy/account-roles/${encodeURIComponent(role.name)}/permissions`)
      .then(async res => (res.ok ? (await res.json()) as AccountRolePermissionDto : null))
      .then(d => {
        if (cancelled || !d) return
        const map: Record<string, string> = {}
        d.groups.forEach(g => g.permissions.forEach(p => { map[p.name] = p.displayName }))
        setNameMap(map)
      })
      .catch(() => { /* 映射仅用于中文名尽力展示，拉取失败静默回退 formatPermName */ })
    return () => { cancelled = true }
  }, [role.name])

  // 具名函数 + setTimeout 边界规避 react-hooks/set-state-in-effect（见 RoleMembersTab 顶部注释）。
  async function loadHistory(guard: { cancelled: boolean }) {
    setLoading(true)
    setFailed(false)
    setExpanded(new Set())
    const qs = new URLSearchParams({ skipCount: String(page * HISTORY_PAGE_SIZE), maxResultCount: String(HISTORY_PAGE_SIZE) })
    try {
      const res = await fetch(`/api/proxy/account-roles/${encodeURIComponent(role.name)}/permission-changes?${qs.toString()}`)
      if (!res.ok) throw new Error(await res.text())
      const d = (await res.json()) as PagedResult<RolePermissionChangeDto>
      if (guard.cancelled) return
      setRows(d.items)
      setTotal(d.totalCount)
    } catch (e) {
      if (guard.cancelled) return
      console.error(e)
      setFailed(true)
    } finally {
      if (!guard.cancelled) setLoading(false)
    }
  }

  useEffect(() => {
    const guard = { cancelled: false }
    const timer = window.setTimeout(() => { void loadHistory(guard) }, 0)
    return () => { window.clearTimeout(timer); guard.cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [role.name, page])

  function permTitle(name: string): string {
    return nameMap[name] ?? formatPermName(name)
  }

  function toggleExpanded(idx: number) {
    setExpanded(prev => {
      const next = new Set(prev)
      if (next.has(idx)) next.delete(idx)
      else next.add(idx)
      return next
    })
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center gap-2 py-12 text-sm text-muted-foreground">
        <Loader2 className="size-4 animate-spin" />{t('role.detailLoadingHistory')}
      </div>
    )
  }
  if (failed) {
    return <p className="py-8 text-center text-sm text-destructive">{t('role.detailHistoryLoadFailedTitle')}</p>
  }
  if (rows.length === 0) {
    return <p className="py-8 text-center text-sm text-muted-foreground">{t('role.detailHistoryEmpty')}</p>
  }

  return (
    <div className="space-y-3">
      <ol className="space-y-3 border-l pl-4">
        {rows.map((row, idx) => {
          const changes = row.changes.map(parsePermissionChangeLine)
          const added = changes.flatMap(c => c.added)
          const removed = changes.flatMap(c => c.removed)
          const hasChange = added.length > 0 || removed.length > 0
          const isExpanded = expanded.has(idx)
          return (
            <li key={`${row.executionTime}-${idx}`} className="relative text-sm">
              <span className="absolute -left-[21px] top-1 size-2 rounded-full bg-primary/60" />
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                <span className="text-muted-foreground">{formatDateTime(row.executionTime)}</span>
                <span className="font-medium">{row.userName || tc('unknownUser')}</span>
                {hasChange ? (
                  <button type="button" className="text-xs text-primary hover:underline" onClick={() => toggleExpanded(idx)}>
                    {t('role.detailHistorySummary', { added: added.length, removed: removed.length })}
                  </button>
                ) : (
                  <span className="text-xs text-muted-foreground">{t('role.detailHistoryNoActualChange')}</span>
                )}
              </div>
              {isExpanded && hasChange && (
                <div className="mt-1.5 space-y-1 border-l pl-3 text-xs">
                  {added.length > 0 && (
                    <div>
                      <span className="text-success">{t('role.detailHistoryAddedLabel')}</span>
                      {'：'}{added.map(permTitle).join(t('role.assignNameSeparator'))}
                    </div>
                  )}
                  {removed.length > 0 && (
                    <div>
                      <span className="text-destructive">{t('role.detailHistoryRemovedLabel')}</span>
                      {'：'}{removed.map(permTitle).join(t('role.assignNameSeparator'))}
                    </div>
                  )}
                </div>
              )}
            </li>
          )
        })}
      </ol>
      {total > HISTORY_PAGE_SIZE && (
        <div className="flex items-center justify-between border-t pt-2">
          <span className="text-xs text-muted-foreground">{tc('totalCount', { count: total })}</span>
          <div className="flex items-center gap-1">
            <IconButton label={tc('prevPage')} className="size-7" disabled={page === 0} onClick={() => setPage(p => p - 1)}>
              <ChevronLeft className="size-3.5" />
            </IconButton>
            <IconButton
              label={tc('nextPage')}
              className="size-7"
              disabled={(page + 1) * HISTORY_PAGE_SIZE >= total}
              onClick={() => setPage(p => p + 1)}
            >
              <ChevronRight className="size-3.5" />
            </IconButton>
          </div>
        </div>
      )}
    </div>
  )
}
