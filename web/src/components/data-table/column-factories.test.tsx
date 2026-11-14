import { describe, it, expect, vi } from 'vitest'
import { render as rtlRender, screen, fireEvent } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { type CellContext, type ColumnDef } from '@tanstack/react-table'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import {
  actionsColumn, booleanBadgeColumn, formatNumber, formatPercent,
  moneyColumn, numberColumn, percentColumn, primaryColumn,
} from './column-factories'
import { isNumericColumn } from './numeric-column'
import zhHansMessages from '../../../messages/zh-Hans.json'

// actionsColumn 的默认标签（编辑/删除/操作）经内部 ActionsCell 用 useTranslations('Common')
// 求值（i18n 批次3b）——组件树需要 NextIntlClientProvider 才能渲染。真实 zh-Hans messages 直供。
function render(ui: React.ReactElement) {
  return rtlRender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

interface Row { id: string; name: string; enabled: boolean }
const row: Row = { id: '1', name: '测试项', enabled: true }

// 直接渲染工厂列的 cell：构造最小 CellContext（只用到 row.original / row.getValue）。
function renderCell<T>(col: ColumnDef<T, unknown>, original: T) {
  const cell = col.cell
  if (typeof cell !== 'function') throw new Error('cell 不是函数')
  const ctx = {
    row: { original, getValue: (k: string) => (original as Record<string, unknown>)[k] },
  } as unknown as CellContext<T, unknown>
  return render(<>{cell(ctx)}</>)
}

// Radix DropdownMenu 的 Trigger 靠 onPointerDown 打开，jsdom 下 fireEvent.click 不触发；用 Enter 打开
// （与 data-table.test 一致）。单 cell 渲染时页面只有触发器一个 button。
function openMenu() {
  fireEvent.keyDown(screen.getByRole('button'), { key: 'Enter' })
}

describe('actionsColumn', () => {
  it('结构：id=actions、不可隐藏、默认宽 48', () => {
    const col = actionsColumn<Row>({})
    expect(col.id).toBe('actions')
    expect(col.enableHiding).toBe(false)
    expect(col.size).toBe(48)
  })

  it('size 可覆盖', () => {
    expect(actionsColumn<Row>({ size: 64 }).size).toBe(64)
  })

  it('点「编辑」调用 onEdit，并传入当前行', async () => {
    const onEdit = vi.fn()
    renderCell(actionsColumn<Row>({ onEdit }), row)
    openMenu()
    fireEvent.click(await screen.findByRole('menuitem', { name: '编辑' }))
    expect(onEdit).toHaveBeenCalledWith(row)
  })

  it('点「删除」调用 onDelete，并传入当前行', async () => {
    const onDelete = vi.fn()
    renderCell(actionsColumn<Row>({ onDelete }), row)
    openMenu()
    fireEvent.click(await screen.findByRole('menuitem', { name: '删除' }))
    expect(onDelete).toHaveBeenCalledWith(row)
  })

  it('未传 onEdit 时菜单无「编辑」项', async () => {
    renderCell(actionsColumn<Row>({ onDelete: vi.fn() }), row)
    openMenu()
    await screen.findByRole('menuitem', { name: '删除' })
    expect(screen.queryByRole('menuitem', { name: '编辑' })).not.toBeInTheDocument()
  })

  it('extraItems 注入的项渲染在菜单中', async () => {
    renderCell(
      actionsColumn<Row>({
        onEdit: vi.fn(),
        extraItems: () => <DropdownMenuItem>停用</DropdownMenuItem>,
      }),
      row,
    )
    openMenu()
    expect(await screen.findByRole('menuitem', { name: '停用' })).toBeInTheDocument()
  })

  it('支持自定义 editLabel/deleteLabel', async () => {
    renderCell(actionsColumn<Row>({ onEdit: vi.fn(), editLabel: '修改' }), row)
    openMenu()
    expect(await screen.findByRole('menuitem', { name: '修改' })).toBeInTheDocument()
  })
})

describe('booleanBadgeColumn', () => {
  it('结构：accessorKey、默认宽 80、默认带 select 筛选 + true/false 选项', () => {
    const col = booleanBadgeColumn<Row>('enabled', '状态', { trueLabel: '启用', falseLabel: '停用' })
    expect((col as { accessorKey?: string }).accessorKey).toBe('enabled')
    expect(col.size).toBe(80)
    const meta = col.meta as { filterVariant?: string; filterOptions?: { value: unknown; label: string }[] }
    expect(meta.filterVariant).toBe('select')
    expect(meta.filterOptions).toEqual([
      { value: true, label: '启用' },
      { value: false, label: '停用' },
    ])
  })

  it('filterable=false 时不加筛选 meta', () => {
    const col = booleanBadgeColumn<Row>('enabled', '状态', {
      trueLabel: '启用', falseLabel: '停用', filterable: false,
    })
    expect(col.meta).toBeUndefined()
  })

  it('defaultHidden=true 时 meta 带 defaultHidden，且与筛选 meta 共存', () => {
    const col = booleanBadgeColumn<Row>('enabled', '状态', {
      trueLabel: '启用', falseLabel: '停用', defaultHidden: true,
    })
    const meta = col.meta as { filterVariant?: string; defaultHidden?: boolean }
    expect(meta.defaultHidden).toBe(true)
    expect(meta.filterVariant).toBe('select') // 默认仍 filterable，两个 meta 项互不排斥
  })

  it('filterable=false 且 defaultHidden=true 时 meta 只含 defaultHidden', () => {
    const col = booleanBadgeColumn<Row>('enabled', '状态', {
      trueLabel: '启用', falseLabel: '停用', filterable: false, defaultHidden: true,
    })
    expect(col.meta).toEqual({ defaultHidden: true })
  })

  it('cell：true → trueLabel', () => {
    const col = booleanBadgeColumn<Row>('enabled', '状态', { trueLabel: '启用', falseLabel: '停用' })
    renderCell(col, { ...row, enabled: true })
    expect(screen.getByText('启用')).toBeInTheDocument()
    expect(screen.queryByText('停用')).not.toBeInTheDocument()
  })

  it('cell：false → falseLabel', () => {
    const col = booleanBadgeColumn<Row>('enabled', '状态', { trueLabel: '启用', falseLabel: '停用' })
    renderCell(col, { ...row, enabled: false })
    expect(screen.getByText('停用')).toBeInTheDocument()
  })

  it('未传 onToggle → 只读 Badge，不是按钮', () => {
    renderCell(booleanBadgeColumn<Row>('enabled', '状态', { trueLabel: '启用', falseLabel: '停用' }), row)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('传 onToggle → 点击 Badge 以取反值调用', () => {
    const onToggle = vi.fn()
    renderCell(
      booleanBadgeColumn<Row>('enabled', '状态', { trueLabel: '启用', falseLabel: '停用', onToggle }),
      row,
    )
    fireEvent.click(screen.getByRole('button', { name: '点击改为停用' }))
    expect(onToggle).toHaveBeenCalledWith(row, false)
  })

  it('isToggleDisabled 为真时按钮 disabled、不触发 onToggle', () => {
    const onToggle = vi.fn()
    renderCell(
      booleanBadgeColumn<Row>('enabled', '状态', {
        trueLabel: '启用',
        falseLabel: '停用',
        onToggle,
        isToggleDisabled: () => true,
      }),
      row,
    )
    const btn = screen.getByRole('button', { name: '点击改为停用' })
    expect(btn).toBeDisabled()
    fireEvent.click(btn)
    expect(onToggle).not.toHaveBeenCalled()
  })
})

describe('primaryColumn', () => {
  it('结构：accessorKey / 默认宽 160 / 有列头', () => {
    const col = primaryColumn<Row>('name', '名称')
    expect((col as { accessorKey?: string }).accessorKey).toBe('name')
    expect(col.size).toBe(160)
    expect(col.header).toBeTypeOf('function')
  })

  it('传 onOpen → 渲染可点按钮，点击带整行调用', () => {
    const onOpen = vi.fn()
    renderCell(primaryColumn<Row>('name', '名称', { onOpen }), row)

    const btn = screen.getByRole('button', { name: '测试项' })
    expect(btn).toHaveAttribute('type', 'button')
    fireEvent.click(btn)
    expect(onOpen).toHaveBeenCalledWith(row)
  })

  it('不传 onOpen（如无编辑权）→ 渲染纯文本，无按钮', () => {
    renderCell(primaryColumn<Row>('name', '名称'), row)

    expect(screen.getByText('测试项')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('isOpenable 为 false 的行渲染纯文本', () => {
    const onOpen = vi.fn()
    renderCell(primaryColumn<Row>('name', '名称', { onOpen, isOpenable: () => false }), row)
    expect(screen.getByText('测试项')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('format 覆盖显示内容（原始值仍用于排序/筛选）', () => {
    renderCell(primaryColumn<Row>('name', '名称', { format: (r) => `#${r.id} ${r.name}` }), row)
    expect(screen.getByText('#1 测试项')).toBeInTheDocument()
  })

  it('存在未折叠文本选区时点击不触发 onOpen（拖选复制不被误判为打开）', () => {
    const onOpen = vi.fn()
    renderCell(primaryColumn<Row>('name', '名称', { onOpen }), row)

    const selectionStub = { isCollapsed: false, toString: () => '测试项' } as unknown as Selection
    const getSelectionSpy = vi.spyOn(window, 'getSelection').mockReturnValue(selectionStub)
    try {
      fireEvent.click(screen.getByRole('button', { name: '测试项' }))
      expect(onOpen).not.toHaveBeenCalled()
    } finally {
      getSelectionSpy.mockRestore()
    }
  })
})

interface Stat { id: string; qty: number | null; amount: number | null; rate: number | null; total: number }
const stat: Stat = { id: '1', qty: 1234.5678, amount: 1234567.8, rate: 0.12345, total: 10 }

describe('formatNumber / formatPercent', () => {
  it('千分位；默认最多 2 位小数、不补零', () => {
    expect(formatNumber(1234.5678)).toBe('1,234.57')
    expect(formatNumber(1200)).toBe('1,200')
  })

  it('fractionDigits 固定位数补零；maxFractionDigits 放宽精度', () => {
    expect(formatNumber(1200, { fractionDigits: 2 })).toBe('1,200.00')
    expect(formatNumber(0.12345678, { maxFractionDigits: 6 })).toBe('0.123457')
  })

  it('minFractionDigits 与 maxFractionDigits 合用：至少补到最少位，多余精度保留到最多位', () => {
    expect(formatNumber(1200, { minFractionDigits: 2, maxFractionDigits: 8 })).toBe('1,200.00')
    expect(formatNumber(12.12345678, { minFractionDigits: 2, maxFractionDigits: 8 })).toBe('12.12345678')
  })

  it('null / undefined / NaN 显示破折号，0 照常显示', () => {
    expect(formatNumber(null)).toBe('—')
    expect(formatNumber(undefined)).toBe('—')
    expect(formatNumber(Number.NaN)).toBe('—')
    expect(formatNumber(0)).toBe('0')
    expect(formatPercent(null)).toBe('—')
  })

  it('浮点残差舍入为零时不显示负号，真负数照常带负号', () => {
    expect(formatNumber(1691356.92 - 1234567.8 - 456789.12, { fractionDigits: 2 })).toBe('0.00')
    expect(formatNumber(-0.004)).toBe('0')
    expect(formatNumber(-1234.5, { fractionDigits: 2 })).toBe('-1,234.50')
    expect(formatPercent(-1e-9)).toBe('0.00%')
    expect(formatPercent(-0.0123)).toBe('-1.23%')
  })

  it('百分比默认按比率乘 100、固定 2 位；ratio=false 原样', () => {
    expect(formatPercent(0.12345)).toBe('12.35%')
    expect(formatPercent(12.3, { ratio: false, fractionDigits: 1 })).toBe('12.3%')
  })
})

describe('numberColumn / moneyColumn / percentColumn', () => {
  it('显式声明数字列：meta.numeric=true，标题推断不出也右对齐', () => {
    const col = numberColumn<Stat>('qty', '在途')
    expect(col.meta).toEqual({ numeric: true })
    expect(isNumericColumn(col.meta, '在途')).toBe(true)
    expect((col as { accessorKey?: string }).accessorKey).toBe('qty')
    expect(col.size).toBe(100)
    expect(col.header).toBeTypeOf('function')
    expect(col.enableSorting).toBeUndefined()
  })

  it('sortable=false 关排序；defaultHidden 与 numeric 共存', () => {
    const col = moneyColumn<Stat>('amount', '金额', { sortable: false, defaultHidden: true, size: 120 })
    expect(col.enableSorting).toBe(false)
    expect(col.meta).toEqual({ numeric: true, defaultHidden: true })
    expect(col.size).toBe(120)
  })

  it('cell：tabular-nums + 各自的格式', () => {
    renderCell(numberColumn<Stat>('qty', '数量', { maxFractionDigits: 4 }), stat)
    const qty = screen.getByText('1,234.5678')
    expect(qty).toHaveClass('tabular-nums')

    renderCell(moneyColumn<Stat>('amount', '金额'), stat)
    expect(screen.getByText('1,234,567.80')).toBeInTheDocument()

    renderCell(percentColumn<Stat>('rate', '占比'), stat)
    expect(screen.getByText('12.35%')).toBeInTheDocument()
  })

  it('空值显示破折号', () => {
    renderCell(moneyColumn<Stat>('amount', '金额'), { ...stat, amount: null })
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('value 覆盖显示值；className 可按行计算', () => {
    renderCell(numberColumn<Stat>('qty', '其他', {
      value: (r) => r.total - 3,
      className: (r) => (r.total > 5 ? 'text-destructive' : undefined),
    }), stat)
    const cell = screen.getByText('7')
    expect(cell).toHaveClass('tabular-nums', 'text-destructive')
  })
})
