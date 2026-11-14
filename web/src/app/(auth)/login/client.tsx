'use client'

import { useActionState, useCallback, useEffect, useRef, useState } from 'react'
import Image from 'next/image'
import { motion } from 'framer-motion'
import { useTranslations } from 'next-intl'
import { Eye, EyeOff, KeyRound, Loader2, Moon, Sun } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { IconButton } from '@/components/ui/icon-button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { changeInitialPasswordAction, loginAction, type LoginState } from '@/app/actions/auth'
import { PasswordPolicyHints } from '@/components/password-policy-hints'
import { evaluatePasswordPolicy, type PasswordPolicy } from '@/lib/password-policy'
import { useTheme } from '@/components/theme-provider'
import { isSafeInternalPath } from '@/lib/safe-internal-path'
import { announceSessionIdentityChange } from '@/components/layout/session-guard'
import { getReleaseDisplay } from '@/lib/release-label'
import { reportClientError } from '@/lib/report-client-error'

const REMEMBER_KEY = 'swapmid-login-username'
const BRAND_NAME = 'SwapMid'

type RecoverableLoginState = LoginState & { refreshRequired?: boolean }

async function invokeLoginAction(
  action: (previous: LoginState | undefined, data: FormData) => Promise<LoginState>,
  previous: RecoverableLoginState | undefined,
  data: FormData,
  failureMessage: string,
): Promise<RecoverableLoginState> {
  if (previous?.refreshRequired) return previous
  try {
    return await action(previous, data)
  } catch (error) {
    reportClientError({
      kind: 'login-action-response',
      message: error instanceof Error ? error.message : 'Login action response unavailable',
    })
    return { error: failureMessage, refreshRequired: true }
  }
}

