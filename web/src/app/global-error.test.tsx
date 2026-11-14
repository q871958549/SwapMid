import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { StrictMode } from 'react'

vi.mock('@/lib/client-session-recovery', () => ({
  recoverSessionToLoginWithIdentityChange: vi.fn(),
}))

import { recoverSessionToLoginWithIdentityChange } from '@/lib/client-session-recovery'
import { GlobalErrorActions } from './global-error'

const recoverSessionToLoginMock = vi.mocked(recoverSessionToLoginWithIdentityChange)

describe('GlobalErrorActions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('恢复请求在途时禁止重复退出与 reset', async () => {
    let resolveRecovery!: (value: 'failed') => void
    recoverSessionToLoginMock.mockReturnValue(new Promise((resolve) => {
      resolveRecovery = resolve
    }))
    const reset = vi.fn()
    render(<GlobalErrorActions reset={reset} />)

    const relogin = screen.getByRole('button', { name: '重新登录' })
    fireEvent.click(relogin)
    fireEvent.click(relogin)

    expect(recoverSessionToLoginMock).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: '正在安全退出…' })).toBeDisabled()
    const retry = screen.getByRole('button', { name: '重试' })
    expect(retry).toBeDisabled()
    fireEvent.click(retry)
    expect(reset).not.toHaveBeenCalled()

    resolveRecovery('failed')
    expect(await screen.findByRole('alert')).toHaveTextContent('本次未跳转')
    expect(screen.getByRole('button', { name: '重新登录' })).toBeEnabled()
    expect(screen.getByRole('button', { name: '重试' })).toBeEnabled()
  })

  it('失败后允许显式重试，但不会自动重放删除', async () => {
    recoverSessionToLoginMock
      .mockResolvedValueOnce('unavailable')
      .mockResolvedValueOnce('failed')
    render(<GlobalErrorActions reset={vi.fn()} />)

    fireEvent.click(screen.getByRole('button', { name: '重新登录' }))
    await screen.findByRole('alert')
    expect(recoverSessionToLoginMock).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByRole('button', { name: '重新登录' }))
    await waitFor(() => expect(recoverSessionToLoginMock).toHaveBeenCalledTimes(2))
    expect(await screen.findByRole('alert')).toHaveTextContent('本次未跳转')
  })

  it('身份已变化时显式重载当前文档并保持按钮锁定', async () => {
    recoverSessionToLoginMock.mockImplementation(async ({ onIdentityChanged }) => {
      onIdentityChanged()
      return 'identity-changed'
    })
    const reload = vi.fn()
    render(<GlobalErrorActions reset={vi.fn()} reload={reload} />)

    fireEvent.click(screen.getByRole('button', { name: '重新登录' }))

    await waitFor(() => expect(reload).toHaveBeenCalledTimes(1))
    expect(screen.getByRole('button', { name: '正在安全退出…' })).toBeDisabled()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('确认退出后保持按钮锁定，等待 replace 导航卸载旧文档', async () => {
    recoverSessionToLoginMock.mockResolvedValue('logged-out')
    render(<GlobalErrorActions reset={vi.fn()} />)

    fireEvent.click(screen.getByRole('button', { name: '重新登录' }))

    await waitFor(() => expect(recoverSessionToLoginMock).toHaveBeenCalledTimes(1))
    expect(screen.getByRole('button', { name: '正在安全退出…' })).toBeDisabled()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it.each(['logged-out', 'session-missing', 'identity-changed'] as const)(
    '%s 后导航未完成，有界开放整页重试且不重复退出', async (outcome) => {
      vi.useFakeTimers()
      try {
        recoverSessionToLoginMock.mockResolvedValue(outcome)
        const reload = vi.fn()
        const reset = vi.fn()
        render(<StrictMode><GlobalErrorActions reset={reset} reload={reload} /></StrictMode>)
        await act(async () => { fireEvent.click(screen.getByRole('button', { name: '重新登录' })) })
        expect(screen.getByRole('button', { name: '重试' })).toBeDisabled()
        await act(async () => { vi.advanceTimersByTime(9_000) })
        expect(screen.getByRole('button', { name: '重试' })).toBeEnabled()
        fireEvent.click(screen.getByRole('button', { name: '重试' }))
        expect(reload).toHaveBeenCalledTimes(1)
        expect(reset).not.toHaveBeenCalled()
        expect(recoverSessionToLoginMock).toHaveBeenCalledTimes(1)
        expect(screen.getByRole('button', { name: '重新登录' })).toBeDisabled()
      } finally { vi.useRealTimers() }
    },
  )

  it('离开错误页会清理导航看门狗，不向后续页面遗留计时任务', async () => {
    vi.useFakeTimers()
    try {
      recoverSessionToLoginMock.mockResolvedValue('logged-out')
      const { unmount } = render(<StrictMode><GlobalErrorActions reset={vi.fn()} reload={vi.fn()} /></StrictMode>)
      await act(async () => { fireEvent.click(screen.getByRole('button', { name: '重新登录' })) })
      unmount()
      expect(vi.getTimerCount()).toBe(0)
    } finally { vi.useRealTimers() }
  })
})
