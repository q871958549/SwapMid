'use server'

import { getWebOAuthClientId } from '@/lib/site-instance'
import { getTranslations } from 'next-intl/server'
import { createSession } from '@/lib/session'
import { fetchAbpUserConfig } from '@/lib/abp-config'
import { getSessionIdentityVersion } from '@/lib/session-identity'
import {
  SERVER_BACKEND_DEADLINE_MS,
  SERVER_BACKEND_RESPONSE_LIMIT_BYTES,
  serverBackendFetch,
} from '@/lib/server-backend-fetch'
import { parsePasswordPolicy, type PasswordPolicy } from '@/lib/password-policy'
import type { ExternalAuthProvidersDto } from '@/lib/api/types'

export interface LoginState {
  error?: string
  ok?: boolean
  identityVersion?: string
  /** 账号被标记"下次登录须改密"（首登/管理员重置后）：携带 ABP 下发的改密令牌进入改密表单 */
  changePassword?: boolean
  changePasswordToken?: string
}

/** 与 `@/lib/nav-utils` 的 `NavT` 同一约定：next-intl 未启用严格消息类型，一律 `(key) => string`。 */
type Translator = (key: string) => string

// OpenIddict 密码模式失败时返回 { error, error_description }，ABP 把 SignInManager 的具体原因
// 写进 description（英文）。按关键字映射成可操作的三语提示（Login namespace）；匹配不上退回通用文案。
function mapTokenError(desc: string, t: Translator, errorCode?: string): string {
  if (errorCode === 'temporarily_unavailable') return t('errorTooManyAttempts')
  const d = desc.toLowerCase()
  if (d.includes('account changed during login')) return t('errorAccountChangedDuringLogin')
  if (d.includes('locked')) return t('errorAccountLocked')
  if (d.includes('too many login')) return t('errorTooManyAttempts')
  if (d.includes('not active') || d.includes('inactive')) return t('errorAccountInactive')
  if (d.includes('not allowed')) return t('errorLoginNotAllowed')
  return t('errorInvalidCredentials')
}

export async function requestToken(params: Record<string, string>): Promise<Response> {
  return serverBackendFetch('/connect/token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'password',
      client_id: getWebOAuthClientId(),
      // offline_access → issues a refresh token so the session can outlive the ~1h access token
      scope: 'SwapMid offline_access',
      ...params,
    }),
    deadlineMs: SERVER_BACKEND_DEADLINE_MS.authentication,
    maxResponseBytes: SERVER_BACKEND_RESPONSE_LIMIT_BYTES.oauthToken,
  })
}

// 拿到令牌后建会话（登录与"改密并登录"共用）
export async function establishSession(
  tokenJson: { access_token: string; refresh_token?: string; expires_in?: number },
  username: string,
  t: Translator,
): Promise<LoginState> {
  const { access_token, refresh_token, expires_in } = tokenJson

  let userConfig
  try {
    userConfig = await fetchAbpUserConfig(access_token, username)
  } catch {
    return { error: t('errorFetchUserConfigFailed') }
  }

  const session = await createSession({
    // Full session (incl. tokens) lives server-side in Redis/memory; cookie only holds a UUID.
    userId: userConfig.userId,
    accessToken: access_token,
    refreshToken: refresh_token,
    accessTokenExpiresAt: Date.now() + (expires_in ?? 3600) * 1000,
    userName: userConfig.userName,
    name: userConfig.name,
    grantedPolicies: userConfig.grantedPolicies,
    theme: userConfig.theme,
    fontSize: userConfig.fontSize,
    language: userConfig.language,
  })

  return {
    ok: true,
    identityVersion: getSessionIdentityVersion(session),
  }
}

