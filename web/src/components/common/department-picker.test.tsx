import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor } from '@testing-library/react'
import type { ReactElement } from 'react'
import { NextIntlClientProvider } from 'next-intl'
import { DepartmentPicker } from './department-picker'
import zhHansMessages from '../../../messages/zh-Hans.json'

function render(ui: ReactElement) {
  return rtlRender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

const items = [
  { id: 'root', name: '大研发平台', code: 'RD', level: 1, order: 1, isEnabled: true, enterpriseId: 'e', creationTime: '2020-01-01T00:00:00Z', fullPath: '大研发平台' },
  { id: 'product', parentId: 'root', name: '产品中心', code: 'PD', level: 2, order: 1, isEnabled: true, enterpriseId: 'e', creationTime: '2020-01-01T00:00:00Z', fullPath: '大研发平台/产品中心' },
  { id: 'group', parentId: 'product', name: '产品组', code: 'PG', level: 3, order: 1, isEnabled: true, enterpriseId: 'e', creationTime: '2020-01-01T00:00:00Z', fullPath: '大研发平台/产品中心/产品组' },
]

const fetchMock = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()

beforeEach(() => {
  fetchMock.mockReset()
  fetchMock.mockResolvedValue({
    ok: true,
    json: async () => ({ items, totalCount: items.length }),
  } as Response)
  vi.stubGlobal('fetch', fetchMock)
  window.localStorage.removeItem('swapmid-department-picker-display')
})

async function openPicker() {
  fireEvent.click(screen.getByRole('combobox'))
  await screen.findByRole('treeitem', { name: /大研发平台/ })
}

describe('DepartmentPicker 树状展开', () => {
  it('触发器显示部门名，不含全角空格缩进', async () => {
    render(<DepartmentPicker value="product" onChange={vi.fn()} />)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(screen.getByRole('combobox')).toHaveTextContent('产品中心')
    expect(screen.getByRole('combobox').textContent).not.toMatch(/\u3000/)
  })

  it('打开后默认展开根的下一级，孙级仍收起', async () => {
    render(<DepartmentPicker onChange={vi.fn()} />)
    await openPicker()
    expect(screen.getByRole('treeitem', { name: /产品中心/ })).toBeInTheDocument()
    expect(screen.queryByRole('treeitem', { name: /产品组/ })).not.toBeInTheDocument()
  })

  it('点展开箭头只展开子级，不选中该部门', async () => {
    const onChange = vi.fn()
    render(<DepartmentPicker onChange={onChange} />)
    await openPicker()
    fireEvent.click(screen.getByRole('button', { name: '展开「产品中心」' }))
    expect(await screen.findByRole('treeitem', { name: /产品组/ })).toBeInTheDocument()
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-expanded', 'true')
  })

  it('点部门名选中并关闭', async () => {
    const onChange = vi.fn()
    render(<DepartmentPicker onChange={onChange} />)
    await openPicker()
    fireEvent.click(screen.getByRole('button', { name: '产品中心' }))
    expect(onChange).toHaveBeenCalledWith('product', expect.objectContaining({ id: 'product' }))
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-expanded', 'false')
  })

  it('搜索可找到被收起的孙级', async () => {
    render(<DepartmentPicker onChange={vi.fn()} />)
    await openPicker()
    fireEvent.change(screen.getByPlaceholderText('搜索部门...'), { target: { value: '产品组' } })
    expect(await screen.findByRole('treeitem', { name: /产品组/ })).toBeInTheDocument()
    expect(screen.getByRole('treeitem', { name: /大研发平台/ })).toBeInTheDocument()
  })

  it('收起全部后只留根节点（无选中时）', async () => {
    render(<DepartmentPicker onChange={vi.fn()} />)
    await openPicker()
    fireEvent.click(screen.getByRole('button', { name: '收起全部' }))
    expect(screen.getByRole('treeitem', { name: /大研发平台/ })).toBeInTheDocument()
    expect(screen.queryByRole('treeitem', { name: /产品中心/ })).not.toBeInTheDocument()
  })

  it('清空钮在 trigger 之外', async () => {
    render(<DepartmentPicker value="root" onChange={vi.fn()} clearable />)
    await waitFor(() => expect(screen.getByRole('combobox')).toHaveTextContent('大研发平台'))
    const trigger = screen.getByRole('combobox')
    const clearBtn = screen.getByRole('button', { name: '清空已选' })
    expect(trigger.contains(clearBtn)).toBe(false)
  })
})
