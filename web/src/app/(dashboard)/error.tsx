'use client'

// Dashboard 段错误边界：兜住任何后台页面在（软导航/SSR）渲染时抛出的错误。
// 缺它时——会话过期后 apiFetch 抛 401、或部署期后端 502/网络错误——Server Component
// 渲染抛错没有边界接住，客户端软导航即「页面不显示任何内容」（本次白屏根因）。
// 生产构建会把 Server 端错误信息脱敏（只剩 digest），故这里不臆测错误类型，
// 一律给出两个明确出口：重试（reset 当前段）/ 重新登录（身份版本绑定的 CAS logout）。
import { useEffect, useState } from 'react'
import { useTranslations } from 'next-intl'
import { AlertTriangle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { reportClientError } from '@/lib/report-client-error'
import { useSessionIdentityVersion } from '@/components/providers/session-identity-provider'
import { recoverSessionToLoginWithIdentityChange } from '@/lib/client-session-recovery'

export default function DashboardError({
  error,
  reset,
}: {
  error: Error & { digest?: string }
  reset: () => void
}) {
  const t = useTranslations('ErrorBoundary')
  const identityVersion = useSessionIdentityVersion()
  const errorKey = error.digest ?? error.message
  const [actionKey, setActionKey] = useState(errorKey)
  const [retrying, setRetrying] = useState(false)
  const [relogging, setRelogging] = useState(false)
  if (actionKey !== errorKey) {
    setActionKey(errorKey)
    setRetrying(false)
    setRelogging(false)
  }

  useEffect(() => {
    console.error('[dashboard error]', error)
    reportClientError({ kind: 'boundary-dashboard', message: error.message, stack: error.stack, digest: error.digest })
  }, [error])

  const busy = retrying || relogging

  return (
    <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-5 p-8 text-center">
      <div className="flex size-14 items-center justify-center rounded-full bg-destructive/10">
        <AlertTriangle className="size-7 text-destructive" />
      </div>
      <div className="space-y-1.5">
        <h2 className="text-lg font-semibold">{t('title')}</h2>
        <p className="text-muted-foreground max-w-sm text-sm leading-relaxed">
          {t('description')}
        </p>
      </div>
      <div className="flex gap-2">
        <Button
          variant="outline"
          disabled={busy}
          aria-busy={retrying}
          onClick={() => {
            setRetrying(true)
            reset()
          }}
        >
          {retrying ? t('retrying') : t('retry')}
        </Button>
        <Button
          disabled={busy}
          aria-busy={relogging}
          onClick={() => {
            // 409/503/网络故障保持 fail-closed；未知失败留在当前边界供用户重试。
            setRelogging(true)
            void recoverSessionToLoginWithIdentityChange({
              identityVersion,
              onIdentityChanged: () => window.location.reload(),
            }).finally(() => setRelogging(false))
          }}
        >
          {relogging ? t('retrying') : t('reLogin')}
        </Button>
      </div>
      {retrying && (
        <p className="text-muted-foreground text-xs" role="status">{t('retryHint')}</p>
      )}
      {error.digest && (
        <p className="text-muted-foreground/60 text-xs">{t('errorDigestPrefix', { digest: error.digest })}</p>
      )}
    </div>
  )
}
