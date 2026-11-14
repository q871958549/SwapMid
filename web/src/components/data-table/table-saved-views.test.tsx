import { useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import zhHansMessages from '../../../messages/zh-Hans.json'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import { useUrlListState } from '@/lib/crud/use-url-list-state'
import { DataTable } from './data-table'
import { DataTableColumnHeader } from './data-table-column-header'
import {
  resetTablePreferenceCloudStateForTests,
  TABLE_CLOUD_DEBOUNCE_MS,
} from './table-preference-cloud'

vi.mock('next/navigation', () => ({ usePathname: () => '/saved-views-test' }))

interface SampleRow {
  id: string
  name: string
  status: string
  remark: string
}

const columns: ColumnDef<SampleRow, unknown>[] = [
  {
    accessorKey: 'name',
    header: ({ column }) => <DataTableColumnHeader column={column} title="样品名称" />,
  },
  {
    accessorKey: 'status',
    header: ({ column }) => <DataTableColumnHeader column={column} title="流转状态" />,
  },
  {
    accessorKey: 'remark',
    header: '备注',
    enableSorting: false,
  },
]

const rows: SampleRow[] = [
  { id: '1', name: '主板样机', status: '在库', remark: '批次A' },
  { id: '2', name: '电机组件', status: '领用中', remark: '批次B' },
]

function TestHarness({
  onSearchObserved,
  onSortingObserved,
}: {
  onSearchObserved?: (q: string) => void
  onSortingObserved?: (sorting: SortingState) => void
}) {
  const [filters, setFilters] = useUrlListState({ dept: '', stage: '' })
  const [sorting, setSorting] = useState<SortingState>([])
  const [search, setSearch] = useState('')

  return (
    <>
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <SessionIdentityProvider identityVersion="id-v1" storagePrincipalId="principal-presets">
          <div>
            <span data-testid="active-dept">{filters.dept || 'ALL'}</span>
            <span data-testid="active-search">{search || 'EMPTY'}</span>
            <span data-testid="active-sort">
              {sorting[0] ? `${sorting[0].id}:${sorting[0].desc ? 'desc' : 'asc'}` : 'NONE'}
            </span>
            <button
              type="button"
              onClick={() => setFilters({ dept: '研发部' })}
            >
              切到研发部
            </button>
            <button
              type="button"
              onClick={() => setFilters({ dept: '' })}
            >
              清空部门
            </button>
          </div>
          <DataTable<SampleRow>
            columns={columns}
            data={rows}
            tableKey="saved-views-demo"
            manualPagination
            manualSorting
            rowCount={2}
            pageIndex={0}
            manualPageSize={15}
            sorting={sorting}
            onSortingChange={(next) => {
              setSorting(next)
              onSortingObserved?.(next)
            }}
            onSearchChange={(q) => {
              setSearch(q)
              onSearchObserved?.(q)
            }}
            onPaginationChange={vi.fn()}
          />
        </SessionIdentityProvider>
      </NextIntlClientProvider>
    </>
  )
}

describe('DataTable 单表命名视图预设（Saved Views）', () => {
  beforeEach(() => {
    localStorage.clear()
    resetTablePreferenceCloudStateForTests()
    window.history.replaceState({}, '', '/saved-views-test')
  })

  afterEach(() => {
    vi.useRealTimers()
    resetTablePreferenceCloudStateForTests()
  })

  it('在列设置弹层保存命名视图（列显隐+搜索+排序+URL筛选），重置后一键恢复并同步云端', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const putBodies: unknown[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(async (_input, init) => {
        if (init?.method === 'PUT') {
          putBodies.push(JSON.parse(String(init.body ?? 'null')))
        }
        return { ok: true, json: async () => ({ density: 'standard', tables: {} }) } as unknown as Response
      }),
    )

    render(<TestHarness />)
    const searchInput = await screen.findByPlaceholderText('搜索...')

    // 1. 设置 URL 筛选 + 搜索词 + 点击列头排序 + 隐藏「备注」列
    fireEvent.click(screen.getByRole('button', { name: '切到研发部' }))
    expect(screen.getByTestId('active-dept')).toHaveTextContent('研发部')

    fireEvent.change(searchInput, { target: { value: '电机' } })
    act(() => { vi.advanceTimersByTime(350) })
    expect(screen.getByTestId('active-search')).toHaveTextContent('电机')

    fireEvent.click(screen.getByRole('button', { name: /样品名称/ }))
    expect(screen.getByTestId('active-sort')).toHaveTextContent('name:asc')

    fireEvent.click(screen.getByRole('button', { name: '列' }))
    const panel = await screen.findByRole('dialog')
    fireEvent.click(within(panel).getByRole('checkbox', { name: '备注' }))
    await waitFor(() => expect(screen.queryByText('批次A')).not.toBeInTheDocument())

    // 2. 在弹层顶部保存为命名预设「研发电机排查」
    fireEvent.click(within(panel).getByRole('button', { name: '存为预设' }))
    const nameInput = within(panel).getByPlaceholderText('输入预设名称（最多 16 字）')
    fireEvent.change(nameInput, { target: { value: '研发电机排查' } })
    fireEvent.keyDown(nameInput, { key: 'Enter' })

    expect(within(panel).getByRole('button', { name: '研发电机排查' })).toBeInTheDocument()

    // 500ms 防抖后写回云端载荷包含 presets（含 sortItems 与 urlFilters）
    act(() => { vi.advanceTimersByTime(TABLE_CLOUD_DEBOUNCE_MS + 50) })
    await waitFor(() => expect(putBodies.length).toBeGreaterThan(0))
    const latestPut = putBodies.at(-1) as { tableKey: string; view: { presets?: unknown[] } }
    expect(latestPut.tableKey).toBe('saved-views-demo')
    expect(latestPut.view.presets).toEqual([
      expect.objectContaining({
        name: '研发电机排查',
        order: null,
        visibility: { remark: false },
        search: '电机',
        sortItems: [{ id: 'name', desc: false }],
        urlFilters: { dept: '研发部' },
      }),
    ])

    // 3. 把列恢复默认、清空搜索词、清空部门筛选、切换排序
    fireEvent.click(within(panel).getByRole('button', { name: '恢复默认列' }))
    await waitFor(() => expect(screen.getByText('批次A')).toBeInTheDocument())
    fireEvent.change(searchInput, { target: { value: '' } })
    act(() => { vi.advanceTimersByTime(350) })
    fireEvent.click(screen.getByRole('button', { name: '清空部门' }))
    fireEvent.click(screen.getByRole('button', { name: /流转状态/ }))

    expect(screen.getByTestId('active-dept')).toHaveTextContent('ALL')
    expect(screen.getByTestId('active-search')).toHaveTextContent('EMPTY')
    expect(screen.getByTestId('active-sort')).toHaveTextContent('status:asc')

    // 4. 点击预设芯片「研发电机排查」，一键恢复列显隐、搜索词、排序与 URL 筛选
    fireEvent.click(within(panel).getByRole('button', { name: '研发电机排查' }))
    await waitFor(() => {
      expect(screen.queryByText('批次A')).not.toBeInTheDocument()
      expect(screen.getByTestId('active-dept')).toHaveTextContent('研发部')
      expect(screen.getByTestId('active-search')).toHaveTextContent('电机')
      expect(screen.getByTestId('active-sort')).toHaveTextContent('name:asc')
    })
    expect((searchInput as HTMLInputElement).value).toBe('电机')

    // 5. 删除预设后芯片移除
    fireEvent.click(within(panel).getByRole('button', { name: '删除预设「研发电机排查」' }))
    expect(within(panel).queryByRole('button', { name: '研发电机排查' })).not.toBeInTheDocument()
  })

  it('单表最多保存 5 个视图预设，达到上限时「存为预设」按钮禁用', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) } as Response))
    render(<TestHarness />)
    await screen.findByPlaceholderText('搜索...')

    fireEvent.click(screen.getByRole('button', { name: '列' }))
    const panel = await screen.findByRole('dialog')

    for (let i = 1; i <= 5; i += 1) {
      fireEvent.click(within(panel).getByRole('button', { name: '存为预设' }))
      const input = within(panel).getByPlaceholderText('输入预设名称（最多 16 字）')
      fireEvent.change(input, { target: { value: `视图${i}` } })
      fireEvent.click(within(panel).getByRole('button', { name: '保存' }))
    }

    expect(within(panel).getByRole('button', { name: '存为预设' })).toBeDisabled()
  })
})
