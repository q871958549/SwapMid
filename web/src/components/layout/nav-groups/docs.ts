import { BookOpen, Code2, Table2 } from 'lucide-react'
import type { NavGroup } from '../nav-types'

export const docsGroup: NavGroup = {
  key: 'docs',
  title: '开发者文档',
  titleKey: 'groups.docs',
  icon: BookOpen,
  items: [
    {
      title: 'API 参考',
      titleKey: 'items.docs.apiReference',
      href: '/docs/api-reference',
      icon: Code2,
      permission: 'SwapMid.DeveloperDocs.ApiReference',
    },
    {
      title: '数据库表字典',
      titleKey: 'items.docs.dbDictionary',
      href: '/docs/db-dictionary',
      icon: Table2,
      permission: 'SwapMid.DeveloperDocs.DbSchema',
    },
  ],
}
