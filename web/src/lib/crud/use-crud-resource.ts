'use client'

import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { useRouter } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { type SortingState } from '@tanstack/react-table'
import { toast } from '@/components/ui/use-toast'
import { absorbPlatformUpdating } from '@/lib/platform-updating'
import { useOptionalSessionIdentity } from '@/components/providers/session-identity-provider'
import { type PagedResult } from '@/lib/api-client'
import { bindSessionIdentityToUrl } from '@/lib/session-availability'
import { patchListUrl } from './list-url'
import { serializeSortParam, toAbpSorting } from './list-sort'
import { INTERACTIVE_LIST_MAX_PAGE_SIZE } from './list-pagination'
import { hasActiveRegisteredFilters, resetRegisteredListFilters, subscribeListFilters } from './use-url-list-state'

/**
 * 「导出全部」在页面未声明 `exportMaxRows` 时的**保守默认**行数上限。
 *
 * 为什么是 5 万：这个值要同时满足两件互相拉扯的事——不能挡住真实的业务导出诉求，又要在
 * 出问题时把损失关在可控范围内。5 万行的 Excel 已经是人不会再逐行看、只会拿去做透视的量级；
 * 而按 `fetchAll` 每块 1000 行算，它是 50 次往返、后端累计扫约 1250 万行，几十秒能收敛。
 * 再往上（20 万行 = 200 次往返、累计扫约 2 亿行）就不是"慢一点"而是"拖垮库"。
 *
 * 真需要更多行的页面有两条正路，都不是调大这个值：①上服务端流式导出（`serverExportPath`，
 * 内存 O(batch)、无 OFFSET 递增，样品域已有成例）；②确知表有业务上界时显式传
 * `exportMaxRows: null` 声明无上限。
 *
 * ⚠️ 改这个值前先想清楚：它是 59 个页面的共同兜底，调大一次就是同时放开 59 处。
 */
export const DEFAULT_EXPORT_MAX_ROWS = 50_000

/**
 * 服务端分页列表取数（数据层）。封掉 employees/users 那套逐字重写的
 * `rows/total/page/pageSize/search/reload/didMountRef` + 分块导出全量。
 *
 * 首屏用 page.tsx 经 `apiFetch` 传入的 `initial`（跳过首帧请求）；之后翻页/搜索/
 * 额外筛选变化都下推后端 Skip/Take 真分页。返回的 `tableProps` 直接 spread 给
 * `<DataTable>` 的 manual 模式 props。
 *
 * @example
 * const list = useCrudResource<Employee>({
 *   endpoint: 'employees',
 *   initial: data,
 *   errorTitle: '员工列表加载失败',
 *   extraDeps: [departmentId, status],
 *   buildParams: (qs) => {
 *     if (departmentId) { qs.set('departmentId', departmentId); qs.set('includeChildDepartments', 'true') }
 *     if (status !== '') qs.set('status', status)
 *   },
 * })
 * // extraDeps 变化时 hook 自动回第 1 页，不必再手写 list.setPage(0)
 * <DataTable columns={columns} data={list.rows} {...list.tableProps} />
 */
