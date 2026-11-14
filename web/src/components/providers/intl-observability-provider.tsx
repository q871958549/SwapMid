'use client'

import { useCallback, type ComponentProps, type ReactNode } from 'react'
import { NextIntlClientProvider, useLocale, type IntlError } from 'next-intl'
import {
  createI18nIncident,
  formatI18nIncident,
  getSafeMessageFallback,
} from '@/i18n/runtime'
import { reportClientError } from '@/lib/report-client-error'

type MessageFallback = NonNullable<ComponentProps<typeof NextIntlClientProvider>['getMessageFallback']>

const reportedIncidents = new Set<string>()
const MAX_REPORTED_INCIDENTS = 200

function scheduleClientIncident(
  incident: ReturnType<typeof createI18nIncident>,
  error: IntlError,
) {
  const message = formatI18nIncident(incident)
  if (reportedIncidents.has(message)) return
  if (reportedIncidents.size >= MAX_REPORTED_INCIDENTS) reportedIncidents.clear()
  reportedIncidents.add(message)

  queueMicrotask(() => {
    reportClientError({
      kind: error.code === 'MISSING_MESSAGE' ? 'i18n-missing-message' : 'i18n-message-error',
      message,
      stack: error.stack,
      buildId: incident.buildId,
    })
  })
}

/**
 * next-intl 的 onError/getMessageFallback 是函数，不能从 Server Component 序列化传入。
 * 本客户端嵌套 Provider 按官方推荐继承外层 locale/messages，只覆盖错误处理策略。
 */
export function IntlObservabilityProvider({
  children,
  buildId,
}: {
  children: ReactNode
  buildId?: string
}) {
  const locale = useLocale()

  const onError = useCallback((error: IntlError) => {
    // MISSING_MESSAGE 需要 namespace/key，交给 getMessageFallback 统一上报；其余错误保留控制台线索。
    if (error.code !== 'MISSING_MESSAGE') console.error(error)
  }, [])

  const getMessageFallback = useCallback<MessageFallback>(({ error, key, namespace }) => {
    const path = typeof window === 'undefined' ? 'unknown' : window.location.pathname
    scheduleClientIncident(
      createI18nIncident({
        code: error.code,
        locale,
        namespace,
        key,
        path,
        buildId,
      }),
      error,
    )
    return getSafeMessageFallback(locale)
  }, [buildId, locale])

  return (
    <NextIntlClientProvider locale={locale} onError={onError} getMessageFallback={getMessageFallback}>
      {children}
    </NextIntlClientProvider>
  )
}
