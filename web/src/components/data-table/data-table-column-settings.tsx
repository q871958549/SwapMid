'use client'

import { useRef, useState } from 'react'
import { type Column, type Table } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import { GripVertical, Pin, PinOff, Search } from 'lucide-react'
import { MotionConfig, Reorder, useDragControls } from 'framer-motion'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { IconButton } from '@/components/ui/icon-button'
import { Input } from '@/components/ui/input'
import { SegmentedControl } from '@/components/ui/segmented-control'
import { cn } from '@/lib/utils'
import { resolveColumnTitle } from './data-table-columns'
import { DataTableSavedViews } from './data-table-saved-views'
import { useTableDensity } from './use-table-density'
import { STRUCTURAL_COL_IDS, type TableDensity } from './use-table-persistence'
import type { TableSavedViewsController } from './use-table-saved-views'

/** 工具栏传给列设置面板的状态与动作（由 DataTable 持有）。 */
export interface ColumnSettingsState {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** 用户固定到左侧的列（按固定先后）。 */
  pinned: string[]
  onTogglePinned: (columnId: string) => void
  /** 拖完一次：可移动数据列的新顺序。 */
  onReorder: (movable: string[]) => void
  onRestore: () => void
  dirty: boolean
  /** 单表命名视图预设控制器（列设置顶部渐进展示，工具栏按钮增量 = 0）。 */
  savedViews?: TableSavedViewsController
}

/** 列多于这个数才显示搜索框。 */
const SEARCH_THRESHOLD = 8

interface RowProps<TData> {
  column: Column<TData, unknown>
  title: string
  /** 至少留一个数据列可见。 */
  lastVisible: boolean
  pin: 'page' | 'user' | 'none'
  onTogglePinned: () => void
}

function RowBody<TData>({ column, title, lastVisible, pin, onTogglePinned }: RowProps<TData>) {
  const t = useTranslations('Common')
  const visible = column.getIsVisible()
  return (
    <>
      <Checkbox
        checked={visible}
        disabled={!column.getCanHide() || (visible && lastVisible)}
        onCheckedChange={(value) => column.toggleVisibility(value === true)}
        aria-label={title}
      />
      <span className={cn('min-w-0 flex-1 truncate text-sm', !visible && 'text-muted-foreground')}>{title}</span>
      <IconButton
        label={pin === 'page' ? t('pinnedByPage') : pin === 'user' ? t('unpin') : t('pinLeft')}
        aria-pressed={pin !== 'none'}
        disabled={pin === 'page'}
        onClick={onTogglePinned}
        className={cn('size-7 text-muted-foreground', pin === 'none' && 'opacity-0 group-hover/col:opacity-100 focus-visible:opacity-100', pin !== 'none' && 'text-foreground')}
      >
        {pin === 'user' ? <PinOff className="size-3.5" /> : <Pin className="size-3.5" />}
      </IconButton>
    </>
  )
}

function DraggableRow<TData>({ id, onDragEnd, ...props }: RowProps<TData> & {
  id: string
  onDragEnd: () => void
}) {
  const t = useTranslations('Common')
  // 只认把手发起拖拽：整行可拖会和勾选、固定按钮抢手势。
  const controls = useDragControls()
  return (
    <Reorder.Item
      as="li"
      value={id}
      dragListener={false}
      dragControls={controls}
      // 不设 dragConstraints：面板在 Radix 弹层里，弹层靠 transform 定位，motion 量出来的约束框会错位，
      // 拖起即被甩到上千像素外（2026-11-06 e2e 实测 translateY 1006px）。列表本身在滚动容器里，越界会被裁掉，影响不大。
      onDragEnd={onDragEnd}
      // zIndex 必须写进 whileDrag（Reorder.Item 内联 style 会盖过 className 的 z-*）。
      whileDrag={{ zIndex: 30, boxShadow: 'var(--shadow-md)', cursor: 'grabbing' }}
      className="group/col flex items-center gap-2 rounded-md bg-popover px-1.5 py-1 hover:bg-accent"
    >
      <span
        aria-hidden
        title={t('dragToReorder')}
        onPointerDown={(e) => controls.start(e)}
        className="cursor-grab touch-none rounded p-0.5 text-muted-foreground/60 hover:text-foreground"
      >
        <GripVertical className="size-3.5" />
      </span>
      <RowBody {...props} />
    </Reorder.Item>
  )
}

function StaticRow<TData>(props: RowProps<TData>) {
  return (
    <li className="group/col flex items-center gap-2 rounded-md px-1.5 py-1 hover:bg-accent">
      {/* 与可拖行的把手对齐。 */}
      <span aria-hidden className="w-4.5 shrink-0" />
      <RowBody {...props} />
    </li>
  )
}

