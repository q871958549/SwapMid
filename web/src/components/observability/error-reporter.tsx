'use client'

import { useEffect } from 'react'
import { reportClientError } from '@/lib/report-client-error'

/**
 * 全局未捕获错误监听——补 React error boundary（error.tsx / global-error.tsx）接不住的盲区:
 * 事件回调里的异步抛错、Promise rejection、非 React 渲染路径的脚本错误。这些边界拦不到,
 * 不挂监听就连日志都留不下,只在用户浏览器控制台一闪而过。
 *
 * 形态参照 version-watcher:挂在 dashboard layout 的一个零渲染 client 组件,
 * useEffect 注册 window 监听、卸载时清理。上报经 reportClientError → 后端 Seq。
 */
export function ErrorReporter({ buildId }: { buildId?: string }) {
  useEffect(() => {
    const onError = (e: ErrorEvent) => {
      reportClientError({
        kind: 'window-error',
        message: e.message,
        stack: e.error?.stack,
        buildId,
      })
    }
    const onRejection = (e: PromiseRejectionEvent) => {
      const reason: unknown = e.reason
      const message =
        typeof reason === 'string'
          ? reason
          : (reason instanceof Error ? reason.message : String(reason))
      reportClientError({
        kind: 'unhandledrejection',
        message,
        stack: reason instanceof Error ? reason.stack : undefined,
        buildId,
      })
    }

    window.addEventListener('error', onError)
    window.addEventListener('unhandledrejection', onRejection)
    return () => {
      window.removeEventListener('error', onError)
      window.removeEventListener('unhandledrejection', onRejection)
    }
  }, [buildId])

  return null
}
