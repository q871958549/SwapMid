'use client'

import { useEffect, useMemo, useRef, useState } from 'react'
import { usePathname } from 'next/navigation'
import { useLocale, useTranslations } from 'next-intl'
import {
  type ColumnDef,
  type ColumnFiltersState,
  type FilterFn,
  type RowSelectionState,
  type SortingState,
  type VisibilityState,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
} from '@tanstack/react-table'
import { statusAwareFilter, pinyinAwareGlobalFilter } from './data-table-filters'
import { normalizeStructuralColumns, registerColumnTitles, buildSelectionColumn } from './data-table-columns'
import { DataTableSkeleton } from './data-table-skeleton'
import { useTableExport } from './use-table-export'
import { DataTablePagination } from './data-table-pagination'
import { DataTableToolbar } from './data-table-toolbar'
import { DataTableGrid } from './data-table-grid'
import { DataTableCards } from './data-table-cards'
import { useNarrowScreen } from './use-narrow-screen'
import {
  clampColumnSizing,
  clearStoredColumnLayout,
  deriveTableKey,
  getColumnKey,
  reconcileColumnOrder,
  saveOrder,
  savePageSize,
} from './use-table-persistence'
import { scheduleCloudTableViewSync } from './table-preference-cloud'
import { useTablePreferences } from './use-table-preferences'
import { useTableDensity } from './use-table-density'
import { useDebouncedSearchEmit } from './use-debounced-search-emit'
import { useManualPageSizeSync } from './use-manual-page-size-sync'
import { useTableSavedViews } from './use-table-saved-views'
import { type AuditUserInfo, withAuditColumns, collectAuditUserIds, loadAuditUsers } from './data-table-audit'
import { useHasPermission } from '@/components/providers/permissions-provider'
import { useOptionalSessionIdentity } from '@/components/providers/session-identity-provider'
import { findNavItemByPath } from '@/lib/nav-utils'

/**
 * DataTable 根 props 已到约 35 个，客户端/服务端两套分页挤在同一接口。
 * 🔴 新能力不要再往本接口加根 props：服务端分页 / 导出 / 重置 / 刷新一律由
 * `useCrudResource.tableProps` 下发，页面 `{...list.tableProps}`。
 * 只有纯展示开关（如 disableCompactHeight）才留在根上。
 */
