import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { DictItemsDialog } from './dict-items-dialog'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import { PermissionsProvider } from '@/components/providers/permissions-provider'

const read = 'SwapMid.SystemManagement.DataDictionaries'
const update = read + '.Update'
function render(ui: React.ReactElement, permissions = { [read]: true, [update]: true }) {
  return rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <PermissionsProvider value={permissions}>{ui}</PermissionsProvider>
    </NextIntlClientProvider>,
  )
}

const { refreshMock } = vi.hoisted(() => ({ refreshMock: vi.fn() }))
vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: refreshMock }) }))
vi.mock('@/components/ui/use-toast', () => ({ toast: vi.fn() }))

const dict = {
  id: 'dict-1',
  code: 'order_status',
  name: '订单状态',
  description: null,
  isEnabled: true,
  itemCount: 3,
  creationTime: '2026-09-01T00:00:00Z',
}

const items = [
  { id: 'i1', dictionaryId: 'dict-1', label: '待处理', value: 'PENDING', sortOrder: 1, isEnabled: true, description: null },
  { id: 'i2', dictionaryId: 'dict-1', label: '处理中', value: 'PROCESSING', sortOrder: 2, isEnabled: true, description: null },
  { id: 'i3', dictionaryId: 'dict-1', label: '已完成', value: 'COMPLETED', sortOrder: 3, isEnabled: true, description: null },
]

function stubFetch() {
  const fetchMock = vi.fn<typeof fetch>(async (url, init) => {
    const u = String(url)
    if (u.endsWith('/items') && !init?.method) {
      return { ok: true, json: async () => ({ items }), text: async () => '' } as unknown as Response
    }
    throw new Error('unexpected fetch: ' + u)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('DictItemsDialog', () => {
  it('只读用户可查看字典项但没有新增编辑删除按钮', async () => {
    const fetchMock = stubFetch()
    render(<DictItemsDialog dict={dict} open onOpenChange={vi.fn()} />, { [read]: true, [update]: false })
    expect(await screen.findByText('待处理')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '新增字典项' })).toBeNull()
    expect(screen.queryByRole('button', { name: '编辑' })).toBeNull()
    expect(screen.queryByRole('button', { name: '删除' })).toBeNull()
    expect(fetchMock.mock.calls.every(([, init]) => !init?.method)).toBe(true)
  })

  it('维护用户新增字典项时自动递增排序号并默认同步值', async () => {
    stubFetch()
    render(<DictItemsDialog dict={dict} open onOpenChange={vi.fn()} />)

    expect(await screen.findByText('待处理')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: '编辑' })).toHaveLength(3)
    expect(screen.getAllByRole('button', { name: '删除' })).toHaveLength(3)

    fireEvent.click(screen.getByRole('button', { name: '新增字典项' }))

    const labelInput = await screen.findByLabelText('显示文本 *')
    const valueInput = screen.getByLabelText('值')
    const sortInput = screen.getByLabelText('排序号')

    await waitFor(() => {
      expect(sortInput).toHaveValue(4)
    })

    fireEvent.change(labelInput, { target: { value: '已取消' } })
    expect(valueInput).toHaveValue('已取消')
  })
})
