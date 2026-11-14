import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { afterEach, describe, expect, it, vi } from 'vitest'
import messages from '../../../../../messages/zh-Hans.json'
import { ComparePermissionsDialog } from './compare-dialog'

vi.mock('./permission-role-picker', () => ({ PermissionRolePicker: ({ value, onChange, label }: { value: string; onChange: (value: string) => void; label: string }) => <button aria-label={label} onClick={() => onChange('Other')}>{value || '请选择'}</button> }))
const p = messages.Account.permission
const data = (isGranted: boolean) => ({ groups: [{ name: 'Sales', displayName: '销售', permissions: [
  { name: 'Orders', displayName: '订单', parentName: null, isGranted: true },
  { name: 'Orders.Delete', displayName: '删除', parentName: 'Orders', isGranted },
] }] })
function ui(open = true) { return <NextIntlClientProvider locale="zh-Hans" messages={messages}><ComparePermissionsDialog open={open} onOpenChange={() => {}} initialRole="Sales" dirty /></NextIntlClientProvider> }
afterEach(() => vi.unstubAllGlobals())

describe('权限对比', () => {
  it('默认带入当前角色，只展示已保存差异，并可搜索完整资源路径', async () => {
    vi.stubGlobal('fetch', vi.fn(async (path: string) => Response.json(data(path.includes('Sales')))))
    render(ui())
    expect(screen.getByRole('button', { name: p.compareRoleALabel })).toHaveTextContent('Sales')
    expect(screen.getByText(p.compareUnsavedHint)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: p.compareRoleBLabel }))
    expect(await screen.findByText('订单 / 删除')).toBeInTheDocument()
    expect(screen.queryByText(p.compareBothLabel)).not.toBeInTheDocument()
    fireEvent.change(screen.getByPlaceholderText(p.compareSearch), { target: { value: '不存在' } })
    expect(screen.queryByText('订单 / 删除')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('radio', { name: p.filter.all }))
    expect(screen.getByText(p.compareBothLabel)).toBeInTheDocument()
  })

  it('关闭对比取消仍在运行的请求', async () => {
    const signals: AbortSignal[] = []
    vi.stubGlobal('fetch', vi.fn((_path, init) => { signals.push(init.signal); return new Promise(() => {}) }))
    const view = render(ui())
    fireEvent.click(screen.getByRole('button', { name: p.compareRoleBLabel }))
    await waitFor(() => expect(signals).toHaveLength(2))
    view.rerender(ui(false))
    expect(signals.every(signal => signal.aborted)).toBe(true)
  })

  it('失败后可重试', async () => {
    let fail = true
    vi.stubGlobal('fetch', vi.fn(async (path: string) => fail ? new Response(null, { status: 503 }) : Response.json(data(path.includes('Sales')))))
    render(ui())
    fireEvent.click(screen.getByRole('button', { name: p.compareRoleBLabel }))
    const alert = await screen.findByRole('alert')
    fail = false
    fireEvent.click(within(alert).getByRole('button', { name: p.retry }))
    expect(await screen.findByText('订单 / 删除')).toBeInTheDocument()
  })
})
