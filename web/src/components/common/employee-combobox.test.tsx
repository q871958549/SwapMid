import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { EmployeeCombobox } from './employee-combobox'
import zhHansMessages from '../../../messages/zh-Hans.json'

/**
 * EmployeeCombobox 内部用 <RemoteCombobox>（i18n 批次3d 起无条件调用 useTranslations('Common')
 * 求默认文案，即使调用方已显式传 placeholder/searchPlaceholder/emptyText 也逃不过 hook 本身
 * 要求 NextIntlClientProvider 上下文），故本文件测试树也需要该 Provider 才能渲染。
 */
function render(ui: React.ReactElement) {
  return rtlRender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

/** 构造 fetch 响应桩：ok/status + items 载荷。 */
function mockResponse(status: number, items: unknown[] = []) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => ({ items, totalCount: items.length }),
  } as Response
}

const fetchMock = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()

beforeEach(() => {
  fetchMock.mockReset()
  vi.stubGlobal('fetch', fetchMock)
})

/** 打开下拉触发首次拉取（空 query 不防抖）。 */
function openCombobox() {
  fireEvent.click(screen.getByRole('combobox'))
}

describe('EmployeeCombobox 搜索失败反馈', () => {
  it('403 时空态显示权限提示,而非「未找到匹配员工」', async () => {
    fetchMock.mockResolvedValue(mockResponse(403))
    render(<EmployeeCombobox onChange={vi.fn()} />)
    openCombobox()
    expect(
      await screen.findByText('员工目录暂不可用，请重新登录后再试')
    ).toBeInTheDocument()
    expect(screen.queryByText('未找到匹配员工')).not.toBeInTheDocument()
  })

  it('其他非 2xx 时显示带状态码的失败提示', async () => {
    fetchMock.mockResolvedValue(mockResponse(500))
    render(<EmployeeCombobox onChange={vi.fn()} />)
    openCombobox()
    expect(await screen.findByText('员工搜索失败（HTTP 500），请稍后重试')).toBeInTheDocument()
  })

  it('请求成功但无结果时仍显示「未找到匹配员工」', async () => {
    fetchMock.mockResolvedValue(mockResponse(200, []))
    render(<EmployeeCombobox onChange={vi.fn()} />)
    openCombobox()
    expect(await screen.findByText('未找到匹配员工')).toBeInTheDocument()
  })

  it('403 后再次搜索成功,错误提示清除并显示员工选项', async () => {
    fetchMock.mockResolvedValueOnce(mockResponse(403))
    render(<EmployeeCombobox onChange={vi.fn()} />)
    openCombobox()
    await screen.findByText('员工目录暂不可用，请重新登录后再试')

    fetchMock.mockResolvedValue(
      mockResponse(200, [{ id: 'e1', name: '田烨', employeeNo: 'HAN005170', departmentName: 'IT部', status: 0 }])
    )
    fireEvent.change(screen.getByPlaceholderText('输入姓名 / 工号 / 拼音首字母...'), { target: { value: '田' } })
    // 输入触发 300ms 防抖后的第二次拉取
    expect(await screen.findByText('田烨')).toBeInTheDocument()
    expect(
      screen.queryByText('员工目录暂不可用，请重新登录后再试')
    ).not.toBeInTheDocument()
  })
})

