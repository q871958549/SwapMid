'use client'

import { useState } from 'react'
import { usePathname } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { Menu } from 'lucide-react'
import { IconButton } from '@/components/ui/icon-button'
import { Sheet, SheetContent, SheetTitle } from '@/components/ui/sheet'
import { Sidebar, type NavPreferenceSnapshot, type SidebarBrand } from './sidebar'

interface MobileNavDrawerProps {
  grantedPolicies: Record<string, boolean>
  initialNavPreference?: NavPreferenceSnapshot | null
  /** 与桌面侧栏同一份运行时发布标识，避免窄屏抽屉显示成 dev。 */
  releaseTag?: string
  brand?: SidebarBrand
  remoteChrome?: boolean
}

/**
 * 窄屏（`md` 以下）导航入口：Header 左端汉堡钮 + 左侧 Sheet 抽屉，抽屉里装的就是 `Sidebar` 的 drawer 形态。
 *
 * 设计取舍（2026-09-16 手机浏览器进后台壳被双栏挤压）：
 * - **不**把常驻栏自动收成图标列：图标列仍占 64px、且依赖 hover Popover 展开子项——触摸端没有 hover，
 *   等于把导航藏进「先点图标再点弹层」两跳；而 360px 屏减去 64px 后主栏依旧偏窄。抽屉把整块宽度还给内容。
 * - 桌面常驻栏与本抽屉是**两个 Sidebar 实例**、由断点互斥显示（`hidden md:flex` / `md:hidden`），
 *   不用 `matchMedia` 决定结构——服务端不知道视口宽度，JS 判断必然带来首帧结构不一致（hydration 抖动）。
 *   两实例状态互不共享是刻意的：抽屉每次打开都是全新面板，回到「跟随当前页所在域」的默认态，
 *   正是移动端想要的行为；导航个性顺序两边都从 layout 预取的同一份 props 初始化。
 * - SheetContent 内容在关闭时卸载（Radix 默认），常态下窄屏 DOM 只多一颗汉堡钮。
 */
export function MobileNavDrawer({
  grantedPolicies,
  initialNavPreference = null,
  releaseTag = '',
  brand,
  remoteChrome,
}: MobileNavDrawerProps) {
  const ts = useTranslations('Sidebar')
  const pathname = usePathname()
  const [open, setOpen] = useState(false)

  // 路由变化即收起：兜住所有不经 Sidebar.onNavigate 的跳转（域切换器里的「工作台」链接、全局搜索等）。
  // render-adjust 模式（与 sidebar.tsx 的 openGroupKey 同款），勿改 effect。
  const [lastPathname, setLastPathname] = useState(pathname)
  if (lastPathname !== pathname) {
    setLastPathname(pathname)
    if (open) setOpen(false)
  }

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <IconButton
        label={ts('openNav')}
        showTooltip={false}
        aria-expanded={open}
        className="size-9 md:hidden"
        onClick={() => setOpen(true)}
      >
        <Menu className="size-5" />
      </IconButton>
      <SheetContent
        side="left"
        showCloseButton={false}
        aria-describedby={undefined}
        // 宽 min(280px, 85vw)：小屏（320）留出可点的遮罩，≥330 固定 280、与桌面 w-56 观感相当。
        // 写成单个 w-[min()] 而非 w + max-w：Sheet 基类自带 sm:max-w-sm，再叠 max-w-* 会撞
        // swapmid/dialog-max-width-prefix 的级联坑（见 .claude/rules/frontend-controls.md）。
        // md: 那份是为了盖住 Sheet 基类的 md:w-[calc(100%-1rem)]——无前缀的 w-* 压不过带断点的变体。
        // 刻意不给 md:hidden：抽屉打开时把窗口拉宽，遮罩仍在、内容一藏就成了「点不掉的黑幕」，
        // 让它作为普通抽屉留着（Esc / 点遮罩可关）更稳；桌面本就没有触发它的入口。
        // 拉宽到 md 以上时它跟着 Sheet 变成浮动卡片，位置正好落在桌面常驻栏上。
        className="w-[min(17.5rem,85vw)] md:w-[min(17.5rem,85vw)]"
      >
        <SheetTitle className="sr-only">{ts('navMenu')}</SheetTitle>
        <Sidebar
          variant="drawer"
          grantedPolicies={grantedPolicies}
          initialNavPreference={initialNavPreference}
          releaseTag={releaseTag}
          brand={brand}
          remoteChrome={remoteChrome}
          onRequestClose={() => setOpen(false)}
        />
      </SheetContent>
    </Sheet>
  )
}
