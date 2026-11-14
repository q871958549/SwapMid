import { Briefcase, Building2, Network, Users } from 'lucide-react'
import type { NavGroup } from '../nav-types'

export const enterpriseGroup: NavGroup = {
  key: 'enterprise',
  title: '组织主数据',
  titleKey: 'groups.enterprise',
  icon: Building2,
  items: [
    {
      title: '企业管理',
      titleKey: 'items.enterprise.enterprises',
      href: '/enterprise-management/enterprises',
      icon: Building2,
      permission: 'SwapMid.EnterpriseManagement.Enterprises',
    },
    {
      title: '部门管理',
      titleKey: 'items.enterprise.departments',
      href: '/enterprise-management/departments',
      icon: Network,
      permission: 'SwapMid.EnterpriseManagement.Departments',
    },
    {
      title: '岗位管理',
      titleKey: 'items.enterprise.positions',
      href: '/enterprise-management/positions',
      icon: Briefcase,
      permission: 'SwapMid.EnterpriseManagement.Positions',
    },
    {
      title: '员工管理',
      titleKey: 'items.enterprise.employees',
      href: '/enterprise-management/employees',
      icon: Users,
      permission: 'SwapMid.EnterpriseManagement.Employees',
    },
  ],
}
