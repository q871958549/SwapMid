'use client'

import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslations } from 'next-intl'
import { type RowSelectionState, type SortingState } from '@tanstack/react-table'
import { KeyRound, ShieldCheck } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Combobox, type ComboboxOption } from '@/components/ui/combobox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { PasswordInput } from '@/components/ui/password-input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { toast } from '@/components/ui/use-toast'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { DataTable } from '@/components/data-table/data-table'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { ConfirmDialog } from '@/components/data-table/confirm-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import type { PagedResult } from '@/lib/api-client'
import { useLazyRef } from '@/lib/use-lazy-ref'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import { useMutateAndRefresh } from '@/lib/crud/use-mutate-and-refresh'
import { useUrlListState } from '@/lib/crud/use-url-list-state'
import { useHasPermission } from '@/components/providers/permissions-provider'
import { Permissions } from '@/lib/permissions'
import { getApiErrorMessage } from '@/lib/api-error'
import { requestStartImpersonation } from '@/lib/impersonation-client'
import { useSessionIdentityVersion } from '@/components/providers/session-identity-provider'
import { announceSessionIdentityChange } from '@/components/layout/session-guard'
import { RoleMultiSelect } from './role-controls'
import { getUsersColumns } from './users-columns'
import { BatchRolesDialog, SecuritySettingsDialog } from './users-dialogs'
import { EffectivePermissionsSheet } from './effective-permissions-sheet'
import {
  type AbpRole, type AbpUser, type DepartmentForFilter, type EmployeeForLink, type FormState, defaultForm,
} from './users-types'
import { ACCOUNT_USER_LIST_MAX_PAGE_SIZE } from './constants'
import type { PasswordPolicy } from '@/lib/password-policy'

