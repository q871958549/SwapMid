import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { TooltipProvider } from '@/components/ui/tooltip'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import messages from '../../../../../messages/zh-Hans.json'
import { PermissionsClient } from './client'

const { fetchMock, routeState } = vi.hoisted(() => ({ fetchMock: vi.fn(), routeState: { role: 'Sales' } }))
vi.mock('next/navigation', () => ({
  useSearchParams: () => new URLSearchParams({ roleName: routeState.role }),
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), replace: vi.fn() }),
  usePathname: () => '/account-management/permissions',
}))
vi.mock('@/components/ui/remote-combobox', () => ({
  RemoteCombobox: ({ onChange, disabled }: { onChange: (role: string) => void; disabled: boolean }) =>
    <button disabled={disabled} onClick={() => onChange('Other')}>切换测试角色</button>,
}))
vi.mock('./compare-dialog', () => ({ ComparePermissionsDialog: () => null }))
vi.mock('./export-permission-report-button', () => ({ ExportPermissionReportButton: () => null }))
vi.mock('@/components/ui/use-toast', () => ({ toast: vi.fn() }))

const response = () => ({ entityDisplayName: 'Sales', groups: [
  { name: 'Sales', displayName: '销售', permissions: [
    { name: 'Sales.Orders', displayName: '订单', parentName: null, isGranted: true },
    { name: 'Sales.Orders.Create', displayName: '新建订单', parentName: 'Sales.Orders', isGranted: true },
    { name: 'Sales.Orders.Delete', displayName: '删除订单', parentName: 'Sales.Orders', isGranted: true },
  ] },
  { name: 'Customs', displayName: '报关', permissions: [
    { name: 'Customs.Products', displayName: '物料', parentName: null, isGranted: true },
    { name: 'Customs.Products.Delete', displayName: '删除物料', parentName: 'Customs.Products', isGranted: true },
  ] },
] })
const p = messages.Account.permission
function content(identityVersion = 'permission-test-identity') {
  return <SessionIdentityProvider identityVersion={identityVersion} storagePrincipalId="permission-test-user"><NextIntlClientProvider locale="zh-Hans" messages={messages}><TooltipProvider><PermissionsClient /></TooltipProvider></NextIntlClientProvider></SessionIdentityProvider>
}
function show(identityVersion?: string) { return render(content(identityVersion)) }
beforeEach(() => {
  routeState.role = 'Sales'
  window.sessionStorage.clear()
  fetchMock.mockReset()
  vi.stubGlobal('fetch', fetchMock)
})
afterEach(() => vi.unstubAllGlobals())

