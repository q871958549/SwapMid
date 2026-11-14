'use client'

import { useRef, useState } from 'react'
import { useRouter } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { Loader2, Minus, Plus, Upload } from 'lucide-react'
import { useTheme } from '@/components/theme-provider'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { IconButton } from '@/components/ui/icon-button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'
import { useAvatarStore } from '@/stores/use-avatar-store'
import { useSessionIdentityVersion } from '@/components/providers/session-identity-provider'
import { bindSessionIdentityToUrl } from '@/lib/session-availability'
import { PasswordPolicyHints } from '@/components/password-policy-hints'
import { evaluatePasswordPolicy, isPasswordComplexityFailure, type PasswordPolicy } from '@/lib/password-policy'

export function ProfileClient({
  name,
  userName,
  passwordPolicy,
}: {
  name: string
  userName: string
  passwordPolicy: PasswordPolicy | null
}) {
  const t = useTranslations('Profile')
  const tp = useTranslations('PasswordPolicy')
  const { theme, setTheme, fontSize, setFontSize } = useTheme()
  const router = useRouter()
  const identityVersion = useSessionIdentityVersion()

  // 头像版本号取自全局 store（与 header 共享）：上传后 bump，header 与本页一起刷新。
  const avatarVersion = useAvatarStore((s) => s.version)
  const bumpAvatar = useAvatarStore((s) => s.bump)
  const [uploading, setUploading] = useState(false)
  const fileRef = useRef<HTMLInputElement>(null)

  const [pwd, setPwd] = useState({ current: '', next: '', confirm: '' })
  const [changingPwd, setChangingPwd] = useState(false)

  const avatarSrc = bindSessionIdentityToUrl(
    `/api/proxy/api/v1/avatars/me${avatarVersion ? `?v=${avatarVersion}` : ''}`,
    identityVersion,
  )
  const initials = (name || userName).slice(0, 2).toUpperCase()

  async function handleAvatarChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    setUploading(true)
    try {
      const fd = new FormData()
      fd.append('file', file)
      const res = await fetch('/api/proxy/api/v1/avatars', { method: 'POST', body: fd })
      if (!res.ok) throw new Error(await res.text())
      bumpAvatar()
      toast({ variant: 'success', title: t('avatarUpdatedTitle') })
      router.refresh() // 失效其他页（如用户列表）的 RSC 缓存；header/本页头像靠 avatar store + no-cache 即时刷新
    } catch (err) {
      console.error(err)
      toast({ variant: 'destructive', title: t('avatarUploadFailedTitle'), description: getApiErrorMessage(err, t('avatarUploadFailedFallback')) })
    } finally {
      setUploading(false)
      if (fileRef.current) fileRef.current.value = ''
    }
  }

  async function handleChangePassword() {
    if (!pwd.current || !pwd.next) {
      toast({ variant: 'destructive', title: t('passwordRequired') })
      return
    }
    if (pwd.next !== pwd.confirm) {
      toast({ variant: 'destructive', title: t('passwordMismatch') })
      return
    }
    if (passwordPolicy && !evaluatePasswordPolicy(pwd.next, passwordPolicy).ok) {
      toast({ variant: 'destructive', title: tp('complexityFailed') })
      return
    }
    setChangingPwd(true)
    try {
      const res = await fetch('/api/proxy/api/account/my-profile/change-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ currentPassword: pwd.current, newPassword: pwd.next }),
      })
      if (!res.ok) {
        const body = await res.text()
        throw new Error(body)
      }
      setPwd({ current: '', next: '', confirm: '' })
      toast({ variant: 'success', title: t('passwordUpdatedTitle') })
    } catch (err) {
      console.error(err)
      const fallback = err instanceof Error && isPasswordComplexityFailure(err.message)
        ? tp('complexityFailed')
        : t('changePasswordFailedFallback')
      toast({ variant: 'destructive', title: t('changePasswordFailedTitle'), description: getApiErrorMessage(err, fallback) })
    } finally {
      setChangingPwd(false)
    }
  }

  return (
    <div className="mx-auto w-full max-w-2xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold">{t('pageTitle')}</h1>
        <p className="text-muted-foreground text-sm">{t('pageDescription')}</p>
      </div>

      {/* 头像 + 账号信息 */}
      <section className="bg-card rounded-lg border p-6">
        <div className="flex items-center gap-4">
          <Avatar className="size-16">
            <AvatarImage src={avatarSrc} alt={t('avatarAlt')} />
            <AvatarFallback className="text-lg">{initials}</AvatarFallback>
          </Avatar>
          <div className="min-w-0 flex-1">
            <p className="truncate font-medium">{name || userName}</p>
            <p className="text-muted-foreground truncate text-sm">@{userName}</p>
          </div>
          <input
            ref={fileRef}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="hidden"
            onChange={handleAvatarChange}
          />
          <Button variant="outline" size="sm" disabled={uploading} onClick={() => fileRef.current?.click()}>
            {uploading ? <Loader2 className="size-4 animate-spin" /> : <Upload className="size-4" />}
            {t('changeAvatarLabel')}
          </Button>
        </div>
        <p className="text-muted-foreground mt-3 text-xs">{t('avatarHint')}</p>
      </section>

      {/* 界面偏好 */}
      <section className="bg-card space-y-5 rounded-lg border p-6">
        <h2 className="font-medium">{t('preferencesTitle')}</h2>

        <div className="space-y-2">
          <Label>{t('themeLabel')}</Label>
          <Select value={theme} onValueChange={(v) => setTheme(v as 'light' | 'dark' | 'system')}>
            <SelectTrigger className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="system">{t('themeFollowSystem')}</SelectItem>
              <SelectItem value="light">{t('themeLight')}</SelectItem>
              <SelectItem value="dark">{t('themeDark')}</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="space-y-2">
          <Label htmlFor="font-size">{t('fontSizeLabel')}</Label>
          <div className="flex items-center gap-2">
            <IconButton
              variant="outline"
              label={t('decreaseFontLabel')}
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
              onChange={(e) => setFontSize(Number(e.target.value))}
              className="w-20 text-center"
            />
            <IconButton
              variant="outline"
              label={t('increaseFontLabel')}
              className="size-9"
              onClick={() => setFontSize(fontSize + 1)}
              disabled={fontSize >= 18}
            >
              <Plus className="size-4" />
            </IconButton>
          </div>
        </div>
      </section>

      {/* 修改密码 */}
      <section className="bg-card space-y-4 rounded-lg border p-6">
        <h2 className="font-medium">{t('changePasswordTitle')}</h2>
        <div className="space-y-2">
          <Label htmlFor="current-password">{t('currentPasswordLabel')}</Label>
          <Input
            id="current-password"
            type="password"
            autoComplete="current-password"
            value={pwd.current}
            onChange={(e) => setPwd((p) => ({ ...p, current: e.target.value }))}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="new-password">{t('newPasswordLabel')}</Label>
          <Input
            id="new-password"
            type="password"
            autoComplete="new-password"
            value={pwd.next}
            onChange={(e) => setPwd((p) => ({ ...p, next: e.target.value }))}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="confirm-password">{t('confirmPasswordLabel')}</Label>
          <Input
            id="confirm-password"
            type="password"
            autoComplete="new-password"
            value={pwd.confirm}
            onChange={(e) => setPwd((p) => ({ ...p, confirm: e.target.value }))}
          />
        </div>
        <PasswordPolicyHints policy={passwordPolicy} password={pwd.next} />
        <div className="flex justify-end">
          <Button onClick={handleChangePassword} disabled={changingPwd}>
            {changingPwd && <Loader2 className="size-4 animate-spin" />}
            {t('savePasswordButton')}
          </Button>
        </div>
      </section>
    </div>
  )
}