export function LoginClient({
  initialPasswordPolicy,
  releaseTag = '',
}: {
  initialPasswordPolicy: PasswordPolicy | null
  releaseTag?: string
}) {
  const t = useTranslations('Login')
  const tc = useTranslations('Common')
  const release = getReleaseDisplay(releaseTag)
  const { resolvedTheme, setTheme } = useTheme()
  const bgRef = useRef<HTMLDivElement>(null)
  const loginWithRecovery = useCallback(
    (previous: RecoverableLoginState | undefined, data: FormData) => invokeLoginAction(loginAction, previous, data, t('errorActionResponse')),
    [t],
  )
  const changeWithRecovery = useCallback(
    (previous: RecoverableLoginState | undefined, data: FormData) => invokeLoginAction(changeInitialPasswordAction, previous, data, t('errorActionResponse')),
    [t],
  )
  const [state, action, pending] = useActionState(loginWithRecovery, undefined)
  const [changeState, changeAction, changePending] = useActionState(changeWithRecovery, undefined)
  const [showPassword, setShowPassword] = useState(false)
  const [showNewPassword, setShowNewPassword] = useState(false)
  const [capsLock, setCapsLock] = useState(false)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [newPasswordValue, setNewPasswordValue] = useState('')
  const [localComplexityError, setLocalComplexityError] = useState<string | null>(null)
  const passwordPolicy = initialPasswordPolicy

  const changePasswordToken = changeState?.changePasswordToken ?? state?.changePasswordToken
  const mustChange = Boolean((changeState?.changePassword ?? state?.changePassword) && changePasswordToken)

  useEffect(() => {
    void Promise.resolve().then(() => {
      try {
        const remembered = localStorage.getItem(REMEMBER_KEY)
        if (remembered) setUsername((v) => v || remembered)
      } catch {
        // 浏览器禁用存储时仍允许正常登录
      }
    })
  }, [])

  useEffect(() => {
    const success = state?.ok ? state : changeState?.ok ? changeState : null
    if (success) {
      try {
        if (username) localStorage.setItem(REMEMBER_KEY, username)
      } catch {
        // 忽略存储写入异常
      }
      if (success.identityVersion) {
        announceSessionIdentityChange(success.identityVersion)
      }
      const redirectTo = new URLSearchParams(window.location.search).get('redirect')
      const dest = isSafeInternalPath(redirectTo) ? redirectTo : '/dashboard'
      window.location.replace(dest)
    }
  }, [state, changeState, username])

  useEffect(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
    let raf = 0
    function onMove(e: PointerEvent) {
      if (e.pointerType === 'touch') return
      cancelAnimationFrame(raf)
      raf = requestAnimationFrame(() => {
        const mx = (e.clientX / window.innerWidth - 0.5) * 2
        const my = (e.clientY / window.innerHeight - 0.5) * 2
        const el = bgRef.current
        if (el) {
          el.style.setProperty('--login-mx', mx.toFixed(3))
          el.style.setProperty('--login-my', my.toFixed(3))
        }
      })
    }
    window.addEventListener('pointermove', onMove, { passive: true })
    return () => {
      window.removeEventListener('pointermove', onMove)
      cancelAnimationFrame(raf)
    }
  }, [])

  function detectCapsLock(e: React.KeyboardEvent<HTMLInputElement>) {
    setCapsLock(e.getModifierState?.('CapsLock') ?? false)
  }

  function passwordToggle(shown: boolean, toggle: () => void) {
    return (
      <button
        type="button"
        tabIndex={-1}
        aria-label={shown ? t('hidePassword') : t('showPassword')}
        onClick={toggle}
        className="text-muted-foreground hover:text-foreground absolute top-1/2 right-2.5 -translate-y-1/2 transition-colors"
      >
        {shown ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
      </button>
    )
  }

  return (
    <div ref={bgRef} className="relative flex min-h-dvh items-center justify-center px-4 py-4 pt-[max(1rem,env(safe-area-inset-top))] pb-[max(1rem,env(safe-area-inset-bottom))]">
      <div className="login-bg" aria-hidden>
        <div className="login-parallax login-parallax-far">
          <div className="login-aurora login-aurora-1" />
          <div className="login-aurora login-aurora-2" />
          <div className="login-aurora login-aurora-3" />
        </div>
        <div className="login-parallax login-parallax-mid">
          <div className="login-grid" />
        </div>
        <div className="login-vignette" />
        <div className="login-noise" />
      </div>

      <IconButton
        label={t('toggleTheme')}
        variant="outline"
        onClick={() => setTheme(resolvedTheme === 'dark' ? 'light' : 'dark')}
        className="absolute top-[max(1rem,env(safe-area-inset-top))] right-[max(1rem,env(safe-area-inset-right))] z-20 size-9 rounded-lg border-border/60 bg-card/60 backdrop-blur-md"
      >
        <Sun className="size-4 rotate-0 scale-100 transition-all dark:-rotate-90 dark:scale-0" />
        <Moon className="absolute size-4 rotate-90 scale-0 transition-all dark:rotate-0 dark:scale-100" />
      </IconButton>

      <motion.div
        initial={false}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        className="relative z-10 w-full max-w-sm"
      >
        <div className="mb-8 text-center select-none">
          <motion.div
            initial={false}
            animate={{ scale: 1 }}
            className="mx-auto mb-4 flex size-16 items-center justify-center rounded-2xl bg-white shadow-lg ring-1 ring-black/5"
          >
            <Image src="/swapmid-logo.svg" alt={BRAND_NAME} width={48} height={48} className="size-12" unoptimized priority />
          </motion.div>
          <h1 className="text-2xl font-bold tracking-tight">{BRAND_NAME}</h1>
          <p className="text-muted-foreground mt-1 text-sm">{tc('platformTagline')}</p>
        </div>

        {mustChange ? (
          <Card className="gap-4 shadow-xl border-0 bg-card/80 backdrop-blur-xl">
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-lg">
                <KeyRound className="size-4" />
                {t('setPasswordTitle')}
              </CardTitle>
              <CardDescription>{t('setPasswordDescription')}</CardDescription>
            </CardHeader>
            <CardContent>
              <form
                action={changeAction}
                className="space-y-4"
                onSubmit={(event) => {
                  if (!passwordPolicy) return
                  if (!evaluatePasswordPolicy(newPasswordValue, passwordPolicy).ok) {
                    event.preventDefault()
                    setLocalComplexityError(t('errorPasswordComplexity'))
                  } else {
                    setLocalComplexityError(null)
                  }
                }}
              >
                <input type="hidden" name="username" value={username} />
                <input type="hidden" name="password" value={password} />
                <input type="hidden" name="changePasswordToken" value={changePasswordToken} />

                <div className="space-y-1.5">
                  <Label htmlFor="new-password">{t('newPassword')}</Label>
                  <div className="relative">
                    <Input
                      id="new-password"
                      name="newPassword"
                      type={showNewPassword ? 'text' : 'password'}
                      placeholder={t('newPasswordPlaceholder')}
                      autoComplete="new-password"
                      className="pr-9"
                      value={newPasswordValue}
                      onChange={(e) => {
                        setNewPasswordValue(e.target.value)
                        if (localComplexityError) setLocalComplexityError(null)
                      }}
                      onKeyDown={detectCapsLock}
                      onKeyUp={detectCapsLock}
                      onBlur={() => setCapsLock(false)}
                      autoFocus
                      required
                    />
                    {passwordToggle(showNewPassword, () => setShowNewPassword((v) => !v))}
                  </div>
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="confirm-password">{t('confirmPassword')}</Label>
                  <Input
                    id="confirm-password"
                    name="confirmPassword"
                    type="password"
                    placeholder={t('confirmPasswordPlaceholder')}
                    autoComplete="new-password"
                    onKeyDown={detectCapsLock}
                    onKeyUp={detectCapsLock}
                    onBlur={() => setCapsLock(false)}
                    required
                  />
                </div>
                {capsLock && (
                  <p className="text-xs text-warning">{t('capsLockHint')}</p>
                )}

                {(localComplexityError || changeState?.error) && (
                  <motion.p
                    role="alert"
                    initial={{ opacity: 0, y: -4 }}
                    animate={{ opacity: 1, y: 0 }}
                    className="text-sm text-destructive bg-destructive/10 rounded-md px-3 py-2"
                  >
                    {localComplexityError ?? changeState?.error}
                  </motion.p>
                )}

                {changeState?.refreshRequired && (
                  <Button type="button" variant="outline" className="w-full" onClick={() => window.location.reload()}>
                    {t('refreshLoginPage')}
                  </Button>
                )}

                <Button type="submit" className="w-full" disabled={changePending || changeState?.refreshRequired}>
                  {changePending ? (
                    <>
                      <Loader2 className="size-4 animate-spin" />
                      {t('submitting')}
                    </>
                  ) : t('submitAndLogin')}
                </Button>

                <div className="rounded-md bg-muted/50 px-3 py-2">
                  <PasswordPolicyHints policy={passwordPolicy} password={newPasswordValue} />
                </div>
              </form>
            </CardContent>
          </Card>
        ) : (
          <Card className="gap-4 shadow-xl border-0 bg-card/80 backdrop-blur-xl">
            <CardHeader>
              <CardTitle className="text-lg">{t('title')}</CardTitle>
              <CardDescription>{t('description')}</CardDescription>
            </CardHeader>
            <CardContent>
              <form action={action} className="space-y-4">
                <div className="space-y-1.5">
                  <Label htmlFor="username">{t('username')}</Label>
                  <Input
                    id="username"
                    name="username"
                    placeholder={t('usernamePlaceholder')}
                    autoComplete="username"
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    autoFocus
                    required
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="password">{t('password')}</Label>
                  <div className="relative">
                    <Input
                      id="password"
                      name="password"
                      type={showPassword ? 'text' : 'password'}
                      placeholder={t('passwordPlaceholder')}
                      autoComplete="current-password"
                      className="pr-9"
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      onKeyDown={detectCapsLock}
                      onKeyUp={detectCapsLock}
                      onBlur={() => setCapsLock(false)}
                      required
                    />
                    {passwordToggle(showPassword, () => setShowPassword((v) => !v))}
                  </div>
                  {capsLock && (
                    <p className="text-xs text-warning">{t('capsLockHint')}</p>
                  )}
                </div>

                {(state?.error || changeState?.error) && (
                  <motion.p
                    role="alert"
                    initial={{ opacity: 0, y: -4 }}
                    animate={{ opacity: 1, y: 0 }}
                    className="text-sm text-destructive bg-destructive/10 rounded-md px-3 py-2"
                  >
                    {state?.error ?? changeState?.error}
                  </motion.p>
                )}

                {(state?.refreshRequired || changeState?.refreshRequired) && (
                  <Button type="button" variant="outline" className="w-full" onClick={() => window.location.reload()}>
                    {t('refreshLoginPage')}
                  </Button>
                )}

                <Button type="submit" className="w-full" disabled={pending || state?.refreshRequired || changeState?.refreshRequired}>
                  {pending ? (
                    <>
                      <Loader2 className="size-4 animate-spin" />
                      {t('loggingIn')}
                    </>
                  ) : t('loginButton')}
                </Button>

                <div className="text-muted-foreground space-y-1 rounded-md bg-muted/50 px-3 py-2 text-xs leading-relaxed">
                  <p>{t('forgotPasswordHint')}</p>
                </div>
              </form>
            </CardContent>
          </Card>
        )}

        <footer className="mt-6 space-y-1 text-center">
          <p className="text-muted-foreground/60 text-xs" title={release.title}>
            {t('techSupportFooter', { brand: BRAND_NAME, version: release.text })}
          </p>
        </footer>
      </motion.div>
    </div>
  )
}
