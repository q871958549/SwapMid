import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { TooltipProvider } from '@/components/ui/tooltip'
import messages from '../../../../../messages/zh-Hans.json'
import { PermissionRolePicker } from './permission-role-picker'

afterEach(() => vi.unstubAllGlobals())

describe('角色搜索失败恢复', () => {
  it('关闭下拉后点击重试仍直接发起请求', async () => {
    let fail = true
    const fetchMock = vi.fn(async () => fail ? new Response(null, { status: 503 }) : Response.json({ items: [{ id: '1', name: 'Sales', isStatic: false }] }))
    vi.stubGlobal('fetch', fetchMock)
    render(<NextIntlClientProvider locale="zh-Hans" messages={messages}><TooltipProvider><PermissionRolePicker value="" onChange={() => {}} /></TooltipProvider></NextIntlClientProvider>)
    const picker = screen.getByRole('combobox')
    fireEvent.click(picker)
    const alert = await screen.findByRole('alert')
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    fail = false
    fireEvent.click(within(alert).getByRole('button', { name: messages.Account.permission.retry }))
    await waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThanOrEqual(2))
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })
})
