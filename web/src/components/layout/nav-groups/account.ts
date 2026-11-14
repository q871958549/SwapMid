import { KeySquare, Shield, UserCog, UsersRound } from 'lucide-react'
import type { NavGroup } from '../nav-types'

export const accountGroup: NavGroup = {
  key: 'account',
  title: '账号权限',
  titleKey: 'groups.account',
  icon: UserCog,
  items: [
    {
      title: '账号管理',
      titleKey: 'items.account.users',
      href: '/account-management/users',
      icon: UsersRound,
      permission: 'SwapMid.AccountManagement.Users',
    },
    {
      title: '角色管理',
      titleKey: 'items.account.roles',
      href: '/account-management/roles',
      icon: Shield,
      permission: 'SwapMid.AccountManagement.Roles',
    },
    {
      title: '权限管理',
      titleKey: 'items.account.permissions',
      href: '/account-management/permissions',
      icon: KeySquare,
      permission: 'SwapMid.AccountManagement.Roles.Update',
    },
  ],
}