export interface UseCrudResourceOptions<TRow> {
  /** 后端资源相对路径（不含 `/api/proxy/`），如 `employees`。 */
  endpoint: string
  /** page.tsx 提供的首屏数据。 */
  initial: PagedResult<TRow>
  /** 每页条数，默认 15。 */
  pageSize?: number
  /**
   * 交互列表单页上限，默认 {@link INTERACTIVE_LIST_MAX_PAGE_SIZE}（200）。
   * 写入 `tableProps.maxPageSize`，并钳制初始 `pageSize` 与 `setPageSize`。
   * 需要更宽的列表必须同时：本项 + `page.tsx` 的 `parseServerListPagination` 第四参
   * （DataTable 若 spread `tableProps` 会自动拿到上限）。宽分页场景可传 500。
   */
  pageSizeMax?: number
  /**
   * 追加额外查询参数。hook 已自动设 `skipCount`/`maxResultCount`/`filter`，
   * 这里只补页面级筛选（部门、状态等）。是普通闭包，直接读组件作用域。
   */
  buildParams?: (params: URLSearchParams) => void
  /**
   * 额外筛选依赖。变化时重取（值由 `buildParams` 闭包读取），并自动回到第 1 页
   * （与搜索 / 排序同语义）。调用方不必再手写 `list.setPage(0)`；写了也只是幂等。
   *
   * 查询按钮式页面把 `queryVersion` 放这里（筛选值不要放）——点「查询」才变、才回第 1 页。
   */
  extraDeps?: unknown[]
  /** 取数失败 toast 标题，默认「列表加载失败」。 */
  errorTitle?: string
  /**
   * 把 page/pageSize/search/sorting 镜像到 URL query（键 page/size/q/sort，shallow 更新），配合
   * page.tsx 从 searchParams 解析初值下传，实现「切走再切回 / 刷新」保留分页、搜索与排序。
   *
   * 默认仍是 false：同页第二份列表 / 抽屉内列表写地址栏会污染主列表。
   * **顶栏列表必须显式 `urlSync: true`**；次列表必须显式 `urlSync: false`。
   * 门禁见 `use-crud-resource-urlsync-contract.test.ts`——漏写会红。
   */
  urlSync?: boolean
  /** 初始页码（0-based），URL 持久化时由 page.tsx 从 `?page`(1-based) 解析下传；默认 0。 */
  initialPage?: number
  /** 初始搜索词，URL 持久化时由 page.tsx 从 `?q` 解析下传；默认 ''。 */
  initialSearch?: string
  /** 初始排序，URL 持久化时由 page.tsx 用 `parseSortParam(sp.sort)` 解析下传（首屏取数需同步拼
   * `Sorting`，见 `toAbpSorting`），默认 []。 */
  initialSorting?: SortingState
  /**
   * 「导出全部」的行数硬上限。**不传 = 用保守默认 {@link DEFAULT_EXPORT_MAX_ROWS}（5 万）**，
   * 传 `null` = 显式声明本页不需要上限。
   *
   * 🔴 **默认值这一层是 2026-08-04 补的，补的是一个全站敞口**：此前不传就是「不限」，而 64 个
   * `useCrudResource` 页面里只有 1 页真的传了——其余 59 页（另 3 页显式关了导出、1 页走服务端
   * 流式）的「导出全部」都会一路拉到 `rounds > 2000` 的 200 万行熔断才停。`fetchAll` 是 OFFSET
   * 递增的分块拉取，行数 N 时后端累计扫描约 O(N²/2) 行（16 万行就要 165 次请求、累计扫约 1.35
   * 亿行），一次点击足以把库拖垮。`docs/data-scale-lessons.md` §2.1「无界 + 静默不许当默认」
   * 早就给出了这个结论，只是一直没兑现。
   *
   * 达到上限会自动 toast 提示「结果过多、请先筛选」，调用方无需额外接线——**这正是与旧默认的
   * 关键差别**：旧行为在超大列表上不是"导出了全部"，而是慢到用户放弃或浏览器崩掉，两种都比
   * 「给你 5 万行并明说截断了」糟。
   *
   * 什么时候显式传 `null`：确知该表行数有业务上界（跟组织规模成正比，如部门/岗位/字典），
   * 且用户确实需要一次拿全。传 `null` 是**声明**，不是省事——它会在 review 时被看见。
   */
  exportMaxRows?: number | null
  /**
   * **服务端流式导出**端点（不含 `/api/proxy/`），如 `samples/export`。
   *
   * 设了它，「导出全部」不再把行拉进浏览器：后端按稳定键逐批取数、边取边写文件流式回吐，
   * 浏览器只负责保存。破百万的列表走这条——前端拉取那条的两堵墙（浏览器内存、OFFSET 递增
   * 导致后端约 O(N²/2) 的累计扫描）在这里都不存在。
   *
   * 只下发**筛选**参数（`filter` + `buildParams`），不下发分页与排序：导出是全量、排序由服务端
   * 固定为该域默认序（keyset 谓词与 ORDER BY 是一体的）。设了本项时 {@link exportMaxRows} 失效，
   * 行数上限改由服务端裁决——超限**整体拒绝**并提示先筛选，不会静默少给行。
   */
  serverExportPath?: string
}

