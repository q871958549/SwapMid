import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createTranslator } from 'next-intl'
import zhHansMessages from '../../../messages/zh-Hans.json'

vi.mock('@/lib/session', () => ({ createSession: vi.fn(), deleteSession: vi.fn() }))

vi.mock('next-intl/server', () => ({
  getTranslations: async (namespace: keyof typeof zhHansMessages) =>
    createTranslator({ locale: 'zh-Hans', messages: zhHansMessages, namespace }),
}))

vi.spyOn(console, 'error').mockImplementation(() => {})

import { createSession } from '@/lib/session'
import {
  loginAction,
  changeInitialPasswordAction,
  getPasswordPolicyAction,
  type LoginState,
} from './auth'

const createSessionMock = vi.mocked(createSession)

let fetchMock: ReturnType<typeof vi.fn>

beforeEach(() => {
  vi.clearAllMocks()
  fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  createSessionMock.mockResolvedValue({
    identityVersion: 'identity-login',
    accessToken: 'at-1',
    refreshToken: 'rt-1',
    accessTokenExpiresAt: Date.now() + 3_600_000,
    userName: 'admin',
    name: '管理员',
    grantedPolicies: { 'SwapMid.Foo': true },
  })
})

function fd(fields: Record<string, string>): FormData {
  const f = new FormData()
  for (const [k, v] of Object.entries(fields)) f.set(k, v)
  return f
}

function tokenError(bodyStr: string, status = 400): Response {
  return { ok: false, status, text: async () => bodyStr } as Response
}

function tokenOk(): Response {
  return {
    ok: true,
    json: async () => ({ access_token: 'at-1', refresh_token: 'rt-1', expires_in: 3600 }),
  } as Response
}

function configOk(): Response {
  return {
    ok: true,
    json: async () => ({
      currentUser: { id: 'user-admin-id', userName: 'admin', name: '管理员' },
      auth: {
        grantedPolicies: {
          'SwapMid.Foo': true,
          'AbpIdentity.Users': true,
        },
      },
    }),
  } as Response
}

describe('loginAction · 入参校验', () => {
  it('用户名或密码为空 → 提示且不发请求', async () => {
    const r = await loginAction(undefined, fd({ username: '', password: '' }))
    expect(r).toEqual<LoginState>({ error: '请输入用户名和密码' })
    expect(fetchMock).not.toHaveBeenCalled()
  })
})

describe('loginAction · mapTokenError 关键字映射', () => {
  const cases: Array<[string, string]> = [
    ['The account is locked out.', '连续输错次数过多，账号已临时锁定，请约 5 分钟后再试'],
    ['too many login attempts', '登录尝试过于频繁，请稍后再试'],
    ['User is not active.', '账号已停用，请联系管理员'],
    ['User account is inactive', '账号已停用，请联系管理员'],
    ['Login is not allowed.', '当前账号暂不允许登录，请联系管理员'],
    ['Invalid username or password', '用户名或密码错误'],
  ]
  it.each(cases)('error_description=%s → %s', async (desc, expected) => {
    fetchMock.mockResolvedValueOnce(tokenError(JSON.stringify({ error_description: desc })))
    const r = await loginAction(undefined, fd({ username: 'u', password: 'p' }))
    expect(r).toEqual<LoginState>({ error: expected })
  })

  it('响应体非 JSON → 用原文匹配，未命中关键字退回通用文案', async () => {
    fetchMock.mockResolvedValueOnce(tokenError('invalid_grant'))
    const r = await loginAction(undefined, fd({ username: 'u', password: 'p' }))
    expect(r).toEqual<LoginState>({ error: '用户名或密码错误' })
  })

  it('账号并发冲突提示重新登录，且不重放密码请求或建立会话', async () => {
    fetchMock.mockResolvedValueOnce(tokenError(JSON.stringify({
      error: 'invalid_grant',
      error_description: 'The account changed during login. Please sign in again.',
    })))
    const result = await loginAction(undefined, fd({ username: 'u', password: 'p' }))
    expect(result).toEqual<LoginState>({ error: '登录期间账号状态发生变化，请重新登录' })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(createSessionMock).not.toHaveBeenCalled()
  })

  it('temporarily_unavailable 映射为登录限流文案', async () => {
    fetchMock.mockResolvedValueOnce(tokenError(JSON.stringify({
      error: 'temporarily_unavailable',
      error_description: 'x',
    })))
    const r = await loginAction(undefined, fd({ username: 'u', password: 'p' }))
    expect(r).toEqual<LoginState>({ error: '登录尝试过于频繁，请稍后再试' })
  })
})

