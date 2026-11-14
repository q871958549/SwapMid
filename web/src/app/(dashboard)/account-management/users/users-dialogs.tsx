'use client'

import { useEffect, useRef, useState } from 'react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import {
  Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { PasswordInput } from '@/components/ui/password-input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'
import { RoleMultiSelect } from './role-controls'
import { type AbpRole, type SecuritySettings } from './users-types'

type AssignMode = 'append' | 'replace' | 'remove' | 'clear'

/**
 * 批量分配角色对话框（自包含状态机）。
 * 批量操作 payload 特殊（append/replace 语义），不走 useCrudForm，独立管理自身状态。
 * 模态对话框打开时遮挡列表，故无需在外层禁用触发按钮。
 */
export function BatchRolesDialog({
  target,
  onClose,
  roles,
  onDone,
}: {
  /** 选中的账号行 + 清空选择回调；非空即打开。 */
  target: { ids: string[]; clear: () => void } | null
  onClose: () => void
  roles: AbpRole[]
  /** 成功后重取列表（服务端分页的 reload）。 */
  onDone: () => void
}) {
  const t = useTranslations('Account')
  const tCommon = useTranslations('Common')
  const [roleNames, setRoleNames] = useState<string[]>([])
  const [mode, setMode] = useState<AssignMode>('append')
  const [busy, setBusy] = useState(false)

  // 每次打开（target 变为新的非空值）重置为初始态（追加 / 未选角色），与原页 bulkActions onClick 的重置一致。
  // 用「渲染期按 prop 变化调整 state」官方模式（react.dev），避免 effect 内同步 setState 触发级联渲染。
  const [prevTarget, setPrevTarget] = useState(target)
  if (target !== prevTarget) {
    setPrevTarget(target)
    if (target) { setRoleNames([]); setMode('append') }
  }

  async function submit() {
    if (!target) return
    setBusy(true)
    try {
      const res = await fetch('/api/proxy/account-users/batch-set-roles', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          userIds: target.ids,
          roleNames: mode === 'clear' ? [] : roleNames,
          append: mode === 'append',
          mode,
        }),
      })
      if (!res.ok) throw new Error(await res.text())
      const result = (await res.json()) as { succeeded?: number; skipped?: number }
      const doneTitle = mode === 'clear'
        ? t('user.batchClearDoneTitle')
        : mode === 'remove'
          ? t('user.batchRemoveDoneTitle')
          : t('user.batchAssignDoneTitle')
      toast({
        variant: 'success',
        title: doneTitle,
        description: t('user.batchAssignDoneDesc', { count: result.succeeded ?? 0 })
          + (result.skipped ? t('common.skippedSuffix', { count: result.skipped }) : ''),
      })
      target.clear()
      onClose()
      onDone()
    } catch (error) {
      toast({
        variant: 'destructive',
        title: t('user.batchAssignFailedTitle'),
        description: getApiErrorMessage(error, t('user.batchAssignFailedFallback')),
      })
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={!!target} onOpenChange={o => { if (!o) onClose() }}>
      <DialogContent className="sm:max-w-[460px]" aria-describedby={undefined}>
        <DialogHeader>
          <DialogTitle>{t('user.batchAssignDialogTitle', { count: target?.ids.length ?? 0 })}</DialogTitle>
        </DialogHeader>
        <div className="space-y-4 py-1">
          <div className="space-y-1.5">
            <Label>{t('user.assignMethodLabel')}</Label>
            <Select value={mode} onValueChange={value => setMode(value as AssignMode)}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="append">{t('user.appendOption')}</SelectItem>
                <SelectItem value="replace">{t('user.replaceOption')}</SelectItem>
                <SelectItem value="remove">{t('user.removeOption')}</SelectItem>
                <SelectItem value="clear">{t('user.clearOption')}</SelectItem>
              </SelectContent>
            </Select>
          </div>
          {mode !== 'clear' && (
            <div className="space-y-1.5">
              <Label>{t('user.targetRolesLabel')}</Label>
              <RoleMultiSelect options={roles} value={roleNames} onChange={setRoleNames} />
            </div>
          )}
          {mode === 'remove' && (
            <p className="text-xs text-muted-foreground">{t('user.removeHint')}</p>
          )}
          {mode === 'clear' && (
            <p className="text-xs text-destructive">{t('user.clearHint')}</p>
          )}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>{tCommon('cancel')}</Button>
          <Button
            variant={mode === 'clear' ? 'destructive' : 'default'}
            onClick={() => void submit()}
            disabled={busy || (mode !== 'clear' && roleNames.length === 0)}
          >
            {busy ? tCommon('processing') : mode === 'clear'
              ? t('user.confirmClearButton')
              : mode === 'remove'
                ? t('user.confirmRemoveButton')
                : t('user.confirmAssignButton')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

const FALLBACK_SECURITY = (defaultPassword: string): SecuritySettings => ({
  defaultPassword,
  forceChangePasswordOnFirstLogin: true,
  passwordRequiredLength: 6,
  passwordRequireDigit: true,
  passwordRequireLowercase: true,
  passwordRequireUppercase: true,
  passwordRequireNonAlphanumeric: true,
})

// 文案随 useTranslations 求值，改成函数、由渲染期的组件（SecuritySettingsDialog）调用，而非模块级常量。
function getPasswordRuleToggles(t: ReturnType<typeof useTranslations>) {
  return [
    ['passwordRequireDigit', t('user.ruleDigit')],
    ['passwordRequireLowercase', t('user.ruleLowercase')],
    ['passwordRequireUppercase', t('user.ruleUppercase')],
    ['passwordRequireNonAlphanumeric', t('user.ruleSpecial')],
  ] as const
}

/**
 * 账号安全设置对话框（自包含状态机）：初始密码 + 首登强制改密 + 密码复杂度规则。
 * 打开时拉取当前设置（失败用 fallbackPassword 兜底），保存成功经 onSaved 把新初始密码回传给页面。
 */
export function SecuritySettingsDialog({
  open,
  onOpenChange,
  fallbackPassword,
  onSaved,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** 拉取失败时的兜底初始密码（页面当前已知值）。 */
  fallbackPassword: string
  /** 保存成功后把后端返回的新初始密码回传页面（用于建号默认/重置展示）。 */
  onSaved: (defaultPassword: string) => void
}) {
  const t = useTranslations('Account')
  const tCommon = useTranslations('Common')
  const [security, setSecurity] = useState<SecuritySettings | null>(null)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  // fallbackPassword 仅在拉取失败的 catch 分支用到；用 ref 读最新值，避免它变化时重复拉取。
  const fallbackRef = useRef(fallbackPassword)
  useEffect(() => { fallbackRef.current = fallbackPassword })

  // 打开时清空上次的错误提示（渲染期按 open 变化调整 state，避免 effect 内同步 setState）。
  const [prevOpen, setPrevOpen] = useState(open)
  if (open !== prevOpen) {
    setPrevOpen(open)
    if (open) setMessage(null)
  }

  // 打开时拉取当前设置（不清空 security，避免再次打开闪「加载中」；与原页 openSecuritySettings 一致）。
  useEffect(() => {
    if (!open) return
    let cancelled = false
    void (async () => {
      try {
        const res = await fetch('/api/proxy/account-management/settings/security')
        if (!res.ok) throw new Error(await res.text())
        const data = (await res.json()) as SecuritySettings
        if (!cancelled) setSecurity(data)
      } catch {
        if (!cancelled) setSecurity(FALLBACK_SECURITY(fallbackRef.current))
      }
    })()
    return () => { cancelled = true }
  }, [open])

  async function save() {
    if (!security) return
    setSaving(true)
    setMessage(null)
    try {
      const res = await fetch('/api/proxy/account-management/settings/security', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(security),
      })
      if (!res.ok) throw new Error(await res.text())
      const data = (await res.json()) as SecuritySettings
      setSecurity(data)
      onSaved(data.defaultPassword)
      onOpenChange(false)
      toast({ variant: 'success', title: t('user.securitySavedTitle'), description: t('user.securitySavedDesc') })
    } catch (error) {
      // 失败提示必须显示在对话框内（toast 在右下角且 4s 消失，用户误以为"保存无效"）
      setMessage(getApiErrorMessage(error, t('user.securitySaveFailedFallback')))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[460px]" aria-describedby={undefined}>
        <DialogHeader>
          <DialogTitle>{t('user.securityDialogTitle')}</DialogTitle>
        </DialogHeader>
        {!security ? (
          <p className="text-muted-foreground py-6 text-center text-sm">{t('user.loadingText')}</p>
        ) : (
          <div className="space-y-5 py-2">
            <div className="space-y-2">
              <Label htmlFor="account-default-password">{t('user.defaultPasswordLabel')}</Label>
              <PasswordInput
                id="account-default-password"
                value={security.defaultPassword}
                onChange={e => setSecurity(s => s && ({ ...s, defaultPassword: e.target.value }))}
                className="font-mono"
                placeholder={t('user.defaultPasswordPlaceholder')}
              />
              <p className="text-muted-foreground text-xs">{t('user.defaultPasswordHint')}</p>
            </div>

            <div className="flex items-center justify-between gap-4 rounded-md border px-3 py-2.5">
              <div className="min-w-0 flex-1 space-y-0.5">
                <p className="text-sm font-medium">{t('user.forceChangeLabel')}</p>
                <p className="text-muted-foreground text-xs leading-relaxed">{t('user.forceChangeHint')}</p>
              </div>
              <Switch
                className="shrink-0"
                checked={security.forceChangePasswordOnFirstLogin}
                onCheckedChange={v => setSecurity(s => s && ({ ...s, forceChangePasswordOnFirstLogin: v }))}
              />
            </div>

            <div className="space-y-2.5 rounded-md border px-3 py-2.5">
              <p className="text-sm font-medium">{t('user.passwordRuleLabel')}</p>
              <div className="flex items-center justify-between">
                <Label htmlFor="pw-len" className="text-sm font-normal">{t('user.minLengthLabel')}</Label>
                <Input
                  id="pw-len"
                  type="number"
                  min={6}
                  max={128}
                  value={security.passwordRequiredLength}
                  onChange={e => setSecurity(s => s && ({ ...s, passwordRequiredLength: Math.max(6, Number(e.target.value) || 6) }))}
                  className="h-8 w-20 text-right"
                />
              </div>
              {getPasswordRuleToggles(t).map(([key, label]) => (
                <div key={key} className="flex items-center justify-between gap-4">
                  <span className="min-w-0 flex-1 text-sm">{label}</span>
                  <Switch
                    className="shrink-0"
                    checked={security[key]}
                    onCheckedChange={v => setSecurity(s => s && ({ ...s, [key]: v }))}
                  />
                </div>
              ))}
            </div>

            {message && (
              <p className="text-destructive bg-destructive/10 rounded-md px-3 py-2 text-sm">{message}</p>
            )}
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>{tCommon('cancel')}</Button>
          <Button
            onClick={() => void save()}
            disabled={saving || !security || security.defaultPassword.trim().length < security.passwordRequiredLength}
          >
            {saving ? tCommon('saving') : tCommon('save')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
