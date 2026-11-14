'use client'

import { useEffect, useRef, useState, type RefObject } from 'react'
import { ChevronDown, GripVertical } from 'lucide-react'
import { motion, AnimatePresence, Reorder, useDragControls } from 'framer-motion'
import { useTranslations } from 'next-intl'
import { cn } from '@/lib/utils'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import type { NavGroup, NavItem } from './nav-config'
import { PageLink } from './page-link'
import {
  findSidebarHighlightItem,
  isNavItemVisible,
  isPathInNavGroup,
  translateNavTitle,
  type NavT,
} from '@/lib/nav-utils'
import { applyOrder } from '@/lib/nav-order'

/**
 * 拖拽时的公共参数（组与项共用）：
 * - 拖动范围钉在所属列表内：项的外层是组展开动画的 `overflow-hidden`、组的外层是侧栏滚动区，
 *   不钉住时指针一拖出列表，被拖的那一行就被裁掉、看不见了。
 *   dragElastic 0：钉死在边缘，不做回弹拉伸——拉伸的那一截照样会被裁。
 *   范围（ref）只在指针按下期间挂上，平时为空，原因见 `usePointerPressed`。
 * - 不放大：滚动区里 scale 会撑出横向溢出，侧栏冒出横向滚动条、两侧被裁。提起感改由阴影 + 实底表达。
 */
export const DRAG_LIFT = { zIndex: 30, boxShadow: 'var(--shadow-md)', cursor: 'grabbing' } as const

/**
 * 指针在列表里按下期间为 true，松开或取消后复位。拖动范围只在这段时间传给 `dragConstraints`。
 *
 * 不能常驻：framer-motion 对 ref 形式的 `dragConstraints` 会给被拖元素和范围容器挂 ResizeObserver，
 * 尺寸一变就调 `scalePositionWithinConstraints`，不管有没有在拖。
 * 按下时才传 ref：拖拽起步时照常量范围；松开后为空，那段重摆逻辑直接返回。
 */
export function usePointerPressed() {
  const [pressed, setPressed] = useState(false)
  useEffect(() => {
    if (!pressed) return
    const release = () => setPressed(false)
    window.addEventListener('pointerup', release)
    window.addEventListener('pointercancel', release)
    return () => {
      window.removeEventListener('pointerup', release)
      window.removeEventListener('pointercancel', release)
    }
  }, [pressed])
  return [pressed, () => setPressed(true)] as const
}

/**
 * 组内导航项。`reorderable` 时整行可拖（Reorder.Item 包住 PageLink）。
 */
export function NavLinkItem({
  item,
  isActive,
  reorderable,
  constraintsRef,
  onNavigate,
  onPersist,
}: {
  item: NavItem
  isActive: boolean
  reorderable: boolean
  /** 拖动范围（所属组的项列表）。 */
  constraintsRef?: RefObject<HTMLDivElement | null>
  onNavigate: (href: string) => void
  onPersist: () => void
}) {
  const t = useTranslations('Nav') as NavT
  const suppressClickRef = useRef(false)
  const ItemIcon = item.icon

  const link = (
    <PageLink
      href={item.href}
      draggable={false}
      onClick={(e) => {
        if (suppressClickRef.current) { e.preventDefault(); return }
        onNavigate(item.href)
      }}
      className={cn(
        'flex items-center gap-2 rounded-md px-2.5 py-1.5 text-[13px] transition-all duration-150 pointer-coarse:py-2.5',
        isActive
          ? 'bg-primary text-primary-foreground font-medium shadow-sm'
          : 'text-sidebar-foreground/55 hover:text-sidebar-foreground hover:bg-sidebar-accent/50',
      )}
    >
      <ItemIcon className="size-3.5 shrink-0" />
      {translateNavTitle(item, t)}
    </PageLink>
  )

  if (!reorderable) return link

  return (
    <Reorder.Item
      as="div"
      value={item.href}
      onDragStart={() => { suppressClickRef.current = true }}
      onDragEnd={() => {
        setTimeout(() => { suppressClickRef.current = false }, 0)
        onPersist()
      }}
      dragConstraints={constraintsRef}
      dragElastic={0}
      whileDrag={DRAG_LIFT}
      className="rounded-md bg-sidebar"
    >
      {link}
    </Reorder.Item>
  )
}

