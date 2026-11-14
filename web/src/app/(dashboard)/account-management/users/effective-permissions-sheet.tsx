'use client'

// P1-4：任意用户有效权限查询（"他为什么有这权限"）。后端 CurrentUserPermissionAppService.GetForUserAsync
// 内层调用 ABP 原生 IPermissionAppService.GetAsync("U", userId) 时要求调用者持有 ABP 内置
// AbpIdentity.Users.ManagePermissions（与本域现有 admin/角色权限管理同级门槛，而非本仓自定义权限，
// 无法经 useHasPermission 预判）——故不做前端预判隐藏，统一显示入口，403 走下方 forbidden 态兜底
// （同「培训 403 容错=设计行为」先例，见项目记忆）。

import { useEffect, useState } from 'react'
import { useTranslations } from 'next-intl'
import { Loader2, ShieldOff } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Sheet, SheetBody, SheetContent, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { getApiErrorMessage } from '@/lib/api-error'
import type { AccountPermissionGroupDto, AccountUserPermissionDto } from '@/lib/api/types'
import { formatPermName, getPermissionTitle } from '../permissions/permission-tree'
import type { AbpUser } from './users-types'

type LoadState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; data: AccountUserPermissionDto }
  | { status: 'forbidden' }
  | { status: 'error'; message: string }

export function EffectivePermissionsSheet({ user, onClose }: { user: AbpUser | null; onClose: () => void }) {
  const t = useTranslations('Account')
  const [state, setState] = useState<LoadState>({ status: 'idle' })

  // 具名函数 + setTimeout 边界规避 react-hooks/set-state-in-effect（同 roles/role-detail-sheet.tsx
  // 范式：该规则会追踪被调函数内部的 setState，调用点须经 setTimeout/Promise 回调脱离 effect
  // 本轮同步执行才视为安全）。
  async function loadEffectivePermissions(userId: string, guard: { cancelled: boolean }) {
    setState({ status: 'loading' })
    try {
      const res = await fetch(`/api/proxy/account-users/${userId}/effective-permissions`)
      if (res.status === 403) {
        if (!guard.cancelled) setState({ status: 'forbidden' })
        return
      }
      if (!res.ok) throw new Error(await res.text())
      const data = (await res.json()) as AccountUserPermissionDto
      if (!guard.cancelled) setState({ status: 'success', data })
    } catch (e) {
      if (guard.cancelled) return
      console.error(e)
      setState({ status: 'error', message: getApiErrorMessage(e, t('user.effectivePermissionsLoadFailedDesc')) })
    }
  }

  useEffect(() => {
    if (!user) return
    const guard = { cancelled: false }
    const timer = window.setTimeout(() => { void loadEffectivePermissions(user.id, guard) }, 0)
    return () => { window.clearTimeout(timer); guard.cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user])

  const grantedGroups = state.status === 'success'
    ? state.data.groups
        .map(group => ({ group, granted: group.permissions.filter(p => p.isGranted) }))
        .filter(entry => entry.granted.length > 0)
    : []

  return (
    <Sheet open={!!user} onOpenChange={o => { if (!o) onClose() }}>
      <SheetContent side="right" aria-describedby={undefined} className="sm:max-w-2xl">
        {user && (
          <>
            <SheetHeader>
              <SheetTitle>{t('user.effectivePermissionsTitle', { name: user.name || user.userName })}</SheetTitle>
            </SheetHeader>
            <SheetBody>
              {(state.status === 'idle' || state.status === 'loading') && (
                <div className="text-muted-foreground flex items-center justify-center gap-2 py-16 text-sm">
                  <Loader2 className="size-4 animate-spin" />{t('user.loadingText')}
                </div>
              )}
              {state.status === 'forbidden' && (
                <div className="text-muted-foreground rounded-md border border-dashed py-12 text-center text-sm">
                  <ShieldOff className="mx-auto mb-2 size-8 opacity-40" />
                  {t('user.effectivePermissionsForbiddenHint')}
                </div>
              )}
              {state.status === 'error' && (
                <p className="text-destructive py-8 text-center text-sm">{state.message}</p>
              )}
              {state.status === 'success' && grantedGroups.length === 0 && (
                <p className="text-muted-foreground py-8 text-center text-sm">{t('user.effectivePermissionsEmptyHint')}</p>
              )}
              {state.status === 'success' && grantedGroups.length > 0 && (
                <div className="space-y-4">
                  {grantedGroups.map(({ group, granted }) => (
                    <EffectivePermissionsGroup key={group.name} group={group} granted={granted} t={t} />
                  ))}
                </div>
              )}
            </SheetBody>
          </>
        )}
      </SheetContent>
    </Sheet>
  )
}

function EffectivePermissionsGroup({
  group, granted, t,
}: {
  group: AccountPermissionGroupDto
  granted: AccountPermissionGroupDto['permissions']
  t: ReturnType<typeof useTranslations>
}) {
  return (
    <div className="rounded-lg border">
      <div className="flex items-center gap-2 border-b px-3 py-2">
        <div className="min-w-0 flex-1">
          <div className="truncate text-sm font-medium" title={group.displayName}>{group.displayName}</div>
          <div className="truncate font-mono text-xs text-muted-foreground" title={group.name}>{formatPermName(group.name)}</div>
        </div>
        <Badge variant="secondary" className="shrink-0 tabular-nums">
          {t('user.effectivePermissionsGroupCount', { granted: granted.length, total: group.permissions.length })}
        </Badge>
      </div>
      <ul className="divide-y">
        {granted.map(permission => (
          <li key={permission.name} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
            <div className="min-w-0">
              <div className="truncate text-sm" title={getPermissionTitle(permission)}>{getPermissionTitle(permission)}</div>
              <div className="truncate font-mono text-xs text-muted-foreground" title={permission.name}>{formatPermName(permission.name)}</div>
            </div>
            <div className="flex shrink-0 flex-wrap items-center gap-1">
              {(permission.grantedProviders ?? []).map((provider, idx) => (
                <Badge key={`${provider.providerName}-${provider.providerKey}-${idx}`} variant={provider.providerName === 'R' ? 'secondary' : 'outline'} className="text-[10px]">
                  {provider.providerName === 'R' ? provider.providerKey : t('user.effectivePermissionsDirectGrant')}
                </Badge>
              ))}
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}
