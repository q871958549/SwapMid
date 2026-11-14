'use client'

import { useState, type MouseEvent } from 'react'
import { useTranslations } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { type VariantProps } from 'class-variance-authority'
import { KeyRound, LogIn, ShieldCheck } from 'lucide-react'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Badge, badgeVariants } from '@/components/ui/badge'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Skeleton } from '@/components/ui/skeleton'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { dataTableLinkClass } from '@/components/data-table/data-table-link-cell'
import { cn } from '@/lib/utils'
import { actionsColumn, booleanBadgeColumn } from '@/components/data-table/column-factories'
import { getApiErrorMessage } from '@/lib/api-error'
import { bindSessionIdentityToUrl } from '@/lib/session-availability'
import type { BoundEmployeeInfoDto } from '@/lib/api/types'
import { RoleBadges } from './role-controls'
import { type AbpUser } from './users-types'

interface UsersColumnsHandlers {
  onEdit: (user: AbpUser) => void
  onResetPassword: (user: AbpUser) => void
  onDelete: (user: AbpUser) => void
  onImpersonate: (user: AbpUser) => void
  /** P1-4：查看有效权限抽屉，无额外前端权限门控（后端 ManagePermissions 内层校验，403 由抽屉自身容错）。 */
  onEffectivePermissions: (user: AbpUser) => void
  onToggleActive: (user: AbpUser, next: boolean) => void
  statusBusy: boolean
  /** 是否渲染"以此账号登录"入口——由 client.tsx 综合 Impersonate 权限 + 非模拟态两条件算好传入。 */
  canImpersonate: boolean
  /** 当前登录账号名（模拟态下=正被模拟的目标账号名），用于禁用"模拟自己"这一行。 */
  currentUserName: string
  /** 当前文档身份世代，用于无法附加请求头的头像原生 GET。 */
  identityVersion: string
  /** getUsersColumns 是普通函数（非组件，非渲染期调用不能自己 useTranslations），
   * 由调用方 UsersClient（已持有两个 hook）经参数传入。 */
  t: ReturnType<typeof useTranslations>
  tc: ReturnType<typeof useTranslations>
}

// 在职状态映射（本模块就地小映射，不跨模块复用 enterprise-management/employees 的 EMP_STATUS——
// eslint swapmid/no-cross-module-import 焊死跨模块 import，且该模块用 success/secondary/outline 三色，
// 本处按蓝图规格改用 success/warning/destructive 三色区分度更高）。
// 文案随 useTranslations 求值，改成函数、由渲染期的组件（BoundEmployeeCell）调用，而非模块级常量。
type BadgeVariant = NonNullable<VariantProps<typeof badgeVariants>['variant']>
function getEmployeeStatusMeta(t: ReturnType<typeof useTranslations>): Record<number, { label: string; variant: BadgeVariant }> {
  return {
    0: { label: t('user.employedStatus0'), variant: 'success' },
    1: { label: t('user.employedStatus1'), variant: 'warning' },
    2: { label: t('user.employedStatus2'), variant: 'destructive' },
  }
}

type BoundEmployeeState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; data: BoundEmployeeInfoDto | null }
  | { status: 'error'; message: string }

/** 绑定员工标签：点击才懒查详情（列表页不预拉），Popover 内 Skeleton → 基本信息 dl。 */
function BoundEmployeeCell({ userId, label }: { userId: string; label: string }) {
  const t = useTranslations('Account')
  const tc = useTranslations('Common')
  const [state, setState] = useState<BoundEmployeeState>({ status: 'idle' })

  function handleOpenChange(open: boolean) {
    if (!open || state.status === 'loading' || state.status === 'success') return
    setState({ status: 'loading' })
    fetch(`/api/proxy/account-users/${userId}/bound-employee`)
      .then(async (res) => {
        if (!res.ok) throw new Error(await res.text())
        const data = (await res.json()) as BoundEmployeeInfoDto | null
        setState({ status: 'success', data })
      })
      .catch((e: unknown) => {
        console.error(e)
        setState({ status: 'error', message: getApiErrorMessage(e, t('user.loadFailedRetry')) })
      })
  }

  const statusMeta = state.status === 'success' && state.data
    ? getEmployeeStatusMeta(t)[state.data.status]
    : undefined

  return (
    <Popover onOpenChange={handleOpenChange}>
      <PopoverTrigger asChild>
        <button
          type="button"
          className="shrink-0 rounded focus-visible:outline-none focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)"
        >
          <Badge variant="outline" className="h-4 cursor-pointer px-1 text-[10px] hover:bg-accent">
            {label}
          </Badge>
        </button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-72 p-3">
        {state.status === 'idle' || state.status === 'loading' ? (
          <div className="space-y-2">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-3 w-full" />
            <Skeleton className="h-3 w-full" />
            <Skeleton className="h-3 w-2/3" />
          </div>
        ) : state.status === 'error' ? (
          <p className="text-destructive text-xs">{state.message}</p>
        ) : !state.data ? (
          <p className="text-muted-foreground text-xs">{t('user.noBoundEmployeeInfo')}</p>
        ) : (
          <div className="space-y-2">
            <div className="flex items-center justify-between gap-2">
              <div className="truncate font-medium leading-none">{state.data.name}</div>
              <Badge variant={statusMeta?.variant ?? 'secondary'} className="shrink-0 text-[10px]">
                {statusMeta?.label ?? t('user.unknownLabel')}
              </Badge>
            </div>
            <dl className="grid grid-cols-[3.5rem_1fr] gap-x-2 gap-y-1.5 text-xs">
              {[
                [tc('employeeNo'), state.data.employeeNo],
                [tc('enterprise'), state.data.enterpriseName],
                [tc('department'), state.data.departmentName],
                [tc('position'), state.data.positionName ?? state.data.position],
                [tc('email'), state.data.email],
                [tc('phone'), state.data.phone],
              ]
                .filter(([, value]) => value)
                .map(([name, value]) => (
                  <div key={name} className="contents">
                    <dt className="text-muted-foreground">{name}</dt>
                    <dd className="truncate text-foreground">{value}</dd>
                  </div>
                ))}
            </dl>
          </div>
        )}
      </PopoverContent>
    </Popover>
  )
}