export function NavGroupItem({
  group,
  policies,
  pathname,
  open,
  itemOrder,
  reorderable,
  constraintsRef,
  dragBounded = false,
  onToggle,
  onNavigate,
  onReorderItems,
  onPersist,
}: {
  group: NavGroup
  policies: Record<string, boolean>
  pathname: string
  open: boolean
  itemOrder: string[] | null
  /** 工作台态平铺全部域、不支持重排：不渲染把手，根退化为普通 div。 */
  reorderable: boolean
  /** 组的拖动范围（整个组列表）。 */
  constraintsRef?: RefObject<HTMLDivElement | null>
  /** 指针按下期间才为 true：组与项的拖动范围只在这段时间挂上。 */
  dragBounded?: boolean
  onToggle: () => void
  onNavigate: (href: string) => void
  /** 收到的是「当前可见项」的新顺序；不可见项由 applyOrder 保序追加末尾。 */
  onReorderItems: (groupKey: string, nextHrefs: string[]) => void
  /** 拖拽结束时落库一次。 */
  onPersist: () => void
}) {
  const t = useTranslations('Nav') as NavT
  const controls = useDragControls()
  const suppressToggleRef = useRef(false)
  const itemsRef = useRef<HTMLDivElement>(null)
  const visibleItems = applyOrder(group.items, itemOrder, (item) => item.href)
    .filter((item) => isNavItemVisible(item, policies))
  const activeHref = findSidebarHighlightItem(pathname)?.href ?? null
  const isGroupActive = group.alsoOf ? group.items.some((item) => item.href === activeHref) : isPathInNavGroup(pathname, group)

  if (visibleItems.length === 0) return null

  const Icon = group.icon

  const body = (
    <>
      <button
        onClick={() => { if (suppressToggleRef.current) return; onToggle() }}
        onPointerDown={reorderable ? (e) => controls.start(e) : undefined}
        className={cn(
          'flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm transition-all duration-150 pointer-coarse:py-2.5',
          isGroupActive
            ? 'text-sidebar-primary font-medium'
            : 'text-sidebar-foreground/60 hover:text-sidebar-foreground hover:bg-sidebar-accent/40'
        )}
      >
        <span className={cn(
          'flex size-6 shrink-0 items-center justify-center rounded-md transition-colors',
          isGroupActive ? 'bg-primary/10 text-primary' : 'text-sidebar-foreground/50'
        )}>
          <Icon className="size-3.5" />
        </span>
        <span className="flex-1 text-left text-[13px]">{translateNavTitle(group, t)}</span>
        {reorderable && (
          <span
            aria-hidden
            onPointerDown={(e) => { e.stopPropagation(); controls.start(e) }}
            onClick={(e) => e.stopPropagation()}
            className="cursor-grab touch-none rounded opacity-0 transition-opacity group-hover/nav:opacity-40 hover:!opacity-80"
          >
            <GripVertical className="size-3" />
          </span>
        )}
        <ChevronDown className={cn('size-3 opacity-50 transition-transform duration-200', open && 'rotate-180')} />
      </button>

      <AnimatePresence initial={false}>
        {open && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.18, ease: 'easeInOut' }}
            className="overflow-hidden"
          >
            <div className="ml-[1.375rem] mt-0.5 space-y-0.5 border-l border-sidebar-border/50 pl-3 pb-1">
              {reorderable ? (
                <Reorder.Group
                  ref={itemsRef}
                  as="div"
                  axis="y"
                  values={visibleItems.map((item) => item.href)}
                  onReorder={(nextHrefs) => onReorderItems(group.key, nextHrefs)}
                  className="space-y-0.5"
                >
                  {visibleItems.map((item) => (
                    <NavLinkItem
                      key={item.href}
                      item={item}
                      isActive={item.href === activeHref}
                      reorderable
                      constraintsRef={dragBounded ? itemsRef : undefined}
                      onNavigate={onNavigate}
                      onPersist={onPersist}
                    />
                  ))}
                </Reorder.Group>
              ) : (
                visibleItems.map((item) => (
                  <NavLinkItem
                    key={item.href}
                    item={item}
                    isActive={item.href === activeHref}
                    reorderable={false}
                    onNavigate={onNavigate}
                    onPersist={onPersist}
                  />
                ))
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </>
  )

  const rootClass = 'group/nav mb-0.5 rounded-lg'
  if (!reorderable) return <div className={rootClass}>{body}</div>

  return (
    <Reorder.Item
      as="div"
      value={group.key}
      dragListener={false}
      dragControls={controls}
      onDragStart={() => { suppressToggleRef.current = true }}
      onDragEnd={() => {
        setTimeout(() => { suppressToggleRef.current = false }, 0)
        onPersist()
      }}
      dragConstraints={dragBounded ? constraintsRef : undefined}
      dragElastic={0}
      whileDrag={{ ...DRAG_LIFT, boxShadow: 'var(--shadow-lg)' }}
      className={cn(rootClass, 'bg-sidebar')}
    >
      {body}
    </Reorder.Item>
  )
}

export function CollapsedNavGroup({
  group,
  policies,
  pathname,
  itemOrder,
  onNavigate,
}: {
  group: NavGroup
  policies: Record<string, boolean>
  pathname: string
  itemOrder: string[] | null
  onNavigate: (href: string) => void
}) {
  const t = useTranslations('Nav') as NavT
  const [open, setOpen] = useState(false)
  const closeTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(() => () => { if (closeTimer.current) clearTimeout(closeTimer.current) }, [])

  const visibleItems = applyOrder(group.items, itemOrder, (item) => item.href)
    .filter((item) => isNavItemVisible(item, policies))
  if (visibleItems.length === 0) return null

  const Icon = group.icon
  const activeHref = findSidebarHighlightItem(pathname)?.href ?? null
  const isGroupActive = group.alsoOf ? group.items.some((item) => item.href === activeHref) : isPathInNavGroup(pathname, group)

  function openNow() { if (closeTimer.current) clearTimeout(closeTimer.current); setOpen(true) }
  function closeSoon() { closeTimer.current = setTimeout(() => setOpen(false), 120) }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <button
          type="button"
          aria-label={translateNavTitle(group, t)}
          onMouseEnter={openNow}
          onMouseLeave={closeSoon}
          onClick={openNow}
          className={cn(
            'flex w-full items-center justify-center rounded-lg py-2.5 transition-colors',
            isGroupActive
              ? 'bg-primary/10 text-primary'
              : 'text-sidebar-foreground/55 hover:bg-sidebar-accent/40 hover:text-sidebar-foreground'
          )}
        >
          <Icon className="size-4" />
        </button>
      </PopoverTrigger>
      <PopoverContent
        side="right"
        align="start"
        sideOffset={10}
        onMouseEnter={openNow}
        onMouseLeave={closeSoon}
        className="w-52 p-1.5 select-none"
      >
        <p className="px-2 py-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground/70">{translateNavTitle(group, t)}</p>
        <div className="space-y-0.5">
          {visibleItems.map((item) => {
            const isActive = item.href === activeHref
            const ItemIcon = item.icon
            return (
              <PageLink
                key={item.href}
                href={item.href}
                onClick={() => { setOpen(false); onNavigate(item.href) }}
                className={cn(
                  'flex items-center gap-2 rounded-md px-2.5 py-1.5 text-[13px] transition-colors',
                  isActive
                    ? 'bg-primary text-primary-foreground font-medium'
                    : 'text-foreground/70 hover:bg-accent hover:text-foreground'
                )}
              >
                <ItemIcon className="size-3.5 shrink-0" />
                {translateNavTitle(item, t)}
              </PageLink>
            )
          })}
        </div>
      </PopoverContent>
    </Popover>
  )
}