export function UsersClient({
  data, defaultPassword, passwordPolicy, roles,
  initialDepartmentId = '', initialIsActive = '', initialRoleIds = '',
  initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '',
  currentUserName = '', isImpersonating = false, allowImpersonation = true,
}: {
  data: PagedResult<AbpUser>; defaultPassword: string; passwordPolicy: PasswordPolicy | null; roles: AbpRole[]
  initialDepartmentId?: string; initialIsActive?: string; initialRoleIds?: string
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState; initialSearch?: string
  /** 当前登录账号名（模拟态下=正被模拟的目标账号名）；用于禁用"以此账号登录"里的自己那一行。 */
  currentUserName?: string
  /** 当前会话是否已处于模拟态——已在模拟中则隐藏入口（禁链式的 UX 呼应，真正边界在后端 grant）。 */
  isImpersonating?: boolean
  /** 是否允许开启模拟登录，传 false 隐藏该操作项。 */
  allowImpersonation?: boolean
}) {
  const t = useTranslations('Account')
  const tc = useTranslations('Common')
  const ti = useTranslations('Impersonation')
  const identityVersion = useSessionIdentityVersion()
  const {
    items: employees,
    ensure: ensureEmployees,
    invalidate: invalidateEmployees,
  } = useLazyRef<EmployeeForLink>('account-users/employee-options')
  // 部门筛选下拉的数据（部门量小，挂载即拉）
  const { items: departments, ensure: ensureDepartments } = useLazyRef<DepartmentForFilter>('account-users/department-options')
  useEffect(() => { void ensureDepartments() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // 初始密码（建号默认 / 重置展示共用）；安全设置保存后回写。
  const [passwordValue, setPasswordValue] = useState(defaultPassword)
  // 部门 / 状态 / 角色筛选镜像到 URL；分页/搜索由 useCrudResource(urlSync) 镜像。初值来自 page.tsx。
  const [filters, setFilters] = useUrlListState({
    departmentId: initialDepartmentId,
    isActive: initialIsActive,
    roleIds: initialRoleIds,
  })

  const list = useCrudResource<AbpUser>({
    endpoint: 'account-users',
    initial: data,
    errorTitle: t('common.accountListLoadFailedTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    pageSizeMax: ACCOUNT_USER_LIST_MAX_PAGE_SIZE,
    initialSearch,
    extraDeps: [filters.departmentId, filters.isActive, filters.roleIds],
    buildParams: (qs) => {
      if (filters.departmentId) {
        qs.set('departmentId', filters.departmentId)
        qs.set('includeChildDepartments', 'true')
      }
      if (filters.isActive === 'true' || filters.isActive === 'false') {
        qs.set('isActive', filters.isActive)
      }
      for (const roleId of filters.roleIds.split(',').map(id => id.trim()).filter(Boolean)) {
        qs.append('roleIds', roleId)
      }
    },
  })

  const crud = useCrudForm<FormState, AbpUser>({
    entityName: t('common.userEntity'),
    endpoint: 'account-users',
    defaultForm,
    onSuccess: () => {
      // 账号新增/编辑/删除会改变 Employee.UserId 绑定，下次打开表单必须重取候选。
      invalidateEmployees()
      void list.reload()
    },
    getName: (item) => item.userName, // toast 用账号名（而非显示姓名）
    toForm: (u) => ({
      userName: u.userName, name: u.name,
      email: u.email, phoneNumber: u.phoneNumber ?? '',
      password: '', isActive: u.isActive,
      employeeId: u.boundEmployeeId ?? '',
      roleNames: u.roleNames ?? [],
    }),
    // 密码仅新建时提交；编辑账号不改密码。
    toBody: (form, mode) => mode === 'edit'
      ? {
          userName: form.userName, name: form.name, surname: '',
          email: form.email, phoneNumber: form.phoneNumber, isActive: form.isActive,
          employeeId: form.employeeId || null,
          roleNames: form.roleNames,
        }
      : {
          userName: form.userName, name: form.name, surname: '',
          email: form.email, phoneNumber: form.phoneNumber,
          password: form.password, isActive: form.isActive,
          employeeId: form.employeeId || null,
          roleNames: form.roleNames,
        },
    beforeCreate: async () => {
      void ensureEmployees()
      return { password: passwordValue } // 新建默认带出当前初始密码
    },
    beforeEdit: () => { void ensureEmployees() },
  })

  const [resetTarget, setResetTarget] = useState<AbpUser | null>(null)
  const [batchTarget, setBatchTarget] = useState<{ ids: string[]; clear: () => void } | null>(null)
  const [securityOpen, setSecurityOpen] = useState(false)
  const [selectedRowIds, setSelectedRowIds] = useState<RowSelectionState>({})
  const [impersonateTarget, setImpersonateTarget] = useState<AbpUser | null>(null)
  const [impersonating, setImpersonating] = useState(false)
  // P1-4：查看有效权限抽屉目标（非空即打开）。
  const [effectivePermTarget, setEffectivePermTarget] = useState<AbpUser | null>(null)

  // 显隐门控：权限 + 非模拟态 + 实例允许模拟（真正边界在后端 grant 内的四条防护规则，这里只是 UX）。
  const hasImpersonatePerm = useHasPermission(Permissions.AccountManagement.UsersImpersonate)
  const canImpersonate = allowImpersonation && hasImpersonatePerm && !isImpersonating

  // 以此账号登录：换会话身份后用整页硬导航强制整棵 RSC 树（layout+page）按新身份重渲。
  // ⚠️ 不能用 router.push + router.refresh：/dashboard 仍在 Router Cache（staleTimes.dynamic=180s）内、
  // 共享 (dashboard)/layout 在 SPA 导航中被复用，竞态下 layout 段（右上角身份 / 侧栏权限 / 切回横幅所在）
  // 不会重渲——出现「数据已按目标身份、但身份/权限/横幅仍停在管理员」的半切换态。硬导航一次性绕开。
  async function handleImpersonate() {
    if (!impersonateTarget) return
    setImpersonating(true)
    try {
      const result = await requestStartImpersonation(impersonateTarget.id, identityVersion)
      if (result.status === 'started') {
        if (result.identityVersion) {
          announceSessionIdentityChange(result.identityVersion)
        }
        // replace 销毁旧身份文档的历史入口，避免后退恢复进 BFCache。
        window.location.replace('/dashboard')
        return
      }
      if (result.errorKey === 'unavailable') {
        // SessionGuard 会对 tagged 503 导航到会话故障页；这里不再叠一条失败 toast。
        return
      }
      const description = result.errorKey === 'session-expired'
        ? ti('errorSessionExpired')
        : result.errorKey === 'already-impersonating'
          ? ti('errorAlreadyImpersonating')
          : result.errorKey === 'network'
            ? ti('errorNetwork')
            : ti('errorFailed')
      toast({ variant: 'destructive', title: t('user.impersonateFailedTitle'), description })
      setImpersonating(false)
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('user.impersonateFailedTitle'), description: getApiErrorMessage(e, tc('tryAgainLater')) })
      setImpersonating(false)
    }
  }

  const employeeOptions: ComboboxOption[] = employees
    .filter(e => (e.status ?? 0) === 0) // 在职员工
    .map(e => ({
      value: e.id,
      label: e.name,
      description: `${e.employeeNo}${e.enterpriseName ? ' · ' + e.enterpriseName : ''}${e.userId && e.userId !== crud.target?.id ? ' ' + t('user.boundSuffix') : ''}`,
    }))

  // 员工忘记密码 → 重置为初始密码并强制其下次登录改密
  async function handleResetPassword() {
    if (!resetTarget) return
    try {
      const res = await fetch(`/api/proxy/account-users/${resetTarget.id}/reset-password`, { method: 'POST' })
      if (!res.ok) throw new Error(await res.text())
      toast({
        variant: 'success',
        title: t('user.resetPasswordSuccessTitle'),
        description: t('user.resetPasswordSuccessDesc', { name: resetTarget.name || resetTarget.userName, password: passwordValue }),
      })
      setResetTarget(null)
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('user.resetPasswordFailedTitle'), description: t('common.retryOrCheckLogs') })
    }
  }

  const mutate = useMutateAndRefresh(list.reload)
  const [statusBusy, setStatusBusy] = useState(false)
  const toggleActive = useCallback(async (row: AbpUser, next: boolean) => {
    setStatusBusy(true)
    try {
      await mutate(`/api/proxy/account-users/${row.id}/set-active?isActive=${next}`, {
        okTitle: next ? tc('enabledDone') : tc('disabledDone'),
      })
    } finally {
      setStatusBusy(false)
    }
  }, [mutate, tc])

  const columns = useMemo(() => getUsersColumns({
    onEdit: crud.openEdit,
    onResetPassword: setResetTarget,
    onDelete: crud.askDelete,
    onImpersonate: setImpersonateTarget,
    onEffectivePermissions: setEffectivePermTarget,
    onToggleActive: toggleActive,
    statusBusy,
    canImpersonate,
    currentUserName,
    identityVersion,
    t,
    tc,
  }), [
    crud.openEdit, crud.askDelete, toggleActive, statusBusy,
    canImpersonate, currentUserName, identityVersion, t, tc,
  ])

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <PageHeader
        title={t('user.pageTitle')}
        description={t('user.totalCountLabel', { count: list.total })}
        extra={
          <Button size="sm" variant="outline" onClick={() => setSecurityOpen(true)}>
            <KeyRound className="size-3.5" />
            {t('user.securitySettingsButton')}
          </Button>
        }
        action={<CreateButton label={t('user.createTitle')} onClick={crud.openCreate} />}
      />
      <DataTable
        columns={columns}
        data={list.rows}
        searchPlaceholder={t('user.searchPlaceholder')}
        tableKey="users"
        {...list.tableProps}
        maxPageSize={ACCOUNT_USER_LIST_MAX_PAGE_SIZE}
        toolbarExtra={
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="text-muted-foreground shrink-0 text-xs">{tc('department')}</span>
            <div className="w-52 min-w-0">
              <Combobox
                options={departments.map(d => ({ value: d.id, label: d.name, description: d.enterpriseName ?? d.code ?? undefined }))}
                value={filters.departmentId}
                onChange={(v) => { setFilters({ departmentId: v }); list.setPage(0) }}
                placeholder={t('user.allDepartmentsPlaceholder')}
                searchPlaceholder={t('user.searchDeptPlaceholder')}
                emptyText={t('user.noDeptFound')}
                clearable
              />
            </div>
            <span className="text-muted-foreground shrink-0 text-xs">{t('user.statusFilterLabel')}</span>
            <Select
              value={filters.isActive || 'all'}
              onValueChange={(value) => {
                setFilters({ isActive: value === 'all' ? '' : value })
                list.setPage(0)
              }}
            >
              <SelectTrigger className="h-8 w-28" size="sm">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">{t('user.allStatusesPlaceholder')}</SelectItem>
                <SelectItem value="true">{t('user.activeLabel')}</SelectItem>
                <SelectItem value="false">{t('user.inactiveLabel')}</SelectItem>
              </SelectContent>
            </Select>
            <span className="text-muted-foreground shrink-0 text-xs">{t('user.roleFilterLabel')}</span>
            <div className="w-56 min-w-0">
              <RoleMultiSelect
                by="id"
                options={roles}
                value={filters.roleIds ? filters.roleIds.split(',') : []}
                onChange={(ids) => { setFilters({ roleIds: ids.join(',') }); list.setPage(0) }}
                placeholder={t('user.allRolesPlaceholder')}
                className="h-8 min-h-8 py-1"
              />
            </div>
          </div>
        }
        enableRowSelection
        selectedRowIds={selectedRowIds}
        onSelectedRowIdsChange={setSelectedRowIds}
        getRowId={(row) => row.id}
        bulkActions={(_rows, clearSelection, selectedIds) => (
          <Button
            size="sm"
            variant="outline"
            className="h-7"
            onClick={() => setBatchTarget({ ids: selectedIds, clear: clearSelection })}
          >
            <ShieldCheck className="mr-1 size-3.5" />{t('user.batchAssignRolesAction')}
          </Button>
        )}
      />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('user.createTitle'), edit: t('user.editTitle') }}
        contentClassName="sm:max-w-[480px]"
        canSubmit={!!crud.form.userName && !!crud.form.email && (crud.mode === 'edit' || !!crud.form.password)}
      >
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('user.formUserNameLabel')}</Label>
            <Input value={crud.form.userName} onChange={e => crud.set({ userName: e.target.value })} placeholder={t('user.formUserNamePlaceholder')} />
            <p className="text-muted-foreground text-xs">{t('user.formUserNameHint')}</p>
          </div>
          <div className="space-y-1.5">
            <Label>{t('user.formDisplayNameLabel')}</Label>
            <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('user.formDisplayNamePlaceholder')} />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('user.formEmailLabel')}</Label>
            <Input value={crud.form.email} onChange={e => crud.set({ email: e.target.value })} placeholder="user@example.com" />
          </div>
          <div className="space-y-1.5">
            <Label>{t('user.formPhoneLabel')}</Label>
            <Input value={crud.form.phoneNumber} onChange={e => crud.set({ phoneNumber: e.target.value })} placeholder="138..." />
          </div>
        </div>
        {crud.mode === 'create' && (
          <div className="space-y-1.5">
            <Label>{t('user.formPasswordLabel')}</Label>
            <PasswordInput
              value={crud.form.password}
              onChange={e => crud.set({ password: e.target.value })}
              placeholder={passwordPolicy
                ? t('user.formPasswordPlaceholder', { n: passwordPolicy.requiredLength })
                : t('user.formPasswordPlaceholderUnavailable')}
              rightSlot={
                <Button type="button" variant="ghost" size="sm" className="h-6 px-2 text-xs" onClick={() => crud.set({ password: passwordValue })}>
                  {t('user.useDefaultPasswordButton')}
                </Button>
              }
            />
          </div>
        )}
        <div className="space-y-1.5">
          <Label>{t('user.formLinkedEmployeeLabel')}</Label>
          <Combobox
            options={employeeOptions}
            value={crud.form.employeeId}
            onChange={v => {
              // 关联员工后默认带出员工的邮箱/手机号（仍可手动修改）
              const emp = employees.find(e => e.id === v)
              crud.set({
                employeeId: v,
                ...(emp?.email ? { email: emp.email } : {}),
                ...(emp?.phone ? { phoneNumber: emp.phone } : {}),
              })
            }}
            placeholder={t('user.linkedEmployeePlaceholder')}
            searchPlaceholder={t('user.searchEmployeePlaceholder')}
            emptyText={t('user.noEmployeeFound')}
            clearable
          />
        </div>
        <div className="space-y-1.5">
          <Label>{t('user.formRolesLabel')}</Label>
          <RoleMultiSelect
            options={roles}
            value={crud.form.roleNames}
            onChange={roleNames => crud.set({ roleNames })}
          />
        </div>
        <div className="flex items-center gap-2">
          <Switch id="isActive" checked={crud.form.isActive} onCheckedChange={v => crud.set({ isActive: v })} />
          <Label htmlFor="isActive">{t('user.enableAccountLabel')}</Label>
        </div>
      </CrudFormDialog>

      <ConfirmDialog
        open={!!resetTarget}
        onOpenChange={(o) => { if (!o) setResetTarget(null) }}
        title={t('user.resetPasswordDialogTitle')}
        description={t.rich('user.resetPasswordDialogDesc', {
          name: resetTarget?.name || resetTarget?.userName || '',
          userName: resetTarget?.userName ?? '',
          password: passwordValue,
          b: (chunks) => <strong>{chunks}</strong>,
          code: (chunks) => <code className="bg-muted text-foreground mx-1 rounded px-1 py-0.5 font-mono">{chunks}</code>,
        })}
        confirmText={t('user.confirmResetButton')}
        onConfirm={handleResetPassword}
      />

      <ConfirmDialog
        open={!!impersonateTarget}
        onOpenChange={(o) => { if (!o) setImpersonateTarget(null) }}
        title={t('user.impersonateDialogTitle')}
        description={t.rich('user.impersonateDialogDesc', {
          name: impersonateTarget?.name || impersonateTarget?.userName || '',
          userName: impersonateTarget?.userName ?? '',
          b: (chunks) => <strong>{chunks}</strong>,
        })}
        confirmText={t('user.confirmImpersonateButton')}
        busyText={t('user.impersonatingBusyText')}
        busy={impersonating}
        onConfirm={handleImpersonate}
      />

      <DeleteConfirm
        crud={crud}
        entityName={t('common.userEntity')}
        description={(u) => t.rich('user.deleteDesc', { userName: u.userName, b: (chunks) => <strong>{chunks}</strong> })}
      />

      <BatchRolesDialog
        target={batchTarget}
        onClose={() => setBatchTarget(null)}
        roles={roles}
        onDone={list.reloadAndRefresh}
      />

      <SecuritySettingsDialog
        open={securityOpen}
        onOpenChange={setSecurityOpen}
        fallbackPassword={passwordValue}
        onSaved={setPasswordValue}
      />

      <EffectivePermissionsSheet user={effectivePermTarget} onClose={() => setEffectivePermTarget(null)} />
    </div>
  )
}
