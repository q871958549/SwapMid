import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from './data-table'
import { DataTableColumnHeader } from './data-table-column-header'
import { isDescFirstColumn, nextSortState } from './numeric-column'
import zhHansMessages from '../../../messages/zh-Hans.json'

vi.mock('next/navigation', () => ({ usePathname: () => '/test-column-menu' }))

interface Row {
  id: string
  name: string
  amount: number
  remark: string
  code: string
  qty: number
}

const rows: Row[] = [
  { id: '1', name: '乙', amount: 20, remark: '备注一', code: 'B', qty: 2 },
  { id: '2', name: '甲', amount: 30, remark: '备注二', code: 'A', qty: 3 },
  { id: '3', name: '丙', amount: 10, remark: '备注三', code: 'C', qty: 1 },
]

const columns: ColumnDef<Row, unknown>[] = [
  { accessorKey: 'name', header: ({ column }) => <DataTableColumnHeader column={column} title="名称" /> },
  { accessorKey: 'amount', header: ({ column }) => <DataTableColumnHeader column={column} title="金额" /> },
  // 纯文本表头且不可排序：旧版没有任何表头菜单，隐藏不了。
  { accessorKey: 'remark', header: '备注', enableSorting: false },
  // 纯文本表头、没写 enableSorting: false：作者没提供排序，菜单也不该给。
  { accessorKey: 'code', header: '编码' },
  // 外面包了一层的排序表头仍算提供了排序。
  { accessorKey: 'qty', header: ({ column }) => <div className="flex justify-end"><DataTableColumnHeader column={column} title="件数" /></div> },
]

beforeEach(() => {
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) } as Response))
})

async function renderTable() {
  rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <DataTable<Row> columns={columns} data={rows} tableKey="column-menu" />
    </NextIntlClientProvider>,
  )
  await screen.findByPlaceholderText('搜索...')
}

function firstColumnValues(index: number) {
  return screen.getAllByRole('row').slice(1).map((row) => within(row).getAllByRole('cell')[index]?.textContent)
}

function headerCell(name: string) {
  return screen.getAllByRole('columnheader').find((cell) => cell.textContent?.includes(name))!
}

describe('单击排序循环', () => {
  it('文本列：升序 → 降序 → 默认', () => {
    expect(nextSortState(false, false)).toBe('asc')
    expect(nextSortState('asc', false)).toBe('desc')
    expect(nextSortState('desc', false)).toBe(false)
  })

  it('数字 / 日期列：降序 → 升序 → 默认', () => {
    expect(nextSortState(false, true)).toBe('desc')
    expect(nextSortState('desc', true)).toBe('asc')
    expect(nextSortState('asc', true)).toBe(false)
  })

  it('先降序的列：数字、日期筛选、日期类标题；显式 sortDescFirst 优先', () => {
    expect(isDescFirstColumn(undefined, '金额')).toBe(true)
    expect(isDescFirstColumn({ filterVariant: 'dateRange' }, '登记')).toBe(true)
    expect(isDescFirstColumn(undefined, '创建时间')).toBe(true)
    expect(isDescFirstColumn(undefined, '名称')).toBe(false)
    expect(isDescFirstColumn(undefined, '金额', false)).toBe(false)
  })
})

describe('表头单击一步排序', () => {
  it('点一次列名就排序，不再弹菜单；表头带 aria-sort', async () => {
    await renderTable()
    expect(firstColumnValues(0)).toEqual(['乙', '甲', '丙'])

    fireEvent.click(screen.getByRole('button', { name: '名称' }))
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(headerCell('名称')).toHaveAttribute('aria-sort', 'ascending')
    const asc = firstColumnValues(0)

    fireEvent.click(screen.getByRole('button', { name: '名称' }))
    expect(headerCell('名称')).toHaveAttribute('aria-sort', 'descending')
    expect(firstColumnValues(0)).toEqual([...asc].reverse())

    fireEvent.click(screen.getByRole('button', { name: '名称' }))
    expect(headerCell('名称')).not.toHaveAttribute('aria-sort')
    expect(firstColumnValues(0)).toEqual(['乙', '甲', '丙'])
  })

  it('数字列第一次点击是降序', async () => {
    await renderTable()
    fireEvent.click(screen.getByRole('button', { name: '金额' }))
    expect(headerCell('金额')).toHaveAttribute('aria-sort', 'descending')
    expect(firstColumnValues(1)).toEqual(['30', '20', '10'])
  })
})

