import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from './data-table'
import { DataTableColumnHeader } from './data-table-column-header'
import { clampColumnSizing } from './use-table-persistence'
import zhHansMessages from '../../../messages/zh-Hans.json'

vi.mock('next/navigation', () => ({ usePathname: () => '/test-column-resize' }))

interface Row {
  id: string
  name: string
  remark: string
}

const columns: ColumnDef<Row, unknown>[] = [
  { accessorKey: 'name', header: ({ column }) => <DataTableColumnHeader column={column} title="名称" /> },
  { accessorKey: 'remark', header: '备注', enableSorting: false },
]

const rows: Row[] = [{ id: '1', name: '甲', remark: '一段很长很长的备注文字' }]

beforeEach(() => {
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) } as Response))
})

async function renderTable() {
  const view = rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <DataTable<Row> columns={columns} data={rows} tableKey="resize" />
    </NextIntlClientProvider>,
  )
  await screen.findByPlaceholderText('搜索...')
  return view
}

function headerCell(name: string) {
  return screen.getAllByRole('columnheader').find((cell) => cell.textContent?.includes(name))!
}

// 把手对辅助技术隐藏（列头无障碍名只留列名），按属性取。
function handleOf(name: string) {
  return headerCell(name).querySelector<HTMLElement>('[data-slot="column-resize-handle"]')!
}

function drag(handle: HTMLElement, from: number, to: number) {
  fireEvent.mouseDown(handle, { clientX: from })
  fireEvent.mouseMove(document, { clientX: to })
  fireEvent.mouseUp(document, { clientX: to })
}

describe('列宽调整', () => {
  it('拖表头右缘调整列宽，锁定为拖出来的宽度并按列保存', async () => {
    await renderTable()
    expect(headerCell('名称').style.width).toBe('')
    drag(handleOf('名称'), 100, 160)
    // 未设 size 的列从 150 起算，向右拖 60。
    await waitFor(() => expect(headerCell('名称').style.width).toBe('210px'))
    expect(headerCell('名称').style.maxWidth).toBe('210px')
    expect(JSON.parse(localStorage.getItem('swapmid-table:resize:sizing') ?? '{}')).toEqual({ name: 210 })
  })

  it('拖出来的列宽夹在 64–800px', () => {
    expect(clampColumnSizing({ a: 10, b: 2000, c: 123.4 })).toEqual({ a: 64, b: 800, c: 123 })
  })

  it('双击把手恢复自动宽度', async () => {
    await renderTable()
    drag(handleOf('名称'), 100, 160)
    await waitFor(() => expect(headerCell('名称').style.width).toBe('210px'))
    fireEvent.doubleClick(handleOf('名称'))
    await waitFor(() => expect(headerCell('名称').style.width).toBe(''))
  })

  it('列间分隔是把手画的居中短线：两个数据列之间才画，最后一列不画', async () => {
    await renderTable()
    expect(headerCell('名称')).not.toHaveClass('border-r')
    expect(handleOf('名称').className).toContain('after:bg-border')
    expect(handleOf('名称').className).toContain('after:inset-y-3')
    expect(handleOf('备注').className).toContain('after:bg-transparent')
  })

  it('把手不进列头的无障碍名，悬停有提示', async () => {
    await renderTable()
    expect(screen.getByRole('columnheader', { name: '名称' })).toBe(headerCell('名称'))
    expect(handleOf('名称')).toHaveAttribute('title', '拖动调整列宽，双击恢复自动宽度')
  })

  it('刷新后从本地偏好恢复列宽', async () => {
    localStorage.setItem('swapmid-table:resize:sizing', JSON.stringify({ remark: 300 }))
    await renderTable()
    expect(headerCell('备注').style.width).toBe('300px')
  })

  it('列菜单「恢复默认列」同时清掉自定义列宽', async () => {
    await renderTable()
    drag(handleOf('名称'), 100, 160)
    await waitFor(() => expect(headerCell('名称').style.width).toBe('210px'))
    fireEvent.contextMenu(headerCell('备注'))
    fireEvent.click(await screen.findByRole('menuitem', { name: '恢复默认列' }))
    await waitFor(() => expect(headerCell('名称').style.width).toBe(''))
    expect(JSON.parse(localStorage.getItem('swapmid-table:resize:sizing') ?? '{}')).toEqual({})
  })

  it('按下把手时不触发表头的拖拽重排', async () => {
    await renderTable()
    fireEvent.mouseDown(handleOf('名称'), { clientX: 100 })
    const dragStart = new Event('dragstart', { bubbles: true, cancelable: true })
    headerCell('名称').dispatchEvent(dragStart)
    expect(dragStart.defaultPrevented).toBe(true)
    fireEvent.mouseUp(document)
  })
})
