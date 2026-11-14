'use client'

import { Home } from 'lucide-react'
import { resolveTabTitle, type NavT } from '@/lib/nav-utils'
import { HOME_KEY, type Tab } from '@/stores/use-tab-store'

interface MobileTabTitleProps {
  tabs: Tab[]
  activeKey: string
  t: NavT
}

/**
 * 窄屏页签栏的替身：只显示**当前页签**标题（首页带 Home 图标）。
 *
 * 桌面那条可拖拽、可横滚的页签带在 360px 上只塞得下两三个被截断的页签，还要腾位置给左右箭头，
 * 信息密度反而为负；而「我在哪一页」和「切到/关掉别的页签」两个诉求，前者用一行标题、
 * 后者交给右端常驻的 TabListMenu（全部页签 + 关闭其他/全部）即可覆盖。
 * 与 tab-bar.tsx 共用 store 与 resolveTabTitle，详情页由业务数据覆盖的标题在这里同样生效。
 */
export function MobileTabTitle({ tabs, activeKey, t }: MobileTabTitleProps) {
  const active = tabs.find((tab) => tab.key === activeKey) ?? tabs.find((tab) => tab.key === HOME_KEY) ?? tabs[0]
  if (!active) return null
  const isHome = active.key === HOME_KEY
  return (
    <div className="flex min-w-0 flex-1 items-center gap-1.5 px-3 text-[13px] font-medium text-foreground">
      {isHome && <Home className="size-3.5 shrink-0 text-primary" aria-hidden />}
      <span className="truncate">{resolveTabTitle(active, t)}</span>
    </div>
  )
}