/** 账号列表列定义。操作列经列工厂生成（编辑 / 重置为初始密码 / 以此账号登录 / 删除）。 */
export function getUsersColumns({
  onEdit, onResetPassword, onDelete, onImpersonate, onEffectivePermissions, onToggleActive, statusBusy, canImpersonate, currentUserName, identityVersion, t, tc,
}: UsersColumnsHandlers): ColumnDef<AbpUser, unknown>[] {
  return [
    {
      accessorKey: 'userName',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('user.accountColumnHeader')} />,
      size: 220,
      cell: ({ row }) => {
        const u = row.original
        return (
          <div className="flex items-center gap-2.5">
            <button
              type="button"
              className={cn(dataTableLinkClass, 'flex min-w-0 flex-1 items-center gap-2.5 text-left')}
              onClick={(event: MouseEvent<HTMLButtonElement>) => {
                const selection = window.getSelection()
                if (selection && !selection.isCollapsed && selection.toString().trim().length > 0) return
                event.stopPropagation()
                onEdit(u)
              }}
            >
              <Avatar className="size-7 shrink-0">
                <AvatarImage
                  src={bindSessionIdentityToUrl(
                    `/api/proxy/api/v1/avatars/${u.id}`,
                    identityVersion,
                  )}
                  alt=""
                />
                <AvatarFallback className="text-xs">
                  {(u.name || u.userName).slice(0, 2).toUpperCase()}
                </AvatarFallback>
              </Avatar>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-medium">{u.name || u.userName}</span>
                <span className="block truncate text-xs font-normal text-muted-foreground">@{u.userName}</span>
              </span>
            </button>
            {u.boundEmployeeId && (
              <BoundEmployeeCell userId={u.id} label={u.boundEmployeeName ?? t('user.unnamedLabel')} />
            )}
          </div>
        )
      },
    },
    {
      accessorKey: 'email',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('user.emailColumnHeader')} />,
      size: 200,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.email}</span>,
    },
    {
      accessorKey: 'phoneNumber',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('user.phoneColumnHeader')} />,
      size: 140,
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{row.original.phoneNumber ?? '—'}</span>
      ),
    },
    booleanBadgeColumn<AbpUser>('isActive', t('common.statusColumnHeader'), {
      trueLabel: t('user.activeLabel'),
      falseLabel: t('user.inactiveLabel'),
      onToggle: onToggleActive,
      isToggleDisabled: () => statusBusy,
    }),
    {
      accessorKey: 'roleNames',
      enableSorting: false, // 关联角色名，后端排序需 join，暂不支持服务端排序
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('user.rolesColumnHeader')} />,
      meta: { title: t('user.rolesColumnHeader') },
      size: 180,
      cell: ({ row }) => <RoleBadges roles={row.original.roleNames ?? []} />,
    },
    {
      accessorKey: 'creationTime',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('creationTime')} />,
      size: 160,
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {new Date(row.original.creationTime).toLocaleDateString('zh-CN')}
        </span>
      ),
    },
    actionsColumn<AbpUser>({
      size: 56,
      onEdit,
      onDelete,
      extraItems: (u) => (
        <>
          <DropdownMenuItem onClick={() => onResetPassword(u)}>
            <KeyRound className="mr-2 size-3.5" />{t('user.resetPasswordAction')}
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => onEffectivePermissions(u)}>
            <ShieldCheck className="mr-2 size-3.5" />{t('user.effectivePermissionsAction')}
          </DropdownMenuItem>
          {canImpersonate && (
            <DropdownMenuItem
              disabled={!u.isActive || u.userName === currentUserName}
              onClick={() => onImpersonate(u)}
            >
              <LogIn className="mr-2 size-3.5" />{t('user.impersonateDialogTitle')}
            </DropdownMenuItem>
          )}
        </>
      ),
    }),
  ]
}