describe('loginAction · changePasswordToken 小驼峰键（回归坑）', () => {
  it('响应含小驼峰 changePasswordToken → 进改密流程，不误报密码错误', async () => {
    fetchMock.mockResolvedValueOnce(tokenError(JSON.stringify({ changePasswordToken: 'cpt-123' })))
    const r = await loginAction(undefined, fd({ username: 'u', password: 'p' }))
    expect(r).toEqual<LoginState>({ changePassword: true, changePasswordToken: 'cpt-123' })
  })

  it('大写 ChangePasswordToken 也兜底解析（?? 回退）', async () => {
    fetchMock.mockResolvedValueOnce(tokenError(JSON.stringify({ ChangePasswordToken: 'cpt-456' })))
    const r = await loginAction(undefined, fd({ username: 'u', password: 'p' }))
    expect(r).toEqual<LoginState>({ changePassword: true, changePasswordToken: 'cpt-456' })
  })
})

describe('loginAction · 网络异常', () => {
  it('fetch 抛错 → 网络连接失败文案', async () => {
    fetchMock.mockRejectedValueOnce(new Error('ECONNREFUSED'))
    const r = await loginAction(undefined, fd({ username: 'u', password: 'p' }))
    expect(r).toEqual<LoginState>({ error: '网络连接失败，请检查后端服务是否启动' })
  })
})

describe('loginAction · 成功建会话', () => {
  it('token ok + config ok → 建会话（仅本应用前缀权限）、返回 ok', async () => {
    fetchMock.mockResolvedValueOnce(tokenOk()).mockResolvedValueOnce(configOk())

    const r = await loginAction(undefined, fd({ username: 'admin', password: 'p' }))

    expect(r).toEqual<LoginState>({
      ok: true,
      identityVersion: 'identity-login',
    })
    expect(createSessionMock).toHaveBeenCalledTimes(1)
    expect(createSessionMock).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: 'user-admin-id',
        accessToken: 'at-1',
        refreshToken: 'rt-1',
        userName: 'admin',
        name: '管理员',
        grantedPolicies: { 'SwapMid.Foo': true },
      }),
    )
    const body = String((fetchMock.mock.calls[0][1] as RequestInit).body)
    expect(body).toContain('grant_type=password')
  })

  it('token ok 但 config 拉取失败 → 获取用户信息失败，不建会话', async () => {
    fetchMock.mockResolvedValueOnce(tokenOk()).mockResolvedValueOnce({ ok: false, status: 500 } as Response)

    const r = await loginAction(undefined, fd({ username: 'admin', password: 'p' }))

    expect(r).toEqual<LoginState>({ error: '获取用户信息失败' })
    expect(createSessionMock).not.toHaveBeenCalled()
  })
})

