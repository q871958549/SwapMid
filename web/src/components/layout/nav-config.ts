import { BookOpen, Building2, LayoutDashboard, Settings, Workflow } from 'lucide-react'
import type { NavDomain, NavGroup } from './nav-types'
import { dataGroup } from './nav-groups/data'
import { messageGroup } from './nav-groups/message'
import { apiGroup } from './nav-groups/api'
import { enterpriseGroup } from './nav-groups/enterprise'
import { accountGroup } from './nav-groups/account'
import { systemGroup } from './nav-groups/system'
import { docsGroup } from './nav-groups/docs'

export type { NavDomain, NavGroup, NavItem } from './nav-types'

export const navGroups: NavGroup[] = [
  dataGroup,
  messageGroup,
  apiGroup,
  enterpriseGroup,
  accountGroup,
  systemGroup,
  docsGroup,
]

export const WORKBENCH_DOMAIN: NavDomain = {
  key: 'workbench',
  title: '工作台',
  titleKey: 'domains.workbench.title',
  description: '平台运行概览、最近访问与域导航入口',
  descriptionKey: 'domains.workbench.description',
  icon: LayoutDashboard,
  groupKeys: [],
}

export const navDomains: NavDomain[] = [
  {
    key: 'integration',
    title: '集成引擎',
    titleKey: 'domains.integration.title',
    description: '多源流式数据管道、消息总线可靠投递与对外 API 网关',
    descriptionKey: 'domains.integration.description',
    icon: Workflow,
    groupKeys: ['data', 'message', 'api'],
  },
  {
    key: 'organization',
    title: '组织与身份',
    titleKey: 'domains.organization.title',
    description: '企业部门岗位员工主数据、账号角色与细粒度权限治理',
    descriptionKey: 'domains.organization.description',
    icon: Building2,
    groupKeys: ['enterprise', 'account'],
  },
  {
    key: 'platform',
    title: '系统与运维',
    titleKey: 'domains.platform.title',
    description: '编码规则、数据字典、定时调度、通知告警与系统体检',
    descriptionKey: 'domains.platform.description',
    icon: Settings,
    groupKeys: ['system'],
  },
  {
    key: 'developer',
    title: '开发者中心',
    titleKey: 'domains.developer.title',
    description: 'OpenAPI 接口参考、集成指南与数据库表结构字典',
    descriptionKey: 'domains.developer.description',
    icon: BookOpen,
    groupKeys: ['docs'],
  },
]
