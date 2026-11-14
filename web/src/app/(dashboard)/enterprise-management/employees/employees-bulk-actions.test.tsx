import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { useEmployeeBulkActions, EmployeeBulkDialogs } from './employees-bulk-actions'
import { ACCOUNT_PROVISION_CHUNK_SIZE } from './constants'
import type { AccountRoleDto } from '@/lib/api/types'
import zhHansMessages from '../../../../../messages/zh-Hans.json'

// 浏览器 pane 无头 hydrate 冻结（记忆 preview-headless-verification 坑 3），对话框交互层由本测试
// 兜底（同 roles/role-dialogs.test.tsx P0-13 先例）：P2-L1 批量开通指定角色的两个弹窗——
// 角色清单懒加载、勾选后请求体 roleNames、不勾选时 roleNames 为空、勾选行路径 ids+roleNames 同传。
// 文案经 useTranslations('Enterprise'/'Common') 求值——组件树需要 NextIntlClientProvider，
// 真实 zh-Hans messages 直供（同名遮蔽 render，见记忆 web-testing）。
function render(ui: React.ReactElement) {
  return rtlRender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

// toast 是全局 store 副作用，mock 掉避免用例间串味（同 role-dialogs.test.tsx）。
vi.mock('@/components/ui/use-toast', () => ({ toast: vi.fn() }))

const roles: AccountRoleDto[] = [
  { id: 'r-1', name: 'ops', isDefault: false, isStatic: false, isPublic: true, creationTime: '2026-07-20T00:00:00Z', memberCount: 0, permissionCount: 5, concurrencyStamp: 's1', description: null },
  { id: 'r-2', name: 'scanner', isDefault: false, isStatic: false, isPublic: true, creationTime: '2026-07-20T00:00:00Z', memberCount: 0, permissionCount: 3, concurrencyStamp: 's2', description: null },
]

/** 按 URL 分流的 fetch 桩：assignable-roles 回角色清单、两个开通端点回成功摘要。
 * 用 vi.fn<typeof fetch> 锚定完整签名——mock.calls 元组才含第二位 init，取请求体断言时 tsc 不报单元素元组错。
 * `holdFirstProvision`：卡住第一片开通请求，让「显示进度」用例在 fetch 完成前断言文案。
 * `holdEachProvision`：每一片都停住，由用例逐片放行，避免真实定时器拖过用例结束。 */
function stubFetch(options?: {
  holdFirstProvision?: Promise<void>
  holdEachProvision?: () => Promise<void>
}) {
  let provisionCalls = 0
  const fetchMock = vi.fn<typeof fetch>(async (url) => {
    const u = String(url)
    if (u.includes('/account-users/assignable-roles')) {
      return { ok: true, json: async () => ({ items: roles }), text: async () => '' } as unknown as Response
    }
    if (u.includes('/employees/batch-generate-accounts')) {
      provisionCalls += 1
      if (options?.holdEachProvision) {
        await options.holdEachProvision()
      } else if (provisionCalls === 1 && options?.holdFirstProvision) {
        await options.holdFirstProvision
      } else {
        await new Promise(resolve => setTimeout(resolve, 40))
      }
      return { ok: true, json: async () => ({ created: 2, alreadyBound: 0, skipped: 0 }), text: async () => '' } as unknown as Response
    }
    if (u.includes('/employees?')) {
      return {
        ok: true,
        json: async () => ({ items: [{ id: 'e-pending-1' }, { id: 'e-pending-2' }], totalCount: 2 }),
        text: async () => '',
      } as unknown as Response
    }
    throw new Error('unexpected fetch: ' + u)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/**
 * 测试装配层：useEmployeeBulkActions 是 hook、EmployeeBulkDialogs 是其消费方（真实页面里由
 * employees/client.tsx 装配），测试用最小 harness 复刻同一装配——入口按钮对应页头
 * 「全员开通」（openBulkDialog）与选中行浮层「批量生成账号」（openBatchGenerateDialog）。
 */
function Harness({ reload, ids, clear }: { reload: () => void; ids: string[]; clear: () => void }) {
  const bulk = useEmployeeBulkActions(reload)
  return (
    <>
      <button onClick={() => bulk.openBulkDialog()}>OPEN_BULK</button>
      <button onClick={() => bulk.openBatchGenerateDialog(ids, clear)}>OPEN_BATCH</button>
      <EmployeeBulkDialogs bulk={bulk} pendingAccountCount={3} />
    </>
  )
}

function renderHarness(ids: string[] = []) {
  const reload = vi.fn()
  const clear = vi.fn()
  render(<Harness reload={reload} ids={ids} clear={clear} />)
  return { reload, clear }
}

/** 取指定角色名所在清单行（label 包 Checkbox + 角色名）的复选框。 */
function roleCheckbox(name: string): HTMLElement {
  const label = screen.getByText(name).closest('label')
  if (!label) throw new Error(`未找到「${name}」所在角色行`)
  return within(label).getByRole('checkbox')
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('EmployeeBulkDialogs · P2-L1 批量开通指定角色', () => {
  it('「一键开通全员」弹窗：打开即懒加载角色清单，勾选一个后确认 → 先拉未开号名单再分片 batch-generate-accounts', async () => {
    const fetchMock = stubFetch()
    const { reload } = renderHarness()

    fireEvent.click(screen.getByRole('button', { name: 'OPEN_BULK' }))
    expect(screen.getByText('为全部未开通账号的在职员工开通账号？')).toBeInTheDocument()
    // 懒加载：打开时经 /api/proxy/account-users/assignable-roles 拉候选角色
    await screen.findByText('ops')
    expect(fetchMock.mock.calls.some(c => String(c[0]).includes('/account-users/assignable-roles'))).toBe(true)
    expect(screen.getByText('scanner')).toBeInTheDocument()

    fireEvent.click(roleCheckbox('ops'))
    fireEvent.click(screen.getByRole('button', { name: '确认开通' }))

    await waitFor(() => {
      expect(fetchMock.mock.calls.some(c => String(c[0]).includes('/employees?'))).toBe(true)
      expect(fetchMock.mock.calls.some(c => String(c[0]).includes('batch-generate-accounts'))).toBe(true)
    })
    const listCall = fetchMock.mock.calls.find(c => String(c[0]).includes('/employees?'))!
    expect(String(listCall[0])).toContain('status=0')
    expect(String(listCall[0])).toContain('hasAccount=false')
    const call = fetchMock.mock.calls.find(c => String(c[0]).includes('batch-generate-accounts'))!
    expect(String(call[0])).toBe('/api/proxy/employees/batch-generate-accounts')
    const init = call[1]
    expect(init?.method).toBe('POST')
    const body = JSON.parse(String(init?.body)) as { ids: string[]; roleNames: string[] }
    expect(body.ids).toEqual(['e-pending-1', 'e-pending-2'])
    expect(body.roleNames).toEqual(['ops'])
    // 开通桩在计数后还有延时；等到成功收尾，避免 finally 的 setState 越过用例结束。
    await waitFor(() => expect(reload).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: '开通中…' })).not.toBeInTheDocument()
  })

  it('「一键开通全员」不勾选任何角色直接确认 → roleNames 为空（沿用现行为，仅授默认角色）', async () => {
    const fetchMock = stubFetch()
    const { reload } = renderHarness()

    fireEvent.click(screen.getByRole('button', { name: 'OPEN_BULK' }))
    await screen.findByText('ops') // 等角色清单就绪，确保和「勾选」用例同一时序，只差勾选动作
    fireEvent.click(screen.getByRole('button', { name: '确认开通' }))

    await waitFor(() => {
      expect(fetchMock.mock.calls.some(c => String(c[0]).includes('batch-generate-accounts'))).toBe(true)
    })
    const call = fetchMock.mock.calls.find(c => String(c[0]).includes('batch-generate-accounts'))!
    const body = JSON.parse(String(call[1]?.body)) as { ids: string[]; roleNames: string[] }
    expect(body.ids).toEqual(['e-pending-1', 'e-pending-2'])
    expect(body.roleNames).toEqual([])
    await waitFor(() => expect(reload).toHaveBeenCalled())
    expect(screen.queryByRole('button', { name: '开通中…' })).not.toBeInTheDocument()
  })

  it('勾选行「批量生成账号」弹窗：勾选角色确认 → batch-generate-accounts 请求体同时含 ids 与 roleNames；成功后 clear + reload', async () => {
    const fetchMock = stubFetch()
    const { reload, clear } = renderHarness(['e1', 'e2'])

    fireEvent.click(screen.getByRole('button', { name: 'OPEN_BATCH' }))
    expect(screen.getByText('为选中的 2 名员工生成账号？')).toBeInTheDocument()
    await screen.findByText('scanner')

    fireEvent.click(roleCheckbox('scanner'))
    fireEvent.click(screen.getByRole('button', { name: '批量生成账号' }))
    // 单片开通成功后立刻关弹窗并清掉进度（「已处理 0 / N」只存在于那一次 fetch
    // 的几十毫秒内）。CI 自托管争用下 findByText(/已处理/) 会闪过；本用例契约是
    // 请求体 ids+roleNames 以及成功后 clear/reload，进度可见性由下一例覆盖。

    await waitFor(() => {
      expect(fetchMock.mock.calls.some(c => String(c[0]).includes('batch-generate-accounts'))).toBe(true)
    })
    const call = fetchMock.mock.calls.find(c => String(c[0]).includes('batch-generate-accounts'))!
    expect(String(call[0])).toBe('/api/proxy/employees/batch-generate-accounts')
    const init = call[1]
    expect(init?.method).toBe('POST')
    const body = JSON.parse(String(init?.body)) as { ids: string[]; roleNames: string[] }
    expect(body.ids).toEqual(['e1', 'e2'])
    expect(body.roleNames).toEqual(['scanner'])

    // 成功路径收尾：清空行选中 + 重取列表（与既有 confirmBatchDelete 同约定）。
    // clear 发生在 finally 的 setBatchBusy 之前的同一轮同步代码里，等到它被调用时 busy 已复位。
    await waitFor(() => expect(clear).toHaveBeenCalled())
    expect(reload).toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: '开通中…' })).not.toBeInTheDocument()
  })

  it('勾选超过分片大小时按 10 人一片提交，并显示进度', async () => {
    const releaseProvision: Array<() => void> = []
    const fetchMock = stubFetch({
      holdEachProvision: () => new Promise<void>(resolve => { releaseProvision.push(resolve) }),
    })
    const ids = Array.from({ length: ACCOUNT_PROVISION_CHUNK_SIZE + 1 }, (_, i) => `e${i}`)
    const { reload, clear } = renderHarness(ids)

    fireEvent.click(screen.getByRole('button', { name: 'OPEN_BATCH' }))
    await screen.findByText('ops')
    fireEvent.click(screen.getByRole('button', { name: '批量生成账号' }))
    // 第一片被卡住，进度与「开通中」稳定可见，避免成功后立刻卸掉导致 findBy 闪过。
    expect(await screen.findByText(`已处理 0 / ${ids.length}`)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '开通中…' })).toBeDisabled()
    expect(releaseProvision).toHaveLength(1)
    releaseProvision[0]!()

    await waitFor(() => {
      const calls = fetchMock.mock.calls.filter(c => String(c[0]).includes('batch-generate-accounts'))
      expect(calls).toHaveLength(2)
      expect(releaseProvision).toHaveLength(2)
    })
    // 第二片仍被卡住，进度停在第一片完成之后。
    expect(screen.getByText(`已处理 ${ACCOUNT_PROVISION_CHUNK_SIZE} / ${ids.length}`)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '开通中…' })).toBeDisabled()

    const calls = fetchMock.mock.calls.filter(c => String(c[0]).includes('batch-generate-accounts'))
    const first = JSON.parse(String(calls[0][1]?.body)) as { ids: string[] }
    const second = JSON.parse(String(calls[1][1]?.body)) as { ids: string[] }
    expect(first.ids).toHaveLength(ACCOUNT_PROVISION_CHUNK_SIZE)
    expect(second.ids).toHaveLength(1)
    expect([...first.ids, ...second.ids]).toEqual(ids)

    releaseProvision[1]!()
    // 计数到 2 只说明第二片已发出。必须等到该片返回、进度卸掉、busy 复位，
    // 否则 confirmBatchGenerate 的 finally 会在环境拆除后 setState。
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: '开通中…' })).not.toBeInTheDocument()
      expect(screen.queryByText(/已处理/)).not.toBeInTheDocument()
      expect(clear).toHaveBeenCalledTimes(1)
      expect(reload).toHaveBeenCalledTimes(1)
    })
  })
})
