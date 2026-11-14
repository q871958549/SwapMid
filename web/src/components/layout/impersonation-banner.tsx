'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { ShieldAlert, Undo2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { toast } from '@/components/ui/use-toast'
import { requestStopImpersonation } from '@/lib/impersonation-client'
import { useSessionIdentityVersion } from '@/components/providers/session-identity-provider'
import { announceSessionIdentityChange } from '@/components/layout/session-guard'

interface ImpersonationBannerProps {
  /** 当前顶层身份（正被模拟的目标账号）展示名。 */
  targetName: string
  targetUserName: string
  /** 发起模拟登录的管理员展示名（来自服务端 session 的 impersonator 快照，仅展示字段）。 */
  originatorName: string
}

/**
 * 模拟登录态的唯一出口——醒目、不可折叠的通栏，置于 Header 上方（见 (dashboard)/layout.tsx）。
 * 只接收展示字段（姓名/账号名），不接触任何 token（安全红线③，见蓝图 §3.4）。
 * 切回走 `/api/auth/stop-impersonation`，成功或身份已变都整页硬跳，不依赖当前业务页重渲。
 */
export function ImpersonationBanner({ targetName, targetUserName, originatorName }: ImpersonationBannerProps) {
  const t = useTranslations('Impersonation')
  const tc = useTranslations('Common')
  const identityVersion = useSessionIdentityVersion()
  const [stopping, setStopping] = useState(false)

  async function handleStop() {
    setStopping(true)
    try {
      const result = await requestStopImpersonation(identityVersion)
      if (result.status === 'switched') {
        if (result.identityVersion) {
          announceSessionIdentityChange(result.identityVersion)
        }
        window.location.replace('/dashboard')
        return
      }
      if (result.errorKey === 'unavailable') {
        // SessionGuard 会对 tagged 503 导航到会话故障页；这里不再叠一条失败 toast。
        return
      }
      toast({
        variant: 'destructive',
        title: t('bannerStopFailedTitle'),
        description: result.errorKey === 'session-expired'
          ? t('errorSessionExpired')
          : t('errorStopFailed'),
      })
      setStopping(false)
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('bannerStopFailedTitle'), description: tc('tryAgainLater') })
      setStopping(false)
    }
  }

  return (
    <div className="flex h-10 shrink-0 items-center gap-2 border-b border-warning/40 bg-warning-soft px-4 text-sm text-warning select-none">
      <ShieldAlert className="size-4 shrink-0" />
      <span className="min-w-0 truncate">
        {targetName
          ? t.rich('actingAsWithUsername', {
              b: (chunks) => <strong className="font-semibold">{chunks}</strong>,
              u: (chunks) => <span className="text-warning/80">{chunks}</span>,
              name: targetName,
              userName: targetUserName,
              originator: originatorName,
            })
          : t.rich('actingAsWithoutUsername', {
              b: (chunks) => <strong className="font-semibold">{chunks}</strong>,
              name: targetUserName,
              originator: originatorName,
            })}
      </span>
      <Button
        size="sm"
        variant="outline"
        className="ml-auto h-7 shrink-0 border-warning/50 bg-transparent text-warning hover:bg-warning/10"
        onClick={handleStop}
        disabled={stopping}
      >
        <Undo2 className="size-3.5" />
        {stopping ? t('switchingBack') : t('switchBackToMyAccount')}
      </Button>
    </div>
  )
}
