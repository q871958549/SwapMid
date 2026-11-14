'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { cn } from '@/lib/utils'
import type { DomainPrefs } from '@/lib/domain-prefs'
import { applyNavOrder, type NavOrderState } from '@/lib/nav-order'
import type { NavT, ResolvedDomain } from '@/lib/nav-utils'
import { DomainSwitcher } from './domain-switcher'
import { DomainPrefsDialog } from './domain-prefs-dialog'
import type { NavDomain, NavGroup } from './nav-config'

interface SidebarDomainPickerProps {
  domains: ResolvedDomain[]
  shownDomains: ResolvedDomain[]
  hiddenDomains: ResolvedDomain[]
  recentDomainKeys: string[]
  prefs: DomainPrefs
  active: NavDomain
  collapsed: boolean
  isDrawer: boolean
  orderState: NavOrderState
  onPickDomain: (key: string) => void
  onSaveDomainPrefs: (next: DomainPrefs) => boolean
}

export function SidebarDomainPicker({
  domains,
  shownDomains,
  hiddenDomains,
  recentDomainKeys,
  prefs,
  active,
  collapsed,
  isDrawer,
  orderState,
  onPickDomain,
  onSaveDomainPrefs,
}: SidebarDomainPickerProps) {
  const t = useTranslations('Nav') as NavT
  const ts = useTranslations('Sidebar') as NavT
  const [manageDomainsOpen, setManageDomainsOpen] = useState(false)

  function previewGroupsOf(domainKey: string): NavGroup[] {
    const resolved = domains.find((d) => d.domain.key === domainKey)
    if (!resolved) return []
    return applyNavOrder(resolved.groups, orderState)
  }

  return (
    <div className={cn('shrink-0 border-b', collapsed ? 'px-2 py-2' : 'px-2.5 py-2')}>
      <DomainSwitcher
        domains={shownDomains}
        hiddenDomains={hiddenDomains}
        recentDomainKeys={recentDomainKeys}
        active={active}
        collapsed={collapsed}
        popoverSide={isDrawer ? 'bottom' : 'right'}
        onPick={onPickDomain}
        onManage={() => setManageDomainsOpen(true)}
        previewGroupsOf={isDrawer ? undefined : previewGroupsOf}
        t={t}
        ts={ts}
      />
      <DomainPrefsDialog
        open={manageDomainsOpen}
        onOpenChange={setManageDomainsOpen}
        domains={domains}
        prefs={prefs}
        onSave={onSaveDomainPrefs}
        t={t}
        ts={ts}
      />
    </div>
  )
}
