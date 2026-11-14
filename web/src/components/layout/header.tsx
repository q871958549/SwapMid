'use client'

import { useState } from 'react'
import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useLocale, useTranslations } from 'next-intl'
import { useTheme } from '@/components/theme-provider'
import { KeyRound, Loader2, LogOut, Minus, Moon, Plus, Settings, Sun, User, UserCog } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { IconButton } from '@/components/ui/icon-button'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ConnectedTabListMenu } from '@/components/layout/connected-tab-list-menu'
import { GlobalSearch } from '@/components/layout/global-search'
import { useAvatarStore } from '@/stores/use-avatar-store'
import { useSessionIdentityVersion } from '@/components/providers/session-identity-provider'
import { bindSessionIdentityToUrl } from '@/lib/session-availability'
import { recoverSessionToLoginWithIdentityChange } from '@/lib/client-session-recovery'
import { PasswordPolicyHints } from '@/components/password-policy-hints'
import { usePasswordPolicy } from '@/lib/use-password-policy'
import { evaluatePasswordPolicy, isPasswordComplexityFailure } from '@/lib/password-policy'
import { PLATFORM_LOCALE_COOKIE, type SupportedLocale } from '@/lib/locale'

interface HeaderProps {
  userName: string
  name: string
  grantedPolicies: Record<string, boolean>
  leading?: React.ReactNode
}

