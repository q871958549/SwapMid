'use client'

import Link from 'next/link'
import { DataTableLinkCell, dataTableLinkClass } from '@/components/data-table/data-table-link-cell'
import { useTranslations } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { Copy, Info, Lock, MoreHorizontal, Pencil, Trash2, UsersRound } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { IconButton } from '@/components/ui/icon-button'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem,
  DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { booleanBadgeColumn } from '@/components/data-table/column-factories'
import type { AccountRoleDto } from '@/lib/api/types'

interface RoleColumnHandlers {
  onViewDetail: (role: AccountRoleDto) => void
  onAssign: (role: AccountRoleDto) => void
  onCopy: (role: AccountRoleDto) => void
  onEdit: (role: AccountRoleDto) => void
  onDelete: (role: AccountRoleDto) => void
  /** getRolesColumns 是普通函数（非组件，非渲染期调用不能自己 useTranslations），
   * 由调用方 RolesClient（已持有两个 hook）经参数传入。 */
  t: ReturnType<typeof useTranslations>
  tc: ReturnType<typeof useTranslations>
}

/** 角色列表列定义（P0-10 + P1-1 详情入口）：名称(点击开详情抽屉) / 描述 / 成员数 / 权限数(点击跳权限页) /
 * 默认 / 系统角色 / 公开(默认隐藏) / 操作。 */
export function getRolesColumns({ onViewDetail, onAssign, onCopy, onEdit, onDelete, t, tc }: RoleColumnHandlers): ColumnDef<AccountRoleDto, unknown>[] {
  return [
    {
      accessorKey: 'name',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('role.nameColumnHeader')} />,
      size: 180,
      cell: ({ row }) => (
        <button
          type="button"
          className={`${dataTableLinkClass} font-medium focus-visible:outline-none focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color) rounded`}
          onClick={() => onViewDetail(row.original)}
        >
          {row.original.name}
        </button>
      ),
    },
    {
      accessorKey: 'description',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('role.descriptionColumnHeader')} />,
      size: 220,
      cell: ({ row }) => {
        const desc = row.original.description
        return desc
          ? <span className="block truncate text-sm text-muted-foreground" title={desc}>{desc}</span>
          : <span className="text-sm text-muted-foreground">—</span>
      },
    },
    {
      accessorKey: 'memberCount',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('role.memberCountColumnHeader')} />,
      size: 100,
      cell: ({ row }) => <Badge variant="secondary" className="tabular-nums">{row.original.memberCount}</Badge>,
    },
    {
      accessorKey: 'permissionCount',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('role.permissionCountColumnHeader')} />,
      size: 110,
      cell: ({ row }) => (
        <DataTableLinkCell
          href={`/account-management/permissions?roleName=${encodeURIComponent(row.original.name)}`}
          title={t('role.permissionCountLinkTitle')}
        >
          <Badge variant="outline" className="tabular-nums hover:bg-accent">
            {row.original.permissionCount}
          </Badge>
        </DataTableLinkCell>
      ),
    },
    booleanBadgeColumn<AccountRoleDto>('isDefault', t('common.defaultRoleLabel'), { trueLabel: t('common.yes'), falseLabel: t('common.no'), size: 100 }),
    // isStatic 复用 common.systemRoleLabel（与 RemoteCombobox 里"系统角色"描述同一措辞），
    // 比原「静态角色」更贴合业务语义——admin 等内置角色本质是"系统角色"而非技术意义的"静态"。
    booleanBadgeColumn<AccountRoleDto>('isStatic', t('common.systemRoleLabel'), { trueLabel: t('common.yes'), falseLabel: t('common.no'), trueVariant: 'outline', size: 100 }),
    // isPublic 默认隐藏（内部平台低价值），列显隐菜单可再开；见 data-table-filters.ts 的 meta.defaultHidden。
    booleanBadgeColumn<AccountRoleDto>('isPublic', t('role.isPublicColumnHeader'), { trueLabel: t('common.yes'), falseLabel: t('common.no'), size: 100, defaultHidden: true }),
    {
      id: 'actions',
      size: 56,
      // 手写下拉是本页认可的逃生舱（多动作 + isStatic 联动条件渲染，见 web/AGENTS.md「下拉按钮/菜单触发器规范」A 型）。
      cell: ({ row }) => {
        const r = row.original
        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <IconButton label={tc('actions')} className="size-7">
                <MoreHorizontal className="size-4" />
              </IconButton>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onClick={() => onViewDetail(r)}>
                <Info className="mr-2 size-3.5" />{t('role.detailAction')}
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={() => onAssign(r)}>
                <UsersRound className="mr-2 size-3.5" />{t('role.assignAccountsAction')}
              </DropdownMenuItem>
              <DropdownMenuItem asChild>
                <Link href={`/account-management/permissions?roleName=${encodeURIComponent(r.name)}`}>
                  <Lock className="mr-2 size-3.5" />{t('role.configurePermissionsAction')}
                </Link>
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              {/* 复制对 isStatic 角色同样开放——"以 admin 的权限集为模板建新角色"是最常见的复制场景，
                  后端 CopyAsync 也未限制源角色 isStatic（仅新角色恒非 isStatic）。 */}
              <DropdownMenuItem onClick={() => onCopy(r)}>
                <Copy className="mr-2 size-3.5" />{t('role.copyAction')}
              </DropdownMenuItem>
              {!r.isStatic && (
                <>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem onClick={() => onEdit(r)}>
                    <Pencil className="mr-2 size-3.5" />{t('role.editTitle')}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem variant="destructive" onClick={() => onDelete(r)}>
                    <Trash2 className="mr-2 size-3.5" />{tc('delete')}
                  </DropdownMenuItem>
                </>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        )
      },
    },
  ]
}