describe('EmployeeCombobox 正常路径', () => {
  it('重新打开时精确匹配员工 Id 补部门信息，跨部门范围不会清空已选人员', async () => {
    const onChange = vi.fn()
    fetchMock.mockResolvedValue(mockResponse(200, [
      { id: 'other', name: '张三', employeeNo: 'H001', departmentName: '财务部' },
      { id: 'chosen', name: '张三', employeeNo: 'H002', departmentName: '技术中心', status: 1 },
    ]))
    render(<EmployeeCombobox value="chosen" selectedName="张三" departmentId="quality" onChange={onChange} showSelectedDetails showStatus />)
    expect(await screen.findByText('H002 · 技术中心 · 停薪留职')).toBeInTheDocument()
    const query = new URL(String(fetchMock.mock.calls[0][0]), 'http://localhost').searchParams
    expect(query.get('filter')).toBe('张三')
    expect(query.get('maxResultCount')).toBe('20')
    expect(query.has('departmentId')).toBe(false)
    expect(screen.queryByText('H001 · 财务部')).not.toBeInTheDocument()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('补充展示查询失败时仍回显原姓名，且不清空原员工 Id', async () => {
    const onChange = vi.fn()
    fetchMock.mockRejectedValue(new Error('offline'))
    render(<EmployeeCombobox value="chosen" selectedName="张三" onChange={onChange} showSelectedDetails />)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    expect(screen.getByRole('combobox')).toHaveTextContent('张三')
    expect(onChange).not.toHaveBeenCalled()
  })

  it('切换已选员工后忽略旧的补充信息，即使请求端未响应取消', async () => {
    let resolveOld!: (response: Response) => void
    fetchMock.mockImplementationOnce(() => new Promise<Response>(resolve => { resolveOld = resolve }))
      .mockResolvedValue(mockResponse(200, [{ id: 'new', name: '李四', employeeNo: 'H002', departmentName: '技术中心' }]))
    const onChange = vi.fn()
    const { rerender } = render(<EmployeeCombobox value="old" selectedName="张三" onChange={onChange} showSelectedDetails />)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    rerender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <EmployeeCombobox value="new" selectedName="李四" onChange={onChange} showSelectedDetails />
    </NextIntlClientProvider>)
    expect(await screen.findByText('H002 · 技术中心')).toBeInTheDocument()
    await act(async () => { resolveOld(mockResponse(200, [{ id: 'old', name: '张三', employeeNo: 'H001', departmentName: '旧部门' }])) })
    expect(screen.queryByText('H001 · 旧部门')).not.toBeInTheDocument()
    expect(screen.getByRole('combobox')).toHaveTextContent('李四')
  })

  it('传入部门时请求带 departmentId 与 includeChildDepartments', async () => {
    fetchMock.mockResolvedValue(mockResponse(200, []))
    render(<EmployeeCombobox onChange={vi.fn()} departmentId="dept-1" />)
    openCombobox()
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    const url = String(fetchMock.mock.calls[0][0])
    expect(url).toContain('departmentId=dept-1')
    expect(url).toContain('includeChildDepartments=true')
  })

  it('requireDepartment 且未选部门时禁用、不发请求', () => {
    render(<EmployeeCombobox onChange={vi.fn()} requireDepartment />)
    expect(screen.getByRole('combobox')).toBeDisabled()
    expect(screen.getByRole('combobox')).toHaveTextContent('请先选择部门')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('请求带 maxResultCount,选项渲染工号 · 部门 description', async () => {
    fetchMock.mockResolvedValue(
      mockResponse(200, [{ id: 'e1', name: '田烨', employeeNo: 'HAN005170', departmentName: 'IT部', status: 2 }])
    )
    render(<EmployeeCombobox onChange={vi.fn()} showStatus />)
    openCombobox()
    expect(await screen.findByText('田烨')).toBeInTheDocument()
    expect(screen.getByText('HAN005170 · IT部 · 已离职')).toBeInTheDocument()
    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/proxy/employee-lookup?maxResultCount=20')
  })

  it('选中选项回调员工 Id + 姓名', async () => {
    const onChange = vi.fn()
    fetchMock.mockResolvedValue(
      mockResponse(200, [{ id: 'e1', name: '田烨', employeeNo: 'HAN005170', departmentName: 'IT部', status: 0 }])
    )
    render(<EmployeeCombobox onChange={onChange} />)
    openCombobox()
    fireEvent.click(await screen.findByText('田烨'))
    await waitFor(() => expect(onChange).toHaveBeenCalledWith('e1', '田烨'))
  })

  it('把 RemoteCombobox 的 AbortSignal 原样传给 fetch，卸载时取消请求', async () => {
    let fetchSignal: AbortSignal | undefined
    fetchMock.mockImplementation((_input, init) => {
      fetchSignal = init?.signal ?? undefined
      return new Promise((_resolve, reject) => {
        fetchSignal?.addEventListener(
          'abort',
          () => reject(new DOMException('aborted', 'AbortError')),
          { once: true },
        )
      })
    })
    const { unmount } = render(<EmployeeCombobox onChange={vi.fn()} />)

    openCombobox()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    expect(fetchSignal).toBeInstanceOf(AbortSignal)
    expect(fetchSignal?.aborted).toBe(false)

    unmount()
    expect(fetchSignal?.aborted).toBe(true)
  })
})