export interface UseCrudResourceReturn<TRow> {
  rows: TRow[]
  total: number
  page: number
  pageSize: number
  search: string
  loading: boolean
  setPage: (page: number) => void
  setPageSize: (size: number) => void
  setSearch: (value: string) => void
  /** 客户端重取当前页（仅刷 React state）。用于分页/搜索/筛选反应；**写操作后请改用 reloadAndRefresh**。 */
  reload: () => Promise<void>
  /** 写操作后专用：reload 即时刷新可见列表 + router.refresh 失效 Next Router Cache（切页签返回不显旧值）。
   * 内联/批量写 handler 应调它（或把它作为 reload 传给批量 hook），而非裸 reload。 */
  reloadAndRefresh: () => Promise<void>
  /** 重置列表视图：搜索/页码/排序回默认 + 清空页面级 useUrlListState 筛选（URL 键一并删除）+
   * 强制重取一次（不含每页条数——它归用户偏好，见 hook 内 resetView 定义处注释）。
   * 已随 tableProps 下发给 DataTable 工具栏的「重置」按钮，页面无需接线。 */
  resetView: () => void
  /** 直接 spread 给 `<DataTable>`（manual 模式）。 */
  tableProps: {
    manualPagination: true
    rowCount: number
    pageIndex: number
    manualPageSize: number
    maxPageSize: number
    initialSearch: string
    onPaginationChange: (next: { pageIndex: number; pageSize: number }) => void
    onSearchChange: (value: string) => void
    loading: boolean
    onExportAll: () => Promise<TRow[]>
    /** 服务端流式导出（仅在 options.serverExportPath 设置时存在）；DataTable 优先用它。 */
    onServerExport?: () => Promise<void>
    manualSorting: true
    sorting: SortingState
    onSortingChange: (sorting: SortingState) => void
    onResetView: () => void
    /** 刷新数据：重取当前页 + 失效 Next Router Cache（工具栏「刷新」按钮）。跨路由导入/外部变更后
     * 手动拉最新，同时失效 RSC 缓存（= reloadAndRefresh）。与 onResetView（清筛选/分页回默认）区分。 */
    onReload: () => Promise<void>
    /** 视图是否偏离默认（搜索/页码/排序/页面级 useUrlListState 筛选任一非默认；每页条数不参与，
     * 见 hook 内 viewDirty 定义处注释）：驱动工具栏「重置」按钮的条件显隐（默认视图下不渲染，见蓝图 §7）。 */
    viewDirty: boolean
  }
}

