import { navDomains, navGroups, type NavDomain, type NavGroup } from '@/components/layout/nav-config'

export interface NavCatalog {
  groups: readonly NavGroup[]
  domains: readonly NavDomain[]
}

export function navCatalog(): NavCatalog {
  return {
    groups: navGroups,
    domains: navDomains,
  }
}
