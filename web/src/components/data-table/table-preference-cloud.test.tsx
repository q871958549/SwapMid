import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import zhHansMessages from '../../../messages/zh-Hans.json'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import { TablePreferenceCloudSync } from '@/components/layout/table-preference-cloud-sync'
import { DataTable } from './data-table'
import { DataTableColumnHeader } from './data-table-column-header'
import {
  resetTablePreferenceCloudStateForTests,
  TABLE_CLOUD_DEBOUNCE_MS,
} from './table-preference-cloud'
import { applyCloudTablePreferences } from './use-table-persistence'

vi.mock('next/navigation', () => ({ usePathname: () => '/cloud-table' }))

interface Row {
  id: string
  name: string
  city: string
}

const columns: ColumnDef<Row, unknown>[] = [
  {
    accessorKey: 'name',
    header: ({ column }) => <DataTableColumnHeader column={column} title="姓名" />,
  },
  {
    accessorKey: 'city',
    header: ({ column }) => <DataTableColumnHeader column={column} title="城市" />,
  },
]

const rows: Row[] = [
  { id: '1', name: '王五', city: '深圳' },
  { id: '2', name: '赵六', city: '上海' },
]

describe('列表视图偏好上云（TablePreferenceCloudSync + DataTable）', () => {
  beforeEach(() => {
    localStorage.clear()
    resetTablePreferenceCloudStateForTests()
    window.history.replaceState({}, '', '/cloud-table')
  })

  afterEach(() => {
    vi.useRealTimers()
    resetTablePreferenceCloudStateForTests()
  })

  it('壳层挂载单飞拉取云端快照后，已挂载 DataTable 热更新列显隐、行密度与服务端每页条数', async () => {
    const fetchMock = vi.fn<typeof fetch>(async (input) => {
      const url = String(input)
      if (url === '/api/proxy/api/v1/table-preference') {
        return {
          ok: true,
          json: async () => ({
            density: 'compact',
            tables: {
              'cloud-orders': {
                visibility: { city: false },
                pageSize: 50,
              },
            },
          }),
        } as unknown as Response
      }
      return { ok: true, json: async () => ({ items: [] }) } as unknown as Response
    })
    vi.stubGlobal('fetch', fetchMock)

    const onPaginationChange = vi.fn()
    const { container } = render(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <SessionIdentityProvider identityVersion="id-v1" storagePrincipalId="principal-alice">
          <TablePreferenceCloudSync />
          <TablePreferenceCloudSync />
          <DataTable<Row>
            columns={columns}
            data={rows}
            tableKey="cloud-orders"
            manualPagination
            rowCount={80}
            pageIndex={0}
            manualPageSize={15}
            onPaginationChange={onPaginationChange}
          />
        </SessionIdentityProvider>
      </NextIntlClientProvider>,
    )

    await screen.findByPlaceholderText('搜索...')
    // 同一账号挂载两处也只发一次单飞 GET
    const getCalls = fetchMock.mock.calls.filter(([u]) => String(u) === '/api/proxy/api/v1/table-preference')
    expect(getCalls).toHaveLength(1)

    await waitFor(() => {
      expect(screen.queryByText('城市')).not.toBeInTheDocument()
      expect(onPaginationChange).toHaveBeenCalledWith({ pageIndex: 0, pageSize: 50 })
    })
    expect(JSON.parse(localStorage.getItem('swapmid-table:density') ?? 'null')).toBe('compact')
    // 紧凑行密度会作用到表体单元格高度类
    expect(container.querySelector('tbody td')?.className).toContain('py-1.5')
  })

  it('用户调整列显隐后 500ms 防抖写回云端，恢复默认列且无其他表级偏好时发送空视图清除记录', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const putBodies: { url: string; body: unknown }[] = []
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      const url = String(input)
      if (init?.method === 'PUT') {
        putBodies.push({ url, body: JSON.parse(String(init.body ?? 'null')) })
      }
      return { ok: true, json: async () => ({ density: 'standard', tables: {} }) } as unknown as Response
    })
    vi.stubGlobal('fetch', fetchMock)

    render(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <SessionIdentityProvider identityVersion="id-v1" storagePrincipalId="principal-bob">
          <DataTable<Row>
            columns={columns}
            data={rows}
            tableKey="cloud-edit"
            manualPagination
            rowCount={rows.length}
            pageIndex={0}
            manualPageSize={15}
            onPaginationChange={vi.fn()}
          />
        </SessionIdentityProvider>
      </NextIntlClientProvider>,
    )

    await screen.findByPlaceholderText('搜索...')
    expect(putBodies).toHaveLength(0)

    // 打开「列」面板隐藏「城市」列
    fireEvent.click(screen.getByRole('button', { name: '列' }))
    const cityToggle = await screen.findByRole('checkbox', { name: '城市' })
    fireEvent.click(cityToggle)

    // 防抖尚未到期前不发 PUT
    act(() => { vi.advanceTimersByTime(TABLE_CLOUD_DEBOUNCE_MS - 100) })
    expect(putBodies).toHaveLength(0)

    act(() => { vi.advanceTimersByTime(150) })
    await waitFor(() => expect(putBodies).toHaveLength(1))
    expect(putBodies[0]).toEqual({
      url: '/api/proxy/api/v1/table-preference/table',
      body: {
        tableKey: 'cloud-edit',
        view: expect.objectContaining({
          visibility: { city: false },
        }),
      },
    })

    // 点击「恢复默认列」后发送 view: null 通知后端删除本表设置
    fireEvent.click(screen.getByRole('button', { name: '恢复默认列' }))
    act(() => { vi.advanceTimersByTime(TABLE_CLOUD_DEBOUNCE_MS + 50) })
    await waitFor(() => expect(putBodies).toHaveLength(2))
    expect(putBodies[1]).toEqual({
      url: '/api/proxy/api/v1/table-preference/table',
      body: {
        tableKey: 'cloud-edit',
        view: null,
      },
    })
  })

  it('同浏览器切换账号时清除上一账号残留的 swapmid-table:* 键，防止跨账号偏好串味', () => {
    localStorage.setItem('swapmid-table:owner', JSON.stringify('principal-user-a'))
    localStorage.setItem('swapmid-table:salary:visibility', JSON.stringify({ bonus: false }))
    localStorage.setItem('swapmid-table:salary:pageSize', JSON.stringify(100))
    localStorage.setItem('swapmid-table:density', JSON.stringify('compact'))

    const result = applyCloudTablePreferences('principal-user-b', {
      density: 'standard',
      tables: {
        employees: { pageSize: 20 },
      },
    })

    expect(localStorage.getItem('swapmid-table:salary:visibility')).toBeNull()
    expect(localStorage.getItem('swapmid-table:salary:pageSize')).toBeNull()
    expect(JSON.parse(localStorage.getItem('swapmid-table:employees:pageSize') ?? '0')).toBe(20)
    expect(JSON.parse(localStorage.getItem('swapmid-table:density') ?? 'null')).toBe('standard')
    expect(result.changedTables).toEqual(expect.arrayContaining(['salary', 'employees']))
    expect(result.densityChanged).toBe(true)
  })
})