export async function loginAction(
  prevState: LoginState | undefined,
  formData: FormData,
): Promise<LoginState> {
  const t = (await getTranslations('Login')) as Translator
  const username = formData.get('username') as string
  const password = formData.get('password') as string

  if (!username || !password) {
    return { error: t('errorEmptyCredentials') }
  }

  try {
    const tokenRes = await requestToken({ username, password })

    if (!tokenRes.ok) {
      const body = await tokenRes.text()
      let parsed: {
        error?: string
        error_description?: string
        changePasswordToken?: string
        ChangePasswordToken?: string
      } = {}
      try { parsed = JSON.parse(body) } catch { /* 按原文匹配 */ }
      // OAuth 错误体可能含一次性 changePasswordToken，故不整体落日志；但必须保留 error_description。
      console.error(
        '[login] token error',
        tokenRes.status,
        parsed.error ?? 'unknown',
        parsed.error_description ?? body.slice(0, 200),
      )
      // ABP 对"须改密"账号不发令牌，而是回传一次性改密令牌（小驼峰 changePasswordToken），
      // 客户端用 旧密码+新密码+该令牌 再次调 /connect/token 完成改密并直接登录。
      const changePasswordToken = parsed.changePasswordToken ?? parsed.ChangePasswordToken
      if (changePasswordToken) {
        return { changePassword: true, changePasswordToken }
      }
      return { error: mapTokenError(parsed.error_description ?? body, t, parsed.error) }
    }

    const tokenJson = await tokenRes.json()
    return establishSession(tokenJson, username, t)
  } catch (e) {
    console.error('[login] caught error', e)
    return { error: t('errorNetworkError') }
  }
}

/** 登录页尚无 session，经有界内核拉匿名密码规则。失败返回 null，禁止回退写死口径。 */
export async function getPasswordPolicyAction(): Promise<PasswordPolicy | null> {
  try {
    const res = await serverBackendFetch('/api/v1/account-management/password-policy', {
      cache: 'no-store',
      deadlineMs: SERVER_BACKEND_DEADLINE_MS.publicPage,
      maxResponseBytes: SERVER_BACKEND_RESPONSE_LIMIT_BYTES.oauthToken,
    })
    if (!res.ok) return null
    return parsePasswordPolicy(await res.json())
  } catch {
    return null
  }
}

export async function getExternalAuthProvidersAction(): Promise<ExternalAuthProvidersDto | null> {
  try {
    const res = await serverBackendFetch('/api/v1/auth-providers', {
      cache: 'no-store',
      deadlineMs: SERVER_BACKEND_DEADLINE_MS.publicPage,
      maxResponseBytes: SERVER_BACKEND_RESPONSE_LIMIT_BYTES.oauthToken,
    })
    if (!res.ok) return null
    return (await res.json()) as ExternalAuthProvidersDto
  } catch {
    return null
  }
}

// 首登/管理员重置后的强制改密：改密成功即直接登录
export async function changeInitialPasswordAction(
  prevState: LoginState | undefined,
  formData: FormData,
): Promise<LoginState> {
  const t = (await getTranslations('Login')) as Translator
  const username = formData.get('username') as string
  const password = formData.get('password') as string
  const newPassword = formData.get('newPassword') as string
  const confirmPassword = formData.get('confirmPassword') as string
  const changePasswordToken = formData.get('changePasswordToken') as string

  if (!newPassword || newPassword !== confirmPassword) {
    return { changePassword: true, changePasswordToken, error: t('errorPasswordMismatch') }
  }
  if (newPassword === password) {
    return { changePassword: true, changePasswordToken, error: t('errorSamePassword') }
  }

  try {
    const tokenRes = await requestToken({
      username,
      password,
      ChangePasswordToken: changePasswordToken,
      NewPassword: newPassword,
    })

    if (!tokenRes.ok) {
      const body = await tokenRes.text()
      let parsed: { error?: string; error_description?: string } = {}
      try { parsed = JSON.parse(body) } catch { /* 原文仍用于用户提示 */ }
      console.error(
        '[change-password] token error',
        tokenRes.status,
        parsed.error ?? 'unknown',
        parsed.error_description ?? body.slice(0, 200),
      )
      const desc = parsed.error_description ?? body
      const d = desc.toLowerCase()
      if (d.includes('password')) {
        return {
          changePassword: true,
          changePasswordToken,
          error: t('errorPasswordComplexity'),
        }
      }
      return { error: t('errorSessionExpired') }
    }

    const tokenJson = await tokenRes.json()
    return establishSession(tokenJson, username, t)
  } catch (e) {
    console.error('[change-password] caught error', e)
    return { changePassword: true, changePasswordToken, error: t('errorChangePasswordNetworkError') }
  }
}
