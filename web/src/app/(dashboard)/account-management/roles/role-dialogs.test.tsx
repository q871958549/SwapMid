import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { AssignAccountsDialog, CopyRoleDialog } from './role-dialogs'
import type { AccountRoleDto } from '@/lib/api/types'
import zhHansMessages from '../../../../../messages/zh-Hans.json'

// 浏览器 pane 无头 hydrate 冻结（记忆 preview-headless-verification 坑 3），对话框交互层
// 由本测试兜底（同 page-switch B 组先例）：AssignAccountsDialog 的 diff 计算/两步确认/请求体、
// CopyRoleDialog 的打开与提交路由。文案经 useTranslations('Account'/'Common') 求值——
// 组件树需要 NextIntlClientProvider，真实 zh-Hans messages 直供（同名遮蔽 render，见记忆 web-testing）。
function render(ui: React.ReactElement) {
  return rtlRender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

// AssignAccountsDialog 直接 useRouter().refresh；CopyRoleDialog 经 useCrudForm 内部 refresh。
const { refreshMock } = vi.hoisted(() => ({ refreshMock: vi.fn() }))
vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: refreshMock }) }))
// toast 是全局 store 副作用（两组件与 useCrudForm 同 import 源），mock 掉避免用例间串味。
vi.mock('@/components/ui/use-toast', () => ({ toast: vi.fn() }))

const role: AccountRoleDto = {
  id: 'r-1',
  name: 'ops',
  isDefault: false,
  isStatic: false,
  isPublic: true,
  creationTime: '2026-07-20T00:00:00Z',
  memberCount: 2,
  permissionCount: 10,
  concurrencyStamp: 'stamp-1',
  description: null,
}

// 张三/李四已是 ops 成员，王五不是——初始勾选集应恰为 {张三, 李四}。
const accounts = [
  { id: 'a1', userName: 'zhang', name: '张三', roleNames: ['ops'] },
  { id: 'a2', userName: 'li', name: '李四', roleNames: ['ops', 'admin'] },
  { id: 'a3', userName: 'wang', name: '王五', roleNames: [] as string[] },
]

/** 按 URL 分流的 fetch 桩：for-assignment 回账号清单、set-role-members/copy 回成功。
 * 用 vi.fn<typeof fetch> 锚定完整签名——mock.calls 元组才含第二位 init，取请求体断言时 tsc 不报单元素元组错。 */