describe('列菜单', () => {
  it('右键不可排序的纯文本表头：排序置灰，可以隐藏，再从别的列恢复默认列', async () => {
    await renderTable()
    fireEvent.contextMenu(headerCell('备注'))
    const menu = await screen.findByRole('menu')
    expect(within(menu).getByRole('menuitem', { name: '此列不支持排序' })).toHaveAttribute('aria-disabled', 'true')
    expect(within(menu).getByRole('menuitem', { name: '恢复默认列' })).toHaveAttribute('aria-disabled', 'true')

    fireEvent.click(within(menu).getByRole('menuitem', { name: '隐藏列' }))
    await waitFor(() => expect(screen.queryByText('备注一')).not.toBeInTheDocument())

    fireEvent.contextMenu(headerCell('名称'))
    fireEvent.click(await screen.findByRole('menuitem', { name: '恢复默认列' }))
    await waitFor(() => expect(screen.getByText('备注一')).toBeInTheDocument())
  })

  it('列头的无障碍名只是列名，不含悬停入口', async () => {
    await renderTable()
    expect(screen.getByRole('columnheader', { name: '名称' })).toBe(headerCell('名称'))
    expect(within(headerCell('名称')).queryByRole('button', { name: '列菜单' })).not.toBeInTheDocument()
  })

  it('悬停入口「列菜单」打开同一份菜单，可直接选降序', async () => {
    await renderTable()
    // 入口对辅助技术隐藏，按属性取。
    const trigger = headerCell('名称').querySelector<HTMLButtonElement>('button[aria-label="列菜单"]')!
    fireEvent.keyDown(trigger, { key: 'Enter' })
    fireEvent.click(await screen.findByRole('menuitemcheckbox', { name: '降序' }))
    expect(headerCell('名称')).toHaveAttribute('aria-sort', 'descending')
  })

  it('纯文本表头即使默认可排序，菜单也不给排序项；包了一层的排序表头照常给', async () => {
    await renderTable()
    fireEvent.contextMenu(headerCell('编码'))
    expect(await screen.findByRole('menuitem', { name: '此列不支持排序' })).toHaveAttribute('aria-disabled', 'true')
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument())

    fireEvent.contextMenu(headerCell('件数'))
    expect(await screen.findByRole('menuitemcheckbox', { name: '升序' })).toBeInTheDocument()
  })

  it('只剩一个数据列时不能再隐藏', async () => {
    await renderTable()
    for (const name of ['备注', '金额', '编码', '件数']) {
      fireEvent.contextMenu(headerCell(name))
      fireEvent.click(await screen.findByRole('menuitem', { name: '隐藏列' }))
      await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument())
    }
    fireEvent.contextMenu(headerCell('名称'))
    expect(await screen.findByRole('menuitem', { name: '隐藏列' })).toHaveAttribute('aria-disabled', 'true')
  })
})

describe('表头视觉', () => {
  it('可排序列整格是点击面，不可排序列不是手型；表头统一 12px 并带分隔线', async () => {
    await renderTable()
    expect(headerCell('名称')).toHaveClass('cursor-pointer', 'text-xs')
    // 不用通高边框做列间分隔（观感生硬），分隔交给列宽把手的短线。
    expect(headerCell('名称')).not.toHaveClass('border-r')
    expect(headerCell('备注')).not.toHaveClass('cursor-pointer')
    expect(headerCell('编码')).not.toHaveClass('cursor-pointer')
    // 排序按钮用伪元素铺满格子。
    expect(within(headerCell('名称')).getByRole('button', { name: '名称' }).className).toContain('after:inset-0')
  })

  it('数字列箭头在列名左侧，文本列在右侧', async () => {
    await renderTable()
    fireEvent.click(screen.getByRole('button', { name: '金额' }))
    fireEvent.click(screen.getByRole('button', { name: '名称' }))
    const amount = within(headerCell('金额')).getByRole('button', { name: '金额' })
    const name = within(headerCell('名称')).getByRole('button', { name: '名称' })
    expect(amount.firstElementChild?.tagName.toLowerCase()).toBe('svg')
    expect(name.lastElementChild?.tagName.toLowerCase()).toBe('svg')
  })
})
