'use client'

// 全局错误边界：兜住连 root layout 都渲染失败的极端情况（dashboard/error.tsx 只能接住
// 其 children 段内的错误，layout 自身抛错由这里接管）。global-error 会替换整个
// <html>，此时 globals.css 未必加载，故全部用内联样式自包含，不依赖 Tailwind / 项目组件。
//
// i18n 例外登记（批次4k扫描仪表已排查，整文件登记，勿删）：本组件替换的是根 layout.tsx 自身
// （NextIntlClientProvider 就渲染在 app/layout.tsx 内），触发场景恰恰包含"root layout 渲染失败"，
// 此时 provider 未必已挂载——调用 useTranslations()/useLocale() 若在无 provider 场景下执行会
// 抛 "No intl context found"，导致这个本该兜底的错误页自身崩溃、彻底白屏（比不翻译更糟）。
// 故全文件保持硬编码中文，与其自包含内联样式（不依赖 Tailwind/项目组件）同一保守策略。
// 对照：(dashboard)/error.tsx 只接住 (dashboard) 段 children 的渲染错误（不含同段 layout 自身），
// 根 layout 在那种场景下必已成功渲染、NextIntlClientProvider 必然可用，故该文件正常走 useTranslations。
import { useEffect, useRef, useState } from 'react'
import { reportClientError } from '@/lib/report-client-error'
import { recoverSessionToLoginWithIdentityChange } from '@/lib/client-session-recovery'

export function GlobalErrorActions({
  reset,
  reload = () => window.location.reload(),
}: {
  reset: () => void
  reload?: () => void
}) {
  const recoveryInFlight = useRef(false)
  const [isRecovering, setIsRecovering] = useState(false)
  const [recoveryFailed, setRecoveryFailed] = useState(false)
  const [navigationStalled, setNavigationStalled] = useState(false)
  const mounted = useRef(false)
  const navigationWatchdog = useRef<ReturnType<typeof globalThis.setTimeout> | null>(null)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      if (navigationWatchdog.current !== null) globalThis.clearTimeout(navigationWatchdog.current)
    }
  }, [])

  const recoverToLogin = async () => {
    if (recoveryInFlight.current) return
    recoveryInFlight.current = true
    setIsRecovering(true)
    setRecoveryFailed(false)

    const outcome = await recoverSessionToLoginWithIdentityChange({
      onIdentityChanged: reload,
    }).catch(() => 'failed' as const)
    if (!mounted.current) return
    if (
      outcome === 'logged-out'
      || outcome === 'session-missing'
      || outcome === 'identity-changed'
    ) {
      // 已完成身份绑定退出后只开放整页刷新；不能再重复删除可能已变化的会话。
      navigationWatchdog.current = globalThis.setTimeout(() => setNavigationStalled(true), 8_000)
      return
    }

    recoveryInFlight.current = false
    setIsRecovering(false)
    setRecoveryFailed(true)
  }

  return (
    <>
      <div style={{ display: 'flex', gap: '0.5rem', justifyContent: 'center' }}>
        <button
          disabled={isRecovering && !navigationStalled}
          onClick={() => navigationStalled ? reload() : reset()}
          style={{
            padding: '0.5rem 1rem',
            borderRadius: '0.5rem',
            border: '1px solid #404040',
            background: 'transparent',
            color: '#e5e5e5',
            cursor: isRecovering && !navigationStalled ? 'not-allowed' : 'pointer',
            opacity: isRecovering && !navigationStalled ? 0.6 : 1,
            fontSize: '0.875rem',
          }}
        >
          重试
        </button>
        <button
          aria-busy={isRecovering && !navigationStalled}
          disabled={isRecovering}
          onClick={() => void recoverToLogin()}
          style={{
            padding: '0.5rem 1rem',
            borderRadius: '0.5rem',
            border: 'none',
            background: '#2563eb',
            color: '#fff',
            cursor: isRecovering ? 'not-allowed' : 'pointer',
            opacity: isRecovering ? 0.7 : 1,
            fontSize: '0.875rem',
          }}
        >
          {isRecovering && !navigationStalled ? '正在安全退出…' : '重新登录'}
        </button>
      </div>
      {(recoveryFailed || navigationStalled) && (
        <p
          role="alert"
          style={{ fontSize: '0.8125rem', color: '#fca5a5', lineHeight: 1.5, margin: '0.75rem 0 0' }}
        >
          会话状态已变化或暂时不可用。为避免误退其他登录，本次未跳转，请稍后重试。
        </p>
      )}
    </>
  )
}

export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string }
  reset: () => void
}) {
  useEffect(() => {
    console.error('[global error]', error)
    reportClientError({ kind: 'boundary-global', message: error.message, stack: error.stack, digest: error.digest })
  }, [error])

  return (
    <html lang="zh-CN">
      <body
        style={{
          margin: 0,
          minHeight: '100vh',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontFamily: 'system-ui, -apple-system, "Segoe UI", sans-serif',
          background: '#0a0a0a',
          color: '#e5e5e5',
        }}
      >
        <div style={{ textAlign: 'center', padding: '2rem', maxWidth: '28rem' }}>
          <h2 style={{ fontSize: '1.125rem', fontWeight: 600, marginBottom: '0.5rem' }}>
            系统出错了
          </h2>
          <p style={{ fontSize: '0.875rem', color: '#a3a3a3', lineHeight: 1.6, marginBottom: '1.5rem' }}>
            页面遇到意外错误。请重试，若持续无法访问请重新登录。
          </p>
          <GlobalErrorActions reset={reset} />
        </div>
      </body>
    </html>
  )
}