export function Header({ userName, name, grantedPolicies, leading }: HeaderProps) {
  const router = useRouter()
  const t = useTranslations('Header')
  const currentLocale = useLocale()
  const tc = useTranslations('Common')
  const tp = useTranslations('PasswordPolicy')
  const { theme, setTheme, resolvedTheme, fontSize, setFontSize } = useTheme()
  const identityVersion = useSessionIdentityVersion()
  const avatarVersion = useAvatarStore((s) => s.version)
  const avatarUrl = bindSessionIdentityToUrl(
    `/api/proxy/api/v1/avatars/me${avatarVersion ? `?v=${avatarVersion}` : ''}`,
    identityVersion,
  )
  const [accountOpen, setAccountOpen] = useState(false)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [passwordMessage, setPasswordMessage] = useState('')
  const [isChangingPassword, setIsChangingPassword] = useState(false)
  const [isLoggingOut, setIsLoggingOut] = useState(false)
  const passwordPolicy = usePasswordPolicy(accountOpen)

  function handleLocaleChange(next: SupportedLocale) {
    document.cookie = `${PLATFORM_LOCALE_COOKIE}=${next}; path=/; max-age=31536000; samesite=lax`
    void fetch('/api/proxy/api/v1/user-preference', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ language: next }),
    }).catch(() => {})
    router.refresh()
  }

  async function handleLogout() {
    if (isLoggingOut) return
    setIsLoggingOut(true)
    try {
      const outcome = await recoverSessionToLoginWithIdentityChange({
        identityVersion,
        onIdentityChanged: () => window.location.replace('/dashboard'),
      })
      if (outcome !== 'logged-out' && outcome !== 'session-missing') {
        if (outcome !== 'identity-changed') setIsLoggingOut(false)
      }
    } catch {
      setIsLoggingOut(false)
    }
  }

  async function handleChangePassword() {
    setPasswordMessage('')

    if (!currentPassword || !newPassword) {
      setPasswordMessage(t('passwordRequired'))
      return
    }

    if (newPassword !== confirmPassword) {
      setPasswordMessage(t('passwordMismatch'))
      return
    }

    if (passwordPolicy && !evaluatePasswordPolicy(newPassword, passwordPolicy).ok) {
      setPasswordMessage(tp('complexityFailed'))
      return
    }

    setIsChangingPassword(true)
    try {
      const res = await fetch('/api/proxy/api/account/my-profile/change-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ currentPassword, newPassword }),
      })

      if (!res.ok) {
        const body = await res.text()
        setPasswordMessage(isPasswordComplexityFailure(body) ? tp('complexityFailed') : t('passwordFailed'))
        return
      }

      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      setPasswordMessage(t('passwordUpdated'))
    } catch {
      setPasswordMessage(t('networkError'))
    } finally {
      setIsChangingPassword(false)
    }
  }

  return (
    <header className="flex h-14 items-center gap-2 border-b bg-background px-3 select-none md:px-6">
      {leading}
      <ConnectedTabListMenu grantedPolicies={grantedPolicies} className="md:hidden" />
      <div className="mr-auto flex min-w-0 items-center md:flex-1">
        <GlobalSearch grantedPolicies={grantedPolicies} />
      </div>

      <IconButton
        label={t('toggleTheme')}
        className="size-8"
        onClick={() => setTheme(resolvedTheme === 'dark' ? 'light' : 'dark')}
      >
        <Sun className="size-4 rotate-0 scale-100 transition-all dark:-rotate-90 dark:scale-0" />
        <Moon className="absolute size-4 rotate-90 scale-0 transition-all dark:rotate-0 dark:scale-100" />
      </IconButton>

      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" className="flex items-center gap-2 px-2" aria-label={name || userName}>
            <Avatar className="size-7">
              <AvatarImage src={avatarUrl} alt="" />
              <AvatarFallback className="text-xs">{(name || userName).slice(0, 2).toUpperCase()}</AvatarFallback>
            </Avatar>
            <span className="hidden text-sm font-medium md:inline">{name || userName}</span>
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-48">
          <div className="px-2 py-1.5">
            <p className="text-sm font-medium">{name}</p>
            <p className="text-muted-foreground text-xs">@{userName}</p>
          </div>
          <DropdownMenuSeparator />
          <DropdownMenuItem asChild>
            <Link href="/profile">
              <User className="size-4" />
              {t('profile')}
            </Link>
          </DropdownMenuItem>
          <DropdownMenuItem
            onSelect={(event) => {
              event.preventDefault()
              setAccountOpen(true)
            }}
          >
            <UserCog className="size-4" />
            {t('accountManagement')}
          </DropdownMenuItem>
          <DropdownMenuItem
            onSelect={(event) => {
              event.preventDefault()
              setSettingsOpen(true)
            }}
          >
            <Settings className="size-4" />
            {t('systemSettings')}
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem
            variant="destructive"
            disabled={isLoggingOut}
            onClick={() => void handleLogout()}
          >
            {isLoggingOut
              ? <Loader2 className="size-4 animate-spin" />
              : <LogOut className="size-4" />}
            {t('logout')}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={accountOpen} onOpenChange={setAccountOpen}>
        <DialogContent className="sm:max-w-[420px]" aria-describedby={undefined}>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <KeyRound className="size-4" />
              {t('changePassword')}
            </DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="current-password">{t('currentPassword')}</Label>
              <Input
                id="current-password"
                type="password"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(event) => setCurrentPassword(event.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="new-password">{t('newPassword')}</Label>
              <Input
                id="new-password"
                type="password"
                autoComplete="new-password"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="confirm-password">{t('confirmPassword')}</Label>
              <Input
                id="confirm-password"
                type="password"
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(event) => setConfirmPassword(event.target.value)}
              />
            </div>
            <PasswordPolicyHints policy={passwordPolicy} password={newPassword} />
            {passwordMessage && (
              <p className="text-sm text-muted-foreground">{passwordMessage}</p>
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setAccountOpen(false)}>{tc('close')}</Button>
            <Button onClick={handleChangePassword} disabled={isChangingPassword}>
              {isChangingPassword && <Loader2 className="size-4 animate-spin" />}
              {tc('save')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={settingsOpen} onOpenChange={setSettingsOpen}>
        <DialogContent className="sm:max-w-[420px]" aria-describedby={undefined}>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Settings className="size-4" />
              {t('systemSettings')}
            </DialogTitle>
          </DialogHeader>
          <div className="space-y-5">
            <div className="space-y-2">
              <Label>{t('language')}</Label>
              <Select value={currentLocale} onValueChange={(value) => handleLocaleChange(value as SupportedLocale)}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="zh-Hans">{t('localeZhHans')}</SelectItem>
                  <SelectItem value="zh-Hant">{t('localeZhHant')}</SelectItem>
                  <SelectItem value="en">{t('localeEn')}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>{t('theme')}</Label>
              <Select value={theme} onValueChange={(value) => setTheme(value as 'light' | 'dark' | 'system')}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="system">{t('followSystem')}</SelectItem>
                  <SelectItem value="light">{t('light')}</SelectItem>
                  <SelectItem value="dark">{t('dark')}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="font-size">{t('fontSize')}</Label>
              <div className="flex items-center gap-2">
                <IconButton
                  variant="outline"
                  label={t('decreaseFont')}
                  className="size-9"
                  onClick={() => setFontSize(fontSize - 1)}
                  disabled={fontSize <= 13}
                >
                  <Minus className="size-4" />
                </IconButton>
                <Input
                  id="font-size"
                  type="number"
                  min={13}
                  max={18}
                  value={fontSize}
                  onChange={(event) => setFontSize(Number(event.target.value))}
                  className="text-center"
                />
                <IconButton
                  variant="outline"
                  label={t('increaseFont')}
                  className="size-9"
                  onClick={() => setFontSize(fontSize + 1)}
                  disabled={fontSize >= 18}
                >
                  <Plus className="size-4" />
                </IconButton>
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button onClick={() => setSettingsOpen(false)}>{t('done')}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </header>
  )
}
