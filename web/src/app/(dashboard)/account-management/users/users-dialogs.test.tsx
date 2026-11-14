import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { BatchRolesDialog } from './users-dialogs'
import type { AbpRole } from './users-types'
import zhHansMessages from '../../../../../messages/zh-Hans.json'

function render(ui: React.ReactElement) {
  return rtlRender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

vi.mock('@/components/ui/use-toast', () => ({ toast: vi.fn() }))

const roles: AbpRole[] = [
  { id: 'r1', name: 'clerk', isDefault: false, isStatic: false, isPublic: true },
]

const target = { ids: ['u1', 'u2'], clear: vi.fn() }

describe('BatchRolesDialog', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn(async () => ({
      ok: true,
      json: async () => ({ succeeded: 2, skipped: 0 }),
      text: async () => '',
    })))
  })

  it('追加未选角色时不能提交；清空不要求选择角色，并提交 mode=clear', async () => {
    render(
      <BatchRolesDialog target={target} onClose={vi.fn()} roles={roles} onDone={vi.fn()} />,
    )

    expect(screen.getByRole('button', { name: '确认分配' })).toBeDisabled()

    fireEvent.click(screen.getAllByRole('combobox')[0])
    fireEvent.click(await screen.findByRole('option', { name: /清空/ }))

    const confirm = screen.getByRole('button', { name: '确认清空' })
    expect(confirm).toBeEnabled()
    expect(screen.getByText(/全部角色都会被移除/)).toBeInTheDocument()
    expect(screen.queryByText('目标角色')).not.toBeInTheDocument()

    fireEvent.click(confirm)
    await waitFor(() => expect(fetch).toHaveBeenCalled())
    const init = vi.mocked(fetch).mock.calls[0][1]
    expect(JSON.parse(String(init?.body))).toMatchObject({
      userIds: ['u1', 'u2'],
      roleNames: [],
      append: false,
      mode: 'clear',
    })
  }, 20_000)
})
