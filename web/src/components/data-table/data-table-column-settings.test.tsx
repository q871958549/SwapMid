import type { ComponentProps } from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from './data-table'
import { DataTableColumnHeader } from './data-table-column-header'
import { reconcileColumnOrder } from './use-table-persistence'
import zhHansMessages from '../../../messages/zh-Hans.json'

vi.mock('next/navigation', () => ({ usePathname: () => '/test-column-settings' }))

interface Row {
  id: string
  name: string
  amount: number
  remark: string
  code: string
}

const columns: ColumnDef<Row, unknown>[] = [
  { accessorKey: 'name', header: ({ column }) => <DataTableColumnHeader column={column} title="名称" /> },
  { accessorKey: 'amount', header: ({ column }) => <DataTableColumnHeader column={column} title="金额" /> },
  { accessorKey: 'remark', header: '备注', enableSorting: false },
  { accessorKey: 'code', header: '编码' },
]

const rows: Row[] = [{ id: '1', name: '甲', amount: 10, remark: '备注一', code: 'C1' }]

beforeEach(() => {
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) } as Response))
})

async function renderTable(cols: ColumnDef<Row, unknown>[] = columns, props: Partial<ComponentProps<typeof DataTable<Row>>> = {}) {
  rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <DataTable<Row> columns={cols} data={rows} tableKey="settings" {...props} />
    </NextIntlClientProvider>,
  )
  await screen.findByPlaceholderText('搜索...')
}

/**
 * 面板开着时，焦点一旦落到面板外，Popover 就当成「点到外面」关掉。真实浏览器里就是菜单关闭动画
 * 放完、焦点还给触发按钮的那一下（jsdom 没有动画，面板本身未必关，所以直接查焦点落点）。
 */
async function selectAndExpectFocusStaysInPanel(item: HTMLElement) {
  const landed: Element[] = []
  const track = (event: FocusEvent) => landed.push(event.target as Element)
  document.addEventListener('focusin', track)
  try {
    fireEvent.click(item)
    const panel = await screen.findByRole('dialog')
    await waitFor(() => expect(panel).toContainElement(document.activeElement as HTMLElement))
    expect(landed.filter((element) => !panel.contains(element))).toEqual([])
    return panel
  } finally {
    document.removeEventListener('focusin', track)
  }
}

function headerNames() {
  return screen.getAllByRole('columnheader').map((cell) => cell.textContent?.trim())
}

function headerCell(name: string) {
  return screen.getAllByRole('columnheader').find((cell) => cell.textContent?.includes(name))!
}

async function openSettings() {
  fireEvent.click(screen.getByRole('button', { name: '列' }))
  return screen.findByRole('dialog')
}

describe('列顺序协调：用户固定列', () => {
  it('用户固定的列排在页面钉住的列之后、普通列之前，结构列不受影响', () => {
    const cols: ColumnDef<Row, unknown>[] = [
      { id: 'select' },
      { accessorKey: 'name' },
      { accessorKey: 'amount', meta: { pin: 'left' } },
      { accessorKey: 'remark' },
      { accessorKey: 'code' },
      { id: 'actions' },
    ]
    expect(reconcileColumnOrder(['name', 'remark', 'code'], cols, ['code', 'actions', 'missing']))
      .toEqual(['select', 'amount', 'code', 'name', 'remark', 'actions'])
  })
})

describe('列设置面板', () => {
  it('「列」按钮打开面板，勾选控制列显隐', async () => {
    await renderTable()
    const panel = await openSettings()
    expect(within(panel).getByText('列设置')).toBeInTheDocument()
    fireEvent.click(within(panel).getByRole('checkbox', { name: '备注' }))
    await waitFor(() => expect(screen.queryByText('备注一')).not.toBeInTheDocument())
  })

  it('固定到左侧：列移到最前并钉住，列菜单可取消固定，刷新后保留', async () => {
    await renderTable()
    const panel = await openSettings()
    const remarkRow = within(panel).getByRole('checkbox', { name: '备注' }).closest('li')!
    fireEvent.click(within(remarkRow).getByRole('button', { name: '固定到左侧' }))
    await waitFor(() => expect(headerNames()[0]).toBe('备注'))
    expect(headerCell('备注')).toHaveClass('sticky')
    expect(JSON.parse(localStorage.getItem('swapmid-table:settings:pinned') ?? '[]')).toEqual(['remark'])
    expect(within(panel).getByText('固定在左侧')).toBeInTheDocument()

    fireEvent.keyDown(panel, { key: 'Escape' })
    fireEvent.contextMenu(headerCell('备注'))
    fireEvent.click(await screen.findByRole('menuitem', { name: '取消固定' }))
    await waitFor(() => expect(headerCell('备注')).not.toHaveClass('sticky'))
  })

  it('「恢复默认列」一次还原显隐、固定与列顺序', async () => {
    localStorage.setItem('swapmid-table:settings:pinned', JSON.stringify(['code']))
    localStorage.setItem('swapmid-table:settings:visibility', JSON.stringify({ amount: false }))
    await renderTable()
    expect(headerNames()[0]).toBe('编码')
    const panel = await openSettings()
    fireEvent.click(within(panel).getByRole('button', { name: '恢复默认列' }))
    await waitFor(() => expect(headerNames()).toEqual(['名称', '金额', '备注', '编码']))
    expect(within(panel).getByRole('button', { name: '恢复默认列' })).toBeDisabled()
  })

  it('列菜单「列设置…」打开同一个面板，焦点不落到面板外', async () => {
    await renderTable()
    fireEvent.contextMenu(headerCell('名称'))
    const panel = await selectAndExpectFocusStaysInPanel(await screen.findByRole('menuitem', { name: '列设置…' }))
    expect(within(panel).getByText('列设置')).toBeInTheDocument()
  })

  it('「更多」里的「列设置…」：等菜单关好再开，焦点不回「更多」、面板不闪关', async () => {
    // 工具栏按钮多到「列」收进「更多」。
    await renderTable(columns, { onImportRows: vi.fn(), onReload: vi.fn(), onResetView: vi.fn(), viewDirty: true })
    const more = screen.getByRole('button', { name: '更多' })
    fireEvent.keyDown(more, { key: 'Enter' })
    const panel = await selectAndExpectFocusStaysInPanel(await screen.findByRole('menuitem', { name: '列设置…' }))
    expect(within(panel).getByText('列设置')).toBeInTheDocument()
    expect(more).not.toHaveFocus()
  })

  it('列多时出现搜索框，按列名筛选', async () => {
    const many: ColumnDef<Row, unknown>[] = Array.from({ length: 9 }, (_, i) => ({
      id: `c${i}`, accessorFn: () => i, header: `第${i}列`,
    }))
    await renderTable(many)
    const panel = await openSettings()
    fireEvent.change(within(panel).getByPlaceholderText('搜索列'), { target: { value: '第3' } })
    expect(within(panel).getAllByRole('checkbox')).toHaveLength(1)
    fireEvent.change(within(panel).getByPlaceholderText('搜索列'), { target: { value: '不存在' } })
    expect(within(panel).getByText('没有匹配的列')).toBeInTheDocument()
  })
})