export function useCrudResource<TRow>(
  options: UseCrudResourceOptions<TRow>,
): UseCrudResourceReturn<TRow> {
  const { initial, extraDeps = [] } = options
  const router = useRouter()
  // 取数失败 toast 的默认文案走 i18n（Common namespace）；errorTitle 传参覆盖时按调用方原样使用。
  const t = useTranslations('Common')
  // 大多数 CRUD 能在独立测试/Story 场景复用；仅原生下载必须绑定受认证首屏的身份世代。
  const identityVersion = useOptionalSessionIdentity()?.identityVersion ?? null
  const optsRef = useRef(options)
  // React 19 禁止 render 期间写 ref；改用 effect 更新（latest-ref 模式）。
  useEffect(() => { optsRef.current = options })
  // 列表请求的所有权与组件生命周期绑定：新请求先使旧请求失效并取消传输，卸载后也不再
  // 发起/提交请求。序号门禁不可只靠 AbortController 替代——fetch mock、polyfill 或已经完成
  // 的响应解析可能不理会 abort，仍需阻止旧响应覆盖最新列表。
  const mountedRef = useRef(false)
  const listRequestSequenceRef = useRef(0)
  const activeListRequestRef = useRef<{ id: number; controller: AbortController } | null>(null)
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
      listRequestSequenceRef.current += 1
      const activeRequest = activeListRequestRef.current
      activeListRequestRef.current = null
      activeRequest?.controller.abort()
    }
  }, [])

  const [rows, setRows] = useState<TRow[]>(initial.items)
  const [total, setTotal] = useState(initial.totalCount)
  const [page, setPage] = useState(options.initialPage ?? 0)
  const pageSizeMax = options.pageSizeMax ?? INTERACTIVE_LIST_MAX_PAGE_SIZE
  const [pageSize, setPageSize] = useState(() => {
    const requested = options.pageSize ?? 15
    return Math.min(Math.max(1, Number.isFinite(requested) ? Math.floor(requested) : 15), pageSizeMax)
  })
  const [search, setSearch] = useState(options.initialSearch ?? '')
  const [sorting, setSorting] = useState<SortingState>(options.initialSorting ?? [])
  const [loading, setLoading] = useState(false)
  // 「重置视图与数据必须一致」的强制重取信号（蓝图 §7.2 语义规范）：query-button 式页面
  // （extraDeps 只含 queryVersion，不含页面级 filters）里，若用户只动过页面级筛选、从未碰过
  // search/page/pageSize/sorting，resetView() 清筛选时这四项本就已是默认值——不触发 state 变化，
  // reload 引用不变，didMount effect 不会重取，导致「筛选已清空但表格仍显示旧查询结果」。
  // resetNonce 在 resetView() 里无条件自增，强制 reload 换引用、必然重取一次。
  const [resetNonce, setResetNonce] = useState(0)

  // extraDeps 序列化成稳定 key：页面级筛选变化时纳入 reload 依赖（literal 数组，满足 react-hooks 规则）。
  const extraKey = JSON.stringify(extraDeps)
  // extraDeps 变化自动回第 1 页（与搜索/排序同语义）。用 render-adjust：同一轮把 page
  // 置 0，随后重建的 reload 闭包带着 skipCount=0，避免「先打旧页再打首页」双请求。
  // 首屏 seenExtraKey === extraKey，不会把 URL 恢复的 initialPage 打回 0。
  const [seenExtraKey, setSeenExtraKey] = useState(extraKey)
  if (seenExtraKey !== extraKey) {
    setSeenExtraKey(extraKey)
    if (page !== 0) setPage(0)
  }
  // 排序序列化成 ABP Sorting 串（"字段 asc|desc, ..."）：列 id 即字段名，后端按白名单大小写不敏感匹配。
  const sortingParam = toAbpSorting(sorting)

  const reload = useCallback(async () => {
    if (!mountedRef.current) return

    const requestId = ++listRequestSequenceRef.current
    activeListRequestRef.current?.controller.abort()
    const controller = new AbortController()
    activeListRequestRef.current = { id: requestId, controller }

    // 捕获本次请求的选项快照，避免动态 endpoint / 文案 / buildParams 在等待期间变化后，
    // 旧请求用新选项记录错误或拼出跨查询参数。
    const { endpoint, errorTitle, buildParams } = optsRef.current
    const ownsLatestRequest = () => {
      const activeRequest = activeListRequestRef.current
      return mountedRef.current
        && listRequestSequenceRef.current === requestId
        && activeRequest?.id === requestId
        && activeRequest.controller === controller
    }

    setLoading(true)
    try {
      const qs = new URLSearchParams({
        skipCount: String(page * pageSize),
        maxResultCount: String(pageSize),
      })
      if (search.trim()) qs.set('filter', search.trim())
      if (sortingParam) qs.set('Sorting', sortingParam)
      buildParams?.(qs)
      const res = await fetch(
        `/api/proxy/${endpoint}?${qs.toString()}`,
        { signal: controller.signal },
      )
      if (!res.ok) {
        const text = await res.text()
        if (absorbPlatformUpdating(text)) return
        throw new Error(text)
      }
      const d = (await res.json()) as PagedResult<TRow>
      if (!ownsLatestRequest()) return
      setRows(d.items)
      setTotal(d.totalCount)
    } catch (e) {
      // 被后续请求替代或组件卸载属于预期控制流：保持 Promise<void>，且不污染错误日志/toast。
      if (!ownsLatestRequest() || controller.signal.aborted) return
      console.error(`[${endpoint}/reload]`, e)
      toast({ variant: 'destructive', title: errorTitle ?? t('loadFailedTitle'), description: t('tryAgainLater') })
    } finally {
      if (!ownsLatestRequest()) return
      activeListRequestRef.current = null
      setLoading(false)
    }
    // extraKey / sortingParam 纳入依赖：页面级筛选或排序变化时重建 reload → didMount effect 触发重取。
    // resetNonce：resetView() 强制重取信号，即使 page/pageSize/search/sorting 恰好都已是默认值
    // （用户只碰过页面级筛选）也保证 reload 换引用、触发一次真实重取（见上方 resetNonce 注释）。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, pageSize, search, sortingParam, extraKey, resetNonce, t])

  // 写操作后专用：即时刷新可见列表(reload) + 失效 Next Router Cache(router.refresh)，防「切页签返回读旧 RSC」。
  // 与裸 reload 分开：分页/搜索反应用 reload（不该每次翻页都 refresh），写操作用本方法。
  const reloadAndRefresh = useCallback(async () => {
    await reload()
    // 等待期间用户可能已离开本页；此时刷新会误作用于新路由。若只是被同页更新的 reload
    // 替代，组件仍挂载，依然执行 refresh 以保留写后失效 Router Cache 的既有语义。
    if (mountedRef.current) router.refresh()
  }, [reload, router])

  // 首帧用 initial，跳过；之后随 reload 引用变化（翻页/搜索/筛选）重取。
  const didMount = useRef(false)
  useEffect(() => {
    if (!didMount.current) { didMount.current = true; return }
    void reload()
  }, [reload])

  // 导出：后端单页有上限（ABP MaxMaxResultCount，见 HttpApiHostModule，当前 5000），按当前筛选分块拉全量。
  // chunk 取 1000（保守、远低于上限即可正确分页，故与上限值解耦、无需随后端同步）。
  //
  // 🔴 **终止条件只认「本页返回 0 行」**——历史上这里连错两次，两次都是**静默少给行**：
  //   ① `all.length >= d.totalCount`：把后端报的总数当权威。列表改用**截断计数**后
  //      （为省掉千万级 COUNT(*) 只回 `cap+1`，样品列表已如此）会在 cap 处停下。
  //   ② `d.items.length < chunk`：把「本页不满」当作「没有下一页」。但服务端可以合法地
  //      把单页钳得比请求的更小（样品 ListMaxPageSize=500 < chunk=1000），于是**每一页都
  //      不满**，第一页之后即停——导出只剩 500 行（2026-08-03 修，B 批引入）。
  // 两次的失败形态完全一样：用户拿到一份看不出少了行的 Excel，比直接报错糟得多。
  // 现判据与总数、与服务端页大小都无关，代价只是末尾多一次返回空的请求。
  //
  // maxRows 是**行数硬上限**（可选）：不传维持原「拉到底」行为；传了则拉满即停并回传，
  // 由调用方负责提示用户「结果过多，请先筛选」。破千万的列表必须传——否则一次导出是
  // O(N²) 的 OFFSET 递增扫描（16 万行就要 165 次请求、后端累计扫约 1.35 亿行）。
  const fetchAll = useCallback(async (maxRows?: number): Promise<TRow[]> => {
    const o = optsRef.current
    const all: TRow[] = []
    const chunk = 1000
    let rounds = 0
    for (let skip = 0; ; ) {
      // 🔴 **skip 按「本页实际返回行数」递增，终止条件是「返回 0 行」——不是「返回不足一 chunk」**。
      // 服务端可以合法地把单页钳得比我们请求的更小（样品列表 ListMaxPageSize=500 < chunk=1000），
      // 这时每页都「不足一 chunk」，旧判据会在**第一页之后就停**，导出静默只剩 500 行。
      // 按固定 chunk 递增 skip 同样错：会跳过服务端没返回的那 500 行。两个 bug 一起修。
      if (++rounds > 2000) {
        // 服务端忽略 skipCount（每页返回同一批行）时的兜底：宁可显式失败，也不静默截断或死循环。
        throw new Error(t('exportPagingAbnormal'))
      }
      const qs = new URLSearchParams({ skipCount: String(skip), maxResultCount: String(chunk) })
      if (search.trim()) qs.set('filter', search.trim())
      if (sortingParam) qs.set('Sorting', sortingParam)
      o.buildParams?.(qs)
      const res = await fetch(`/api/proxy/${o.endpoint}?${qs.toString()}`)
      if (!res.ok) throw new Error(await res.text())
      const d = (await res.json()) as PagedResult<TRow>
      all.push(...d.items)
      if (d.items.length === 0) break
      skip += d.items.length
      if (maxRows !== undefined && all.length >= maxRows) {
        // 截断必须说出来：静默给一份少了行的导出，用户完全看不出来。
        toast({
          variant: 'destructive',
          title: t('exportTruncatedTitle', { count: maxRows }),
          description: t('exportTruncatedHint'),
        })
        return all.slice(0, maxRows)
      }
    }
    return all
  }, [search, sortingParam, t])

  /**
   * 服务端流式导出：只把**当前筛选条件**下发，后端生成文件流式回吐，浏览器下载器直接消费响应流。
   * 刻意不传分页（导出是全量）与排序（服务端固定域默认序，keyset 谓词与 ORDER BY 一体）。
   * 必须在点击的同步调用链内 window.open，不能先 await；否则会丢失 user activation 并被弹窗策略拦截。
   * 不使用 download 属性：文件名以 Content-Disposition 为准，错误响应则留在新标签而不会伪装成 CSV。
   */
  const serverExport = useCallback(async () => {
    const o = optsRef.current
    if (!o.serverExportPath) return
    if (!identityVersion) {
      throw new Error('SessionIdentityProvider is required for server export')
    }
    const qs = new URLSearchParams()
    if (search.trim()) qs.set('filter', search.trim())
    o.buildParams?.(qs)
    const query = qs.toString()
    window.open(
      bindSessionIdentityToUrl(
        `/api/proxy/${o.serverExportPath}${query ? `?${query}` : ''}`,
        identityVersion,
      ),
      '_blank',
      'noopener,noreferrer',
    )
  }, [identityVersion, search])

  // URL 同步包装：urlSync 开启时，翻页/改每页数/搜索在改内部 state 的同时把 page/size/q 增量写进 URL。
  // 约定：page 用 1-based（默认页 1 不入 URL）、size（默认值不入 URL）、q（空不入 URL），保持地址栏干净。
  const syncUrl = options.urlSync ?? false
  // extraDeps 回第 1 页走的是 render-adjust 的裸 setPage，不会经过 setPageSynced。
  // 这里在提交后补清 URL 的 page 键；首屏 extraKey 不变，ref 相等，不写地址栏。
  const extraKeyForUrlRef = useRef(extraKey)
  useEffect(() => {
    if (extraKeyForUrlRef.current === extraKey) return
    extraKeyForUrlRef.current = extraKey
    if (syncUrl && page === 0) patchListUrl({ page: '' })
  }, [extraKey, page, syncUrl])
  const defaultSize = options.pageSize ?? 15
  const setPageSynced = useCallback((p: number) => {
    setPage(p)
    if (syncUrl) patchListUrl({ page: p > 0 ? p + 1 : '' })
  }, [syncUrl])
  const setPageSizeSynced = useCallback((s: number) => {
    const next = Math.min(Math.max(1, Number.isFinite(s) ? Math.floor(s) : 1), pageSizeMax)
    setPageSize(next)
    if (syncUrl) patchListUrl({ size: next === defaultSize ? '' : next })
  }, [syncUrl, defaultSize, pageSizeMax])
  const setSearchSynced = useCallback((v: string) => {
    setSearch(v)
    if (syncUrl) patchListUrl({ q: v })
  }, [syncUrl])
  const setSortingSynced = useCallback((s: SortingState) => {
    setSorting(s)
    if (syncUrl) patchListUrl({ sort: serializeSortParam(s) })
  }, [syncUrl])

  // 重置列表视图：q/page/sort 回默认（synced 版本会同时清 URL 键）+ 清页面级筛选 + 强制重取一次。
  // pageSize 不在此列（蓝图 §7.2 P1 决议）：与列顺序/列显隐同归「用户偏好」而非「会话视图态」——
  // 用户选的每页条数是持续性展示偏好，「重置搜索/筛选」不该顺手把它拨回默认，体验上会显得莫名其妙。
  // 跨会话与跨设备持久化已由 DataTable 结合 TablePreferenceAppService 统一承接（按 effectiveTableKey 存
  // localStorage + User 级分片 Setting，无显式 URL ?size= 时挂载自动恢复）。
  // 注意：查询按钮式页面（queryVersion 不进取数依赖）历史上清筛选不主动重取——resetNonce 已治好
  // 这个不一致（见上方定义处注释），重置后无论走哪种取数模式都保证重新查询一次。
  const resetView = useCallback(() => {
    setSearchSynced('')
    setPageSynced(0)
    setSortingSynced([])
    resetRegisteredListFilters()
    setResetNonce((n) => n + 1)
  }, [setSearchSynced, setPageSynced, setSortingSynced])

  // 页面级 useUrlListState 筛选是否存在非默认值：经外部 store 订阅（同页任一实例 set/reset/
  // 注册/注销都会 notify），驱动下方 viewDirty；SSR 快照恒 false（筛选状态本就来自客户端 hook）。
  const filtersActive = useSyncExternalStore(subscribeListFilters, hasActiveRegisteredFilters, () => false)
  // 视图是否偏离默认：搜索非空 / 不在第 1 页 / 有排序 / 页面级筛选非默认，任一即为 true——
  // pageSize 不参与（它不属于「重置」语义覆盖的会话视图态，见上方 resetView 注释），
  // 否则按钮会在「只是调大了每页条数」时出现，但点击并不会改动它，自相矛盾。
  // 驱动工具栏「重置」按钮条件显隐（蓝图 §7.2：平时工具栏只剩刷新一个环形图标，混淆源根除）。
  const viewDirty = search !== '' || page !== 0 || sorting.length > 0 || filtersActive

  return {
    rows,
    total,
    page,
    pageSize,
    search,
    loading,
    setPage: setPageSynced,
    setPageSize: setPageSizeSynced,
    setSearch: setSearchSynced,
    reload,
    reloadAndRefresh,
    resetView,
    tableProps: {
      manualPagination: true,
      rowCount: total,
      pageIndex: page,
      manualPageSize: pageSize,
      maxPageSize: pageSizeMax,
      initialSearch: options.initialSearch ?? '',
      onPaginationChange: ({ pageIndex, pageSize: nextSize }) => { setPageSynced(pageIndex); setPageSizeSynced(nextSize) },
      onSearchChange: (v: string) => { setSearchSynced(v); setPageSynced(0) },
      loading,
      // DataTable 的 onExportAll 签名无参 ⇒ 上限在这里注入，页面只需在 options 里声明 exportMaxRows。
      // `=== null` 必须先判：`??` 对 null 和 undefined 一视同仁，写成 `exportMaxRows ?? DEFAULT`
      // 会让「显式声明无上限」静默变成「用默认上限」——正好把 opt-out 这个能力废掉。
      onExportAll: () => fetchAll(
        options.exportMaxRows === null
          ? undefined
          : options.exportMaxRows ?? DEFAULT_EXPORT_MAX_ROWS,
      ),
      onServerExport: options.serverExportPath ? serverExport : undefined,
      manualSorting: true,
      sorting,
      // setPageSynced(0)：改排序回第 1 页时同步清 URL 的 page 键（裸 setPage 会留下旧页码脏数据）
      onSortingChange: (s: SortingState) => { setSortingSynced(s); setPageSynced(0) },
      onResetView: resetView,
      onReload: reloadAndRefresh,
      viewDirty,
    },
  }
}