interface DataTableProps<TData> {
  columns: ColumnDef<TData, unknown>[]
  data: TData[]
  searchPlaceholder?: string
  /** 是否显示全局搜索框（默认 true）。后端无统一 filter 关键字（仅翻页）或仅按页面级字段筛选的服务端分页列表传 false 隐藏。 */
  enableSearch?: boolean
  toolbarExtra?: React.ReactNode
  /** 以纯图标呈现导出、刷新和列设置。 */
  compactToolbarActions?: boolean
  pageSize?: number
  enableExport?: boolean
  /** 导出按钮是否需要 {资源}.Export 子权限（默认 true）；纯查询报表（单权限、无 CRUD 子权限）可传 false，有查看权即可导出。 */
  requireExportPermission?: boolean
  exportFileName?: string
  showAuditColumns?: boolean
  /**
   * 传入才显示「导入」按钮——**按钮的有无由本回调推导，没有 `enableImport` 开关**。
   * 曾经有：`enableImport` 默认 true，于是 82 个页面都挂着导入按钮，而全站 `onImportRows`
   * 实现数为 0，点下去只弹一句「读取到 N 行」的 alert 就没了（2026-07-22 清理）。改成
   * 「有实现才有按钮」后，死按钮在类型层面不可能再出现。
   * 复杂导入场景（模板下载 / 字段映射 / 预览 / 错误报告）走页面独立导入对话框；本回调适合简单单表直读。
   */
  onImportRows?: (rows: Record<string, string>[], file: File) => void | Promise<void>
  enableRowSelection?: boolean
  selectedRowIds?: RowSelectionState
  onSelectedRowIdsChange?: (selection: RowSelectionState) => void
  getRowId?: (row: TData, index: number) => string
  bulkActions?: (selectedRows: TData[], clearSelection: () => void, selectedIds: string[]) => React.ReactNode
  /**
   * Unique key for this table. Used to persist column order in localStorage
   * so each user's column arrangement is remembered across sessions.
   */
  tableKey?: string
  /**
   * 服务端分页模式（默认关闭，对现有 20+ 客户端分页列表零影响）。开启后：
   * - 表格不再前端切片/搜索/过滤，`data` 视为「当前服务端页」原样渲染；
   * - 总页数由 `rowCount` 推导，页码/每页由 `pageIndex`/`manualPageSize` 受控；
   * - 翻页/改每页 → `onPaginationChange`；搜索框内部防抖后 → `onSearchChange`；
   * - 列筛选行按钮隐藏（改用页面级服务端筛选）；导出仅含当前页。
   */
  manualPagination?: boolean
  /** 服务端总条数（manualPagination 时用于总页数与「共 X 条」）。 */
  rowCount?: number
  /** 受控页码（0-based，manualPagination 时必传）。 */
  pageIndex?: number
  /** 受控每页条数（manualPagination 时必传）。 */
  manualPageSize?: number
  /**
   * 每页档位上限，透传给分页器（默认按 manualPagination 推导为 200 / 10000）。
   * 需要比交互默认更宽的列表用之——如宽分页视图放到 500（后端同步静默钳制到同值）。
   */
  maxPageSize?: number
  /**
   * 总数截断阈值，透传给分页器：`rowCount > countCap` 时显示「{countCap}+」。
   * 仅当**后端确实在做截断计数**时才传，否则会把精确总数误显示成「N+」。
   */
  countCap?: number
  /** 翻页 / 改每页条数回调（manualPagination 时必传）。 */
  onPaginationChange?: (next: { pageIndex: number; pageSize: number }) => void
  /** 搜索词变化（已防抖）回调；manualPagination 时父组件据此向服务端取数。 */
  onSearchChange?: (value: string) => void
  /** 搜索框初值（manualPagination + URL 持久化时从 URL 的 q 恢复搜索框显示；默认空）。首帧不触发 onSearchChange。 */
  initialSearch?: string
  /** 取数中：表格上浮一层遮罩，避免翻页/搜索时空表闪烁。 */
  loading?: boolean
  /** 服务端分页时「导出全部匹配行」：父组件（按需分页）拉全量返回；不传则导出仅含当前页。 */
  onExportAll?: () => Promise<TData[]>
  /**
   * 服务端流式导出：后端生成文件、浏览器只保存。**传了它就优先于 `onExportAll`**——
   * 破百万的列表用这条，行不进浏览器内存。由 `useCrudResource({ serverExportPath })` 下发。
   */
  onServerExport?: () => Promise<void>
  /**
   * 服务端排序（默认关闭）。开启后列头排序不再前端排（破千列表前端只排当页=假排序），
   * 而是把排序状态经 `onSortingChange` 下推后端。需配 `manualPagination` 一起用。
   */
  manualSorting?: boolean
  /** 受控排序状态（manualSorting 时由父组件持有并下推后端）。 */
  sorting?: SortingState
  /** 排序变化回调（manualSorting 时父组件据此向后端取数，通常同时回到第 1 页）。 */
  onSortingChange?: (sorting: SortingState) => void
  /** 「重置列表视图」回调（useCrudResource 的 tableProps 自动下发）：工具栏出现重置按钮，
   * 点击清空搜索/页码/每页/排序 + 页面级 useUrlListState 筛选，URL 键一并删除。 */
  onResetView?: () => void
  /** 视图是否偏离默认，控制重置按钮的条件显隐（仅 `onResetView` 存在时才涉及）。
   * useCrudResource 的 tableProps 自动算出并下发；页面手写 manual props 未传时默认 true——
   * 与现状一致（只要给了 onResetView 就显示），避免旧写法静默丢按钮。 */
  viewDirty?: boolean
  /** 「刷新数据」回调（useCrudResource 的 tableProps 自动下发）：工具栏出现刷新按钮，点击重取当前页
   * 并失效 Router Cache。用于跨路由导入/外部变更后手动拉最新（与 onResetView 清筛选回默认相区分）。 */
  onReload?: () => void | Promise<void>
  /**
   * 关闭「数据少自动收缩高度」（compact）。默认 false：单页且 ≤8 行时表格收缩为 shrink-0、
   * 按内容自然高度渲染，省掉大片空白。但当页面在表格上方还有固定高度块（如统计卡）时，收缩会让
   * 表格脱离 flex-1 吸高、自然高把整页顶出 <main> 视口，冒出「内层(统计卡/表体)+外层(main)」双滚动条。
   * 这类页面传 true，强制表格恒为 flex-1 吃满剩余、仅表体内部滚动。
   */
  disableCompactHeight?: boolean
  /**
   * 未过滤且没有行时替换「暂无数据」。不传则保持原句，SwapMid列表不受影响。
   * 过滤后没有匹配时仍用「没有匹配」。
   */
  emptyState?: React.ReactNode
}

