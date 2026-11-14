'use client'

import Image from 'next/image'
import { usePathname } from 'next/navigation'
import { useEffect, useRef, useState } from 'react'
import { X } from 'lucide-react'
import { MotionConfig, Reorder } from 'framer-motion'
import { useTranslations } from 'next-intl'
import { cn } from '@/lib/utils'
import { navGroups, WORKBENCH_DOMAIN } from './nav-config'
import { SidebarDomainPicker } from './sidebar-domain-picker'
import { SidebarFooter } from './sidebar-footer'
import { CollapsedNavGroup, NavGroupItem, usePointerPressed } from './sidebar-nav-items'
import { useDomainNav } from './use-domain-nav'
import {
  allAlsoInGroups,
  DASHBOARD_PATH,
  findNavDomainKeyByPath,
  findNavGroupByPath,
  findNavItemByPath,
  resolveDomains,
  translateNavTitle,
  type NavT,
} from '@/lib/nav-utils'
import {
  applyNavOrder,
  applyOrder,
  isNavOrderCustomizedIn,
  mergeScopedOrder,
  NAV_ORDER_MAX_LENGTH,
  parseNavOrder,
  resetNavOrderIn,
  serializeCompactNavOrder,
  type NavOrderState,
} from '@/lib/nav-order'
import { toast } from '@/components/ui/use-toast'
import { ToastAction } from '@/components/ui/toast'
import { getReleaseDisplay } from '@/lib/release-label'
import { IconButton } from '@/components/ui/icon-button'

export { SidebarFooter } from './sidebar-footer'

/**
 * 侧栏形态：
 * - `inline`：桌面常驻左栏（默认），`md` 以下整体 `hidden`——窄屏不再与主栏抢宽度；
 * - `drawer`：窄屏抽屉（`mobile-nav-drawer.tsx` 用 Sheet 装它），恒为展开态、不读写折叠 cookie、
 *   关闭拖拽重排（触摸端拖拽与纵向滚动抢手势，且抽屉是临时面板、不是「布置工作台」的场景）。
 */
export type SidebarVariant = 'inline' | 'drawer'

export interface SidebarBrand {
  src: string
  alt: string
  title: string
  subtitle: string
}

export const SWAPMID_SIDEBAR_BRAND: SidebarBrand = {
  src: '/swapmid-logo.svg',
  alt: 'SwapMid',
  title: 'SwapMid',
  subtitle: 'Enterprise',
}

interface SidebarProps {
  grantedPolicies: Record<string, boolean>
  initialCollapsed?: boolean
  /** 服务端预取的导航个性顺序（layout 注入），首帧即正确顺序，避免挂载后跳变 */
  initialNavPreference?: NavPreferenceSnapshot | null
  /** 服务端读到的 SWAPMID_RELEASE 原文。未传时与本地开发一样显示 dev。 */
  releaseTag?: string
  variant?: SidebarVariant
  /** 抽屉形态专用：点导航项 / 品牌行关闭钮时通知宿主收起抽屉。 */
  onRequestClose?: () => void
  brand?: SidebarBrand
  remoteChrome?: boolean
}

/** GET /nav-preference 的返回。新字段可缺省：滚动发布期间旧后端不返回它们。 */
export interface NavPreferenceSnapshot {
  value?: string | null
  hasPersonalOrder?: boolean
  domainPrefs?: string | null
  isAdmin?: boolean
}

// 折叠状态存 cookie：服务端在 layout 里读取并作为首屏初始值，避免刷新时出现"展开→收起"的闪动。
const COLLAPSE_COOKIE = 'swapmid_sidebar_collapsed'

// 保存前瘦身的比对基准：nav-config 的内置顺序
// 次级归属组（如 HR 域里的「报表中心」副本）也能拖动排序，key 要在基准里，否则保存时被当成已下线丢掉
const ORDERABLE_GROUPS = [...navGroups, ...allAlsoInGroups()]
const NAV_ORDER_DEFAULTS = {
  groupKeys: ORDERABLE_GROUPS.map((group) => group.key),
  itemHrefsByGroup: Object.fromEntries(ORDERABLE_GROUPS.map((group) => [group.key, group.items.map((item) => item.href)])),
}