describe('角色权限编辑', () => {
  it('筛选销售时取消全部删除仍包含报关，只向审计接口提交撤销差异', async () => {
    fetchMock.mockImplementation(async (_path: string, init?: RequestInit) =>
      init?.method === 'PUT' ? new Response(null, { status: 204 })
        : Response.json(_path.includes('guides') ? [] : response()))
    show()
    const revoke = await screen.findByRole('button', { name: p.revokeDeleteButton })
    await waitFor(() => expect(revoke).toBeEnabled())
    fireEvent.change(screen.getByPlaceholderText(p.moduleSearchPlaceholder), { target: { value: '销售' } })
    fireEvent.click(revoke)
    const confirm = await screen.findByRole('alertdialog', { name: p.revokeDeleteTitle })
    expect(within(confirm).getByText(/报关 · 物料 \/ 删除物料/)).toBeInTheDocument()
    fireEvent.click(within(confirm).getByRole('button', { name: p.revokeDeleteConfirm }))
    fireEvent.click(screen.getByRole('button', { name: p.saveButton }))
    fireEvent.click(await screen.findByRole('button', { name: p.confirmSave }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/proxy/account-roles/Sales/permissions', expect.objectContaining({
      method: 'PUT', body: JSON.stringify({ permissions: [
        { name: 'Sales.Orders.Delete', isGranted: false },
        { name: 'Customs.Products.Delete', isGranted: false },
      ] }),
    })))
    await waitFor(() => expect(screen.getByRole('button', { name: p.saveButton })).toBeDisabled())
  })

  it('前一个角色的慢响应不能覆盖新角色权限', async () => {
    let resolveOld!: (value: Response) => void
    fetchMock.mockImplementation((path: string) => {
      if (path.includes('guides')) return Promise.resolve(Response.json([]))
      if (path.includes('providerKey=Sales')) return new Promise<Response>(resolve => { resolveOld = resolve })
      return Promise.resolve(Response.json({ entityDisplayName: 'Other', groups: [] }))
    })
    show()
    await waitFor(() => expect(resolveOld).toBeTypeOf('function'))
    fireEvent.click(screen.getByRole('button', { name: '切换测试角色' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('providerKey=Other'), expect.anything()))
    resolveOld(Response.json(response()))
    await waitFor(() => expect(screen.getByRole('button', { name: p.revokeDeleteButton })).toBeDisabled())
    expect(screen.queryByText('删除订单')).not.toBeInTheDocument()
  })

  it('搜索删除后批量撤权只修改匹配项，恢复勾选后不再有草稿', async () => {
    fetchMock.mockImplementation(async (path: string) => Response.json(path.includes('guides') ? [] : response()))
    show()
    await screen.findByRole('checkbox', { name: '删除订单' })
    fireEvent.change(screen.getByPlaceholderText(p.moduleSearchPlaceholder), { target: { value: '删除' } })
    const select = screen.getByRole('checkbox', { name: p.selectFilteredAria })
    fireEvent.click(select)
    expect(screen.getByRole('checkbox', { name: p.viewLabel })).toBeChecked()
    expect(screen.getByText('待保存 2 项')).toBeInTheDocument()
    fireEvent.click(select)
    expect(screen.getByRole('button', { name: p.saveButton })).toBeDisabled()
  })

  it('切换角色先确认放弃，取消时保留草稿', async () => {
    fetchMock.mockImplementation(async (path: string) => Response.json(path.includes('guides') ? [] : response()))
    show()
    fireEvent.click(await screen.findByRole('checkbox', { name: '删除订单' }))
    fireEvent.click(screen.getByRole('button', { name: '切换测试角色' }))
    const dialog = await screen.findByRole('alertdialog', { name: p.discardTitle })
    fireEvent.click(within(dialog).getByRole('button', { name: messages.Common.cancel }))
    expect(screen.getByRole('checkbox', { name: '删除订单' })).not.toBeChecked()
    expect(fetchMock.mock.calls.some(([path]) => path.includes('providerKey=Other'))).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: '切换测试角色' }))
    fireEvent.click(await screen.findByRole('button', { name: p.discardConfirm }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('providerKey=Other'), expect.anything()))
  })

  it('权限说明加载失败时不可编辑，重试后恢复', async () => {
    let fail = true
    fetchMock.mockImplementation(async (path: string) => path.includes('guides') && fail ? new Response('说明暂不可用', { status: 503 }) : Response.json(path.includes('guides') ? [] : response()))
    show()
    await screen.findByRole('alert')
    expect(screen.getByRole('button', { name: p.saveButton })).toBeDisabled()
    expect(screen.queryByRole('checkbox', { name: '删除订单' })).not.toBeInTheDocument()
    fail = false
    fireEvent.click(screen.getByRole('button', { name: p.retry }))
    expect(await screen.findByRole('checkbox', { name: '删除订单' })).toBeEnabled()
  })

  it('写入失败保留草稿并允许在预览中重试', async () => {
    let fail = true
    fetchMock.mockImplementation(async (path: string, init?: RequestInit) => init?.method === 'PUT'
      ? new Response(fail ? '保存暂不可用' : null, { status: fail ? 503 : 204 }) : Response.json(path.includes('guides') ? [] : response()))
    show()
    fireEvent.click(await screen.findByRole('checkbox', { name: '删除订单' }))
    fireEvent.click(screen.getByRole('button', { name: p.saveButton }))
    fireEvent.click(await screen.findByRole('button', { name: p.confirmSave }))
    expect(await screen.findByRole('alert')).toHaveTextContent('保存暂不可用')
    fail = false
    fireEvent.click(screen.getByRole('button', { name: p.confirmSave }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: p.reviewTitle })).not.toBeInTheDocument())
    expect(screen.getByRole('checkbox', { name: '删除订单' })).not.toBeChecked()
  })

  it('写前发现其他管理员变更时不覆盖，保留草稿提示重新核对', async () => {
    let changed = false
    fetchMock.mockImplementation(async (path: string) => {
      if (path.includes('guides')) return Response.json([])
      const data = response()
      if (changed) data.groups[0].permissions[1].isGranted = false
      return Response.json(data)
    })
    show()
    fireEvent.click(await screen.findByRole('checkbox', { name: '删除订单' }))
    changed = true
    fireEvent.click(screen.getByRole('button', { name: p.saveButton }))
    fireEvent.click(await screen.findByRole('button', { name: p.confirmSave }))
    expect(await screen.findByRole('alert')).toHaveTextContent(p.concurrentChangeHint)
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(false)
  })

  it('页签卸载后重新进入可以恢复草稿，保存前保持原授权', async () => {
    fetchMock.mockImplementation(async (path: string) => Response.json(path.includes('guides') ? [] : response()))
    const view = show()
    fireEvent.click(await screen.findByRole('checkbox', { name: '删除订单' }))
    view.unmount()
    show()
    await screen.findByText(p.draftFound)
    expect(screen.getByRole('checkbox', { name: '删除订单' })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: '删除订单' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: p.restoreDraft }))
    expect(screen.getByRole('checkbox', { name: '删除订单' })).not.toBeChecked()
    expect(screen.getByRole('button', { name: p.saveButton })).toBeEnabled()
  })

  it('暂存后服务端授权变化时拒绝恢复旧草稿', async () => {
    let changed = false
    fetchMock.mockImplementation(async (path: string) => {
      const data = response()
      if (changed) data.groups[0].permissions[1].isGranted = false
      return Response.json(path.includes('guides') ? [] : data)
    })
    const view = show()
    fireEvent.click(await screen.findByRole('checkbox', { name: '删除订单' }))
    view.unmount(); changed = true; show()
    await screen.findByText(p.draftStale)
    expect(screen.getByRole('button', { name: p.restoreDraft })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: p.discardDraft }))
    expect(screen.getByRole('checkbox', { name: '新建订单' })).not.toBeChecked()
  })

  it('返回编辑并扩大运维授权范围后必须重新确认', async () => {
    const ops = ['Rebuild', 'Recalculate'].map((name, index) => ({ name: `Sales.Orders.${name}`, displayName: `运维操作 ${index + 1}`, parentName: 'Sales.Orders', isGranted: false }))
    fetchMock.mockImplementation(async (path: string) => {
      const data = response(); data.groups[0].permissions.push(...ops)
      return Response.json(path.includes('guides') ? ops.map(item => ({ name: item.name, kind: 3, impact: '仅限运维' })) : data)
    })
    show()
    fireEvent.click(await screen.findByRole('checkbox', { name: '运维操作 1' }))
    fireEvent.click(screen.getByRole('button', { name: p.saveButton }))
    fireEvent.click(await screen.findByRole('checkbox', { name: p.opsAcknowledge }))
    expect(screen.getByRole('button', { name: p.confirmSave })).toBeEnabled()
    fireEvent.click(screen.getByRole('button', { name: p.continueEditing }))
    fireEvent.click(screen.getByRole('checkbox', { name: '运维操作 2' }))
    fireEvent.click(screen.getByRole('button', { name: p.saveButton }))
    expect(screen.getByRole('checkbox', { name: p.opsAcknowledge })).not.toBeChecked()
    expect(screen.getByRole('button', { name: p.confirmSave })).toBeDisabled()
  })

  it('带草稿重新进入时加载失败，重试不能清掉未知草稿', async () => {
    let fail = false
    fetchMock.mockImplementation(async (path: string) => fail ? new Response('加载失败', { status: 503 }) : Response.json(path.includes('guides') ? [] : response()))
    const view = show()
    fireEvent.click(await screen.findByRole('checkbox', { name: '删除订单' }))
    view.unmount(); fail = true; show()
    await screen.findByRole('alert')
    fail = false
    fireEvent.click(screen.getByRole('button', { name: p.retry }))
    await screen.findByText(p.draftFound)
    fireEvent.click(screen.getByRole('button', { name: p.restoreDraft }))
    expect(screen.getByRole('checkbox', { name: '删除订单' })).not.toBeChecked()
  })

  it('身份世代切换后不能读取上次登录的草稿', async () => {
    fetchMock.mockImplementation(async (path: string) => Response.json(path.includes('guides') ? [] : response()))
    const view = show()
    fireEvent.click(await screen.findByRole('checkbox', { name: '删除订单' }))
    view.unmount(); show('another-permission-identity')
    expect(await screen.findByRole('checkbox', { name: '删除订单' })).toBeChecked()
    expect(screen.queryByText(p.draftFound)).not.toBeInTheDocument()
  })

  it('同一路由的角色链接参数改变时加载新角色，不沿用旧角色状态', async () => {
    fetchMock.mockImplementation(async (path: string) => Response.json(path.includes('guides') ? [] : path.includes('providerKey=Other') ? { entityDisplayName: 'Other', groups: [] } : response()))
    const view = show()
    await screen.findByRole('checkbox', { name: '删除订单' })
    routeState.role = 'Other'
    view.rerender(content())
    await screen.findByText(p.emptyPermissions)
    expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('providerKey=Other'), expect.anything())
    expect(screen.queryByRole('checkbox', { name: '删除订单' })).not.toBeInTheDocument()
  })
})
