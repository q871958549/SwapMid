import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render as rtlRender, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from './data-table'
import { DataTableColumnHeader } from './data-table-column-header'
import zhHansMessages from '../../../messages/zh-Hans.json'

vi.mock('next/navigation', () => ({ usePathname: () => '/test-table-cards' }))

interface Row { id: string; name: string; city: string; qty: number }

function col(accessorKey: keyof Row, title: string, extra: Partial<ColumnDef<Row, unknown>> = {}): ColumnDef<Row, unknown> {
  return {
    accessorKey,
    header: ({ column }) => <DataTableColumnHeader column={column} title={title} />,
    ...extra,
  }
}

const columns: ColumnDef<Row, unknown>[] = [
  col('name', '名称'),
  col('city', '城市', { meta: { pin: 'left' } }),
  col('qty', '数量', { meta: { defaultHidden: true } }),
]

const rows: Row[] = [{ id: '1', name: '甲物料', city: '苏州', qty: 3 }]

function renderTable() {
  return rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <DataTable columns={columns} data={rows} tableKey="cards-test" enableSearch={false} />
    </NextIntlClientProvider>,
  )
}

function installMatchMedia(matches: boolean) {
  window.matchMedia = (query: string) => ({
    matches: matches && query.includes('max-width'),
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }) as unknown as MediaQueryList
}

beforeEach(() => {
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) } as Response))
})

afterEach(() => {
  installMatchMedia(false)
})

describe('DataTable · 窄屏卡片', () => {
  it('桌面仍是表，不渲染卡片', async () => {
    installMatchMedia(false)
    renderTable()
    await screen.findByRole('table')
    expect(document.querySelector('[data-slot="data-table-cards"]')).toBeNull()
    expect(screen.getByText('甲物料')).toBeInTheDocument()
  })

  it('窄屏用卡片：左钉列当标题，默认隐藏列不出现', async () => {
    installMatchMedia(true)
    renderTable()
    const cards = await waitFor(() => {
      const node = document.querySelector('[data-slot="data-table-cards"]')
      expect(node).not.toBeNull()
      return node as HTMLElement
    })
    expect(screen.queryByRole('table')).toBeNull()
    expect(cards).toHaveTextContent('苏州')
    expect(cards).toHaveTextContent('名称')
    expect(cards).toHaveTextContent('甲物料')
    expect(cards).not.toHaveTextContent('数量')
    expect(cards).not.toHaveTextContent('城市')
  })
})