export function Sidebar({
  grantedPolicies,
  initialCollapsed = false,
  initialNavPreference = null,
  releaseTag = '',
  variant = 'inline',
  onRequestClose,
  brand = SWAPMID_SIDEBAR_BRAND,
  remoteChrome = true,
}: SidebarProps) {
  const pathname = usePathname()
  const t = useTranslations('Nav') as NavT
  const ts = useTranslations('Sidebar') as NavT
  const isDrawer = variant === 'drawer'
  // 乐观高亮：点击导航的瞬间就把"当前页"切到目标项（不等路由 commit / 数据返回），
  // 让切换看起来"已经完成"，而不是用转圈去暗示"正在等待"。路由真正 commit 后清除。
  const [navTarget, setNavTarget] = useState<{ href: string; fromPath: string } | null>(null)
  // 必须显式清除而不能只靠 fromPath===pathname 判断失配：滞留的旧 navTarget 会在"经页签栏
  // 切回它的出发页"时复活（fromPath 恰好再次等于 pathname），高亮指向早已离开的旧目标。
  if (navTarget && navTarget.fromPath !== pathname) {
    setNavTarget(null)
  }
  const activePath = navTarget?.fromPath === pathname ? navTarget.href : pathname

  // 顺序持久化在服务端（每账号独立；全员默认由 admin 显式发布），由 layout 服务端预取注入——首帧即最终顺序。
  const [groupOrder, setGroupOrder] = useState<string[] | null>(() => parseNavOrder(initialNavPreference?.value ?? null).groupOrder)
  const [itemOrders, setItemOrders] = useState<Record<string, string[]>>(() => parseNavOrder(initialNavPreference?.value ?? null).itemOrders)
  // 是否存了个人顺序（决定底栏显不显示「重置」）。不能用「顺序非空」推断：没有个人顺序时拿到的是全员默认，
  // 那时点重置什么也不会变。旧后端不返回该字段时退回旧推断。
  const [hasPersonalOrder, setHasPersonalOrder] = useState(() => initialNavPreference?.hasPersonalOrder
    ?? (groupOrder !== null || Object.keys(itemOrders).length > 0))
  const isAdmin = !!initialNavPreference?.isAdmin
  const [collapsedState, setCollapsed] = useState(initialCollapsed)
  // 抽屉恒为展开态：cookie 记的是桌面左栏的折叠偏好，与抽屉无关，两边互不污染。
  const collapsed = !isDrawer && collapsedState
  const release = getReleaseDisplay(releaseTag)

  // 点导航项：桌面只做乐观高亮；抽屉还要顺手收起自己（用户目标是「进那一页」，面板留着只会挡住内容）。
  function handleNavigate(href: string) {
    setNavTarget({ href, fromPath: pathname })
    if (isDrawer) onRequestClose?.()
  }

  function toggleCollapsed() {
    setCollapsed((prev) => {
      const next = !prev
      // 写 cookie，下次刷新服务端 layout 直接按正确宽度渲染（无闪动）。
      document.cookie = `${COLLAPSE_COOKIE}=${next ? '1' : '0'}; path=/; max-age=31536000; samesite=lax`
      return next
    })
  }

  // 拖拽过程中只更新本地 state（驱动实时让位），onDragEnd 才落库一次。
  const orderRef = useRef({ groupOrder, itemOrders })
  useEffect(() => { orderRef.current = { groupOrder, itemOrders } })
  // 只存与默认顺序不同的部分；仍超后端 2048 上限时返回 null 并提示
  function compactedOrderValue(failTitle: string): string | null {
    const value = serializeCompactNavOrder(orderRef.current, NAV_ORDER_DEFAULTS)
    if (value.length <= NAV_ORDER_MAX_LENGTH) return value
    toast({ variant: 'destructive', title: failTitle, description: ts('orderTooLong') })
    return null
  }

  function postOrder(path: string, value: string) {
    return fetch(`/api/proxy/nav-preference${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ value }),
    }).then((res) => { if (!res.ok) throw new Error(String(res.status)) })
  }

  function persistNow() {
    if (!remoteChrome) return
    const value = compactedOrderValue(ts('orderSaveFailed'))
    if (value === null) return
    // 拖回与内置一致时存的是空串，后端视同没有个人顺序
    setHasPersonalOrder(value !== '')
    void postOrder('', value)
      .catch(() => toast({ variant: 'destructive', title: ts('orderSaveFailed'), description: ts('orderSaveRetry') }))
  }

  function publishDefault() {
    if (!remoteChrome) return
    const value = compactedOrderValue(ts('publishDefaultFailed'))
    if (value === null) return
    void postOrder('', value)
      .then(() => toast({ title: ts('publishDefaultDone') }))
      .catch(() => toast({ variant: 'destructive', title: ts('publishDefaultFailed'), description: ts('orderSaveRetry') }))
  }

  // ===== 业务域门户化：侧栏只显示「当前业务域」的组（resolveDomains 与工作台共用，可见性一致）=====
  const domains = resolveDomains(grantedPolicies, { includeAlsoIn: true })
  // 仪表盘（/dashboard）无所属业务域 → 归入「工作台态」：菜单区平铺全部可见域，不再借显 domains[0]。
  const isDashboardPath = activePath === DASHBOARD_PATH
  const pathDomainKey = isDashboardPath
    ? WORKBENCH_DOMAIN.key
    : findNavDomainKeyByPath(activePath)
  const [pickedDomainKey, setPickedDomainKey] = useState<string | null>(null)
  // 路由进入新域时清除手动选择，让侧栏跟随当前页所属域
  const [lastPathDomainKey, setLastPathDomainKey] = useState(pathDomainKey)
  if (lastPathDomainKey !== pathDomainKey) {
    setLastPathDomainKey(pathDomainKey)
    const previousDomainKey = pickedDomainKey ?? lastPathDomainKey
    const homedHere = !!previousDomainKey && !!findNavItemByPath(activePath)?.alsoIn?.includes(previousDomainKey)
    setPickedDomainKey(homedHere ? previousDomainKey : null)
  }
  const domainNav = useDomainNav({
    domains,
    grantedPolicies,
    initialDomainPrefs: initialNavPreference?.domainPrefs,
    activeDomainKey: pickedDomainKey ?? pathDomainKey ?? '',
    onPick: setPickedDomainKey,
    shortcuts: !isDrawer,
    persistRemote: false,
    ts,
  })
  // 没有归属域的页面（个人中心等）落到个人顺序的第一个域
  const activeDomainKey = pickedDomainKey ?? pathDomainKey ?? domainNav.shownDomains[0]?.domain.key ?? domains[0]?.domain.key ?? ''
  // 工作台态：不绑定单一域，菜单区平铺全部可见域
  const isWorkbench = activeDomainKey === WORKBENCH_DOMAIN.key
  const activeResolved = isWorkbench ? undefined : (domains.find((d) => d.domain.key === activeDomainKey) ?? domains[0])
  const activeDomain = isWorkbench ? WORKBENCH_DOMAIN : activeResolved?.domain

  // 仅对「当前域内、已按权限过滤」的组应用用户自定义顺序
  const orderedGroups = applyOrder(activeResolved?.groups ?? [], groupOrder, (group) => group.key)

  const activeGroupKey = findNavGroupByPath(activePath, orderedGroups)?.key
  const [openGroupKey, setOpenGroupKey] = useState<string | null>(null)
  // 路由变化后清除手动展开/收起，回归跟随当前页所在分组
  const [lastActivePath, setLastActivePath] = useState(activePath)
  if (lastActivePath !== activePath) {
    setLastActivePath(activePath)
    setOpenGroupKey(null)
  }
  const effectiveOpenGroupKey = openGroupKey ?? activeGroupKey ?? null

  function handleReorderGroups(nextKeys: string[]) {
    setGroupOrder((prev) => mergeScopedOrder(prev, nextKeys))
  }

  function handleReorderItems(groupKey: string, nextHrefs: string[]) {
    setItemOrders((prev) => ({ ...prev, [groupKey]: nextHrefs }))
  }

  // 恢复范围：单域视图只恢复本域的组与项，工作台全景恢复全部
  const resetScopeKeys = isWorkbench ? null : (activeResolved?.groups ?? []).map((group) => group.key)
  const scopeCustomized = hasPersonalOrder && (resetScopeKeys === null
    || isNavOrderCustomizedIn({ groupOrder, itemOrders }, resetScopeKeys, NAV_ORDER_DEFAULTS))

  function applyOrderState(next: NavOrderState) {
    setGroupOrder(next.groupOrder)
    setItemOrders(next.itemOrders)
    orderRef.current = next
  }

  function resetOrder() {
    const previous = orderRef.current
    const previousPersonal = hasPersonalOrder
    if (resetScopeKeys === null) {
      applyOrderState({ groupOrder: null, itemOrders: {} })
      setHasPersonalOrder(false)
      if (remoteChrome) void fetch('/api/proxy/nav-preference/reset', { method: 'POST' }).catch(() => {})
    } else {
      applyOrderState(resetNavOrderIn(previous, resetScopeKeys))
      persistNow()
    }
    toast({
      title: resetScopeKeys === null
        ? ts('resetAllOrderDone')
        : ts('resetDomainOrderDone', { domain: activeDomain ? translateNavTitle(activeDomain, t) : '' }),
      action: (
        <ToastAction
          altText={ts('undo')}
          onClick={() => {
            applyOrderState(previous)
            if (previousPersonal) persistNow()
          }}
        >
          {ts('undo')}
        </ToastAction>
      ),
    })
  }

  // 拖拽重排只在桌面常驻栏的单域视图开放；抽屉一律走非 Reorder 分支。
  const reorderable = !isDrawer
  const groupsRef = useRef<HTMLDivElement>(null)
  const [pointerPressed, pressPointer] = usePointerPressed()

  return (
    <aside
      data-variant={variant}
      className={cn(
        'flex h-full shrink-0 flex-col overflow-hidden bg-sidebar select-none',
        isDrawer
          ? 'w-full'
          : cn('hidden rounded-xl border shadow-md transition-[width] duration-200 md:flex', collapsed ? 'w-16' : 'w-56'),
      )}
    >
      <div className={cn('flex h-14 items-center border-b', collapsed ? 'justify-center px-0' : 'gap-2.5 px-4')}>
        <div className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-white shadow-sm ring-1 ring-black/5">
          {/* unoptimized：小图直出原文件，不依赖 /_next/image（sharp 缺失时该端点 500 裂图）
              i18n 例外登记（批次4k扫描仪表已排查，勿删）："SwapMid"/"SwapMid" 是品牌名，
              与登录页 <h1>/根 layout.tsx 的 <title> 元数据/dashboard 欢迎语字面完全一致，
              全站从未翻译过（专有名词，见 docs/i18n-blueprint.md 批次4a"SwapMid"例外同一口径）。 */}
          <Image src={brand.src} alt={brand.alt} width={26} height={26} className="size-6.5" unoptimized />
        </div>
        {!collapsed && (
          <div className="min-w-0 flex-1">
            <p className="truncate text-[13px] font-semibold leading-none tracking-tight">{brand.title}</p>
            <p className="text-muted-foreground text-[11px] mt-0.5">{brand.subtitle}</p>
          </div>
        )}
        {isDrawer && (
          <IconButton
            label={ts('closeNav')}
            showTooltip={false}
            onClick={onRequestClose}
            className="-mr-2 size-8 shrink-0 text-muted-foreground/70 hover:bg-sidebar-accent/40 hover:text-foreground"
          >
            <X className="size-4" />
          </IconButton>
        )}
      </div>

      {activeDomain && (
        <SidebarDomainPicker
          domains={domains}
          shownDomains={domainNav.shownDomains}
          hiddenDomains={domainNav.hiddenDomains}
          recentDomainKeys={domainNav.recentDomainKeys}
          prefs={domainNav.prefs}
          active={activeDomain}
          collapsed={collapsed}
          isDrawer={isDrawer}
          orderState={{ groupOrder, itemOrders }}
          onPickDomain={domainNav.goToDomain}
          onSaveDomainPrefs={domainNav.saveDomainPrefs}
        />
      )}

      <nav className={cn(
        'scrollbar-fade min-h-0 flex-1 overflow-y-auto overscroll-none py-3 [overflow-anchor:none]',
        collapsed ? 'space-y-1 px-2' : 'px-2.5'
      )}>
        {isWorkbench ? (
          domainNav.shownDomains.map(({ domain, groups: allGroups }) => {
            const groups = applyNavOrder(allGroups.filter((group) => !group.alsoOf), { groupOrder, itemOrders })
            const DomainIcon = domain.icon
            return (
              <div key={domain.key} className="mb-1.5">
                <button
                  type="button"
                  onClick={() => setPickedDomainKey(domain.key)}
                  title={`${ts('enterDomain')}${translateNavTitle(domain, t)}`}
                  className={cn(
                    'flex w-full items-center gap-2 rounded-md px-2.5 pt-2 pb-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground/55 transition-colors hover:text-foreground',
                    collapsed && 'justify-center'
                  )}
                >
                  <DomainIcon className="size-3.5 shrink-0" />
                  {!collapsed && <span className="flex-1 text-left">{translateNavTitle(domain, t)}</span>}
                </button>
                {collapsed
                  ? groups.map((group) => (
                      <CollapsedNavGroup
                        key={group.key}
                        group={group}
                        policies={grantedPolicies}
                        pathname={activePath}
                        itemOrder={itemOrders[group.key] ?? null}
                        onNavigate={handleNavigate}
                      />
                    ))
                  : groups.map((group) => (
                      <NavGroupItem
                        key={group.key}
                        group={group}
                        policies={grantedPolicies}
                        pathname={activePath}
                        open={effectiveOpenGroupKey === group.key}
                        itemOrder={itemOrders[group.key] ?? null}
                        reorderable={false}
                        onToggle={() => setOpenGroupKey(effectiveOpenGroupKey === group.key ? '' : group.key)}
                        onNavigate={handleNavigate}
                        onReorderItems={handleReorderItems}
                        onPersist={persistNow}
                      />
                    ))}
              </div>
            )
          })
        ) : collapsed ? (
          orderedGroups.map((group) => (
            <CollapsedNavGroup
              key={group.key}
              group={group}
              policies={grantedPolicies}
              pathname={activePath}
              itemOrder={itemOrders[group.key] ?? null}
              onNavigate={handleNavigate}
            />
          ))
        ) : reorderable ? (
          <MotionConfig reducedMotion="user">
            <Reorder.Group
              ref={groupsRef}
              as="div"
              axis="y"
              values={orderedGroups.map((group) => group.key)}
              onReorder={handleReorderGroups}
              onPointerDownCapture={pressPointer}
            >
              {orderedGroups.map((group) => (
                <NavGroupItem
                  key={group.key}
                  group={group}
                  policies={grantedPolicies}
                  pathname={activePath}
                  open={effectiveOpenGroupKey === group.key}
                  itemOrder={itemOrders[group.key] ?? null}
                  reorderable
                  constraintsRef={groupsRef}
                  dragBounded={pointerPressed}
                  onToggle={() => setOpenGroupKey(effectiveOpenGroupKey === group.key ? '' : group.key)}
                  onNavigate={handleNavigate}
                  onReorderItems={handleReorderItems}
                  onPersist={persistNow}
                />
              ))}
            </Reorder.Group>
          </MotionConfig>
        ) : (
          orderedGroups.map((group) => (
            <NavGroupItem
              key={group.key}
              group={group}
              policies={grantedPolicies}
              pathname={activePath}
              open={effectiveOpenGroupKey === group.key}
              itemOrder={itemOrders[group.key] ?? null}
              reorderable={false}
              onToggle={() => setOpenGroupKey(effectiveOpenGroupKey === group.key ? '' : group.key)}
              onNavigate={handleNavigate}
              onReorderItems={handleReorderItems}
              onPersist={persistNow}
            />
          ))
        )}
      </nav>

      <SidebarFooter
        collapsed={collapsed}
        isCustomized={scopeCustomized}
        resetLabel={isWorkbench ? ts('resetAllOrder') : ts('resetDomainOrder')}
        release={release}
        showCollapseToggle={!isDrawer}
        onToggleCollapsed={toggleCollapsed}
        onResetOrder={resetOrder}
        onPublishDefault={isAdmin && !isDrawer ? publishDefault : undefined}
      />
    </aside>
  )
}
