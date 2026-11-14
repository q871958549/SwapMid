'use client'

import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { useRouter } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { AnimatePresence, MotionConfig, Reorder } from 'framer-motion'
import { ChevronLeft, ChevronRight, Home, X } from 'lucide-react'
import { cn } from '@/lib/utils'
import { canAccessPath, resolveTabTitle, type NavT } from '@/lib/nav-utils'
import { useTabStore, resolveStoredPagePath, HOME_KEY, type Tab } from '@/stores/use-tab-store'
import { TabListMenu } from '@/components/layout/tab-list-menu'
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from '@/components/ui/context-menu'

interface TabBarProps {
  grantedPolicies: Record<string, boolean>
}

// useSyncExternalStore 的 server 快照：SSR/hydration 首帧恒按未水合渲染，与客户端首帧一致
const getServerHydratedSnapshot = () => false

export function TabBar({ grantedPolicies }: TabBarProps) {
  const router = useRouter()
  const t = useTranslations('Nav') as NavT
  const { tabs, activeKey, closeTab, closeOthers, closeLeft, closeRight, closeAll, setActive, setTabsOrder, purgeUnauthorizedTabs } =
    useTabStore()

  const scrollRef = useRef<HTMLDivElement>(null)

  // 当前正在被拖拽的页签 key：驱动"非激活页签拖起时补底色+边框"（透明页签拖起来不能只有一行字漂移）。
  const [draggingKey, setDraggingKey] = useState<string | null>(null)

  // 拖拽释放的瞬间，浏览器仍会在同一元素上补派发一次原生 click，用 ref 而非 state 记录以避免
  // 多余重渲染；在 handleTabClick 里挡掉误触导航。复位延后到下一 tick（见 handleDragEnd），
  // 否则会在 click 事件抵达前就被清空，形同虚设。
  const suppressClickRef = useRef(false)

  // 页签 store 是 skipHydration + RouteWatcher 挂载后手动 rehydrate，恢复的页签相对组件首次
  // 挂载属于"后插入"，AnimatePresence 的 initial={false} 管不到。hydrated 记录水合完成时刻：
  // 水合完成前渲染的页签一律禁进场动画（见下方 enterInitial），水合完成后新开的页签正常播放；
  // 已挂载页签的 initial 只在挂载那一刻生效，水合稍后完成不会倒过来补播动画。
  // 必须用 useSyncExternalStore 而非"effect 里订阅 onFinishHydration"：rehydrate 由 RouteWatcher
  // 的 effect 触发且同步完成（sessionStorage），其声明顺序在本组件之前——等到本组件 effect 建立
  // 订阅时水合早已结束、回调永不再发，hydrated 会卡死 false。useSyncExternalStore 在订阅建立时
  // 自动比对快照与渲染值，订阅前已完成的水合同样能驱动重渲染，两种时序都闭环。
  const hydrated = useSyncExternalStore(
    useTabStore.persist.onFinishHydration,
    useTabStore.persist.hasHydrated,
    getServerHydratedSnapshot,
  )

  // 溢出状态：left/right 表示对应方向还有被裁掉的页签（驱动渐变遮罩与箭头）。
  const [scrollState, setScrollState] = useState({ left: false, right: false })
  const isOverflowing = scrollState.left || scrollState.right

  const updateScrollState = useCallback(() => {
    const el = scrollRef.current
    if (!el) return
    const left = el.scrollLeft > 1
    const right = el.scrollLeft + el.clientWidth < el.scrollWidth - 1
    setScrollState(prev => (prev.left === left && prev.right === right ? prev : { left, right }))
  }, [])

  const scrollActiveTabIntoView = useCallback(() => {
    const activeTab = scrollRef.current?.querySelector<HTMLElement>('[aria-selected="true"]')
    activeTab?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
  }, [])

  useEffect(() => {
    const el = scrollRef.current
    if (!el) return
    updateScrollState()
    el.addEventListener('scroll', updateScrollState, { passive: true })
    // 容器尺寸变化（箭头插拔挤压滚动区 / 窗口缩放）会让「已滚到最右」失效、激活页签被顶出视口，
    // 故 resize 时重新滚入再重算。scrollIntoView 已可见时无操作，不会与用户滚动抖动。
    const observer = new ResizeObserver(() => {
      scrollActiveTabIntoView()
      updateScrollState()
    })
    observer.observe(el)

    // 滚轮纵转横：页签栏不在纵向滚动区内，deltaY 直接转横滚。触摸板横扫（deltaX 为主）走原生。
    // React 合成 wheel 是 passive 的无法 preventDefault，须原生 non-passive 监听。
    function handleWheel(e: WheelEvent) {
      if (!el || el.scrollWidth <= el.clientWidth) return
      if (Math.abs(e.deltaX) >= Math.abs(e.deltaY)) return
      e.preventDefault()
      el.scrollLeft += e.deltaMode === 1 ? e.deltaY * 24 : e.deltaY
    }
    el.addEventListener('wheel', handleWheel, { passive: false })

    return () => {
      el.removeEventListener('scroll', updateScrollState)
      el.removeEventListener('wheel', handleWheel)
      observer.disconnect()
    }
  }, [updateScrollState, scrollActiveTabIntoView])

  // 页签增删后 scrollWidth 变化不触发 scroll/resize 事件，需主动重算。
  useEffect(() => { updateScrollState() }, [tabs, updateScrollState])

  // 激活页签自动滚入视野：当前页的页签必须永远可见（页签 append 在队尾而视口停在左端时，
  // 用户会以为页签没开出来）。tab 元素带 scroll-mx-8，滚入时避开两端渐变遮罩。
  // 只跟随 activeKey：关闭/拖拽非激活页签时不抢滚动位置（用户可能正在连续清理）。
  useEffect(() => {
    scrollActiveTabIntoView()
    // instant 滚动同步改 scrollLeft，直接重算溢出状态——scroll 事件在后台页签会被渲染节流延迟
    updateScrollState()

    // 🔴 新开页签必须补滚一次：新页签进场动画是 width 0 → auto（150ms，见下方 Reorder.Item），
    // 本 effect 在 DOM 提交后立即执行，此刻它的宽度还是 0，scrollIntoView 按零宽盒子定位；
    // 随后页签展开约 100px 便溢出视口右缘，而 ResizeObserver 观察的是滚动容器自身、
    // 页签展开不改变容器尺寸 → 不触发二次滚动。表现为「新页签只露出一小条，要手动拨」。
    // 动画结束后补滚一次即可，节点已被移除时 scrollActiveTabIntoView 内部自会短路。
    const timer = setTimeout(() => {
      scrollActiveTabIntoView()
      updateScrollState()
    }, 180)
    return () => clearTimeout(timer)
  }, [activeKey, scrollActiveTabIntoView, updateScrollState])

  function scrollByPage(direction: 1 | -1) {
    const el = scrollRef.current
    if (!el) return
    el.scrollBy({ left: direction * Math.max(el.clientWidth * 0.6, 120), behavior: 'smooth' })
  }

  // 依赖含 tabs：sessionStorage rehydrate 恢复的页签也要过权限清洗
  // （换账号后旧会话残留的无权页签）。purge 无变化时不 set，不会循环。
  useEffect(() => {
    const nextActiveKey = purgeUnauthorizedTabs(tab => canAccessPath(tab.path, grantedPolicies))
    if (nextActiveKey !== activeKey) {
      router.replace(resolveStoredPagePath(nextActiveKey))
    }
  }, [tabs, activeKey, grantedPolicies, purgeUnauthorizedTabs, router])

  // close* 系列返回的是落点页签的 key（裸 pathname）——统一解析成存量 path（含筛选/分页 query）
  // 再导航，避免"关掉一个页签，落点页面的列表状态被裸路径冲掉"。带 query 的 path 原样直达。
  function navigate(path: string) { router.push(resolveStoredPagePath(path)) }
  function handleTabClick(tab: Tab) {
    if (suppressClickRef.current) return // 拖拽刚释放，同一元素补派发的 click，见 suppressClickRef 注释
    if (!canAccessPath(tab.path, grantedPolicies)) {
      navigate(closeTab(tab.key))
      return
    }

    setActive(tab.key)
    navigate(tab.path)
  }
  function handleClose(e: React.MouseEvent, key: string) {
    e.stopPropagation()
    navigate(closeTab(key))
  }
  function handleCloseCtx(key: string) { navigate(closeTab(key)) }
  function handleCloseOthers(key: string) { closeOthers(key); navigate(key) }
  function handleCloseLeft(key: string) { navigate(closeLeft(key)) }
  function handleCloseRight(key: string) { navigate(closeRight(key)) }
  function handleCloseAll() { closeAll(); navigate(HOME_KEY) }
  function handleDragStart(key: string) {
    suppressClickRef.current = true
    setDraggingKey(key)
  }
  function handleDragEnd() {
    setDraggingKey(null)
    setTimeout(() => { suppressClickRef.current = false }, 0)
  }
  // 中键关闭：浏览器肌肉记忆（Chrome 原生页签行为），Home 常驻不可关闭
  function handleAuxClick(e: React.MouseEvent, key: string) {
    if (e.button !== 1 || key === HOME_KEY) return
    navigate(closeTab(key))
  }

  return (
    <div className="hidden h-10 shrink-0 bg-muted md:flex">
      {/* 窄屏整条隐藏：标题与 PageHeader 重复，页签菜单改挂 Header（ConnectedTabListMenu）。
          桌面仍是可拖拽页签带。用 CSS 断点互斥，服务端不知视口宽度、不抖首帧。 */}
      <div className="flex h-full min-w-0 flex-1">
        {isOverflowing && (
          <button
            type="button"
            aria-label={t('tabBar.scrollLeftAriaLabel')}
            disabled={!scrollState.left}
            onClick={() => scrollByPage(-1)}
            className="flex w-7 shrink-0 items-center justify-center border-b text-muted-foreground transition-colors hover:text-foreground disabled:pointer-events-none disabled:opacity-30"
          >
            <ChevronLeft className="size-4" />
          </button>
        )}

        <div className="relative min-w-0 flex-1">
          {/* Scroll region: items-end so tabs sit on the bottom baseline.
              基线由下面 className 里的 inset shadow 画出——**不是** border-b，换掉的理由见那段长注释；
              激活页签用 -mb-px 下探盖住它，视觉上"开口向下"连进内容区。
              relative：AnimatePresence mode="popLayout" 的退场页签以此为绝对定位基准 */}
          <MotionConfig reducedMotion="user">
            <Reorder.Group
              as="div"
              axis="x"
              values={tabs}
              onReorder={setTabsOrder}
              ref={scrollRef}
              role="tablist"
              // px-2.5(10px) 而非 px-2：首/尾页签激活时凹角向外伸出一个 --radius(10px)，
              // padding 小于它就会被 overflow-x-auto 裁掉一截，表现为"边上那个页签的凹角缺一块"。
              //
              // 🔴 底部基线**不能用 `border-b`**，必须是 `pb-px` + inset shadow（2026-08-07 修「页签与
              // 内容区割裂」）。机制链：`overflow-x-auto` 会让 `overflow-y` 的**计算值**联动成 auto；
              // 于是激活页签 `-mb-px` 下探的那 1px 落进 border 区（在 padding box **之外**，计入
              // scrollable overflow），把 scrollHeight 撑得比 clientHeight 大 1px；而本组件的
              // `scrollActiveTabIntoView` 用 `block:'nearest'`（切页签 / resize / 新页签补滚都会调），
              // 它会顺手把这 1px **垂直**滚掉并粘住（实测 scrollTop=1）——下探被精确抵消、border-b 重新
              // 露出来，就是用户看到的那条把激活页签和内容区隔开的灰缝。
              // 改成 `pb-px` 后，下探停在 padding box **内**（不计入 scrollable overflow），垂直可滚性
              // 连根拔除、scrollIntoView 无事可做；inset shadow 把基线画在同位同色，双主题观感不变，
              // 且照样被激活页签盖住。**别改回 border-b**，那条缝会立刻回来。
              className="relative flex h-full items-end gap-0.5 overflow-x-auto px-2.5 pb-px pt-1.5 shadow-[inset_0_-1px_0_var(--border)] [&::-webkit-scrollbar]:hidden"
              style={{ scrollbarWidth: 'none' }}
            >
              <AnimatePresence initial={false} mode="popLayout">
                {tabs.map((tab, index) => {
                  const isActive = tab.key === activeKey
                  const isHome = tab.key === HOME_KEY
                  const isDragging = draggingKey === tab.key
                  // 分隔线：首个不画；激活页签自身及其右邻不画（紧贴激活页签边框，画了显多余）；
                  // 拖拽中的页签也不画——它此刻切到「激活页签同款形态」（补底色+描边+凹角），
                  // 分隔线既多余又要占用 before 伪元素（凹角要用），两个理由都指向隐藏。
                  const showSeparator = !isActive && !isDragging && index > 0 && tabs[index - 1]?.key !== activeKey
                  // 水合完成前挂载的页签禁用进场动画，见上方 hydrated 注释
                  const enterInitial = hydrated ? { opacity: 0, width: 0 } : false

                  return (
                    <ContextMenu key={tab.key}>
                      <ContextMenuTrigger asChild>
                        <Reorder.Item
                          as="div"
                          value={tab}
                          role="tab"
                          aria-selected={isActive}
                          dragListener={!isHome}
                          initial={enterInitial}
                          animate={{ opacity: 1, width: 'auto' }}
                          exit={{ opacity: 0, width: 0 }}
                          transition={{
                            opacity: { duration: 0.15, ease: 'easeOut' },
                            width: { duration: 0.15, ease: 'easeOut' },
                          }}
                          whileDrag={{
                            // 提起感：scale/shadow/cursor 之外，zIndex 须盖过两端渐变遮罩（z-10，见下方）与相邻页签
                            scale: 1.04,
                            zIndex: 30,
                            boxShadow: 'var(--shadow-lg)',
                            cursor: 'grabbing',
                          }}
                          onDragStart={() => handleDragStart(tab.key)}
                          onDragEnd={handleDragEnd}
                          onClick={() => handleTabClick(tab)}
                          onAuxClick={(e) => handleAuxClick(e, tab.key)}
                          className={cn(
                            // Shape: rounded top corners, browser-tab style
                            // ⚠️ 本体刻意**没有** overflow-hidden：Chrome 式底部外翻凹角（.tab-corner）是画在
                            // 盒外的伪元素，本体一裁就没了。进退场 width 0→auto 的内容裁切职责已下放给内层
                            // 内容 wrapper（见下），两边各司其职。
                            // 🔴 `border border-b-0 border-transparent -mb-px` 两态**恒定占位**，别挪进
                            // isActive 分支（2026-08-07 修「切换时短暂割裂」）。原先边框只有激活态才有，
                            // 于是每次切换页签盒宽都会 ±2px、盒高 ±1px——而本组件是 Reorder.Item，
                            // framer-motion 的 layout 动画会检测到这个布局差并用 FLIP 去补偿：实测切换瞬间
                            // 失活页签被写入 `scale(1.0833)`、新激活方 `scale(0.9231)`（恰为两者宽度比），
                            // **动画那 ~300ms 里页签上的文字是被拉伸形变的**。叠加「边框 0ms 瞬现 + 底色
                            // 100ms 渐变 + 位置 300ms spring」三条各跑各的时间轴，就是用户说的「割裂」。
                            // 恒定占位后布局零变化 → FLIP 无差可补 → 形变与 spring 尾巴一并消失，边框改由
                            // `border-color: transparent → var(--border)` 表达，正好落进 transition-colors
                            // 的覆盖面（零新增过渡配置）。整个切换收敛成**单一颜色时间轴**。
                            // duration-150 而非 100：与进出场/侧栏同档，切换更有呼吸感（见动效语汇三档）。
                            'group relative flex h-8 shrink-0 cursor-pointer select-none items-center rounded-t-lg border border-b-0 border-transparent -mb-px px-3 text-[13px] transition-colors duration-150 scroll-mx-8',
                            // 非激活页签左缘分隔线：hover 自身或其左邻时淡出——这条线分别紧贴两者 hover
                            // 底色的左/右边缘，不淡会在圆角底色上切出一道竖线（左邻 hover 用前兄弟变体
                            // `:hover + &`；右邻被 hover 与本线隔着整个页签宽度，无需联动）。
                            // `-left-px` 而非 `left-0`：透明边框把 padding box 内缩了 1px，补偿回去才能与
                            // 改动前逐像素一致。
                            showSeparator && [
                              "before:absolute before:inset-y-2 before:-left-px before:w-px before:bg-border before:content-['']",
                              'hover:before:opacity-0',
                              "[[role='tab']:hover+&]:before:opacity-0",
                            ],
                            isActive
                              // Active: 只改颜色——底色、文字色、边框色（占位边框由 transparent 变实色）。
                              // -mb-px 下探盖住页签栏底部基线现在是**全员**行为（那条基线是 tablist 的
                              // inset shadow，不再是 border-b，见上方长注释）；tab-corner 补底部外翻凹角。
                              // ⚠️ 这里**刻意不加 `z-[1]`**：Reorder.Item 会把 `z-index: unset` 写进内联
                              // style，className 的 z-* 恒被盖（记忆 framer-motion-reorder-pitfalls 坑 1，
                              // 2026-08-06 真机复验：inline style 实测为 `z-index: unset`），加了也是死类。
                              // 后果仅是「hover 激活页签的邻居时，其半透明底色会叠在外伸 8~10px 凹角上」——
                              // 而两者都基于 --background，实际几乎不可辨，不值得为它绕开 motion 的 z 管理。
                              ? 'bg-background text-foreground font-medium border-border tab-corner'
                              // Inactive: muted, subtle hover；拖起时补底色+边框色+凹角，切成与激活页签同款
                              // 形态，透明页签拖起来不能只剩一行字漂移
                              : [
                                  'text-muted-foreground hover:bg-background/60 hover:text-foreground',
                                  isDragging && 'bg-background border-border tab-corner',
                                ],
                          )}
                        >
                          {/* 内容 wrapper：承接被移出本体的裁切职责。min-w-0 让它能被 width 动画压缩到 0，
                              overflow-hidden 裁掉塌缩过程中溢出的文字/图标。 */}
                          <span className="flex min-w-0 items-center gap-1.5 overflow-hidden">
                            {isHome && (
                              <Home className={cn('size-3.5 shrink-0', isActive ? 'text-primary' : '')} />
                            )}
                            <span className="max-w-28 truncate">{resolveTabTitle(tab, t)}</span>

                            {!isHome && (
                              <button
                                tabIndex={-1}
                                onClick={(e) => handleClose(e, tab.key)}
                                className={cn(
                                  'ml-0.5 flex size-4 shrink-0 items-center justify-center rounded transition-all',
                                  isActive
                                    ? 'opacity-50 hover:bg-muted hover:opacity-100'
                                    : 'opacity-0 group-hover:opacity-50 hover:bg-muted hover:!opacity-100'
                                )}
                              >
                                <X className="size-3" />
                              </button>
                            )}
                          </span>
                        </Reorder.Item>
                      </ContextMenuTrigger>

                      <ContextMenuContent className="w-40">
                        <ContextMenuItem onClick={() => handleCloseCtx(tab.key)} disabled={isHome}>
                          {t('tabBar.close')}
                        </ContextMenuItem>
                        <ContextMenuItem
                          onClick={() => handleCloseOthers(tab.key)}
                          disabled={tabs.filter((o) => o.key !== tab.key).length === 0}
                        >
                          {t('tabBar.closeOthers')}
                        </ContextMenuItem>
                        <ContextMenuSeparator />
                        <ContextMenuItem onClick={() => handleCloseLeft(tab.key)} disabled={index <= 1}>
                          {t('tabBar.closeLeft')}
                        </ContextMenuItem>
                        <ContextMenuItem
                          onClick={() => handleCloseRight(tab.key)}
                          disabled={index >= tabs.length - 1}
                        >
                          {t('tabBar.closeRight')}
                        </ContextMenuItem>
                        <ContextMenuSeparator />
                        <ContextMenuItem variant="destructive" onClick={handleCloseAll}>
                          {t('tabBar.closeAll')}
                        </ContextMenuItem>
                      </ContextMenuContent>
                    </ContextMenu>
                  )
                })}
              </AnimatePresence>

              {/* Spacer — fills the remaining tab bar area */}
              <div className="flex-1" />
            </Reorder.Group>
          </MotionConfig>

          {/* 溢出渐变遮罩：提示该方向还有被裁掉的页签。bottom-px 让底部基线露出不被盖白 */}
          {scrollState.left && (
            <div
              aria-hidden
              className="pointer-events-none absolute bottom-px left-0 top-0 z-10 w-8 bg-gradient-to-r from-muted to-transparent"
            />
          )}
          {scrollState.right && (
            <div
              aria-hidden
              className="pointer-events-none absolute bottom-px right-0 top-0 z-10 w-8 bg-gradient-to-l from-muted to-transparent"
            />
          )}
        </div>

        {isOverflowing && (
          <button
            type="button"
            aria-label={t('tabBar.scrollRightAriaLabel')}
            disabled={!scrollState.right}
            onClick={() => scrollByPage(1)}
            className="flex w-7 shrink-0 items-center justify-center border-b text-muted-foreground transition-colors hover:text-foreground disabled:pointer-events-none disabled:opacity-30"
          >
            <ChevronRight className="size-4" />
          </button>
        )}
      </div>

      <div className="flex shrink-0 items-center border-b pl-0.5 pr-1.5">
        <TabListMenu
          tabs={tabs}
          activeKey={activeKey}
          onSelect={handleTabClick}
          onClose={(key) => navigate(closeTab(key))}
          onCloseOthers={() => handleCloseOthers(activeKey)}
          onCloseAll={handleCloseAll}
        />
      </div>
    </div>
  )
}