/**
 * 列设置面板（按钮与表头蓝图 §3.4）：搜索、勾选显隐、拖把手排序、固定到左侧、恢复默认；底部是行密度。改动即时生效。
 * 固定的列单独在上方，不参与拖动；搜索时只筛选、不能拖。
 */
export function DataTableColumnSettings<TData>({ table, settings }: { table: Table<TData>; settings: ColumnSettingsState }) {
  const t = useTranslations('Common')
  const [query, setQuery] = useState('')
  const [density, setDensity] = useTableDensity()

  const columns = table.getAllLeafColumns().filter((column) => !STRUCTURAL_COL_IDS.has(column.id))
  const order = table.getState().columnOrder
  const ordered = [...columns].sort((a, b) => order.indexOf(a.id) - order.indexOf(b.id))
  const pinOf = (column: Column<TData, unknown>) =>
    column.columnDef.meta?.pin === 'left' ? 'page' as const : settings.pinned.includes(column.id) ? 'user' as const : 'none' as const
  const pinnedColumns = ordered.filter((column) => pinOf(column) !== 'none')
  const movableColumns = ordered.filter((column) => pinOf(column) === 'none')
  const lastVisible = columns.filter((column) => column.getIsVisible()).length <= 1
  const titleOf = (column: Column<TData, unknown>) => resolveColumnTitle(column, table, t)

  // 拖动中只改面板里的顺序，松手再落到表格与本地偏好（onReorder 是拖动中的高频回调）。
  const [draft, setDraft] = useState<string[] | null>(null)
  const draftRef = useRef<string[] | null>(null)
  const movableIds = draft ?? movableColumns.map((column) => column.id)
  const byId = new Map(columns.map((column) => [column.id, column]))

  const keyword = query.trim().toLowerCase()
  const matches = (column: Column<TData, unknown>) => !keyword || titleOf(column).toLowerCase().includes(keyword)
  const rowProps = (column: Column<TData, unknown>): RowProps<TData> => ({
    column,
    title: titleOf(column),
    lastVisible,
    pin: pinOf(column),
    onTogglePinned: () => settings.onTogglePinned(column.id),
  })

  return (
    <div className="flex flex-col gap-2">
      {settings.savedViews && <DataTableSavedViews controller={settings.savedViews} />}
      <div className="flex items-center justify-between gap-2 px-1">
        <span className="text-sm font-medium">{t('columnSettings')}</span>
        <Button variant="link" size="sm" className="h-auto px-0 text-muted-foreground" disabled={!settings.dirty} onClick={settings.onRestore}>
          {t('restoreDefaultColumns')}
        </Button>
      </div>
      {columns.length > SEARCH_THRESHOLD && (
        <div className="relative">
          <Search className="absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('searchColumns')} className="h-8 pl-8 text-sm" />
        </div>
      )}
      <div className="max-h-96 overflow-y-auto">
        {pinnedColumns.some(matches) && (
          <>
            <div className="px-1.5 pt-1 pb-0.5 text-xs text-muted-foreground">{t('pinnedSection')}</div>
            <ul>{pinnedColumns.filter(matches).map((column) => <StaticRow key={column.id} {...rowProps(column)} />)}</ul>
            <div className="mx-1.5 my-1 h-px bg-border" />
          </>
        )}
        {keyword ? (
          <ul>
            {movableColumns.filter(matches).map((column) => <StaticRow key={column.id} {...rowProps(column)} />)}
            {!columns.some(matches) && <li className="px-1.5 py-2 text-sm text-muted-foreground">{t('noMatchingColumns')}</li>}
          </ul>
        ) : (
          <MotionConfig reducedMotion="user">
            <Reorder.Group
              as="ul"
              axis="y"
              values={movableIds}
              onReorder={(next: string[]) => { draftRef.current = next; setDraft(next) }}
              className="relative"
            >
              {movableIds.map((id) => {
                const column = byId.get(id)
                if (!column) return null
                return (
                  <DraggableRow
                    key={id}
                    id={id}
                    onDragEnd={() => {
                      if (draftRef.current) settings.onReorder(draftRef.current)
                      draftRef.current = null
                      setDraft(null)
                    }}
                    {...rowProps(column)}
                  />
                )
              })}
            </Reorder.Group>
          </MotionConfig>
        )}
      </div>
      <div className="flex items-center justify-between gap-3 border-t px-1 pt-2">
        <div className="min-w-0">
          <div className="text-sm">{t('rowDensity')}</div>
          {/* 密度是个人习惯，所有列表共用一个设置；不归「恢复默认列」管。 */}
          <div className="text-xs text-muted-foreground">{t('densityAppliesToAll')}</div>
        </div>
        <SegmentedControl<TableDensity>
          aria-label={t('rowDensity')}
          value={density}
          onValueChange={setDensity}
          options={[
            { value: 'standard', label: t('densityStandard') },
            { value: 'compact', label: t('densityCompact') },
          ]}
        />
      </div>
    </div>
  )
}
