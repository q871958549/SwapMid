'use client'

import { useRouter } from 'next/navigation'
import { canAccessPath } from '@/lib/nav-utils'
import { HOME_KEY, resolveStoredPagePath, useTabStore } from '@/stores/use-tab-store'
import { TabListMenu } from '@/components/layout/tab-list-menu'
import { cn } from '@/lib/utils'

interface Props {
  grantedPolicies: Record<string, boolean>
  className?: string
}

/**
 * 页签菜单的独立入口。窄屏 TabBar 整条隐藏后，把它挂到 Header，避免再占一行重复标题。
 */
export function ConnectedTabListMenu({ grantedPolicies, className }: Props) {
  const router = useRouter()
  const { tabs, activeKey, closeTab, closeOthers, closeAll, setActive } = useTabStore()

  function navigate(path: string) {
    router.push(resolveStoredPagePath(path))
  }

  function handleSelect(tab: { key: string; path: string }) {
    if (!canAccessPath(tab.path, grantedPolicies)) {
      navigate(closeTab(tab.key))
      return
    }
    setActive(tab.key)
    navigate(tab.path)
  }

  return (
    <div className={cn('flex items-center', className)}>
      <TabListMenu
        tabs={tabs}
        activeKey={activeKey}
        onSelect={handleSelect}
        onClose={(key) => navigate(closeTab(key))}
        onCloseOthers={() => {
          closeOthers(activeKey)
          navigate(activeKey)
        }}
        onCloseAll={() => {
          closeAll()
          navigate(HOME_KEY)
        }}
      />
    </div>
  )
}