export function DataTable<TData>({
  columns,
  data,
  searchPlaceholder,
  enableSearch = true,
  toolbarExtra,
  compactToolbarActions,
  pageSize = 15,
  enableExport = true,
  requireExportPermission = true,
  exportFileName = 'table-export.csv',
  showAuditColumns = true,
  onImportRows,
  enableRowSelection = false,
  selectedRowIds,
  onSelectedRowIdsChange,
  getRowId,
  bulkActions,
  tableKey,
  manualPagination = false,
  rowCount,
  pageIndex,
  manualPageSize,
  maxPageSize,
  countCap,
  onPaginationChange,
  onSearchChange,
  initialSearch = '',
  loading = false,
  onExportAll,
  onServerExport,
  manualSorting = false,
  sorting: controlledSorting,
  onSortingChange,
  onResetView,
  viewDirty = true,
  onReload,
  disableCompactHeight = false,
  emptyState,
}: DataTableProps<TData>) {
  const t = useTranslations('Common')
  const locale = useLocale()
  const sessionIdentity = useOptionalSessionIdentity()
  const auditIdentityVersion = sessionIdentity?.identityVersion ?? null
  const storagePrincipalId = sessionIdentity?.storagePrincipalId ?? null
  const effectiveSearchPlaceholder = searchPlaceholder ?? t('searchPlaceholder')
  const fileInputRef = useRef<HTMLInputElement | null>(null)
  const pathname = usePathname()
  // 导出/导入子权限跟当前页的导航项走，必须用最长前缀（findNavItemByPath，与页签、侧栏同一套）。
  // 找不到导航项时不限制，沿用默认显示。
  const resourcePermission = useMemo(
    () => findNavItemByPath(pathname)?.permission,
    [pathname],
  )
  const canExport = useHasPermission(resourcePermission ? `${resourcePermission}.Export` : undefined)
  const canImport = useHasPermission(resourcePermission ? `${resourcePermission}.Import` : undefined)
  const [auditUserState, setAuditUserState] = useState<{
    identityVersion: string | null
    users: Record<string, AuditUserInfo>
  }>({ identityVersion: null, users: {} })
  // 身份切换后的首帧也不得短暂复用旧身份姓名；新世代结果返回前统一视为空映射。
  const auditUsers = useMemo<Record<string, AuditUserInfo>>(
    () => auditUserState.identityVersion === auditIdentityVersion ? auditUserState.users : {},
    [auditIdentityVersion, auditUserState],
  )
  const [auditUsersLoading, setAuditUsersLoading] = useState(false)
  const [sorting, setSorting] = useState<SortingState>([])
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([])
  const [globalFilter, setGlobalFilter] = useState(initialSearch)
  const [internalRowSelection, setInternalRowSelection] = useState<RowSelectionState>({})
  const [showFilters, setShowFilters] = useState(false)
  const selectionColumn = useMemo<ColumnDef<TData, unknown>>(() => buildSelectionColumn<TData>(t), [t])
  const tableColumns = useMemo(
    () => withAuditColumns(
      normalizeStructuralColumns(enableRowSelection ? [selectionColumn, ...columns] : columns, t),
      data,
      showAuditColumns,
      auditUsers,
      auditUsersLoading,
      t,
      locale,
      manualSorting,
      manualPagination,
    ),
    [columns, data, enableRowSelection, selectionColumn, showAuditColumns, auditUsers, auditUsersLoading, t, locale, manualSorting, manualPagination]
  )
  const effectiveTableKey = useMemo(
    () => deriveTableKey(tableKey, pathname, tableColumns),
    [tableKey, pathname, tableColumns]
  )
  const defaultColumnOrder = useMemo(
    () => reconcileColumnOrder(tableColumns.map(getColumnKey), tableColumns),
    [tableColumns]
  )
  const defaultVisibility = useMemo<VisibilityState>(() => {
    const result: VisibilityState = {}
    tableColumns.forEach((col) => {
      const key = getColumnKey(col)
      const meta = col.meta as { audit?: boolean; defaultHidden?: boolean } | undefined
      if (meta?.audit || meta?.defaultHidden) result[key] = false
    })
    return result
  }, [tableColumns])
  // 用户偏好（列顺序 / 列显隐 / 每页条数）的持久化状态机：SSR 安全初值 + 挂载后从
  // localStorage 恢复 + 变更即存，全部收在 useTablePreferences 内（见该 hook 注释）。
  const {
    columnOrder, setColumnOrder,
    columnVisibility, setColumnVisibility,
    pagination, setPagination,
    columnSizing, setColumnSizing,
    pinned, setPinned,
    hydrated,
  } = useTablePreferences({
    effectiveTableKey,
    defaultColumnOrder,
    defaultVisibility,
    pageSize,
    tableColumns,
    persistInitialPageSize: !manualPagination,
  })
  const [density] = useTableDensity()
  const rowSelection = selectedRowIds ?? internalRowSelection
  // 「恢复默认列」管列显隐、列宽、固定与列顺序；搜索、筛选、排序、分页归工具栏的「重置」。
  const columnsDirty = Object.keys(columnSizing).length > 0
    || pinned.length > 0
    || reconcileColumnOrder(columnOrder, tableColumns).join() !== defaultColumnOrder.join()
    || tableColumns.some((column) => {
      const key = getColumnKey(column)
      return (columnVisibility[key] ?? true) !== (defaultVisibility[key] ?? true)
    })
  const [columnSettingsOpen, setColumnSettingsOpen] = useState(false)
  function restoreColumns() {
    clearStoredColumnLayout(effectiveTableKey)
    setColumnVisibility(defaultVisibility)
    setColumnSizing({})
    setPinned([])
    setColumnOrder(defaultColumnOrder)
    scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
  }
  function togglePinned(columnId: string) {
    setPinned((current) => current.includes(columnId) ? current.filter((id) => id !== columnId) : [...current, columnId])
    scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
  }
  /** 列设置面板里拖完一次：只传可移动的数据列顺序，结构列与固定列由协调函数归位。 */
  function reorderColumns(movable: string[]) {
    const next = reconcileColumnOrder(movable, tableColumns, pinned)
    setColumnOrder(next)
    saveOrder(effectiveTableKey, next)
    scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
  }
  // 列菜单已在菜单关好后才调用（useAfterMenuClose），这里直接开；再垫 setTimeout 反而赶不上菜单关闭动画。
  function openColumnSettings() {
    setColumnSettingsOpen(true)
  }
  const auditUserColumnsVisible = useMemo(
    () => showAuditColumns && tableColumns.some((column) => {
      const key = getColumnKey(column)
      return (key === 'creator' || key === 'lastModifier') && columnVisibility[key] !== false
    }),
    [showAuditColumns, tableColumns, columnVisibility]
  )
  useEffect(() => {
    registerColumnTitles(tableColumns, t)
  }, [tableColumns, t])

  // 服务端搜索：搜索框内部防抖（300ms）后回调父组件取数；仅 manualPagination 生效、首帧不触发。
  useDebouncedSearchEmit(globalFilter, manualPagination, onSearchChange)

  useManualPageSizeSync({
    effectiveTableKey,
    manualPagination,
    onPaginationChange,
    maxPageSize,
    manualPageSize,
    pageSize,
  })

  // 服务端分页时分页状态由父组件受控；否则用内部 state（现有列表行为不变）。
  const effectivePagination = manualPagination
    ? { pageIndex: pageIndex ?? 0, pageSize: manualPageSize ?? pageSize }
    : pagination

  // 服务端排序时排序状态由父组件受控；否则用内部 state（现有列表前端排序行为不变）。
  const effectiveSorting = manualSorting ? (controlledSorting ?? []) : sorting

  const savedViews = useTableSavedViews({
    effectiveTableKey,
    hydrated,
    storagePrincipalId,
    tableColumns,
    defaultColumnOrder,
    defaultVisibility,
    columnOrder,
    setColumnOrder,
    columnVisibility,
    setColumnVisibility,
    columnSizing,
    setColumnSizing,
    pinned,
    setPinned,
    enableSearch,
    globalFilter,
    setGlobalFilter,
    manualPagination,
    onSearchChange,
    onPaginationChange,
    effectivePageSize: effectivePagination.pageSize,
    manualSorting,
    effectiveSorting,
    onSortingChange,
    setSorting,
    columnFilters,
    setColumnFilters,
    setShowFilters,
  })

  function updateRowSelection(updater: RowSelectionState | ((old: RowSelectionState) => RowSelectionState)) {
    const next = typeof updater === 'function' ? updater(rowSelection) : updater
    if (onSelectedRowIdsChange) {
      onSelectedRowIdsChange(next)
      return
    }
    setInternalRowSelection(next)
  }

  // 🔴 渲染期协调列顺序，不能直接把持久化的 columnOrder 交给 TanStack（2026-08-10 生产实测）。
  // 症状：列表页首访时数据为空 → 审计列（创建时间/创建人/…）按 data 判定**不追加** →
  // useTablePreferences 的 hydration 每个 tableKey 只跑一次，把 columnOrder 定格成
  // [业务列…, 'actions'] → 用户在页内新增首行后审计列出现，但 order 不重算 →
  // TanStack 对「order 里没有的列」按定义序追加到末尾 → 操作列被挤到审计列之前（中间）。
  // 刷新后 SSR 数据非空、审计列在场，hydration 经同一函数协调，于是又「自己好了」。
  // reconcileColumnOrder 是纯函数且幂等：列集与 order 一致时输出全等，正常路径零行为变化，
  // 只在「order 缺列」这个 bug 时刻纠正。任何「首访空列表 → 页内新增首行」的页面都会踩到。
  const effectiveColumnOrder = useMemo(
    () => reconcileColumnOrder(columnOrder, tableColumns, pinned),
    [columnOrder, tableColumns, pinned]
  )

  // TanStack Table intentionally returns table helpers that React Compiler cannot memoize.
  // eslint-disable-next-line react-hooks/incompatible-library
  const table = useReactTable({
    data,
    columns: tableColumns,
    state: { sorting: effectiveSorting, columnFilters, columnVisibility, globalFilter, pagination: effectivePagination, columnOrder: effectiveColumnOrder, rowSelection, columnSizing },
    onSortingChange: (updater) => {
      const next = typeof updater === 'function' ? updater(effectiveSorting) : updater
      if (manualSorting) { onSortingChange?.(next); return }
      setSorting(next)
    },
    onColumnFiltersChange: setColumnFilters,
    onRowSelectionChange: updateRowSelection,
    onColumnVisibilityChange: (updater) => {
      const next = typeof updater === 'function' ? updater(columnVisibility) : updater
      setColumnVisibility(next)
      scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
    },
    onGlobalFilterChange: setGlobalFilter,
    onPaginationChange: (updater) => {
      const next = typeof updater === 'function' ? updater(effectivePagination) : updater
      if (next.pageSize !== effectivePagination.pageSize) {
        savePageSize(effectiveTableKey, next.pageSize)
        scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
      }
      if (manualPagination) {
        onPaginationChange?.(next)
        return
      }
      setPagination(next)
    },
    onColumnOrderChange: (updater) => {
      // updater 基于协调后的值——与传给 TanStack 的 state 保持同一基准，否则拖拽时会以旧序计算
      const next = reconcileColumnOrder(
        typeof updater === 'function' ? updater(effectiveColumnOrder) : updater,
        tableColumns,
        pinned,
      )
      setColumnOrder(next)
      saveOrder(effectiveTableKey, next)
      scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
    },
    // 列宽：拖表头右缘实时生效（onChange），拖出来的值夹在 64–800px；没拖过的列照旧按内容自动排版。
    enableColumnResizing: true,
    columnResizeMode: 'onChange',
    onColumnSizingChange: (updater) => {
      setColumnSizing((current) => clampColumnSizing(typeof updater === 'function' ? updater(current) : updater))
      scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
    },
    defaultColumn: { filterFn: statusAwareFilter },
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    globalFilterFn: pinyinAwareGlobalFilter as FilterFn<TData>,
    manualPagination,
    manualSorting,
    // 平台只支持单列排序：服务端白名单排序（ApplyWhitelistedSorting）只解析 Sorting 串首段，
    // 而 TanStack 默认允许 Shift+点列头做多列排序——放任就是「UI 出多列、后端只认第一列」的
    // 又一处界面撒谎。关掉多列，让可见状态与真实排序恒一致。
    enableMultiSort: false,
    manualFiltering: manualPagination,
    rowCount: manualPagination ? (rowCount ?? 0) : undefined,
    enableRowSelection,
    getRowId,
  })

  const { pageIndex: currentPageIndex, pageSize: currentPageSize } = table.getState().pagination
  const pageCount = table.getPageCount()
  const pageRows = table.getRowModel().rows
  // 客户端分页页也只解析当前渲染页；manualPagination 的 data 本来就是当前页。
  // 用稳定字符串作 effect key，避免 TanStack 每次返回新 rows 数组时造成重复请求。
  const auditUserIdsKey = auditUserColumnsVisible
    ? collectAuditUserIds(pageRows.map((row) => row.original)).join(',')
    : ''

  useEffect(() => {
    let active = true
    const ids = auditUserIdsKey ? auditUserIdsKey.split(',') : []
    if (!auditIdentityVersion || ids.length === 0) {
      setAuditUsersLoading(false)
      setAuditUserState({ identityVersion: auditIdentityVersion, users: {} })
      return undefined
    }

    setAuditUsersLoading(true)
    void loadAuditUsers(ids, auditIdentityVersion).then((users) => {
      if (active) setAuditUserState({ identityVersion: auditIdentityVersion, users })
    }).finally(() => {
      if (active) setAuditUsersLoading(false)
    })

    return () => { active = false }
  }, [auditIdentityVersion, auditUserIdsKey])

  const narrow = useNarrowScreen()
  const totalRows = table.getFilteredRowModel().rows.length
  const activeFilters = columnFilters.length
  const selectedRows = table.getSelectedRowModel().rows.map((row) => row.original)
  // 跨页选中的全部 id（rowSelection 的 key = getRowId）；服务端分页时含非当前页选中，
  // 用于「已选 N」与 bulkActions 第 3 参数（按 id 跨页批量）。selectedRows 仍只当前页行对象。
  const selectedIds = Object.keys(rowSelection).filter((id) => rowSelection[id])
  const selectedCount = selectedIds.length
  const useCompactTableHeight = !disableCompactHeight && pageCount <= 1 && pageRows.length > 0 && pageRows.length <= 8

  const { handleExport, handleImportFile } = useTableExport({ table, exportFileName, onExportAll, onServerExport, onImportRows })

  if (!hydrated) {
    return (
      <DataTableSkeleton
        tableColumns={tableColumns}
        defaultVisibility={defaultVisibility}
        dataLength={data.length}
      />
    )
  }

  return (
    <div className="flex h-full min-h-0 flex-1 flex-col gap-3">
      {/* Toolbar */}
      <DataTableToolbar
        table={table}
        searchPlaceholder={effectiveSearchPlaceholder}
        enableSearch={enableSearch}
        globalFilter={globalFilter}
        onGlobalFilterChange={setGlobalFilter}
        selectedRows={selectedRows}
        selectedIds={selectedIds}
        selectedCount={selectedCount}
        bulkActions={bulkActions}
        clearSelection={() => updateRowSelection({})}
        toolbarExtra={toolbarExtra}
        compactActions={compactToolbarActions}
        showImport={!!onImportRows && canImport}
        fileInputRef={fileInputRef}
        onImportFile={handleImportFile}
        showExport={enableExport && (canExport || !requireExportPermission)}
        onExport={() => void handleExport()}
        showFilterToggle={!manualPagination}
        filtersActive={showFilters}
        onToggleFilters={() => { setShowFilters(v => !v); if (showFilters) setColumnFilters([]) }}
        activeFilters={activeFilters}
        onResetView={onResetView && (() => {
          setGlobalFilter('') // 搜索框受本组件内部 state 驱动，须同步清空（防抖 emit 与父侧清空同值幂等）
          onResetView()
        })}
        viewDirty={viewDirty}
        onReload={onReload}
        loading={loading}
        columnSettings={{
          open: columnSettingsOpen,
          onOpenChange: setColumnSettingsOpen,
          pinned,
          onTogglePinned: togglePinned,
          onReorder: reorderColumns,
          onRestore: restoreColumns,
          dirty: columnsDirty,
          savedViews,
        }}
      />

      {narrow ? (
        <DataTableCards
          table={table}
          isFiltered={!!globalFilter || activeFilters > 0}
          loading={loading}
          emptyState={emptyState}
        />
      ) : (
        <DataTableGrid
          table={table}
          tableColumns={tableColumns}
          onColumnReorder={(next) => {
            setColumnOrder(next)
            saveOrder(effectiveTableKey, next)
            scheduleCloudTableViewSync(storagePrincipalId, effectiveTableKey)
          }}
          showFilters={showFilters}
          loading={loading}
          compact={useCompactTableHeight}
          isFiltered={!!globalFilter || activeFilters > 0}
          onRestoreColumns={restoreColumns}
          columnsDirty={columnsDirty}
          pinned={pinned}
          onTogglePinned={togglePinned}
          onOpenColumnSettings={openColumnSettings}
          density={density}
          emptyState={emptyState}
          // 列筛选只在前端分页的表上有（与工具栏「筛选」开关同一条件）。
          onShowFilters={manualPagination ? undefined : () => setShowFilters(true)}
        />
      )}

      {/* Pagination */}
      <DataTablePagination
        table={table}
        totalLabel={manualPagination ? (rowCount ?? 0) : totalRows}
        filteredFromLabel={!manualPagination && (globalFilter || activeFilters > 0) && data.length !== totalRows ? data.length : null}
        pageIndex={currentPageIndex}
        pageSize={currentPageSize}
        pageCount={pageCount}
        manualPagination={manualPagination}
        maxPageSize={maxPageSize}
        tableKey={effectiveTableKey}
        countCap={countCap}
      />
    </div>
  )
}