function stubFetch() {
  const fetchMock = vi.fn<typeof fetch>(async (url) => {
    const u = String(url)
    if (u.includes('/account-users/for-assignment')) {
      return { ok: true, json: async () => ({ items: accounts }), text: async () => '' } as unknown as Response
    }
    if (u.includes('/account-users/set-role-members')) {
      return { ok: true, json: async () => ({ requested: 2, succeeded: 2, skipped: 0 }), text: async () => '' } as unknown as Response
    }
    if (u.includes('/copy')) {
      return { ok: true, json: async () => ({}), text: async () => '' } as unknown as Response
    }
    throw new Error('unexpected fetch: ' + u)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** 取指定姓名所在账号行（label 包 Checkbox + 姓名/账号名）的复选框。 */
function rowCheckbox(name: string): HTMLElement {
  const label = screen.getByText(name).closest('label')
  if (!label) throw new Error(`未找到「${name}」所在账号行`)
  return within(label).getByRole('checkbox')
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('AssignAccountsDialog · P0-13 分配账号防误伤', () => {
  it('打开即拉 for-assignment：初始勾选 = 现有成员集，diff 为空时「保存」禁用', async () => {
    stubFetch()
    render(<AssignAccountsDialog role={role} onClose={vi.fn()} />)

    await screen.findByText('张三')
    expect(rowCheckbox('张三')).toBeChecked()
    expect(rowCheckbox('李四')).toBeChecked()
    expect(rowCheckbox('王五')).not.toBeChecked()
    // 勾选集 ≡ 初始成员集 → diff 为空 → 保存禁用（旧实现空变更也会发请求，此即防误伤第一层）
    expect(screen.getByRole('button', { name: '保存' })).toBeDisabled()
  })

  it('勾掉一名成员并勾入一名非成员后保存可点；点「保存」出确认摘要（新增/移出人数 + 移出姓名，destructive）', async () => {
    stubFetch()
    render(<AssignAccountsDialog role={role} onClose={vi.fn()} />)
    await screen.findByText('张三')

    fireEvent.click(rowCheckbox('王五')) // 新增 a3
    fireEvent.click(rowCheckbox('张三')) // 移出 a1
    const saveBtn = screen.getByRole('button', { name: '保存' })
    expect(saveBtn).toBeEnabled()

    fireEvent.click(saveBtn)
    // 两模态互斥切换：确认摘要出现（列表 Dialog 已关，同刻只一个 Radix 模态挂载）
    expect(screen.getByText('确认成员变更')).toBeInTheDocument()
    expect(screen.getByText('新增 1 人、移出 1 人')).toBeInTheDocument()
    // 移出 > 0：列出被移出者姓名（destructive 样式的 span）
    expect(screen.getByText('移出的 1 人包括：张三')).toBeInTheDocument()
  })

  it('只新增不移出时，确认摘要不出现「包括」姓名清单', async () => {
    stubFetch()
    render(<AssignAccountsDialog role={role} onClose={vi.fn()} />)
    await screen.findByText('张三')

    fireEvent.click(rowCheckbox('王五')) // 仅新增
    fireEvent.click(screen.getByRole('button', { name: '保存' }))

    expect(screen.getByText('新增 1 人、移出 0 人')).toBeInTheDocument()
    expect(screen.queryByText(/包括/)).not.toBeInTheDocument()
  })

  it('确认后发 set-role-members：userIds=最终勾选集、roleName=目标角色；成功后 onClose + router.refresh', async () => {
    const onClose = vi.fn()
    const onSaved = vi.fn()
    const fetchMock = stubFetch()
    render(<AssignAccountsDialog role={role} onClose={onClose} onSaved={onSaved} />)
    await screen.findByText('张三')

    fireEvent.click(rowCheckbox('王五')) // +a3
    fireEvent.click(rowCheckbox('张三')) // -a1
    fireEvent.click(screen.getByRole('button', { name: '保存' }))
    fireEvent.click(screen.getByRole('button', { name: '确认变更' }))

    await waitFor(() => {
      expect(fetchMock.mock.calls.some(c => String(c[0]).includes('set-role-members'))).toBe(true)
    })
    const call = fetchMock.mock.calls.find(c => String(c[0]).includes('set-role-members'))!
    expect(String(call[0])).toBe('/api/proxy/account-users/set-role-members')
    const init = call[1]
    expect(init?.method).toBe('POST')
    const body = JSON.parse(String(init?.body)) as { roleName: string; userIds: string[] }
    expect(body.roleName).toBe('ops')
    expect([...body.userIds].sort()).toEqual(['a2', 'a3']) // 留任李四 + 新增王五；张三已被移出

    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(onSaved).toHaveBeenCalled()
    expect(refreshMock).toHaveBeenCalled() // 写后失效 Router Cache 铁律
  })

  it('确认步取消回到成员列表（而非整体关闭），不发 set-role-members', async () => {
    const onClose = vi.fn()
    const fetchMock = stubFetch()
    render(<AssignAccountsDialog role={role} onClose={onClose} />)
    await screen.findByText('张三')

    fireEvent.click(rowCheckbox('王五'))
    fireEvent.click(screen.getByRole('button', { name: '保存' }))
    expect(screen.getByText('确认成员变更')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '取消' }))
    // 回到列表模态：账号清单重新可见，父级未被 onClose，也未发写请求
    expect(await screen.findByText('张三')).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
    expect(fetchMock.mock.calls.some(c => String(c[0]).includes('set-role-members'))).toBe(false)
  })
})

describe('CopyRoleDialog · P0-11 角色复制前端接线', () => {
  it('source 传入后弹窗打开，标题带源角色名；名称为空时「创建」禁用', async () => {
    stubFetch()
    render(<CopyRoleDialog source={role} onClose={vi.fn()} />)

    expect(await screen.findByText('复制角色 · ops')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '创建' })).toBeDisabled()
  })

  it('填新角色名提交 → POST /api/proxy/account-roles/{sourceId}/copy 且 body={name}；成功后通知父组件关闭', async () => {
    const onClose = vi.fn()
    const fetchMock = stubFetch()
    render(<CopyRoleDialog source={role} onClose={onClose} />)
    await screen.findByText('复制角色 · ops')

    fireEvent.change(screen.getByPlaceholderText('role_name'), { target: { value: 'ops_copy' } })
    const createBtn = screen.getByRole('button', { name: '创建' })
    expect(createBtn).toBeEnabled()
    fireEvent.click(createBtn)

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    const [url, init] = fetchMock.mock.calls[0]
    expect(String(url)).toBe('/api/proxy/account-roles/r-1/copy')
    expect(init?.method).toBe('POST')
    expect(JSON.parse(String(init?.body))).toEqual({ name: 'ops_copy' })

    // 提交成功 → useCrudForm close → isOpen 落 false → 组件经 effect 通知父组件清空 copyTarget
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })
})