describe('changeInitialPasswordAction · 前置校验', () => {
  it('两次新密码不一致 → 停留改密表单并报错', async () => {
    const r = await changeInitialPasswordAction(
      undefined,
      fd({ username: 'u', password: 'old', newPassword: 'A1b2c3!', confirmPassword: 'mismatch', changePasswordToken: 'cpt' }),
    )
    expect(r).toEqual<LoginState>({ changePassword: true, changePasswordToken: 'cpt', error: '两次输入的新密码不一致' })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('新密码与初始密码相同 → 报错不发请求', async () => {
    const r = await changeInitialPasswordAction(
      undefined,
      fd({ username: 'u', password: 'same', newPassword: 'same', confirmPassword: 'same', changePasswordToken: 'cpt' }),
    )
    expect(r).toEqual<LoginState>({ changePassword: true, changePasswordToken: 'cpt', error: '新密码不能与初始密码相同' })
    expect(fetchMock).not.toHaveBeenCalled()
  })
})

describe('changeInitialPasswordAction · 提交结果分支', () => {
  it('改密失败且含 password 关键字 → 复杂度规则提示，停留表单', async () => {
    fetchMock.mockResolvedValueOnce(tokenError(JSON.stringify({ error_description: 'Password too weak' })))
    const r = await changeInitialPasswordAction(
      undefined,
      fd({ username: 'u', password: 'old', newPassword: 'weak', confirmPassword: 'weak', changePasswordToken: 'cpt' }),
    )
    expect(r.changePassword).toBe(true)
    expect(r.changePasswordToken).toBe('cpt')
    expect(r.error).toContain('新密码不符合当前规则')
  })

  it('改密失败且无 password 关键字 → 会话失效，退回登录', async () => {
    fetchMock.mockResolvedValueOnce(tokenError(JSON.stringify({ error_description: 'token expired' })))
    const r = await changeInitialPasswordAction(
      undefined,
      fd({ username: 'u', password: 'old', newPassword: 'A1b2c3!', confirmPassword: 'A1b2c3!', changePasswordToken: 'cpt' }),
    )
    expect(r).toEqual<LoginState>({ error: '改密会话已失效，请重新登录后再试' })
  })

  it('改密成功 → 直接建会话并返回 ok', async () => {
    fetchMock.mockResolvedValueOnce(tokenOk()).mockResolvedValueOnce(configOk())
    const r = await changeInitialPasswordAction(
      undefined,
      fd({ username: 'admin', password: 'old', newPassword: 'A1b2c3!', confirmPassword: 'A1b2c3!', changePasswordToken: 'cpt' }),
    )
    expect(r).toEqual<LoginState>({
      ok: true,
      identityVersion: 'identity-login',
    })
    expect(createSessionMock).toHaveBeenCalledTimes(1)
    const body = String((fetchMock.mock.calls[0][1] as RequestInit).body)
    expect(body).toContain('ChangePasswordToken=cpt')
    expect(body).toContain('NewPassword=')
  })

  it('改密期间网络异常 → 停留改密表单提示网络错误', async () => {
    fetchMock.mockRejectedValueOnce(new Error('network'))
    const r = await changeInitialPasswordAction(
      undefined,
      fd({ username: 'u', password: 'old', newPassword: 'A1b2c3!', confirmPassword: 'A1b2c3!', changePasswordToken: 'cpt' }),
    )
    expect(r).toEqual<LoginState>({ changePassword: true, changePasswordToken: 'cpt', error: '网络连接失败，请稍后再试' })
  })
})

describe('getPasswordPolicyAction', () => {
  it('解析成功返回结构化规则', async () => {
    fetchMock.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        requiredLength: 8,
        requireDigit: true,
        requireLowercase: false,
        requireUppercase: true,
        requireNonAlphanumeric: false,
      }),
    } as Response)
    await expect(getPasswordPolicyAction()).resolves.toEqual({
      requiredLength: 8,
      requireDigit: true,
      requireLowercase: false,
      requireUppercase: true,
      requireNonAlphanumeric: false,
    })
  })

  it('非 200 或网络失败返回 null，不回退写死口径', async () => {
    fetchMock.mockResolvedValueOnce({ ok: false, status: 500 } as Response)
    await expect(getPasswordPolicyAction()).resolves.toBeNull()
    fetchMock.mockRejectedValueOnce(new Error('network'))
    await expect(getPasswordPolicyAction()).resolves.toBeNull()
  })
})
