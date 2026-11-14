'use client'

/**
 * 列工厂——把列表页里逐页重复、纯样板的列定义收成一行声明。
 *
 * 背景：每个标准 CRUD 页都手写一段几乎逐字相同的「操作列」（DropdownMenu + 编辑/删除）
 * 与「布尔状态列」（success/secondary Badge + 下拉筛选），三页对比下来差异只是手写抖动
 * （size 48/50、separator 有无、`variant="destructive"` vs `className="text-destructive"`）。
 * 收敛成工厂后，新页一行声明即可，既减样板又消除这些无意义的不一致。
 *
 * 这些工厂与 `data-table-columns.tsx`（DataTable 内部的标题解析/筛选推导）正交：
 * 那个是「渲染时怎么取列中文名」，这个是「业务页怎么少写列」。
 *
 * 数字列（`numberColumn` / `moneyColumn` / `percentColumn`）是首选写法：列定义自己声明数字，
 * `numeric-column.ts` 的标题推断只作兜底——「在途」「在库样品」这类标题推断不出。
 *
 * 逃生舱：工厂只覆盖高频的几类列。业务列（自定义 cell）、特殊操作仍照常手写；
 * 操作列的非编辑/删除项用 `extraItems` 注入（启停、查看详情等）。
 */

import { type MouseEvent, type ReactNode } from 'react'
import { type ColumnDef } from '@tanstack/react-table'
import { type VariantProps } from 'class-variance-authority'
import { useTranslations } from 'next-intl'
import { MoreHorizontal, Pencil, Trash2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import { IconButton } from '@/components/ui/icon-button'
import { Badge, badgeVariants } from '@/components/ui/badge'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem,
  DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { dataTableLinkClass } from '@/components/data-table/data-table-link-cell'

type BadgeVariant = NonNullable<VariantProps<typeof badgeVariants>['variant']>

interface ActionsColumnOptions<T> {
  /** 传入则渲染「编辑」项（通常是 `crud.openEdit`）。 */
  onEdit?: (row: T) => void
  /** 传入则渲染「删除」项（通常是 `crud.askDelete`）。危险项，自动与上方分隔。 */
  onDelete?: (row: T) => void
  /**
   * 额外菜单项，渲染在「编辑」与「删除」之间——返回 `<DropdownMenuItem>`（启停、查看详情、
   * 复制等页面特有操作）。返回 falsy 则不渲染。
   */
  extraItems?: (row: T) => ReactNode
  editLabel?: string
  deleteLabel?: string
  /** 列宽，默认 48（仅图标按钮）。带额外项时可适当加大。 */
  size?: number
}

/**
 * 标准「操作列」——右对齐的 `⋯` 下拉菜单，含编辑/删除/额外项。
 *
 * ```tsx
 * const columns = [
 *   // ...业务列
 *   actionsColumn<ApiGroup>({ onEdit: crud.openEdit, onDelete: crud.askDelete }),
 * ]
 * // 带额外项（如启停）：
 * actionsColumn<Module>({
 *   onEdit: crud.openEdit,
 *   onDelete: crud.askDelete,
 *   extraItems: (row) => (
 *     <DropdownMenuItem onClick={() => toggle(row)}>
 *       <Power className="mr-2 size-3.5" />{row.isEnabled ? '停用' : '启用'}
 *     </DropdownMenuItem>
 *   ),
 * })
 * ```
 * 生成的列 `id='actions'`、`enableHiding:false`，中文名「操作」由 `data-table-columns` 兜底注册。
 *
 * `editLabel`/`deleteLabel`/触发按钮的可访问名默认经 `useTranslations('Common')` 求值
 * （`edit`/`delete`/`actions` 三个 key），随语言切换即时生效；调用点显式传入自定义标签
 * （业务文案）时原样透传、不再翻译。
 */
export function actionsColumn<T>({
  onEdit,
  onDelete,
  extraItems,
  editLabel,
  deleteLabel,
  size = 48,
}: ActionsColumnOptions<T>): ColumnDef<T, unknown> {
  return {
    id: 'actions',
    size,
    enableHiding: false,
    cell: ({ row }) => (
      <ActionsCell
        item={row.original}
        onEdit={onEdit}
        onDelete={onDelete}
        extraItems={extraItems}
        editLabel={editLabel}
        deleteLabel={deleteLabel}
      />
    ),
  }
}

interface ActionsCellProps<T> {
  item: T
  onEdit?: (row: T) => void
  onDelete?: (row: T) => void
  extraItems?: (row: T) => ReactNode
  editLabel?: string
  deleteLabel?: string
}

/**
 * 独立小组件而非在 cell 工厂函数体内直接调用 hook：`cell` 本身只是普通函数、经
 * `flexRender` 才被当组件渲染，但列工厂单测（column-factories.test.tsx）会绕过
 * flexRender 直接 `cell(ctx)` 后再 `render()`——hook 必须在真正的组件里才合法。
 */
function ActionsCell<T>({ item, onEdit, onDelete, extraItems, editLabel, deleteLabel }: ActionsCellProps<T>) {
  const t = useTranslations('Common')
  const resolvedEditLabel = editLabel ?? t('edit')
  const resolvedDeleteLabel = deleteLabel ?? t('delete')
  const extra = extraItems?.(item)
  const hasUpper = !!onEdit || !!extra
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <IconButton label={t('actions')} className="size-7">
          <MoreHorizontal className="size-4" />
        </IconButton>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {onEdit && (
          <DropdownMenuItem onClick={() => onEdit(item)}>
            <Pencil className="mr-2 size-3.5" />{resolvedEditLabel}
          </DropdownMenuItem>
        )}
        {extra}
        {onDelete && hasUpper && <DropdownMenuSeparator />}
        {onDelete && (
          <DropdownMenuItem variant="destructive" onClick={() => onDelete(item)}>
            <Trash2 className="mr-2 size-3.5" />{resolvedDeleteLabel}
          </DropdownMenuItem>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

interface PrimaryColumnOptions<T> {
  /**
   * 点击主字段时打开该行的主视图：纯 CRUD 页传 `crud.openEdit`，有详情视图的状态机页传打开详情。
   * **不传 = 渲染纯文本、不可点**——权限门控就按这个来（无编辑权的账号传 `undefined`，
   * 与 `actionsColumn` 不传 `onEdit` 同惯例），不要另造 disabled 态。
   */
  onOpen?: (row: T) => void
  /**
   * 同一列里只有部分行能打开（如考核单仅在可录分时）。返回 false 的行渲染纯文本。
   * 不传 = 只要有 onOpen，每一行都可点。
   */
  isOpenable?: (row: T) => boolean
  /** 自定义显示内容（默认渲染 `accessorKey` 的原始值）。 */
  format?: (row: T) => ReactNode
  size?: number
  /** 附加在文本上的类名（如编码列的 `font-mono`）。 */
  className?: string
  /** 是否允许点击列头排序（默认 true；服务端未列入排序白名单时传 false）。 */
  sortable?: boolean
}

/**
 * 「主字段列」——列表里代表「这行叫什么」的那一列（名称；无名称实体用编码），
 * 渲染成可点击文本，点击打开该行主视图。
 *
 * 解决的问题：编辑入口此前只有行尾 `⋯ → 编辑` 一条路径，动线是「视线移到行尾 → 点 ⋯ → 点编辑」三步；
 * 而用户的直觉是「点这行的名字」。收成工厂后样式全站统一，不必每页各写一套 hover 下划线。
 *
 * ```tsx
 * primaryColumn<Position>('name', '岗位名称', { onOpen: crud.openEdit })
 * primaryColumn<CodeRule>('code', '编码', { onOpen: canEdit ? crud.openEdit : undefined, className: 'font-mono' })
 * ```
 *
 * **每个列表只设一个主视图**（编辑框或详情）。编码与名称作为同一行身份可以都可点，
 * 但必须打开同一个视图——不要挂到第三列，也不要一个进编辑、另一个进详情。
 * 行双击 / 整行单击进编辑是**明确否决**的：双击与「双击选词复制」冲突、整行热区吞掉单元格拖选，
 * 都与「表格内容可选中复制」的既有铁律正面冲突。入口只有身份列 + 操作列。
 */
export function primaryColumn<T>(
  accessorKey: string,
  title: string,
  { onOpen, isOpenable, format, size = 160, className, sortable = true }: PrimaryColumnOptions<T> = {},
): ColumnDef<T, unknown> {
  return {
    accessorKey,
    size,
    ...(sortable ? {} : { enableSorting: false }),
    header: ({ column }) => <DataTableColumnHeader column={column} title={title} />,
    cell: ({ row }) => {
      const canOpen = !!onOpen && (isOpenable?.(row.original) ?? true)
      return (
        <PrimaryCell
          item={row.original}
          content={format ? format(row.original) : (row.getValue(accessorKey) as ReactNode)}
          onOpen={canOpen ? onOpen : undefined}
          className={className}
        />
      )
    },
  }
}

interface PrimaryCellProps<T> {
  item: T
  content: ReactNode
  onOpen?: (row: T) => void
  className?: string
}

function PrimaryCell<T>({ item, content, onOpen, className }: PrimaryCellProps<T>) {
  if (!onOpen) {
    return <span className={cn('cursor-default font-medium', className)}>{content}</span>
  }

  // 拖选刚结束时浏览器仍会补发一次 click——不拦的话「选中工号复制」就变成「打开编辑框」，
  // 直接违反表格内容可选中复制的铁律。判据：本次点击结束时若存在非折叠的文本选区，视为选中操作、不打开。
  // （普通点击的 mousedown 会先清空选区，故这里恒为折叠态，不会误伤真实点击。）
  const handleClick = (e: MouseEvent<HTMLButtonElement>) => {
    const selection = window.getSelection()
    if (selection && !selection.isCollapsed && selection.toString().trim().length > 0) return
    e.stopPropagation()
    onOpen(item)
  }

  return (
    <button
      type="button"
      onClick={handleClick}
      className={cn(
        'select-text text-left font-medium',
        dataTableLinkClass,
        className,
      )}
    >
      {content}
    </button>
  )
}

interface BooleanBadgeColumnOptions<T> {
  /** `true` 时的标签（如「上线」「生效」「启用」）。 */
  trueLabel: string
  /** `false` 时的标签（如「下线」「停用」）。 */
  falseLabel: string
  trueVariant?: BadgeVariant
  falseVariant?: BadgeVariant
  size?: number
  /**
   * 是否加状态下拉筛选（`meta.filterVariant='select'` + true/false 选项），默认 `true`。
   * 服务端分页页若筛选下推后端、不走前端列筛选，可传 `false`。
   */
  filterable?: boolean
  /** 列默认隐藏（如内部平台低价值的 isPublic），列显隐菜单仍可手动开启，默认 `false`。 */
  defaultHidden?: boolean
  /**
   * 传入则 Badge 可点击切换布尔状态（行内启停），不必进编辑框。
   * 不传 = 只读展示。工作流/多态枚举状态不要走这里。
   */
  onToggle?: (row: T, next: boolean) => void
  /** 某行切换中或无权限时禁用点击。 */
  isToggleDisabled?: (row: T) => boolean
}

/**
 * 布尔「状态列」——`true`/`false` 渲染成两色 Badge，默认带状态下拉筛选。
 *
 * ```tsx
 * booleanBadgeColumn<Module>('isEnabled', '状态', { trueLabel: '上线', falseLabel: '下线' })
 * booleanBadgeColumn<Version>('enabled', '是否生效', { trueLabel: '生效', falseLabel: '停用', onToggle })
 * ```
 * 默认 `true→success`（绿）/`false→secondary`（灰）；用 `trueVariant`/`falseVariant` 覆盖。
 * 传 `onToggle` 后点击 Badge 即切换，调用方负责写接口 + toast + 刷新。
 */
export function booleanBadgeColumn<T>(
  accessorKey: string,
  title: string,
  {
    trueLabel,
    falseLabel,
    trueVariant = 'success',
    falseVariant = 'secondary',
    size = 80,
    filterable = true,
    defaultHidden = false,
    onToggle,
    isToggleDisabled,
  }: BooleanBadgeColumnOptions<T>,
): ColumnDef<T, unknown> {
  const meta = {
    ...(filterable
      ? {
          filterVariant: 'select' as const,
          filterOptions: [
            { value: true, label: trueLabel },
            { value: false, label: falseLabel },
          ],
        }
      : {}),
    ...(defaultHidden ? { defaultHidden: true } : {}),
  }
  return {
    accessorKey,
    size,
    header: ({ column }) => <DataTableColumnHeader column={column} title={title} />,
    ...(Object.keys(meta).length > 0 ? { meta } : {}),
    cell: ({ row }) => (
      <BooleanBadgeCell
        item={row.original}
        value={row.getValue<boolean>(accessorKey)}
        trueLabel={trueLabel}
        falseLabel={falseLabel}
        trueVariant={trueVariant}
        falseVariant={falseVariant}
        onToggle={onToggle}
        disabled={isToggleDisabled?.(row.original) ?? false}
      />
    ),
  }
}

function BooleanBadgeCell<T>({
  item,
  value,
  trueLabel,
  falseLabel,
  trueVariant,
  falseVariant,
  onToggle,
  disabled,
}: {
  item: T
  value: boolean
  trueLabel: string
  falseLabel: string
  trueVariant: BadgeVariant
  falseVariant: BadgeVariant
  onToggle?: (row: T, next: boolean) => void
  disabled: boolean
}) {
  const t = useTranslations('Common')
  const badge = <Badge variant={value ? trueVariant : falseVariant}>{value ? trueLabel : falseLabel}</Badge>
  if (!onToggle) return badge

  const nextLabel = value ? falseLabel : trueLabel
  return (
    <button
      type="button"
      disabled={disabled}
      aria-label={t('toggleStatusTo', { label: nextLabel })}
      title={t('toggleStatusTo', { label: nextLabel })}
      onClick={() => onToggle(item, !value)}
      className="cursor-pointer rounded-md disabled:cursor-not-allowed disabled:opacity-60 hover:opacity-80 focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color) focus-visible:outline-none"
    >
      <span className="pointer-events-none">{badge}</span>
    </button>
  )
}

interface NumberFormatOptions {
  /** 固定小数位（补零），金额用 2。不传则按 `maxFractionDigits` 截断、不补零。 */
  fractionDigits?: number
  /** 最多小数位（不补零），默认 2。数量类原样保留精度时调大（高精度计量常到 4–8 位）。 */
  maxFractionDigits?: number
  /** 最少小数位（补零），与 `maxFractionDigits` 合用：金额至少两位、原样保留上游小数高精度尾数。 */
  minFractionDigits?: number
}

/** 空值占位，与各页手写的「—」一致。 */
const EMPTY_NUMBER = '—'

/**
 * 千分位 + 小数位。null / undefined / NaN 显示「—」。
 * locale 固定 zh-CN：平台界面固定简体，千分位、小数点与英文相同，不随语言切换跳动。
 * 舍入后为零的负数不带负号：「合计 − 半成品 − 成品」这类差值常留 -1e-10 的浮点残差，否则会显示「-0.00」。
 */
export function formatNumber(
  value: number | null | undefined,
  { fractionDigits, maxFractionDigits = 2, minFractionDigits }: NumberFormatOptions = {},
): string {
  if (value == null || Number.isNaN(value)) return EMPTY_NUMBER
  return value.toLocaleString('zh-CN', {
    signDisplay: 'negative',
    ...(fractionDigits === undefined
      ? { minimumFractionDigits: minFractionDigits, maximumFractionDigits: maxFractionDigits }
      : { minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits }),
  })
}

interface PercentFormatOptions {
  /** 小数位，默认 2。 */
  fractionDigits?: number
  /** 值是比率（0.123 → 12.30%），默认 `true`。后端已乘过 100 的传 `false`。 */
  ratio?: boolean
}

/** 百分比。null / undefined / NaN 显示「—」；与 `formatNumber` 一样，舍入后为零的负数不带负号。 */
export function formatPercent(
  value: number | null | undefined,
  { fractionDigits = 2, ratio = true }: PercentFormatOptions = {},
): string {
  if (value == null || Number.isNaN(value)) return EMPTY_NUMBER
  const text = (ratio ? value * 100 : value).toFixed(fractionDigits)
  return `${Number(text) === 0 ? text.replace('-', '') : text}%`
}

interface NumericColumnOptions<T> {
  size?: number
  /** 显示用的派生值（如「其他 = 合计 − 半成品 − 成品」）。不传取 `accessorKey` 的原始值；排序仍按原始值。 */
  value?: (row: T) => number | null | undefined
  /** 附加在数字上的类名。传函数可按行着色：超阈值标红、正负分色。 */
  className?: string | ((row: T) => string | undefined)
  /**
   * 表头可点排序，默认 `true`。服务端排序白名单不认这个字段时传 `false`，
   * 表头渲染成不可点的列名（与手写 `enableSorting: false` 相同）。
   */
  sortable?: boolean
  /** 列默认隐藏，列显隐菜单仍可开启。 */
  defaultHidden?: boolean
}

/**
 * 三个数字列工厂共用的骨架：显式 `meta.numeric: true`（表头与单元格同时右对齐，不靠标题推断）、
 * 单元格 `tabular-nums`、按 `format` 出文本。
 */
function numericColumn<T>(
  accessorKey: string,
  title: string,
  format: (value: number | null | undefined) => string,
  { size = 100, value, className, sortable = true, defaultHidden = false }: NumericColumnOptions<T>,
): ColumnDef<T, unknown> {
  return {
    accessorKey,
    size,
    ...(sortable ? {} : { enableSorting: false }),
    header: ({ column }) => <DataTableColumnHeader column={column} title={title} />,
    meta: { numeric: true, ...(defaultHidden ? { defaultHidden: true } : {}) },
    cell: ({ row }) => {
      const v = value ? value(row.original) : row.getValue<number | null | undefined>(accessorKey)
      const extra = typeof className === 'function' ? className(row.original) : className
      return <span className={cn('tabular-nums', extra)}>{format(v)}</span>
    },
  }
}

/**
 * 「数字列」——数量、件数、天数、工时等：千分位，默认最多 2 位小数、不补零。
 *
 * ```tsx
 * numberColumn<Row>('openQty', t('colOpenQty'), { maxFractionDigits: 4 })
 * numberColumn<Row>('hours', t('colHours'), { fractionDigits: 2 })   // 工时固定两位
 * ```
 */
export function numberColumn<T>(
  accessorKey: string,
  title: string,
  { fractionDigits, maxFractionDigits, minFractionDigits, ...options }: NumericColumnOptions<T> & NumberFormatOptions = {},
): ColumnDef<T, unknown> {
  return numericColumn(accessorKey, title, (v) => formatNumber(v, { fractionDigits, maxFractionDigits, minFractionDigits }), options)
}

/**
 * 「金额列」——千分位，固定 2 位小数（`fractionDigits` 可改，如单价到 4 位）。
 *
 * ```tsx
 * moneyColumn<Row>('amount', t('colAmount'), { size: 110 })
 * moneyColumn<Row>('profit', t('colProfit'), { className: (r) => (r.profit < 0 ? 'text-destructive' : undefined) })
 * ```
 */
export function moneyColumn<T>(
  accessorKey: string,
  title: string,
  { fractionDigits = 2, ...options }: NumericColumnOptions<T> & Pick<NumberFormatOptions, 'fractionDigits'> = {},
): ColumnDef<T, unknown> {
  return numericColumn(accessorKey, title, (v) => formatNumber(v, { fractionDigits }), options)
}

/**
 * 「百分比列」——默认值是比率（0.123 → 12.30%），固定 2 位小数。
 *
 * ```tsx
 * percentColumn<Row>('grossMargin', t('colGrossMargin'))
 * percentColumn<Row>('rate', t('colRate'), { ratio: false })   // 后端已乘 100
 * ```
 */
export function percentColumn<T>(
  accessorKey: string,
  title: string,
  { fractionDigits, ratio, ...options }: NumericColumnOptions<T> & PercentFormatOptions = {},
): ColumnDef<T, unknown> {
  return numericColumn(accessorKey, title, (v) => formatPercent(v, { fractionDigits, ratio }), options)
}
