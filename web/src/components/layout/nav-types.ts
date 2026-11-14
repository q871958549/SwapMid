export interface NavItem {
  title: string
  titleKey?: string
  href: string
  icon: React.ComponentType<{ className?: string }>
  /** 账号自助入口（如本人审批），须显式声明；遗漏权限默认拒绝。 */
  authenticated?: boolean
  permission?: string
  /** 任一权限可浏览的共享维护入口；后端仍逐类别校验写权限。 */
  permissionAny?: readonly string[]
  /**
   * 次级归属（蓝图 P2-1 / 决策 Q3）：除所在组的业务域外，还在这些业务域里出现（域 key）。
   * 只是多一个入口：路径 → 域解析、权限、页签标题仍只认主归属。
   */
  alsoIn?: readonly string[]
}

export interface NavGroup {
  key: string
  title: string
  titleKey?: string
  icon: React.ComponentType<{ className?: string }>
  items: NavItem[]
  /** 仅 resolveDomains 生成的次级归属组有：来源组的 key。这类组是入口副本，不在 navGroups 里。 */
  alsoOf?: string
}

export interface NavDomain {
  key: string
  title: string
  titleKey?: string
  icon: React.ComponentType<{ className?: string }>
  description: string
  descriptionKey?: string
  groupKeys: string[]
}
