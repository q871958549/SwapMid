'use client'

import { type ComponentType, type ReactNode } from 'react'
import { type Column, type Table } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import {
  ArrowDown, ArrowUp, ArrowUpDown, Columns3, EllipsisVertical, EyeOff, Filter, FilterX, Pin, PinOff, Settings2,
} from 'lucide-react'
import {
  ContextMenuCheckboxItem, ContextMenuContent, ContextMenuItem, ContextMenuLabel, ContextMenuSeparator,
} from '@/components/ui/context-menu'
import {
  DropdownMenu, DropdownMenuCheckboxItem, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel,
  DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { IconButton } from '@/components/ui/icon-button'
import { useAfterMenuClose } from '@/components/ui/use-after-menu-close'
import { cn } from '@/lib/utils'
import { applySortState, columnOffersSorting } from './data-table-column-header'
import { resolveColumnTitle } from './data-table-columns'
import { STRUCTURAL_COL_IDS } from './use-table-persistence'
import type { SortState } from './numeric-column'

interface MenuKit {
  Item: ComponentType<{ disabled?: boolean; onSelect?: (event: Event) => void; children: ReactNode }>
  CheckboxItem: ComponentType<{ checked?: boolean; disabled?: boolean; onSelect?: (event: Event) => void; children: ReactNode }>
  Label: ComponentType<{ className?: string; children: ReactNode }>
  Separator: ComponentType
}

// 右键菜单与「⋮」下拉渲染同一份内容，只是 Radix 原语不同。
const CONTEXT_KIT: MenuKit = {
  Item: ContextMenuItem, CheckboxItem: ContextMenuCheckboxItem, Label: ContextMenuLabel, Separator: ContextMenuSeparator,
}
const DROPDOWN_KIT: MenuKit = {
  Item: DropdownMenuItem, CheckboxItem: DropdownMenuCheckboxItem, Label: DropdownMenuLabel, Separator: DropdownMenuSeparator,
}

interface ColumnMenuOptions<TData> {
  column: Column<TData, unknown>
  table: Table<TData>
  /** 列显隐恢复到页面默认（审计列、`defaultHidden` 列保持隐藏）。 */
  onRestoreColumns: () => void
  /** 当前列显隐是否已偏离默认。 */
  columnsDirty: boolean
  /** 固定状态：page = 页面钉的（不可取消），user = 用户固定的，none = 未固定。 */
  pin: 'page' | 'user' | 'none'
  onTogglePinned: () => void
  onOpenColumnSettings: () => void
  /** 列筛选；表格不支持列筛选时不传，菜单不出筛选项。 */
  filter?: {
    /** 这一列当前有生效的筛选。 */
    active: boolean
    /** 打开筛选行。 */
    onOpen: () => void
    /** 聚焦筛选行里这一列的输入框（菜单关闭、筛选行渲染出来之后调用）。 */
    onFocus: () => void
    onClear: () => void
  }
}

/** 有列菜单的列：除选择列与操作列外的所有列，含不可排序列与纯文本表头。 */
export function hasColumnMenu(columnId: string) {
  return !STRUCTURAL_COL_IDS.has(columnId)
}

// 「筛选此列」要把焦点交给筛选框、「列设置…」要打开面板，都得等菜单关好，见 useAfterMenuClose。
function ColumnMenuItems<TData>({ column, table, onRestoreColumns, columnsDirty, pin, onTogglePinned, onOpenColumnSettings, filter, afterClose, kit }: ColumnMenuOptions<TData> & { afterClose: (action: () => void) => void; kit: MenuKit }) {
  const t = useTranslations('Common')
  const { Item, CheckboxItem, Label, Separator } = kit
  const sorted: SortState = column.getIsSorted()
  // 至少留一个数据列可见，否则表格只剩选择列和操作列。
  const visibleDataColumns = table.getVisibleLeafColumns().filter((col) => hasColumnMenu(col.id)).length
  const canHide = column.getCanHide() && visibleDataColumns > 1

  const sortItem = (state: SortState, icon: ReactNode, label: string) => (
    <CheckboxItem checked={sorted === state} onSelect={() => applySortState(column, state)}>
      {icon}
      {label}
    </CheckboxItem>
  )

  return (
    <>
      <Label className="max-w-56 truncate">{resolveColumnTitle(column, table, t)}</Label>
      <Separator />
      {columnOffersSorting(column, table) ? (
        <>
          {sortItem(false, <ArrowUpDown className="mr-2 size-3.5 text-muted-foreground" />, t('sortDefault'))}
          {sortItem('asc', <ArrowUp className="mr-2 size-3.5 text-muted-foreground" />, t('ascending'))}
          {sortItem('desc', <ArrowDown className="mr-2 size-3.5 text-muted-foreground" />, t('descending'))}
        </>
      ) : (
        <Item disabled>
          <ArrowUpDown className="size-3.5 text-muted-foreground" />
          {t('sortNotSupported')}
        </Item>
      )}
      <Separator />
      {filter && (
        <>
          <Item onSelect={() => { filter.onOpen(); afterClose(filter.onFocus) }}>
            <Filter className="size-3.5 text-muted-foreground" />
            {t('filterThisColumn')}
          </Item>
          {filter.active && (
            <Item onSelect={filter.onClear}>
              <FilterX className="size-3.5 text-muted-foreground" />
              {t('clearColumnFilter')}
            </Item>
          )}
          <Separator />
        </>
      )}
      <Item disabled={!canHide} onSelect={() => column.toggleVisibility(false)}>
        <EyeOff className="size-3.5 text-muted-foreground" />
        {t('hideColumn')}
      </Item>
      <Item disabled={pin === 'page'} onSelect={onTogglePinned}>
        {pin === 'none'
          ? <><Pin className="size-3.5 text-muted-foreground" />{t('pinLeft')}</>
          : <><PinOff className="size-3.5 text-muted-foreground" />{t(pin === 'page' ? 'pinnedByPage' : 'unpin')}</>}
      </Item>
      <Separator />
      <Item onSelect={() => afterClose(onOpenColumnSettings)}>
        <Settings2 className="size-3.5 text-muted-foreground" />
        {t('columnSettingsEllipsis')}
      </Item>
      <Item disabled={!columnsDirty} onSelect={onRestoreColumns}>
        <Columns3 className="size-3.5 text-muted-foreground" />
        {t('restoreDefaultColumns')}
      </Item>
    </>
  )
}

/** 右键表头弹出的列菜单内容；外层 `ContextMenu` / `ContextMenuTrigger` 由网格包在 `TableHead` 上。 */
export function DataTableColumnContextMenu<TData>(props: ColumnMenuOptions<TData>) {
  const afterClose = useAfterMenuClose()
  return (
    <ContextMenuContent className="min-w-44" onCloseAutoFocus={afterClose.onCloseAutoFocus}>
      <ColumnMenuItems {...props} afterClose={afterClose.schedule} kit={CONTEXT_KIT} />
    </ContextMenuContent>
  )
}

/**
 * 悬停表头时在右侧淡入的「⋮」，点开与右键同一份菜单。
 * 给触屏和不知道可以右键的用户一个看得见的入口。
 *
 * 对辅助技术隐藏、不进 Tab 顺序：否则列头的无障碍名会变成「单号 列菜单」，每列都多念一遍。
 * 键盘用户聚焦排序按钮后按 Shift+F10 / 菜单键，触发同一份右键菜单。
 */
export function DataTableColumnMenuButton<TData>({ className, ...props }: ColumnMenuOptions<TData> & { className?: string }) {
  const t = useTranslations('Common')
  const afterClose = useAfterMenuClose()
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <IconButton
          label={t('columnMenu')}
          className={cn(
            'size-6 text-muted-foreground opacity-0 transition-opacity duration-150',
            'group-hover/head:opacity-100 group-focus-within/head:opacity-100 data-[state=open]:opacity-100',
            '[@media(pointer:coarse)]:opacity-60',
            className,
          )}
          aria-hidden
          tabIndex={-1}
          // 拖动从按钮上开始时不触发列重排。
          draggable={false}
          onDragStart={(event) => event.preventDefault()}
        >
          <EllipsisVertical className="size-3.5" />
        </IconButton>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-44" onCloseAutoFocus={afterClose.onCloseAutoFocus}>
        <ColumnMenuItems {...props} afterClose={afterClose.schedule} kit={DROPDOWN_KIT} />
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
