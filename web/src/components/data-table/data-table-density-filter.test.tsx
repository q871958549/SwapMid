import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from './data-table'
import { DataTableColumnHeader } from './data-table-column-header'
import zhHansMessages from '../../../messages/zh-Hans.json'

vi.mock('next/navigation', () => ({ usePathname: () => '/test-density-filter' }))

interface Row {
  id: string
  name: string
  city: string
}

const columns: ColumnDef<Row, unknown>[] = [
  { accessorKey: 'name', header: ({ column }) => <DataTableColumnHeader column={column} title="姓名" /> },
  { accessorKey: 'city', header: '城市' },
]

const rows: Row[] = [
  { id: '1', name: '张三', city: '深圳' },
  { id: '2', name: '李四', city: '东莞' },
]

beforeEach(() => {
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) } as Response))
})

function renderTables(...tables: { tableKey: string; manualPagination?: boolean }[]) {
  rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      {tables.map(({ tableKey, manualPagination }) => (
        <DataTable<Row> key={tableKey} columns={columns} data={rows} tableKey={tableKey} manualPagination={manualPagination} rowCount={rows.length} />
      ))}
    </NextIntlClientProvider>,
  )
}

async function renderTable(options: { manualPagination?: boolean } = {}) {
  renderTables({ tableKey: 'density-filter', ...options })
  await screen.findByPlaceholderText('搜索...')
}

function grids() {
  return Array.from(document.querySelectorAll<HTMLElement>('[data-density]'))
}

function headerCell(name: string) {
  return screen.getAllByRole('columnheader').find((cell) => cell.textContent?.includes(name))!
}

async function openSettings() {
  fireEvent.click(screen.getAllByRole('button', { name: '列' })[0])
  return screen.findByRole('dialog')
}

describe('行密度', () => {
  it('默认标准；在列设置里切到紧凑，单元格与表头一起变矮并记住', async () => {
    await renderTable()
    expect(grids()[0]).toHaveAttribute('data-density', 'standard')
    expect(screen.getByText('深圳').closest('td')).not.toHaveClass('py-1.5')

    const panel = await openSettings()
    const density = within(panel).getByRole('radiogroup', { name: '行密度' })
    expect(within(density).getByRole('radio', { name: '标准' })).toHaveAttribute('aria-checked', 'true')
    fireEvent.click(within(density).getByRole('radio', { name: '紧凑' }))

    await waitFor(() => expect(grids()[0]).toHaveAttribute('data-density', 'compact'))
    expect(screen.getByText('深圳').closest('td')).toHaveClass('py-1.5')
    expect(headerCell('城市')).toHaveClass('h-8')
    expect(JSON.parse(localStorage.getItem('swapmid-table:density') ?? 'null')).toBe('compact')
  })

  it('所有列表共用一个设置：同页另一个表格跟着变，刷新后保留', async () => {
    localStorage.setItem('swapmid-table:density', JSON.stringify('compact'))
    renderTables({ tableKey: 'density-a' }, { tableKey: 'density-b' })
    await waitFor(() => expect(grids()).toHaveLength(2))
    expect(grids().map((grid) => grid.dataset.density)).toEqual(['compact', 'compact'])

    const panel = await openSettings()
    fireEvent.click(within(panel).getByRole('radio', { name: '标准' }))
    await waitFor(() => expect(grids().map((grid) => grid.dataset.density)).toEqual(['standard', 'standard']))
  })

  it('「恢复默认列」不动行密度', async () => {
    localStorage.setItem('swapmid-table:density', JSON.stringify('compact'))
    localStorage.setItem('swapmid-table:density-filter:visibility', JSON.stringify({ city: false }))
    await renderTable()
    const panel = await openSettings()
    fireEvent.click(within(panel).getByRole('button', { name: '恢复默认列' }))
    await waitFor(() => expect(screen.getByText('深圳')).toBeInTheDocument())
    expect(grids()[0]).toHaveAttribute('data-density', 'compact')
  })
})

describe('列筛选漏斗与列菜单筛选项', () => {
  it('只有生效筛选的列显示漏斗，点漏斗聚焦该列的筛选框', async () => {
    await renderTable()
    expect(screen.queryByRole('button', { name: '已按此列筛选，点击修改' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /筛选/ }))
    const [, cityInput] = screen.getAllByPlaceholderText('筛选...')
    fireEvent.change(cityInput, { target: { value: '深' } })

    const funnel = within(headerCell('城市')).getByRole('button', { name: '已按此列筛选，点击修改' })
    expect(within(headerCell('姓名')).queryByRole('button', { name: '已按此列筛选，点击修改' })).not.toBeInTheDocument()
    cityInput.blur()
    fireEvent.click(funnel)
    expect(cityInput).toHaveFocus()
  })

  it('列菜单「筛选此列」打开筛选行并聚焦该列；有筛选时多出「清除此列筛选」', async () => {
    await renderTable()
    fireEvent.contextMenu(headerCell('城市'))
    expect(screen.queryByRole('menuitem', { name: '清除此列筛选' })).not.toBeInTheDocument()
    fireEvent.click(await screen.findByRole('menuitem', { name: '筛选此列' }))

    const [, cityInput] = await screen.findAllByPlaceholderText('筛选...')
    await waitFor(() => expect(cityInput).toHaveFocus())
    fireEvent.change(cityInput, { target: { value: '深' } })
    await waitFor(() => expect(screen.queryByText('东莞')).not.toBeInTheDocument())

    fireEvent.contextMenu(headerCell('城市'))
    fireEvent.click(await screen.findByRole('menuitem', { name: '清除此列筛选' }))
    await waitFor(() => expect(screen.getByText('东莞')).toBeInTheDocument())
    expect(within(headerCell('城市')).queryByRole('button', { name: '已按此列筛选，点击修改' })).not.toBeInTheDocument()
  })

  it('服务端分页的表没有列筛选，列菜单不给「筛选此列」', async () => {
    await renderTable({ manualPagination: true })
    fireEvent.contextMenu(headerCell('城市'))
    await screen.findByRole('menuitem', { name: '隐藏列' })
    expect(screen.queryByRole('menuitem', { name: '筛选此列' })).not.toBeInTheDocument()
  })
})
