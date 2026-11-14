import { Component, type PropsWithChildren } from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import messages from '../../../../messages/zh-Hans.json'

vi.mock('@/app/actions/auth', () => ({ loginAction: vi.fn(), changeInitialPasswordAction: vi.fn() }))
vi.mock('@/components/theme-provider', () => ({ useTheme: () => ({ resolvedTheme: 'light', setTheme: vi.fn() }) }))
vi.mock('@/components/layout/session-guard', () => ({ announceSessionIdentityChange: vi.fn() }))
vi.mock('@/lib/report-client-error', () => ({ reportClientError: vi.fn() }))
// 认证状态回归不依赖动画时钟；jsdom 不会推进 motion 的视觉合成帧。
vi.mock('framer-motion', () => ({ motion: {
  div: ({ children }: PropsWithChildren) => <div>{children}</div>,
  p: ({ children, role }: PropsWithChildren<{ role?: 'alert' }>) => <p role={role}>{children}</p>,
} }))

import { loginAction, changeInitialPasswordAction } from '@/app/actions/auth'
import { LoginClient } from './client'
import { reportClientError } from '@/lib/report-client-error'

class FailureBoundary extends Component<PropsWithChildren, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  render() {
    return this.state.failed ? <p role="alert">登录异常进入全局错误页</p> : this.props.children
  }
}

function mountLogin() {
  render(<FailureBoundary><NextIntlClientProvider locale="zh-CN" messages={messages} timeZone="Asia/Shanghai">
    <LoginClient initialPasswordPolicy={null} releaseTag="test-only" />
  </NextIntlClientProvider></FailureBoundary>)
  fireEvent.change(screen.getByLabelText('用户名 / 工号', { exact: true }), { target: { value: 'test-employee' } })
  fireEvent.change(screen.getByLabelText('密码', { exact: true }), { target: { value: 'test-only-password' } })
}

function submitLogin() {
  fireEvent.submit(screen.getByRole('button', { name: /^登录$/ }).closest('form')!)
}

beforeEach(() => {
  vi.mocked(loginAction).mockReset()
  vi.mocked(changeInitialPasswordAction).mockReset()
  window.localStorage.clear()
  vi.spyOn(console, 'error').mockImplementation(() => {})
})
afterEach(() => { vi.restoreAllMocks() })

describe('登录响应恢复', () => {
  it('Server Action 拒绝仍保留登录表单，显示刷新入口且不自动重复提交', async () => {
    vi.mocked(loginAction).mockRejectedValue(new Error('An unexpected response was received from the server.'))
    mountLogin()
    submitLogin()
    expect(await screen.findByRole('alert')).toHaveTextContent('登录响应异常，请刷新登录页后重试。')
    expect(screen.getByLabelText('用户名 / 工号', { exact: true })).toHaveValue('test-employee')
    expect(screen.getByRole('button', { name: /^刷新登录页$/ })).toBeEnabled()
    expect(screen.getByRole('button', { name: /^登录$/ })).toBeDisabled()
    await act(async () => { submitLogin() })
    expect(loginAction).toHaveBeenCalledTimes(1)
    expect(reportClientError).toHaveBeenCalledTimes(1)
    expect(JSON.stringify(vi.mocked(reportClientError).mock.calls)).not.toContain('test-only-password')
    expect(JSON.stringify(vi.mocked(reportClientError).mock.calls)).not.toContain('test-employee')
  })

  it('普通凭据校验失败仍显示原提示并允许用户改正后提交', async () => {
    vi.mocked(loginAction).mockResolvedValue({ error: '用户名或密码错误' })
    mountLogin()
    submitLogin()
    expect(await screen.findByText('用户名或密码错误')).toBeVisible()
    expect(screen.getByRole('button', { name: /^登录$/ })).toBeEnabled()
    expect(screen.queryByRole('button', { name: /^刷新登录页$/ })).not.toBeInTheDocument()
  })

  it('强制改密响应拒绝也保留表单并等待刷新，不自动重放密码修改', async () => {
    vi.mocked(loginAction).mockResolvedValue({ changePassword: true, changePasswordToken: 'local-test-token' })
    vi.mocked(changeInitialPasswordAction).mockRejectedValue(new Error('An unexpected response was received from the server.'))
    mountLogin()
    submitLogin()
    const submit = await screen.findByRole('button', { name: /^修改密码并登录$/ })
    fireEvent.change(screen.getByLabelText('新密码', { exact: true }), { target: { value: 'Only-A-Test-Passw0rd!' } })
    fireEvent.change(screen.getByLabelText('确认新密码', { exact: true }), { target: { value: 'Only-A-Test-Passw0rd!' } })
    fireEvent.submit(submit.closest('form')!)
    expect(await screen.findByRole('alert')).toHaveTextContent('登录响应异常，请刷新登录页后重试。')
    expect(screen.getByRole('button', { name: /^修改密码并登录$/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: /^刷新登录页$/ })).toBeEnabled()
    await act(async () => { fireEvent.submit(screen.getByRole('button', { name: /^修改密码并登录$/ }).closest('form')!) })
    expect(changeInitialPasswordAction).toHaveBeenCalledTimes(1)
    const diagnostics = JSON.stringify(vi.mocked(reportClientError).mock.calls)
    expect(diagnostics).not.toContain('test-only-password')
    expect(diagnostics).not.toContain('Only-A-Test-Passw0rd!')
    expect(diagnostics).not.toContain('local-test-token')
  })
})
