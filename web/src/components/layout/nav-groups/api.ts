import { FolderKanban, Globe, KeyRound, Route, Terminal } from 'lucide-react'
import type { NavGroup } from '../nav-types'

export const apiGroup: NavGroup = {
  key: 'api',
  title: 'API 管理',
  titleKey: 'groups.api',
  icon: Globe,
  items: [
    {
      title: 'API 分组',
      titleKey: 'items.api.groups',
      href: '/api-management/groups',
      icon: FolderKanban,
      permission: 'SwapMid.ApiManagement.ApiGroups',
    },
    {
      title: 'API 路由',
      titleKey: 'items.api.routes',
      href: '/api-management/routes',
      icon: Route,
      permission: 'SwapMid.ApiManagement.ApiRoutes',
    },
    {
      title: '在线调试',
      titleKey: 'items.api.testConsole',
      href: '/api-management/test-console',
      icon: Terminal,
      permission: 'SwapMid.ApiManagement.TestConsole',
    },
    {
      title: '第三方客户端',
      titleKey: 'items.api.clients',
      href: '/api-management/clients',
      icon: KeyRound,
      permission: 'SwapMid.ApiManagement.ThirdPartyClients',
    },
  ],
}
